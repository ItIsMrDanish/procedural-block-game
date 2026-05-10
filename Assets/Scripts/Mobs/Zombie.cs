using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Zombie — hostile mob
//
// States:
//   Wander  → shuffles slowly in a random direction (same as Cow wander).
//   Chase   → sprints toward the player when detected within aggroRadius.
//   Attack  → within melee range; deals damage on an interval.
//   Burning → daylight is hitting the zombie; it takes burn damage and panics.
//   Dead    → tip-over + fade then Destroy.
//
// Sunlight:
//   When DayNightCycle.IsNight becomes false the zombie starts burning.
//   Burn damage ticks every burnDamageInterval seconds. This gives the player
//   a visible "caught-in-daylight" warning before it dies rather than
//   an instant despawn.
//
// Setup:
//   1. Duplicate your Cow prefab (or build a new one) and replace Cow with Zombie.
//   2. Attach MobHitbox to the same root GameObject (it calls TakeDamage()).
//   3. Attach ZombieLegAnimator (below) and wire up the leg Transforms.
//   4. Add the prefab to MonsterSpawnManager.monsterPrefabs[].
//
// No changes to Player.cs, MobHitbox.cs, or MonsterSpawnManager.cs required.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider))]
public class Zombie : MonoBehaviour, IMob
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("References")]
    public World world;

    [Tooltip("Optional item(s) dropped on death.")]
    public GameObject dropPrefab;
    [Range(0, 5)] public int dropCount = 1;

    [Header("Stats")]
    public int maxHealth = 20;

    [Header("Body (AABB) — match your mesh size")]
    public float mobHeight = 1.8f;
    public float mobWidth  = 0.4f;

    [Header("Movement")]
    public float wanderSpeed  = 1.2f;
    public float chaseSpeed   = 4.5f;
    public float burnRunSpeed = 6.0f;   // panicked sprint while on fire
    public float gravity      = -18f;
    public float turnSpeed    = 8f;

    [Header("Wander")]
    public float wanderRadius  = 10f;
    public float minIdleTime   = 2f;
    public float maxIdleTime   = 5f;
    public float arrivedRadius = 0.7f;

    [Header("Aggro")]
    [Tooltip("Player enters this radius → zombie starts chasing.")]
    public float aggroRadius = 14f;
    [Tooltip("Zombie stops chasing once player is beyond this distance.")]
    public float deaggroRadius = 22f;

    [Header("Melee Attack")]
    public float attackRange    = 1.5f;  // distance for a melee hit
    public int   meleeDamage    = 3;
    public float attackCooldown = 1.2f;  // seconds between hits

    [Header("Burn (sunlight damage)")]
    public float burnDamageInterval = 1.0f;  // seconds between burn ticks
    public int   burnDamagePerTick  = 4;

    [Header("Mesh / Visuals")]
    [Tooltip("Child Transform holding the visible mesh (pivot at feet or centre).")]
    public Transform meshRoot;
    [Tooltip("Vertical offset so mesh sits on ground. Set to mobHeight*0.5 for centre-pivot mesh.")]
    public float meshVerticalOffset = 0.9f;

    public Renderer meshRenderer;
    public Color hitColour   = Color.red;
    public Color burnColour  = new Color(1f, 0.4f, 0f);
    public float hitFlashTime = 0.12f;

    // ── Private state ──────────────────────────────────────────────────────────

    private int   _currentHealth;
    private bool  _isDead;
    private float _verticalVelocity;
    private bool  _isGrounded;

    private enum State { Idle, Wander, Chase, Attack, Burning }
    private State _state = State.Idle;

    private Vector3 _wanderTarget;
    private float   _idleTimer;
    private float   _attackCooldownTimer;
    private float   _burnTimer;
    private float   _burnRunDir; // random angle used while panicking

    private Color     _originalColour;
    private Coroutine _flashCoroutine;
    private Coroutine _burnFlickerCoroutine;

    private Transform      _playerTransform;
    private HealthAndHunger _playerHealth;   // to call TakeDamage on the player

    // ── Sound timers ──────────────────────────────────────────────────────────
    private float _footstepTimer = 0f;
    private float _groanTimer    = 0f;
    private const float ZombieFootstepInterval = 0.50f;

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    private void Start()
    {
        _currentHealth = maxHealth;

        if (world == null) world = World.Instance;

        GameObject playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
        {
            _playerTransform = playerGO.transform;
            _playerHealth    = playerGO.GetComponent<HealthAndHunger>();
        }

        if (meshRenderer != null)
            _originalColour = meshRenderer.material.color;

        if (meshRoot != null)
            meshRoot.localPosition = new Vector3(
                meshRoot.localPosition.x,
                meshVerticalOffset,
                meshRoot.localPosition.z);

        StartIdleTimer();
        _groanTimer = Random.Range(8f, 25f); // first groan after 8-25 s
    }

    private void Update()
    {
        if (_isDead) return;

        CheckSunburn();
        UpdateState();
        ApplyMovement();
        FaceDirection();
        UpdateSounds();
    }

    // ── Sound ──────────────────────────────────────────────────────────────────

    private void UpdateSounds()
    {
        if (SoundManager.Instance == null) return;

        bool isMoving = _state == State.Wander || _state == State.Chase || _state == State.Burning;

        // Footsteps — faster cadence when chasing / burning
        if (_isGrounded && isMoving)
        {
            float interval = (_state == State.Chase || _state == State.Burning)
                ? ZombieFootstepInterval * 0.55f
                : ZombieFootstepInterval;
            _footstepTimer -= Time.deltaTime;
            if (_footstepTimer <= 0f)
            {
                SoundManager.Instance.PlayZombieFootstep();
                _footstepTimer = interval;
            }
        }
        else
        {
            _footstepTimer = 0f;
        }

        // Periodic groan — more frequent while chasing
        _groanTimer -= Time.deltaTime;
        if (_groanTimer <= 0f)
        {
            SoundManager.Instance.PlayZombieGroan();
            _groanTimer = _state == State.Chase
                ? Random.Range(4f, 10f)
                : Random.Range(10f, 30f);
        }
    }

    // ── Public API (called by MobHitbox) ──────────────────────────────────────

    public void TakeDamage(int amount)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Max(0, _currentHealth - amount);

        if (SoundManager.Instance != null) SoundManager.Instance.PlayZombieHurt();

        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        _flashCoroutine = StartCoroutine(HitFlash());

        // Being hit aggroes the zombie immediately.
        if (_state != State.Burning)
            EnterChase();

        if (_currentHealth <= 0)
            Die(burnDeath: false);
    }

    // ── Sunburn check ──────────────────────────────────────────────────────────

    private void CheckSunburn()
    {
        bool isDay = !DayNightCycle.IsNight;

        if (isDay && _state != State.Burning && _state != State.Attack)
        {
            EnterBurning();
        }
        else if (!isDay && _state == State.Burning)
        {
            // Night returned (shouldn't normally happen but handle it gracefully)
            ExitBurning();
            EnterChase();
        }

        if (_state == State.Burning)
        {
            _burnTimer -= Time.deltaTime;
            if (_burnTimer <= 0f)
            {
                _burnTimer = burnDamageInterval;
                _currentHealth = Mathf.Max(0, _currentHealth - burnDamagePerTick);
                if (_currentHealth <= 0)
                    Die(burnDeath: true);
            }
        }
    }

    private void EnterBurning()
    {
        _state     = State.Burning;
        _burnTimer = burnDamageInterval;
        _burnRunDir = Random.Range(0f, 360f) * Mathf.Deg2Rad;

        if (_burnFlickerCoroutine != null) StopCoroutine(_burnFlickerCoroutine);
        _burnFlickerCoroutine = StartCoroutine(BurnFlicker());
    }

    private void ExitBurning()
    {
        if (_burnFlickerCoroutine != null)
        {
            StopCoroutine(_burnFlickerCoroutine);
            _burnFlickerCoroutine = null;
        }
        if (meshRenderer != null)
            meshRenderer.material.color = _originalColour;
    }

    // ── State machine ──────────────────────────────────────────────────────────

    private void UpdateState()
    {
        if (_state == State.Burning) return; // burning overrides everything

        float distToPlayer = _playerTransform != null
            ? Vector3.Distance(transform.position, _playerTransform.position)
            : float.MaxValue;

        // Aggro check from Idle / Wander
        if (_state != State.Chase && _state != State.Attack && distToPlayer < aggroRadius)
            EnterChase();

        switch (_state)
        {
            case State.Idle:
                _idleTimer -= Time.deltaTime;
                if (_idleTimer <= 0f) PickWanderTarget();
                break;

            case State.Wander:
                Vector3 flat = _wanderTarget - transform.position;
                flat.y = 0f;
                if (flat.magnitude < arrivedRadius) StartIdleTimer();
                break;

            case State.Chase:
                if (distToPlayer > deaggroRadius)
                {
                    StartIdleTimer();
                    break;
                }
                if (distToPlayer <= attackRange)
                    EnterAttack();
                break;

            case State.Attack:
                _attackCooldownTimer -= Time.deltaTime;

                // Re-enter chase if player backed away
                if (distToPlayer > attackRange * 1.5f)
                {
                    EnterChase();
                    break;
                }

                if (_attackCooldownTimer <= 0f)
                {
                    DoMeleeAttack();
                    _attackCooldownTimer = attackCooldown;
                }
                break;
        }
    }

    private void StartIdleTimer()
    {
        _state     = State.Idle;
        _idleTimer = Random.Range(minIdleTime, maxIdleTime);
    }

    private void PickWanderTarget()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        _wanderTarget  = transform.position + new Vector3(offset.x, 0f, offset.y);
        _state         = State.Wander;
    }

    private void EnterChase()
    {
        _state = State.Chase;
    }

    private void EnterAttack()
    {
        _state               = State.Attack;
        _attackCooldownTimer = 0f; // land first hit immediately
    }

    private void DoMeleeAttack()
    {
        if (_playerHealth != null)
            _playerHealth.TakeDamage(meleeDamage);
    }

    // ── Movement & physics ─────────────────────────────────────────────────────

    private void ApplyMovement()
    {
        float speed = CurrentSpeed();
        Vector3 moveDir = GetMoveDirection();

        // Gravity
        _verticalVelocity += gravity * Time.deltaTime;
        if (_isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;

        Vector3 delta = moveDir * (speed * Time.deltaTime);
        delta.y = _verticalVelocity * Time.deltaTime;

        delta = ResolveCollisions(delta);
        transform.position += delta;
    }

    private float CurrentSpeed()
    {
        return _state switch
        {
            State.Chase   => chaseSpeed,
            State.Attack  => 0f,
            State.Burning => burnRunSpeed,
            State.Wander  => wanderSpeed,
            _             => 0f,
        };
    }

    private Vector3 GetMoveDirection()
    {
        Vector3 dir = Vector3.zero;

        switch (_state)
        {
            case State.Chase when _playerTransform != null:
                dir = (_playerTransform.position - transform.position);
                dir.y = 0f;
                dir.Normalize();
                break;

            case State.Wander:
                dir = (_wanderTarget - transform.position);
                dir.y = 0f;
                if (dir.magnitude > 0.01f) dir.Normalize();
                break;

            case State.Burning:
                // Panicked random sprint — changes direction every few seconds
                dir = new Vector3(Mathf.Cos(_burnRunDir), 0f, Mathf.Sin(_burnRunDir));
                break;
        }

        return dir;
    }

    private void FaceDirection()
    {
        Vector3 dir = GetMoveDirection();
        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
    }

    // ── Voxel AABB collision (mirrors Cow.cs / Player.cs style) ───────────────

    private Vector3 ResolveCollisions(Vector3 delta)
    {
        if (delta.y < 0f)
        {
            float res = CheckDownSpeed(delta.y);
            if (res == 0f) { _verticalVelocity = 0f; _isGrounded = true; }
            else           { _isGrounded = false; }
            delta.y = res;
        }
        else if (delta.y > 0f)
        {
            if (CheckUpSpeed(delta.y) == 0f) { _verticalVelocity = 0f; delta.y = 0f; }
        }

        if (delta.x != 0f && CheckSideX(delta.x)) delta.x = 0f;
        if (delta.z != 0f && CheckSideZ(delta.z)) delta.z = 0f;

        return delta;
    }

    private float CheckDownSpeed(float dy)
    {
        float targetY = transform.position.y + dy;
        float px = transform.position.x, pz = transform.position.z, w = mobWidth;

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
        float px = transform.position.x, pz = transform.position.z, w = mobWidth;

        if (CheckVoxel(new Vector3(px - w, targetY, pz - w))) return 0f;
        if (CheckVoxel(new Vector3(px + w, targetY, pz - w))) return 0f;
        if (CheckVoxel(new Vector3(px + w, targetY, pz + w))) return 0f;
        if (CheckVoxel(new Vector3(px - w, targetY, pz + w))) return 0f;

        return dy;
    }

    private bool CheckSideX(float dx)
    {
        float edgeX = transform.position.x + Mathf.Sign(dx) * mobWidth + dx;
        float py = transform.position.y, pz = transform.position.z, w = mobWidth;
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
        float edgeZ = transform.position.z + Mathf.Sign(dz) * mobWidth + dz;
        float py = transform.position.y, px = transform.position.x, w = mobWidth;
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

    // ── Death ──────────────────────────────────────────────────────────────────

    private void Die(bool burnDeath)
    {
        if (_isDead) return;
        _isDead = true;

        if (_flashCoroutine != null)       { StopCoroutine(_flashCoroutine);       _flashCoroutine = null; }
        if (_burnFlickerCoroutine != null)  { StopCoroutine(_burnFlickerCoroutine); _burnFlickerCoroutine = null; }

        if (dropPrefab != null && !burnDeath)   // no drops if burned to death
        {
            for (int i = 0; i < dropCount; i++)
            {
                Vector3 p = transform.position + Vector3.up * 0.5f + Random.insideUnitSphere * 0.4f;
                Instantiate(dropPrefab, p, Quaternion.identity);
            }
        }

        StartCoroutine(DeathAnim(burnDeath));
    }

    private IEnumerator DeathAnim(bool burned)
    {
        float tipTime  = burned ? 0.0f : 0.5f;  // instant collapse if burned away
        float fadeTime = burned ? 0.3f : 0.6f;

        if (tipTime > 0f)
        {
            Quaternion startRot = transform.rotation;
            Quaternion endRot   = startRot * Quaternion.Euler(0f, 0f, 90f);
            float elapsed = 0f;
            while (elapsed < tipTime)
            {
                elapsed += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(startRot, endRot, elapsed / tipTime);
                yield return null;
            }
        }

        // Fade out
        if (meshRenderer != null)
        {
            Material mat = meshRenderer.material;
            Color col    = burned ? burnColour : _originalColour;
            float elapsed = 0f;
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

    // ── Visuals ────────────────────────────────────────────────────────────────

    private IEnumerator HitFlash()
    {
        if (meshRenderer == null) yield break;
        meshRenderer.material.color = hitColour;
        yield return new WaitForSeconds(hitFlashTime);
        if (!_isDead && _state != State.Burning)
            meshRenderer.material.color = _originalColour;
    }

    // Flicker between orange and original while burning
    private IEnumerator BurnFlicker()
    {
        if (meshRenderer == null) yield break;
        while (_state == State.Burning && !_isDead)
        {
            meshRenderer.material.color = burnColour;
            yield return new WaitForSeconds(0.1f);
            if (_state == State.Burning)
                meshRenderer.material.color = _originalColour;
            yield return new WaitForSeconds(0.1f);
        }
    }

    // ── Editor Gizmos ──────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Aggro radius
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);

        // Deaggro radius
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, deaggroRadius);

        // Attack range
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        // AABB
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(
            transform.position + Vector3.up * mobHeight * 0.5f,
            new Vector3(mobWidth * 2f, mobHeight, mobWidth * 2f));

        // Wander target
        if (_state == State.Wander)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, _wanderTarget);
            Gizmos.DrawSphere(_wanderTarget, 0.3f);
        }
    }
#endif
}