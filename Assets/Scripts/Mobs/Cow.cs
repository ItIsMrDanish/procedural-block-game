using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Cow — passive mob
//
// Setup in Unity:
//   1. Create a GameObject with a visible mesh (placeholder cube works fine).
//   2. Attach this component.
//   3. Set the 'world' reference to your World GameObject in the Inspector.
//   4. Optionally assign a 'dropPrefab' for item drops on death.
//
// The cow uses the same voxel AABB collision style as Player.cs (no Rigidbody),
// so it integrates cleanly with the existing world.
//
// States:
//   Idle     → stands still for a random duration, then picks a wander target.
//   Wander   → walks toward a random nearby position.
//   Flee     → sprints away from the player when hit or when player is very close.
//   Dead     → plays a brief tip-over tween, then destroys the GameObject.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider))]
public class Cow : MonoBehaviour, IMob
{

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Assign the World GameObject (the one with the World component).")]
    public World world;

    [Tooltip("Optional item GameObject spawned when the cow dies.")]
    public GameObject dropPrefab;

    [Range(1, 3)]
    [Tooltip("How many drop items to spawn.")]
    public int dropCount = 2;

    [Header("Stats")]
    public int maxHealth = 10;

    [Header("Body (AABB)")]
    public float mobHeight = 1.4f;  // Full height of the cow's collision box
    public float mobWidth = 0.4f;  // Half-width (same convention as Player.cs)

    [Header("Movement")]
    public float walkSpeed = 2.2f;
    public float fleeSpeed = 5.0f;
    public float gravity = -18f;
    public float turnSpeed = 6f;   // How fast the cow rotates toward its target

    [Header("Wander")]
    [Tooltip("Radius around the cow's current position to pick a random wander destination.")]
    public float wanderRadius = 12f;
    public float minIdleTime = 3f;
    public float maxIdleTime = 8f;
    public float arrivedRadius = 0.6f; // Distance threshold to consider destination reached

    [Header("Flee")]
    [Tooltip("Player within this distance triggers flee behaviour even without being hit.")]
    public float fleeDetectRadius = 3.5f;
    [Tooltip("Cow stops fleeing once the player is beyond this distance.")]
    public float fleeStopRadius = 10f;
    public float fleeDuration = 4f; // Seconds to keep fleeing after being hit

    [Header("Mesh Alignment")]
    [Tooltip("Child Transform that holds the visible mesh. If assigned, its local Y is shifted so\n" +
             "the mesh sits on the surface rather than halfway through the ground.\n" +
             "Leave empty if your mesh root is already at foot-level (pivot at bottom).")]
    public Transform meshRoot;

    [Tooltip("Vertical offset applied to meshRoot so the visible mesh aligns with the AABB foot.\n" +
             "For a default Unity cube (pivot at centre): set to mobHeight * 0.5 (e.g. 0.7 for 1.4 height).\n" +
             "For a model with pivot already at the foot: set to 0.")]
    public float meshVerticalOffset = 0.7f;   // half of default mobHeight 1.4

    [Header("Hit Flash")]
    [Tooltip("Renderer on the cow mesh — turns red briefly when hit.")]
    public Renderer meshRenderer;
    public Color hitColor = Color.red;
    public float hitFlashTime = 0.15f;

    // ── Private state ────────────────────────────────────────────────────────

    private int _currentHealth;
    private bool _isDead;

    private enum State { Idle, Wander, Flee }
    private State _state = State.Idle;

    // Movement
    private Vector3 _wanderTarget;
    private float _verticalVelocity;
    private bool _isGrounded;

    // Timers
    private float _idleTimer;
    private float _fleeTimer;

    // Visuals
    private Color _originalColor;
    private Coroutine _flashCoroutine;
    private Coroutine _deathCoroutine;

    // Cached player transform (found once)
    private Transform _playerTransform;

