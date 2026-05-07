using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// DayNightCycle
//
// Drives the full 24-hour cycle:
//   • Rotates a Directional Light that acts as both Sun and Moon.
//   • Moves two billboard quads (Sun / Moon) around the player in an arc.
//   • Lerps World.globalLightLevel + sky colour through four keyframes
//     (Dawn → Day → Dusk → Night) and calls World.SetGlobalLightValue()
//     so the existing voxel shader keeps working.
//   • Exposes IsNight (public static) so MonsterSpawnManager (or anything
//     else) can ask "should monsters spawn?" without coupling further.
//
// Setup:
//   1. Create an empty GameObject called "DayNightCycle" in your scene.
//   2. Attach this script to it.
//   3. In the Inspector drag in:
//        sunLight      → a Directional Light (your scene's main light)
//        world         → the World component (or leave null; auto-found)
//        sunVisual     → a Quad (or Sprite) child of this GameObject
//        moonVisual    → a second Quad child of this GameObject
//   4. Optionally assign a star particle system to 'starsSystem'; it will
//      activate at night and fade in/out automatically.
//
// Time:
//   'dayLengthSeconds' = real-world seconds for one full in-game day.
//   Set it to 1200 (20 min) for a Minecraft-like pace.
//
// The "time of day" is stored as a 0-1 float called 'NormalizedTime':
//   0.0 / 1.0 = midnight
//   0.25      = sunrise
//   0.5       = noon
//   0.75      = sunset
// ─────────────────────────────────────────────────────────────────────────────

