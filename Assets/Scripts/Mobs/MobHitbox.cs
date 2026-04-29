using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// MobHitbox — attach to the SAME GameObject as Cow.cs (or a child collider).
//
// How it works:
//   Player.cs fires BreakBlock() on the Attack input action.
//   BreakBlock() does a ray march to find voxels — it doesn't hit mobs.
//   MobHitbox solves this by running its own raycast check every frame
//   using the player's camera forward direction.
//   When the player presses Attack and the ray hits THIS collider, TakeDamage()
//   is called on the Cow component.
//
// Setup:
//   1. Make sure the cow GameObject (or a child) has a Collider (Box/Capsule).
//   2. Attach MobHitbox to that same GameObject.
//   3. The component auto-locates the Player and Main Camera at Start.
//   4. Tune 'attackReach' and 'damagePerHit' in the Inspector.
//
// No changes to Player.cs are required.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider))]
public class MobHitbox : MonoBehaviour
{

    [Header("Attack Settings")]
    [Tooltip("Max distance (units) at which the player can hit this mob. Match Player.reach.")]
    public float attackReach = 8f;

    [Tooltip("Damage dealt per attack swing.")]
    public int damagePerHit = 2;

    [Tooltip("Minimum seconds between hits (prevents holding Attack = instant kill).")]
    public float attackCooldown = 0.5f;

    // ── Private ──────────────────────────────────────────────────────────────

    private Cow _cow;
    private Transform _cam;
    private Transform _playerTransform;
    private InputSystem _inputSystem;   // Same generated class used by Player.cs

    private float _cooldownTimer = 0f;
    private bool _attackPressed = false;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {

        _cow = GetComponent<Cow>();
        if (_cow == null)
            Debug.LogError("[MobHitbox] No Cow component found on this GameObject!");

        // Wire up the same Attack action Player.cs uses — InputSystem is
        // generated from the Input Action asset, so this just reads the same binding.
        _inputSystem = new InputSystem();
        _inputSystem.Player.Attack.performed += _ => _attackPressed = true;
    }

    private void OnEnable() => _inputSystem.Enable();
    private void OnDisable() => _inputSystem.Disable();

    private void Start()
    {

        _cam = GameObject.Find("Main Camera")?.transform;

        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null) _playerTransform = playerGO.transform;

        if (_cam == null)
            Debug.LogWarning("[MobHitbox] 'Main Camera' not found.");
    }

    private void Update()
    {

        _cooldownTimer -= Time.deltaTime;

        if (_attackPressed)
        {

            _attackPressed = false; // Consume the flag

            if (_cooldownTimer <= 0f && _cam != null)
            {
                TryHit();
            }
        }
    }

    // ── Hit detection ────────────────────────────────────────────────────────

    private void TryHit()
    {

        // Raycast from camera forward. LayerMask.GetMask("Default") or -1 for all.
        Ray ray = new Ray(_cam.position, _cam.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, attackReach))
        {

            // Did the ray hit THIS collider?
            if (hit.collider != null && hit.collider.gameObject == gameObject)
            {

                _cooldownTimer = attackCooldown;

                if (_cow != null)
                    _cow.TakeDamage(damagePerHit);
            }
        }
    }

    // ── Gizmo ────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {

        if (_cam == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(_cam.position, _cam.position + _cam.forward * attackReach);
    }
#endif
}