    // ── Sound timers ──────────────────────────────────────────────────────────
    private float _footstepTimer = 0f;
    private float _mooTimer = 0f;
    private const float CowFootstepInterval = 0.52f;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {

        _currentHealth = maxHealth;

        // Cache the player — assumes the player has the "Player" tag.
        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
            _playerTransform = playerGO.transform;
        else
            Debug.LogWarning("[Cow] No GameObject tagged 'Player' found. Flee behaviour disabled.");

        if (meshRenderer != null)
            _originalColor = meshRenderer.material.color;

        // Fall back: try to find World if not assigned
        if (world == null)
        {
            GameObject wGO = GameObject.Find("World");
            if (wGO != null) world = wGO.GetComponent<World>();
        }

        // ── Mesh alignment ────────────────────────────────────────────────
        // transform.position is the FOOT of the cow (used by all collision code).
        // If the visible mesh pivot is at its centre (default Unity primitives),
        // the cow will appear half-submerged. Fix by shifting the mesh child up.
        //
        //  • meshRoot assigned → only the visual child moves up; AABB stays correct.
        //  • meshRoot not assigned → nothing extra needed because the CowSpawner
        //    already places transform.position at surfaceY + 1 (top of surface block).
        if (meshRoot != null)
        {
            meshRoot.localPosition = new Vector3(
                meshRoot.localPosition.x,
                meshVerticalOffset,
                meshRoot.localPosition.z);
        }

        StartIdleTimer();
        _mooTimer = Random.Range(15f, 40f); // first moo after 15-40 s
    }

    private void Update()
    {

        if (_isDead) return;

        UpdateState();
        ApplyMovement();
        FaceMovementDirection();
        UpdateSounds();
    }

    // ── Sound ────────────────────────────────────────────────────────────────

