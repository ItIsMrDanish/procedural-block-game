using UnityEngine;

/// <summary>
/// A physical item entity dropped into the world when a block is broken.
///
/// BEHAVIOUR
/// ─────────
///  • Spawned by ItemDropManager.SpawnDrop() at the block's centre.
///  • Bobs and rotates visually so it's easy to spot.
///  • After a short spawn delay, magnetically attracts toward the player.
///  • Auto-collects (adds to Inventory) once within pickupRadius.
///  • Displays as a small cube using the block's icon sprite, or falls back
///    to a plain coloured cube if no icon is assigned.
///
/// SETUP (ItemDropManager handles this automatically)
/// ──────
///  Add this component to any GameObject that should be a pickup.
///  Call Init() immediately after AddComponent to configure it.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroppedItem : MonoBehaviour
{
    // ──────────────────── tunables ────────────────────────────────────────────

    [Tooltip("Radius at which the item is collected into the inventory.")]
    public float pickupRadius = 1.2f;

    [Tooltip("Radius at which the item starts magnetically moving toward the player.")]
    public float attractRadius = 4f;

    [Tooltip("Speed at which the item flies toward the player once attracted.")]
    public float attractSpeed = 8f;

    [Tooltip("Seconds after spawning before the item can be collected (prevents instant pickup).")]
    public float pickupDelay = 0.5f;

    [Tooltip("How high the item bobs (world units).")]
    public float bobAmplitude = 0.15f;

    [Tooltip("How fast the item bobs (cycles per second).")]
    public float bobFrequency = 1.5f;

    [Tooltip("Spin speed in degrees per second.")]
    public float spinSpeed = 90f;

    // ──────────────────── private state ───────────────────────────────────────

    private string _itemName;
    private int _amount;
    private Sprite _icon;

    private Inventory _inventory;
    private Transform _player;

    private Rigidbody _rb;
    private float _spawnTime;
    private Vector3 _baseY;          // tracks vertical bob origin
    private bool _attracted;      // true once within attractRadius

    // ──────────────────── public API ──────────────────────────────────────────

    /// <summary>
    /// Configure this DroppedItem immediately after AddComponent.
    /// </summary>
    public void Init(string itemName, int amount, Sprite icon, Inventory inventory, Transform player)
    {
        _itemName = itemName;
        _amount = amount;
        _icon = icon;
        _inventory = inventory;
        _player = player;

        _spawnTime = Time.time;
        _baseY = transform.position;
        _attracted = false;
    }

    // ──────────────────── Unity lifecycle ─────────────────────────────────────

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
        _rb.linearDamping = 2f;
    }

    private void Update()
    {
        if (_player == null || _inventory == null) return;

        float dist = Vector3.Distance(transform.position, _player.position);
        bool canPickup = Time.time >= _spawnTime + pickupDelay;

        // ── Attract toward player ─────────────────────────────────────────────
        if (canPickup && dist <= attractRadius)
        {
            _attracted = true;
            _rb.isKinematic = true;  // disable physics while flying toward player

            Vector3 dir = (_player.position + Vector3.up * 0.8f - transform.position).normalized;
            transform.position += dir * attractSpeed * Time.deltaTime;
        }

        // ── Collect ───────────────────────────────────────────────────────────
        if (canPickup && dist <= pickupRadius)
        {
            _inventory.AddItem(_itemName, _amount, _icon);
            Destroy(gameObject);
            return;
        }

        // ── Bob & spin (only while not attracted) ────────────────────────────
        if (!_attracted)
        {
            float bob = Mathf.Sin((Time.time - _spawnTime) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
            _baseY.y += (_rb.isKinematic ? 0 : 0); // bob origin tracks ground lazily
            Vector3 pos = transform.position;
            // Only adjust Y if we are resting (small vertical velocity)
            if (Mathf.Abs(_rb.linearVelocity.y) < 0.5f)
            {
                // snap to a stable base so bob doesn't drift with gravity
                if (!_rb.isKinematic)
                    _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z);
            }
        }

        // Spin always
        transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
    }
}