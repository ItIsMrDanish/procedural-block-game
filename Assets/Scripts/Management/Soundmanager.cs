using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central sound manager — singleton, persists across scenes.
///
/// ── SETUP ──────────────────────────────────────────────────────────────────
/// 1. Create an empty GameObject named "SoundManager" in your MainMenu scene.
/// 2. Attach this component and fill every audio clip slot in the Inspector.
/// 3. The object marks itself DontDestroyOnLoad, so it carries into the World
///    scene automatically. No second copy is needed there.
///
/// ── CALLING FROM OTHER SCRIPTS ─────────────────────────────────────────────
/// SoundManager.Instance.PlayBlockBreak();
/// SoundManager.Instance.PlayFootstep();
/// etc.  — see the Public API region below.
/// </summary>
public class SoundManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static SoundManager Instance { get; private set; }

    // ── Inspector: clips ──────────────────────────────────────────────────────

    [Header("Player Footsteps")]
    [Tooltip("2-4 footstep variants. A random one plays each step.")]
    public AudioClip[] footstepClips;

    [Header("Block Interaction")]
    public AudioClip[] blockBreakClips;   // 1-3 variants, random pick
    public AudioClip[] blockPlaceClips;   // 1-2 variants, random pick
    [Tooltip("Short ticking/chipping sound played repeatedly while holding the break button. " +
             "Should be very short (0.1–0.2s). 2-3 variants keeps it from feeling robotic.")]
    public AudioClip[] blockChipClips;    // played on a timer while actively breaking

    [Header("Crafting")]
    public AudioClip[] craftingClips;     // e.g. a soft 'clink' or table thud

    [Header("Cow")]
    [Tooltip("Footstep sounds when the cow walks.")]
    public AudioClip[] cowFootstepClips;
    [Tooltip("Ambient moo sounds played on a random timer.")]
    public AudioClip[] cowMooClips;

    [Header("Zombie")]
    [Tooltip("Footstep sounds when the zombie walks.")]
    public AudioClip[] zombieFootstepClips;
    [Tooltip("Groan / ambient sounds played on a random timer.")]
    public AudioClip[] zombieGroanClips;

    [Header("Background Music (in-game)")]
    [Tooltip("Multiple tracks — a random one starts when the World scene loads, " +
             "then another random track plays after each one ends.")]
    public AudioClip[] gameMusicTracks;

    [Header("Main Menu Music")]
    public AudioClip mainMenuMusic;

    [Header("UI")]
    [Tooltip("Short click/click-pop played when any main-menu button is pressed.")]
    public AudioClip menuButtonClick;

    [Header("Ambient Sounds")]
    [Tooltip("Wind, birds, crickets, cave drips, etc. — played at low volume on a random timer.")]
    public AudioClip[] ambientClips;

    [Header("Hurt Sounds")]
    [Tooltip("1-3 variants. Plays when the player takes damage.")]
    public AudioClip[] playerHurtClips;
    [Tooltip("1-2 variants. Plays when a cow takes damage.")]
    public AudioClip[] cowHurtClips;
    [Tooltip("1-2 variants. Plays when a zombie takes damage.")]
    public AudioClip[] zombieHurtClips;

    // ── Inspector: volume knobs ───────────────────────────────────────────────

    [Header("Volumes  (0 – 1)")]
    [Range(0f, 1f)] public float footstepVolume   = 0.55f;
    [Range(0f, 1f)] public float blockVolume       = 0.75f;
    [Range(0f, 1f)] public float craftVolume       = 0.65f;
    [Range(0f, 1f)] public float mobFootstepVolume = 0.45f;
    [Range(0f, 1f)] public float mobVoiceVolume    = 0.70f;
    [Range(0f, 1f)] public float hurtVolume        = 0.85f;
    [Range(0f, 1f)] public float musicVolume       = 0.35f;
    [Range(0f, 1f)] public float menuMusicVolume   = 0.50f;
    [Range(0f, 1f)] public float uiVolume          = 0.80f;
    [Range(0f, 1f)] public float ambientVolume     = 0.30f;

    [Header("Ambient Timing")]
    [Tooltip("Minimum seconds between ambient sound cues.")]
    public float ambientMinInterval = 20f;
    [Tooltip("Maximum seconds between ambient sound cues.")]
    public float ambientMaxInterval = 60f;

    // ── Private AudioSources ──────────────────────────────────────────────────
    // One source per 'channel' so sounds don't cut each other off.

    private AudioSource _sfxSource;       // one-shot SFX (footsteps, blocks, UI)
    private AudioSource _musicSource;     // looping background music
    private AudioSource _ambientSource;   // ambient one-shots

    private Coroutine _musicCoroutine;
    private Coroutine _ambientCoroutine;

    // Which scene are we in? Used to decide which music pool to use.
    private bool _inGameScene = false;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        // Classic singleton with DontDestroyOnLoad.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildSources();
    }

    private void Start()
    {
        // Detect which scene we started in (MainMenu vs World).
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene == "MainMenu")
            StartMainMenuMusic();
        else
            StartGameMusic();

        if (ambientClips != null && ambientClips.Length > 0)
            _ambientCoroutine = StartCoroutine(AmbientLoop());
    }

    private void OnEnable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                               UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Switch music when the scene changes.
        if (scene.name == "MainMenu")
        {
            _inGameScene = false;
            StartMainMenuMusic();
        }
        else
        {
            _inGameScene = true;
            StartGameMusic();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    // Player ──────────────────────────────────────────────────────────────────

    public void PlayFootstep()
        => PlayRandom(_sfxSource, footstepClips, footstepVolume, pitchVariance: 0.08f);

    public void PlayBlockBreak()
        => PlayRandom(_sfxSource, blockBreakClips, blockVolume, pitchVariance: 0.05f);

    public void PlayBlockPlace()
        => PlayRandom(_sfxSource, blockPlaceClips, blockVolume, pitchVariance: 0.05f);

    public void PlayBlockChip()
        => PlayRandom(_sfxSource, blockChipClips, blockVolume * 0.6f, pitchVariance: 0.10f);

    public void PlayCraft()
        => PlayRandom(_sfxSource, craftingClips, craftVolume, pitchVariance: 0.0f);

    // Mobs ────────────────────────────────────────────────────────────────────

    public void PlayCowFootstep()
        => PlayRandom(_sfxSource, cowFootstepClips, mobFootstepVolume, pitchVariance: 0.10f);

    public void PlayCowMoo()
        => PlayRandom(_sfxSource, cowMooClips, mobVoiceVolume, pitchVariance: 0.05f);

    public void PlayZombieFootstep()
        => PlayRandom(_sfxSource, zombieFootstepClips, mobFootstepVolume, pitchVariance: 0.10f);

    public void PlayZombieGroan()
        => PlayRandom(_sfxSource, zombieGroanClips, mobVoiceVolume, pitchVariance: 0.08f);

    // UI ──────────────────────────────────────────────────────────────────────

    public void PlayMenuClick()
    {
        if (menuButtonClick == null) return;
        _sfxSource.pitch = 1f;
        _sfxSource.PlayOneShot(menuButtonClick, uiVolume);
    }

    // Hurt ────────────────────────────────────────────────────────────────────

    public void PlayPlayerHurt()
        => PlayRandom(_sfxSource, playerHurtClips, hurtVolume, pitchVariance: 0.06f);

    public void PlayCowHurt()
        => PlayRandom(_sfxSource, cowHurtClips, hurtVolume, pitchVariance: 0.08f);

    public void PlayZombieHurt()
        => PlayRandom(_sfxSource, zombieHurtClips, hurtVolume, pitchVariance: 0.08f);

    // Volume setters (for a settings screen, if you add one) ─────────────────

    public void SetMusicVolume(float v)
    {
        musicVolume = v;
        if (_musicSource != null) _musicSource.volume = v;
    }

    public void SetSFXVolume(float v)
    {
        footstepVolume = v;
        blockVolume    = v;
    }

    // ── Music helpers ─────────────────────────────────────────────────────────

    private void StartMainMenuMusic()
    {
        if (_musicCoroutine != null) StopCoroutine(_musicCoroutine);

        if (mainMenuMusic == null) return;

        _musicSource.clip   = mainMenuMusic;
        _musicSource.volume = menuMusicVolume;
        _musicSource.loop   = true;
        _musicSource.Play();
    }

    private void StartGameMusic()
    {
        if (_musicCoroutine != null) StopCoroutine(_musicCoroutine);
        if (gameMusicTracks == null || gameMusicTracks.Length == 0) return;

        _musicCoroutine = StartCoroutine(GameMusicLoop());
    }

    /// <summary>
    /// Plays a random track, waits for it to end, then picks the next random track.
    /// Ensures the same track isn't picked twice in a row (if there are ≥ 2 tracks).
    /// </summary>
    private IEnumerator GameMusicLoop()
    {
        int last = -1;
        while (true)
        {
            int idx = PickRandomIndex(gameMusicTracks.Length, last);
            last = idx;

            AudioClip clip = gameMusicTracks[idx];
            if (clip == null) { yield return new WaitForSeconds(30f); continue; }

            _musicSource.loop   = false;
            _musicSource.volume = musicVolume;
            _musicSource.clip   = clip;
            _musicSource.Play();

            // Wait for the track to finish.
            yield return new WaitForSeconds(clip.length + Random.Range(5f, 30f));
        }
    }

    // ── Ambient loop ─────────────────────────────────────────────────────────

    private IEnumerator AmbientLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(ambientMinInterval, ambientMaxInterval));

            // Don't play ambient sounds in the main menu — they'd clash with menu music.
            if (!_inGameScene) continue;

            PlayRandom(_ambientSource, ambientClips, ambientVolume, pitchVariance: 0.04f);
        }
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void BuildSources()
    {
        _sfxSource     = AddSource("SFX",     loop: false, volume: 1f);
        _musicSource   = AddSource("Music",   loop: true,  volume: musicVolume);
        _ambientSource = AddSource("Ambient", loop: false, volume: ambientVolume);
    }

    private AudioSource AddSource(string label, bool loop, float volume)
    {
        GameObject go = new GameObject($"AudioSource_{label}");
        go.transform.SetParent(transform);
        AudioSource src = go.AddComponent<AudioSource>();
        src.loop               = loop;
        src.volume             = volume;
        src.playOnAwake        = false;
        src.spatialBlend       = 0f; // 2D — all sounds are global, not positional
        return src;
    }

    /// <summary>
    /// Picks a random clip from the array and plays it as a one-shot with slight pitch variance.
    /// </summary>
    private void PlayRandom(AudioSource src, AudioClip[] clips, float volume, float pitchVariance)
    {
        if (clips == null || clips.Length == 0) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;

        src.pitch = 1f + Random.Range(-pitchVariance, pitchVariance);
        src.PlayOneShot(clip, volume);
    }

    /// <summary>Picks a random index that isn't <paramref name="exclude"/> (if possible).</summary>
    private int PickRandomIndex(int count, int exclude)
    {
        if (count <= 1) return 0;
        int idx;
        int attempts = 0;
        do { idx = Random.Range(0, count); attempts++; }
        while (idx == exclude && attempts < 10);
        return idx;
    }
}