public class DayNightCycle : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Directional Light that acts as the sun/moon.")]
    public Light sunLight;

    [Tooltip("World component. Auto-found if left null.")]
    public World world;

    [Tooltip("Optional quad / billboard that represents the visible Sun.")]
    public Transform sunVisual;

    [Tooltip("Optional quad / billboard that represents the visible Moon.")]
    public Transform moonVisual;

    [Tooltip("Optional particle system for stars. Activated during night.")]
    public ParticleSystem starsSystem;

    [Header("Time")]
    [Tooltip("Real-world seconds per full in-game day (default 1200 = 20 min).")]
    public float dayLengthSeconds = 1200f;

    [Tooltip("Time of day to start at (0 = midnight, 0.25 = sunrise, 0.5 = noon).")]
    [Range(0f, 1f)]
    public float startingTimeOfDay = 0.25f;

    [Header("Sun / Moon Orbit")]
    [Tooltip("Radius of the celestial-body orbit arc around the player.")]
    public float orbitRadius = 500f;

    [Header("Light Colour Keyframes")]
    [Tooltip("Light colour at dawn (normalizedTime ≈ 0.25).")]
    public Color dawnLightColour  = new Color(1f,  0.7f, 0.4f);
    [Tooltip("Light colour at noon (normalizedTime = 0.5).")]
    public Color dayLightColour   = new Color(1f,  0.98f, 0.9f);
    [Tooltip("Light colour at dusk (normalizedTime ≈ 0.75).")]
    public Color duskLightColour  = new Color(1f,  0.55f, 0.2f);
    [Tooltip("Light colour at night (normalizedTime = 0.0).")]
    public Color nightLightColour = new Color(0.1f, 0.12f, 0.25f);

    [Header("Light Intensity Keyframes")]
    public float dayIntensity   = 1.2f;
    public float dawnDuskIntensity = 0.6f;
    public float nightIntensity = 0.05f;

    [Header("Global Light Level (0-1) Keyframes")]
    [Tooltip("globalLightLevel at noon — fed into the voxel shader.")]
    [Range(0f, 1f)] public float noonLightLevel  = 0.9f;
    [Tooltip("globalLightLevel at midnight.")]
    [Range(0f, 1f)] public float midnightLightLevel = 0.05f;

    // ── Public state ─────────────────────────────────────────────────────────

    /// <summary>Current time of day: 0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset.</summary>
    [HideInInspector] public float NormalizedTime;

    /// <summary>True when it is dark enough for monsters to spawn.</summary>
    public static bool IsNight { get; private set; }

    /// <summary>Normalised brightness of the sky (0 = pitch black, 1 = full day).</summary>
    public static float SkyBrightness { get; private set; }

    // Threshold: night starts / ends at this fraction of a full day cycle.
    // 0.75 = sunset, 0.25 = sunrise — i.e. night is the period 0.75→1.0→0.25.
    private const float SunriseTime = 0.25f;
    private const float SunsetTime  = 0.75f;

    // ── Private ──────────────────────────────────────────────────────────────

    private Transform _player;
    private bool _starsWereActive = false;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        NormalizedTime = startingTimeOfDay;

        if (world == null)
            world = World.Instance;

        if (world != null && world.player != null)
            _player = world.player;

        // Make sure the stars start hidden.
        if (starsSystem != null)
            starsSystem.Stop();
    }

    private void Update()
    {
        if (!World.IsReady) return;

        // Advance time.
        NormalizedTime += Time.deltaTime / dayLengthSeconds;
        if (NormalizedTime >= 1f) NormalizedTime -= 1f;

        UpdateSkyBrightness();
        UpdateIsNight();
        UpdateDirectionalLight();
        UpdateCelestialBodies();
        UpdateStars();
        UpdateWorldShader();
    }

    // ── Sky brightness ────────────────────────────────────────────────────────

    // Maps NormalizedTime to a 0-1 brightness using the four keyframes.
    // Midnight=0, Sunrise=0.25, Noon=0.5, Sunset=0.75, Midnight=1.
    private void UpdateSkyBrightness()
    {
        SkyBrightness = SkyBrightnessAt(NormalizedTime);
    }

    private float SkyBrightnessAt(float t)
    {
        // Night: 0 → sunrise (0.25)
        if (t < SunriseTime)
            return Mathf.Lerp(0f, 1f, t / SunriseTime);
        // Day: sunrise (0.25) → noon (0.5) → sunset (0.75)
        if (t < SunsetTime)
        {
            float mid = (SunriseTime + SunsetTime) * 0.5f; // 0.5
            if (t < mid)
                return Mathf.Lerp(1f, 1f, (t - SunriseTime) / (mid - SunriseTime)); // stays at 1
            else
                return 1f; // holds full brightness until sunset starts
        }
        // Evening: sunset (0.75) → midnight (1.0)
        return Mathf.Lerp(1f, 0f, (t - SunsetTime) / (1f - SunsetTime));
    }

    // ── Night flag ────────────────────────────────────────────────────────────

    private void UpdateIsNight()
    {
        // Night = sun below horizon, i.e. outside the sunrise→sunset window.
        IsNight = NormalizedTime < SunriseTime || NormalizedTime > SunsetTime;
    }

    // ── Directional light ─────────────────────────────────────────────────────

    // The sun sweeps from east (−90° X) through overhead (0°) to west (+90°).
    // We map NormalizedTime → sun angle:
    //   0.25 (sunrise) → −90°   (horizon east)
    //   0.50 (noon)    →   0°   (directly overhead)
    //   0.75 (sunset)  → +90°   (horizon west)
    // The moon is always opposite the sun (+180°).
    private void UpdateDirectionalLight()
    {
        if (sunLight == null) return;

        // Convert NormalizedTime to degrees:
        // Full circle = 360°, time 0 = midnight (sun directly below = 180°).
        float sunAngle = (NormalizedTime * 360f) - 90f; // 0 = midnight → 270°, 0.5 = noon → 90°

        sunLight.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);

        // Colour & intensity
        sunLight.color     = CurrentLightColour();
        sunLight.intensity = CurrentLightIntensity();
    }

    private Color CurrentLightColour()
    {
        float t = NormalizedTime;

        // Midnight → sunrise (0 → 0.25): nightLightColour → dawnLightColour
        if (t < SunriseTime)
            return Color.Lerp(nightLightColour, dawnLightColour, t / SunriseTime);

        float dayMid = (SunriseTime + SunsetTime) * 0.5f; // 0.5 = noon

        // Dawn → noon (0.25 → 0.5)
        if (t < dayMid)
            return Color.Lerp(dawnLightColour, dayLightColour, (t - SunriseTime) / (dayMid - SunriseTime));

        // Noon → dusk (0.5 → 0.75)
        if (t < SunsetTime)
            return Color.Lerp(dayLightColour, duskLightColour, (t - dayMid) / (SunsetTime - dayMid));

        // Dusk → midnight (0.75 → 1.0)
        return Color.Lerp(duskLightColour, nightLightColour, (t - SunsetTime) / (1f - SunsetTime));
    }

    private float CurrentLightIntensity()
    {
        float t = NormalizedTime;
        float dayMid = 0.5f;

        if (t < SunriseTime)
            return Mathf.Lerp(nightIntensity, dawnDuskIntensity, t / SunriseTime);
        if (t < dayMid)
            return Mathf.Lerp(dawnDuskIntensity, dayIntensity, (t - SunriseTime) / (dayMid - SunriseTime));
        if (t < SunsetTime)
            return Mathf.Lerp(dayIntensity, dawnDuskIntensity, (t - dayMid) / (SunsetTime - dayMid));

        return Mathf.Lerp(dawnDuskIntensity, nightIntensity, (t - SunsetTime) / (1f - SunsetTime));
    }

    // ── Celestial body visuals ─────────────────────────────────────────────────

    // Both orb objects orbit in an arc around the player.
    // Sun is at sunAngle, Moon is directly opposite (sunAngle + 180°).
    private void UpdateCelestialBodies()
    {
        Vector3 centre = _player != null ? _player.position : transform.position;

        float sunAngle  = NormalizedTime * 360f - 90f; // degrees; noon = 90° = directly overhead
        float moonAngle = sunAngle + 180f;

        if (sunVisual  != null) sunVisual.position  = OrbPosition(centre, sunAngle);
        if (moonVisual != null) moonVisual.position  = OrbPosition(centre, moonAngle);

        // Keep orbs facing the camera (billboard).
        Camera cam = Camera.main;
        if (cam != null)
        {
            if (sunVisual  != null) sunVisual.forward  = cam.transform.forward;
            if (moonVisual != null) moonVisual.forward = cam.transform.forward;
        }

        // Fade moon in at night, sun in during day.
        if (moonVisual != null)
        {
            Renderer mr = moonVisual.GetComponent<Renderer>();
            if (mr != null)
            {
                Color c = mr.material.color;
                c.a = IsNight ? 1f : 0f;
                mr.material.color = c;
            }
        }
        if (sunVisual != null)
        {
            Renderer sr = sunVisual.GetComponent<Renderer>();
            if (sr != null)
            {
                Color c = sr.material.color;
                // Show sun from just before sunrise to just after sunset.
                bool sunVisible = NormalizedTime > SunriseTime - 0.05f &&
                                  NormalizedTime < SunsetTime  + 0.05f;
                c.a = sunVisible ? 1f : 0f;
                sr.material.color = c;
            }
        }
    }

    // Convert an angle (in degrees, where 0° = east horizon, 90° = overhead) to
    // a world-space position on the orbital circle.
    private Vector3 OrbPosition(Vector3 centre, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        // Orbit in the Y-Z plane (east-west arc visible from the front).
        float y =  Mathf.Sin(rad) * orbitRadius;
        float z =  Mathf.Cos(rad) * orbitRadius;
        return centre + new Vector3(0f, y, z);
    }

    // ── Stars ─────────────────────────────────────────────────────────────────

    private void UpdateStars()
    {
        if (starsSystem == null) return;

        bool shouldShow = IsNight;
        if (shouldShow && !_starsWereActive)
        {
            starsSystem.Play();
            _starsWereActive = true;
        }
        else if (!shouldShow && _starsWereActive)
        {
            starsSystem.Stop();
            _starsWereActive = false;
        }
    }

    // ── World shader integration ───────────────────────────────────────────────

    // Updates World.globalLightLevel (the same value the voxel shader reads)
    // and calls World.SetGlobalLightValue() to push it to the GPU.
    private void UpdateWorldShader()
    {
        if (world == null) return;

        // Map SkyBrightness → midnightLightLevel…noonLightLevel range.
        world.globalLightLevel = Mathf.Lerp(midnightLightLevel, noonLightLevel, SkyBrightness);
        world.SetGlobalLightValue();
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Draw the sun/moon orbit circle.
        Vector3 centre = Application.isPlaying && _player != null
            ? _player.position
            : transform.position;

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.4f);
        int steps = 64;
        Vector3 prev = centre + new Vector3(0f, 0f, orbitRadius);
        for (int i = 1; i <= steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            Vector3 next = centre + new Vector3(0f, Mathf.Sin(a) * orbitRadius, Mathf.Cos(a) * orbitRadius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}
