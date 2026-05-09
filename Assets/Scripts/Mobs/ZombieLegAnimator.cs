using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// ZombieLegAnimator — two-leg walk animation + optional arm raise.
//
// Setup:
//   1. Attach to the root Zombie GameObject alongside Zombie.cs.
//   2. Drag the two leg Transforms into the Inspector slots (or name them
//      "L Leg" and "R Leg" and they'll be found automatically).
//   3. Optionally drag arm Transforms for the classic zombie raise.
// ─────────────────────────────────────────────────────────────────────────────

public class ZombieLegAnimator : MonoBehaviour
{
    [Header("Leg Transforms")]
    public Transform lLeg;
    public Transform rLeg;

    [Header("Arm Transforms (optional)")]
    public Transform lArm;
    public Transform rArm;

    [Header("Leg Animation")]
    [Range(10f, 50f)]  public float legSwingAngle  = 28f;
    [Range(0.5f, 4f)]  public float cyclesPerSecond = 1.4f;
    [Range(60f, 360f)] public float returnSpeed     = 160f;

    [Header("Arm Animation")]
    [Tooltip("Resting forward raise angle (0 = down, 90 = straight forward).")]
    [Range(0f, 90f)]  public float armBaseAngle  = 60f;
    [Tooltip("Additional swing on top of the base angle while walking.")]
    [Range(0f, 30f)]  public float armSwingAngle = 12f;

    // ── Private ───────────────────────────────────────────────────────────────

    private float _phase;
    private float _lLegAngle, _rLegAngle;
    private float _lArmAngle, _rArmAngle;

    private Vector3 _lastPos;
    private bool    _lastPosValid;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        if (lLeg == null) lLeg = FindChild("L Leg");
        if (rLeg == null) rLeg = FindChild("R Leg");
        if (lArm == null) lArm = FindChild("L Arm");
        if (rArm == null) rArm = FindChild("R Arm");
    }

    private void Update()
    {
        bool moving = IsMoving();

        if (moving)
        {
            _phase += cyclesPerSecond * Time.deltaTime * (2f * Mathf.PI);

            // Legs alternate: L forward when R is back and vice versa
            _lLegAngle =  Mathf.Sin(_phase)            * legSwingAngle;
            _rLegAngle =  Mathf.Sin(_phase + Mathf.PI) * legSwingAngle;

            // Arms swing opposite to the leg on their side (counter-phase)
            _lArmAngle = armBaseAngle + Mathf.Sin(_phase + Mathf.PI) * armSwingAngle;
            _rArmAngle = armBaseAngle + Mathf.Sin(_phase)            * armSwingAngle;
        }
        else
        {
            float step = returnSpeed * Time.deltaTime;
            _lLegAngle = Mathf.MoveTowards(_lLegAngle, 0f,          step);
            _rLegAngle = Mathf.MoveTowards(_rLegAngle, 0f,          step);
            _lArmAngle = Mathf.MoveTowards(_lArmAngle, armBaseAngle, step);
            _rArmAngle = Mathf.MoveTowards(_rArmAngle, armBaseAngle, step);
        }

        ApplyX(lLeg, _lLegAngle);
        ApplyX(rLeg, _rLegAngle);
        ApplyX(lArm, _lArmAngle);
        ApplyX(rArm, _rArmAngle);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyX(Transform t, float xDeg)
    {
        if (t == null) return;
        Vector3 e = t.localEulerAngles;
        e.x = xDeg;
        t.localEulerAngles = e;
    }

    private bool IsMoving()
    {
        Vector3 cur = transform.position;
        bool moving = false;
        if (_lastPosValid)
        {
            Vector3 d = cur - _lastPos; d.y = 0f;
            moving = d.sqrMagnitude > (0.01f * 0.01f);
        }
        _lastPos = cur;
        _lastPosValid = true;
        return moving;
    }

    private Transform FindChild(string childName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>())
            if (t.name.Trim() == childName.Trim()) return t;
        return null;
    }
}