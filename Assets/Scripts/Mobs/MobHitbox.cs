using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// MobHitbox — attach to the root Cow GameObject OR any child collider object.
//
// How it works:
//   Player.cs fires BreakBlock() on the Attack input action.
//   BreakBlock() does a ray march to find voxels — it doesn't hit mobs.
//   MobHitbox solves this by listening to the same Attack action and doing its
//   own Physics.Raycast each frame the button is pressed.
//   When the ray hits ANY collider that belongs to this cow's hierarchy,
//   TakeDamage() is called on the Cow component.
//
// Setup:
//   1. Put the cow's Collider on the ROOT GameObject (recommended) or a child.
//   2. Attach MobHitbox to the ROOT Cow GameObject (where Cow.cs lives).
//   3. Give the cow's collider a dedicated layer, e.g. "Mob", so the raycast
//      is not blocked by voxel chunk meshes.
//      • In Edit → Project Settings → Physics, set "Mob" layer to collide with
//        Default (so player raycasts can still reach it).
//      • Set 'mobLayer' in the Inspector to "Mob".
//      If you don't use a dedicated layer, leave mobLayer = "Default" and
//      make sure the cow collider is on the Default layer — but note that
//      chunk meshes on Default can then block the ray.
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

    [Tooltip("Layer name the cow's Collider lives on.\n" +
             "Create a 'Mob' layer in Project Settings → Tags & Layers and put the\n" +
             "cow collider on it so voxel chunk meshes (Default layer) can't block the ray.\n" +
             "If you leave this blank the raycast hits all layers.")]
    public string mobLayer = "Mob";

    // ── Private ──────────────────────────────────────────────────────────────

    private Cow _cow;
    private Transform _cam;
    private Transform _playerTransform;
    private InputSystem _inputSystem;

    private float _cooldownTimer = 0f;
    private bool _attackPressed = false;
    private int _layerMask = -1;   // -1 = all layers; set from mobLayer in Awake

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Search this GameObject AND all parents so MobHitbox can live on a
        // child collider object while Cow.cs lives on the root.
        _cow = GetComponentInParent<Cow>();
        if (_cow == null)
            Debug.LogError("[MobHitbox] No Cow component found on this GameObject or any parent!");

        // Resolve layer mask. Using a dedicated "Mob" layer means voxel chunk
        // meshes (which sit on Default) can never block the attack ray.
        if (!string.IsNullOrEmpty(mobLayer))
        {
            int layer = LayerMask.NameToLayer(mobLayer);
            if (layer == -1)
                Debug.LogWarning($"[MobHitbox] Layer '{mobLayer}' not found — falling back to all layers. " +
                                 "Create a 'Mob' layer in Project Settings → Tags & Layers.");
            else
                _layerMask = 1 << layer;
        }

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
        Ray ray = new Ray(_cam.position, _cam.forward);

        // Use the mob layer mask so voxel chunk meshes (Default layer) don't
        // block the ray before it reaches the cow.
        bool didHit = _layerMask == -1
            ? Physics.Raycast(ray, out RaycastHit hit, attackReach)
            : Physics.Raycast(ray, out hit, attackReach, _layerMask);

        if (!didHit) return;
        if (hit.collider == null) return;

        // Accept a hit on the root OR any child collider that belongs to this
        // cow's hierarchy. This handles prefabs where the visible mesh (and its
        // collider) lives on a child GameObject rather than the root.
        if (hit.collider.transform != _cow.transform &&
            !hit.collider.transform.IsChildOf(_cow.transform))
            return;

        _cooldownTimer = attackCooldown;
        _cow.TakeDamage(damagePerHit);
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