    private void UpdateSounds()
    {
        if (SoundManager.Instance == null) return;

        bool isMoving = _state == State.Wander || _state == State.Flee;

        // Footsteps
        if (_isGrounded && isMoving)
        {
            float interval = _state == State.Flee
                ? CowFootstepInterval * 0.6f
                : CowFootstepInterval;
            _footstepTimer -= Time.deltaTime;
            if (_footstepTimer <= 0f)
            {
                SoundManager.Instance.PlayCowFootstep();
                _footstepTimer = interval;
            }
        }
        else
        {
            _footstepTimer = 0f;
        }

        // Periodic moo
        _mooTimer -= Time.deltaTime;
        if (_mooTimer <= 0f)
        {
            SoundManager.Instance.PlayCowMoo();
            _mooTimer = Random.Range(15f, 45f);
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Deal damage to this cow. Called by MobHitbox when the player attacks.</summary>
    public void TakeDamage(int amount)
    {

        if (_isDead) return;

        _currentHealth = Mathf.Max(0, _currentHealth - amount);

        if (SoundManager.Instance != null) SoundManager.Instance.PlayCowHurt();

        // Flash red
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        _flashCoroutine = StartCoroutine(HitFlash());

        // Enter flee state immediately on hit
        EnterFlee();

        if (_currentHealth <= 0)
            Die();
    }

    // ── State machine ────────────────────────────────────────────────────────

    private void UpdateState()
    {

        // Proximity-based flee overrides idle/wander (but not already-fleeing)
        if (_state != State.Flee && _playerTransform != null)
        {

            float dist = Vector3.Distance(transform.position, _playerTransform.position);
            if (dist < fleeDetectRadius)
                EnterFlee();
        }

        switch (_state)
        {

            case State.Idle:
                _idleTimer -= Time.deltaTime;
                if (_idleTimer <= 0f)
                    PickWanderTarget();
                break;

            case State.Wander:
                // Arrived?
                Vector3 flat = _wanderTarget - transform.position;
                flat.y = 0f;
                if (flat.magnitude < arrivedRadius)
                    StartIdleTimer();
                break;

            case State.Flee:
                _fleeTimer -= Time.deltaTime;
                if (_fleeTimer <= 0f)
                {

                    // Also stop if player is far enough away
                    if (_playerTransform == null ||
                        Vector3.Distance(transform.position, _playerTransform.position) > fleeStopRadius)
                        StartIdleTimer();
                    else
                        _fleeTimer = 0.5f; // Re-check in half a second
                }
                break;
        }
    }

    private void StartIdleTimer()
    {

        _state = State.Idle;
        _idleTimer = Random.Range(minIdleTime, maxIdleTime);
    }

    private void PickWanderTarget()
    {

        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        _wanderTarget = transform.position + new Vector3(offset.x, 0f, offset.y);
        _state = State.Wander;
    }

    private void EnterFlee()
    {

        _state = State.Flee;
        _fleeTimer = fleeDuration;
    }

    // ── Movement & physics ───────────────────────────────────────────────────

    private void ApplyMovement()
    {

        float dt = Time.deltaTime;

        // Gravity
        if (_isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        else
            _verticalVelocity += gravity * dt;

        _verticalVelocity = Mathf.Max(_verticalVelocity, gravity * 2f); // Terminal velocity

        // Horizontal wish direction
        Vector3 moveDir = Vector3.zero;

        if (_state == State.Wander)
        {

            Vector3 toTarget = _wanderTarget - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f)
                moveDir = toTarget.normalized;

        }
        else if (_state == State.Flee && _playerTransform != null)
        {

            Vector3 awayFromPlayer = transform.position - _playerTransform.position;
            awayFromPlayer.y = 0f;
            if (awayFromPlayer.sqrMagnitude > 0.01f)
                moveDir = awayFromPlayer.normalized;
        }

        float speed = (_state == State.Flee) ? fleeSpeed : walkSpeed;

        Vector3 delta = new Vector3(
            moveDir.x * speed * dt,
            _verticalVelocity * dt,
            moveDir.z * speed * dt
        );

        delta = ResolveCollisions(delta);
        transform.Translate(delta, Space.World);
    }

    private void FaceMovementDirection()
    {

        Vector3 moveDir = Vector3.zero;

        if (_state == State.Wander)
        {
            moveDir = (_wanderTarget - transform.position);
        }
        else if (_state == State.Flee && _playerTransform != null)
        {
            moveDir = (transform.position - _playerTransform.position);
        }

        moveDir.y = 0f;

        if (moveDir.sqrMagnitude > 0.01f)
        {

            Quaternion targetRot = Quaternion.LookRotation(moveDir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }
    }

    // ── Voxel AABB collision (mirrors Player.cs style) ───────────────────────

    private Vector3 ResolveCollisions(Vector3 delta)
    {

        // Y first
        if (delta.y < 0f)
        {

            float resolved = CheckDownSpeed(delta.y);
            if (resolved == 0f)
            {
                _verticalVelocity = 0f;
                _isGrounded = true;
            }
            else
            {
                _isGrounded = false;
            }
            delta.y = resolved;

        }
        else if (delta.y > 0f)
        {

            if (CheckUpSpeed(delta.y) == 0f)
            {
                _verticalVelocity = 0f;
                delta.y = 0f;
            }
        }

        if (delta.x != 0f && CheckSideX(delta.x)) delta.x = 0f;
        if (delta.z != 0f && CheckSideZ(delta.z)) delta.z = 0f;

        return delta;
    }

    private float CheckDownSpeed(float dy)
    {

        float targetY = transform.position.y + dy;
        float px = transform.position.x;
        float pz = transform.position.z;
        float w = mobWidth;

        if (CheckVoxel(new Vector3(px - w, targetY, pz - w))) { _isGrounded = true; return 0f; }
        if (CheckVoxel(new Vector3(px + w, targetY, pz - w))) { _isGrounded = true; return 0f; }
        if (CheckVoxel(new Vector3(px + w, targetY, pz + w))) { _isGrounded = true; return 0f; }
        if (CheckVoxel(new Vector3(px - w, targetY, pz + w))) { _isGrounded = true; return 0f; }

        _isGrounded = false;
        return dy;
    }

    private float CheckUpSpeed(float dy)
    {

        float targetY = transform.position.y + mobHeight + dy;
        float px = transform.position.x;
        float pz = transform.position.z;
        float w = mobWidth;

        if (CheckVoxel(new Vector3(px - w, targetY, pz - w))) return 0f;
        if (CheckVoxel(new Vector3(px + w, targetY, pz - w))) return 0f;
        if (CheckVoxel(new Vector3(px + w, targetY, pz + w))) return 0f;
        if (CheckVoxel(new Vector3(px - w, targetY, pz + w))) return 0f;

        return dy;
    }

    private bool CheckSideX(float dx)
    {
        // Leading X edge after the move
        float edgeX = transform.position.x + Mathf.Sign(dx) * mobWidth + dx;
        float py = transform.position.y;
        float pz = transform.position.z;
        float w = mobWidth;

        // Three heights: feet, mid-body, just-below-head — mirrors Player.cs.
        // Two probes was enough to stop movement but left the visual mesh able
        // to clip into blocks at the un-probed mid region.
        for (int i = 0; i < 3; i++)
        {
            float h = py + (i == 0 ? 0.1f : i == 1 ? mobHeight * 0.5f : mobHeight - 0.1f);
            if (CheckVoxel(new Vector3(edgeX, h, pz - w))) return true;
            if (CheckVoxel(new Vector3(edgeX, h, pz + w))) return true;
        }
        return false;
    }

    private bool CheckSideZ(float dz)
    {
        // Leading Z edge after the move
        float edgeZ = transform.position.z + Mathf.Sign(dz) * mobWidth + dz;
        float py = transform.position.y;
        float px = transform.position.x;
        float w = mobWidth;

        for (int i = 0; i < 3; i++)
        {
            float h = py + (i == 0 ? 0.1f : i == 1 ? mobHeight * 0.5f : mobHeight - 0.1f);
            if (CheckVoxel(new Vector3(px - w, h, edgeZ))) return true;
            if (CheckVoxel(new Vector3(px + w, h, edgeZ))) return true;
        }
        return false;
    }

    private bool CheckVoxel(Vector3 pos)
    {

        if (world == null) return false;
        return world.CheckForVoxel(pos);
    }

    // ── Death ────────────────────────────────────────────────────────────────

    private void Die()
    {

        if (_isDead) return;
        _isDead = true;

        // Stop coroutines
        if (_flashCoroutine != null) { StopCoroutine(_flashCoroutine); _flashCoroutine = null; }

        // Spawn drops
        if (dropPrefab != null)
        {

            for (int i = 0; i < dropCount; i++)
            {

                Vector3 spawnPos = transform.position + Vector3.up * 0.5f
                                   + Random.insideUnitSphere * 0.4f;
                Instantiate(dropPrefab, spawnPos, Quaternion.identity);
            }
        }

        // Play tip-over animation then destroy
        _deathCoroutine = StartCoroutine(DeathAnim());
    }

    private IEnumerator DeathAnim()
    {

        // Tip onto its side over 0.5 s, then fade out over 0.5 s
        float elapsed = 0f;
        float tipTime = 0.5f;
        float fadeTime = 0.5f;

        Quaternion startRot = transform.rotation;
        Quaternion endRot = startRot * Quaternion.Euler(0f, 0f, 90f);

        while (elapsed < tipTime)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRot, endRot, elapsed / tipTime);
            yield return null;
        }

        // Fade out mesh
        if (meshRenderer != null)
        {

            Material mat = meshRenderer.material;
            Color col = mat.color;

            elapsed = 0f;
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                col.a = Mathf.Lerp(1f, 0f, elapsed / fadeTime);
                mat.color = col;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(fadeTime);
        }

        Destroy(gameObject);
    }

    // ── Hit flash ────────────────────────────────────────────────────────────

    private IEnumerator HitFlash()
    {

        if (meshRenderer == null) yield break;

        meshRenderer.material.color = hitColor;
        yield return new WaitForSeconds(hitFlashTime);

        if (!_isDead)
            meshRenderer.material.color = _originalColor;
    }

    // ── Editor Gizmos ────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {

        // Flee detection radius
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, fleeDetectRadius);

        // Flee stop radius
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, fleeStopRadius);

        // Wander target
        if (_state == State.Wander)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, _wanderTarget);
            Gizmos.DrawSphere(_wanderTarget, 0.3f);
        }

        // AABB
        Gizmos.color = Color.green;
        Vector3 center = transform.position + Vector3.up * mobHeight * 0.5f;
        Gizmos.DrawWireCube(center, new Vector3(mobWidth * 2, mobHeight, mobWidth * 2));
    }
#endif
}