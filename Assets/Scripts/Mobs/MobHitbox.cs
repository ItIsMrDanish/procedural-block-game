using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// MobHitbox — attach to the root mob GameObject (Cow, Zombie, or any future mob).
//
// Previously hard-coded to Cow. Now uses the IMob interface so it works on any
// mob that implements TakeDamage(int). No other files need to change.
//
// Setup is identical to before:
//   1. Put a Collider on the root (or a child) of the mob prefab.
//   2. Attach MobHitbox to the ROOT GameObject (where Cow.cs / Zombie.cs lives).
//   3. Assign the 'Mob' layer to the collider to prevent chunk meshes blocking the ray.
//   4. Tune attackReach and damagePerHit.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Any mob that can be hit by the player implements this interface.
/// Add it to Cow and Zombie (and future mobs) alongside their TakeDamage method.
/// </summary>
public interface IMob
{
    void TakeDamage(int amount);
}

[RequireComponent(typeof(Collider))]
public class MobHitbox : MonoBehaviour
{
    [Header("Attack Settings")]
    [Tooltip("Max distance (units) at which the player can hit this mob. Match Player.reach.")]
    public float attackReach = 8f;

    [Tooltip("Damage dealt per attack swing.")]
    public int damagePerHit = 2;

    [Tooltip("Minimum seconds between hits (prevents hold-to-insta-kill).")]
    public float attackCooldown = 0.5f;

    [Tooltip("Layer name the mob's Collider lives on.\n" +
             "Create a 'Mob' layer in Project Settings → Tags & Layers and put the\n" +
             "mob collider on it so voxel chunk meshes (Default layer) can't block the ray.")]
    public string mobLayer = "Mob";

    // ── Private ───────────────────────────────────────────────────────────────

    private IMob      _mob;          // Cow, Zombie, or any future IMob
    private Transform _mobRoot;      // root of the mob hierarchy (for hierarchy check)
    private Transform _cam;
    private InputSystem _inputSystem;

    private float _cooldownTimer  = 0f;
    private bool  _attackPressed  = false;
    private int   _layerMask      = -1;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        // Search this GameObject AND all parents for any IMob implementation.
        _mob     = GetComponentInParent<IMob>();
        _mobRoot = _mob != null ? ((MonoBehaviour)_mob).transform : transform;

        if (_mob == null)
            Debug.LogError("[MobHitbox] No IMob component (Cow / Zombie) found on this " +
                           "GameObject or any parent. Did you forget to add the interface?");

        if (!string.IsNullOrEmpty(mobLayer))
        {
            int layer = LayerMask.NameToLayer(mobLayer);
            if (layer == -1)
                Debug.LogWarning($"[MobHitbox] Layer '{mobLayer}' not found — " +
                                 "falling back to all layers.");
            else
                _layerMask = 1 << layer;
        }

        _inputSystem = new InputSystem();
        _inputSystem.Player.Attack.performed += _ => _attackPressed = true;
    }

    private void OnEnable()  => _inputSystem.Enable();
    private void OnDisable() => _inputSystem.Disable();

    private void Start()
    {
        _cam = GameObject.Find("Main Camera")?.transform;
        if (_cam == null)
            Debug.LogWarning("[MobHitbox] 'Main Camera' not found.");
    }

    private void Update()
    {
        _cooldownTimer -= Time.deltaTime;

        if (_attackPressed)
        {
            _attackPressed = false;
            if (_cooldownTimer <= 0f && _cam != null)
                TryHit();
        }
    }

    // ── Hit detection ─────────────────────────────────────────────────────────

    private void TryHit()
    {
        Ray ray = new Ray(_cam.position, _cam.forward);

        bool didHit = _layerMask == -1
            ? Physics.Raycast(ray, out RaycastHit hit, attackReach)
            : Physics.Raycast(ray, out hit, attackReach, _layerMask);

        if (!didHit || hit.collider == null) return;

        // Accept hit on root or any child in this mob's hierarchy.
        if (hit.collider.transform != _mobRoot &&
            !hit.collider.transform.IsChildOf(_mobRoot))
            return;

        _cooldownTimer = attackCooldown;
        _mob.TakeDamage(damagePerHit);
    }

    // ── Gizmo ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_cam == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(_cam.position, _cam.position + _cam.forward * attackReach);
    }
#endif
}