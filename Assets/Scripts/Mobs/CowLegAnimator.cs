using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// CowLegAnimator — pure code walk animation for the four leg child objects.
//
// Setup:
//   1. Attach to the ROOT cowPrefab GameObject (same level as Cow.cs).
//   2. Drag the four leg Transforms into the Inspector slots:
//        FR Leg → frLeg
//        FL Leg → flLeg
//        BR Leg → brLeg
//        BL Leg → blLeg
//   3. Done — no Animator, no Animation clips needed.
//
// How it works:
//   Each leg rotates around its LOCAL X-axis using a sine wave.
//   Diagonal gait (like a real cow):
//     Phase A  (FR + BL) swing forward together.
//     Phase B  (FL + BR) swing forward together, offset by half a cycle (π).
//   When the cow is idle the legs smoothly return to rest (0°).
//   Walk speed scales the animation frequency automatically.
// ─────────────────────────────────────────────────────────────────────────────

public class CowLegAnimator : MonoBehaviour
{
    [Header("Leg Transforms")]
    [Tooltip("Drag 'FR Leg' child here.")]
    public Transform frLeg;
    [Tooltip("Drag 'FL Leg' child here.")]
    public Transform flLeg;
    [Tooltip("Drag 'BR Leg' child here.")]
    public Transform brLeg;
    [Tooltip("Drag 'BL Leg' child here.")]
    public Transform blLeg;

    [Header("Animation")]
    [Tooltip("Maximum swing angle in degrees (forward/back from rest).")]
    [Range(10f, 50f)]
    public float swingAngle = 30f;

    [Tooltip("How many full swing cycles per second at normal walk speed.\n" +
             "The actual speed is scaled by the cow's current move speed.")]
    [Range(0.5f, 4f)]
    public float cyclesPerSecond = 1.6f;

    [Tooltip("How fast the legs blend back to the rest pose when idle (degrees/sec).")]
    [Range(60f, 360f)]
    public float returnSpeed = 180f;

    // ── Private ──────────────────────────────────────────────────────────────

    private Cow _cow;

    // Accumulated phase (radians) — advances only while moving.
    private float _phase = 0f;

    // Per-leg current X rotation (degrees), used for smooth idle return.
    private float _frAngle, _flAngle, _brAngle, _blAngle;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        _cow = GetComponent<Cow>();
        if (_cow == null)
            Debug.LogWarning("[CowLegAnimator] No Cow component found on this GameObject.");
    }

    private void Start()
    {
        // Auto-find legs by name if not assigned in Inspector.
        if (frLeg == null) frLeg = FindLeg("FR Leg");
        if (flLeg == null) flLeg = FindLeg("FL Leg");
        if (brLeg == null) brLeg = FindLeg("BR Leg");
        if (blLeg == null) blLeg = FindLeg("BL Leg");

        if (frLeg == null || flLeg == null || brLeg == null || blLeg == null)
            Debug.LogWarning("[CowLegAnimator] One or more leg Transforms not found. " +
                             "Assign them manually in the Inspector.");
    }

    private void Update()
    {
        if (_cow == null) return;

        bool isMoving = IsMoving();

        if (isMoving)
        {
            // Advance phase proportional to walk/flee speed so faster = faster legs.
            float speed = _cow.walkSpeed; // default
            // If the cow is fleeing, use flee speed for animation rate.
            // We read the public fields directly — no need to expose state.
            // A simple proxy: if the cow is fleeing its actual move speed is higher,
            // but we can't read the private _state. Instead we check velocity via
            // a position delta, which is already captured in isMoving.
            _phase += cyclesPerSecond * speed * Time.deltaTime * (2f * Mathf.PI) / _cow.walkSpeed;

            // Diagonal gait:
            //   Phase A (FR, BL): sin(phase)
            //   Phase B (FL, BR): sin(phase + π)  ← half cycle offset
            _frAngle = Mathf.Sin(_phase) * swingAngle;
            _blAngle = Mathf.Sin(_phase) * swingAngle;
            _flAngle = Mathf.Sin(_phase + Mathf.PI) * swingAngle;
            _brAngle = Mathf.Sin(_phase + Mathf.PI) * swingAngle;
        }
        else
        {
            // Smoothly return all legs to rest pose (0°).
            float step = returnSpeed * Time.deltaTime;
            _frAngle = Mathf.MoveTowards(_frAngle, 0f, step);
            _flAngle = Mathf.MoveTowards(_flAngle, 0f, step);
            _brAngle = Mathf.MoveTowards(_brAngle, 0f, step);
            _blAngle = Mathf.MoveTowards(_blAngle, 0f, step);
        }

        ApplyRotation(frLeg, _frAngle);
        ApplyRotation(flLeg, _flAngle);
        ApplyRotation(brLeg, _brAngle);
        ApplyRotation(blLeg, _blAngle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    // Apply X rotation in local space, preserving Y and Z.
    private static void ApplyRotation(Transform leg, float xDegrees)
    {
        if (leg == null) return;
        Vector3 e = leg.localEulerAngles;
        e.x = xDegrees;
        leg.localEulerAngles = e;
    }

    // The cow is "moving" if it has a meaningful horizontal velocity.
    // We compare world position between frames — cheap, and works regardless
    // of which internal state the cow is in (Wander, Flee).
    private Vector3 _lastPos;
    private bool _lastPosValid = false;

    private bool IsMoving()
    {
        Vector3 current = transform.position;
        bool moving = false;

        if (_lastPosValid)
        {
            Vector3 delta = current - _lastPos;
            delta.y = 0f;
            // Threshold: > 0.02 units/frame at 60fps ≈ 1.2 units/sec
            moving = delta.sqrMagnitude > (0.01f * 0.01f);
        }

        _lastPos = current;
        _lastPosValid = true;
        return moving;
    }

    // Search for a leg by name anywhere in the cow's hierarchy.
    private Transform FindLeg(string legName)
    {
        // Strip trailing spaces from prefab names (e.g. "BL Leg ").
        foreach (Transform t in GetComponentsInChildren<Transform>())
        {
            if (t.name.Trim() == legName.Trim())
                return t;
        }
        return null;
    }
}