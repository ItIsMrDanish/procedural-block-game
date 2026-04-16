using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles player health and hunger.
/// Attach this component to the same GameObject as Player.cs.
///
/// Inspector wiring required:
///   - healthSlider        : UI Slider (max = 20) for health bar
///   - hungerSlider        : UI Slider (max = 20) for hunger bar
///   - deathScreenCanvas   : Canvas to show on death / hide on respawn
///   - hungerDrainInterval : Seconds between each hunger tick (default 30)
///   - fallDamageThreshold : Minimum fall distance (units) before damage (default 3)
/// </summary>
public class HealthAndHunger : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Stats")]
    public int maxHealth = 20;
    public int maxHunger = 20;

    [Header("UI")]
    public Slider healthSlider;
    public Slider hungerSlider;
    public Canvas deathScreenCanvas;

    [Header("Hunger")]
    [Tooltip("Seconds between each -1 hunger tick.")]
    public float hungerDrainInterval = 30f;

    [Header("Fall Damage")]
    [Tooltip("Minimum fall distance (units) before any damage is dealt.")]
    public float fallDamageThreshold = 4f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private int _currentHealth;
    private int _currentHunger;

    private Player _player;          // Reference to sibling Player component
    private bool _isDead = false;

    // Fall damage tracking
    private float _fallStartY;
    private bool _wasFalling = false;

    // Coroutine handles so we can stop/start cleanly
    private Coroutine _hungerCoroutine;
    private Coroutine _starvationCoroutine;
    private Coroutine _regenCoroutine;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _player = GetComponent<Player>();

        if (_player == null)
            Debug.LogError("[PlayerHealth] No Player component found on this GameObject!");
    }

    private void Start()
    {
        InitStats();

        // Hide death screen at game start
        if (deathScreenCanvas != null)
            deathScreenCanvas.gameObject.SetActive(false);

        // Kick off the passive hunger drain loop
        _hungerCoroutine = StartCoroutine(HungerDrainLoop());
    }

    private void Update()
    {
        if (_isDead) return;

        TrackFall();
    }

    // -------------------------------------------------------------------------
    // Initialisation / Respawn
    // -------------------------------------------------------------------------

    private void InitStats()
    {
        _currentHealth = maxHealth;
        _currentHunger = maxHunger;

        RefreshHealthUI();
        RefreshHungerUI();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Called by a UI Button to respawn the player.</summary>
    public void Respawn()
    {
        _isDead = false;

        // Reset stats
        InitStats();

        // Hide death screen, re-enable movement
        if (deathScreenCanvas != null)
            deathScreenCanvas.gameObject.SetActive(false);

        SetMovementEnabled(true);

        // Restart all passive loops
        StopAllPassiveCoroutines();
        _hungerCoroutine = StartCoroutine(HungerDrainLoop());

        // Unlock cursor (in case it was unlocked by death)
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void MainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    /// <summary>Deal damage from any external source (e.g. lava, mobs).</summary>
    public void TakeDamage(int amount)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Max(0, _currentHealth - amount);
        RefreshHealthUI();

        if (_currentHealth <= 0)
            Die();
    }

    /// <summary>Restore health directly (e.g. potions).</summary>
    public void Heal(int amount)
    {
        if (_isDead) return;

        _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
        RefreshHealthUI();
    }

    // -------------------------------------------------------------------------
    // Death
    // -------------------------------------------------------------------------

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;

        SetMovementEnabled(false);
        StopAllPassiveCoroutines();

        if (deathScreenCanvas != null)
            deathScreenCanvas.gameObject.SetActive(true);

        // Unlock and show cursor so the player can click the respawn button
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // -------------------------------------------------------------------------
    // Fall damage
    // -------------------------------------------------------------------------

    private void TrackFall()
    {
        // Player.isGrounded is a public field on Player.cs
        bool isGrounded = _player.isGrounded;

        if (!isGrounded)
        {
            if (!_wasFalling)
            {
                // Just left the ground — record take-off / drop height
                _fallStartY = transform.position.y;
                _wasFalling = true;
            }
        }
        else
        {
            if (_wasFalling)
            {
                // Just landed
                float fallDistance = _fallStartY - transform.position.y;

                if (fallDistance > fallDamageThreshold)
                {
                    // Each unit beyond the threshold = 1 damage point
                    int damage = Mathf.FloorToInt(fallDistance - fallDamageThreshold);
                    if (damage > 0)
                        TakeDamage(damage);
                }

                _wasFalling = false;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Hunger drain loop
    // -------------------------------------------------------------------------

    private IEnumerator HungerDrainLoop()
    {
        while (!_isDead)
        {
            yield return new WaitForSeconds(hungerDrainInterval);

            if (_isDead) yield break;

            _currentHunger = Mathf.Max(0, _currentHunger - 1);
            RefreshHungerUI();

            // When hunger hits 0, start starvation damage (if not already running)
            if (_currentHunger == 0 && _starvationCoroutine == null)
                _starvationCoroutine = StartCoroutine(StarvationLoop());

            // When hunger rises above 0 again (e.g. eating), stop starvation
            // (Handled in a hypothetical Eat() method — see below)

            // Health regen: >10 hunger and not at full health
            UpdateRegenState();
        }
    }

    // -------------------------------------------------------------------------
    // Starvation (0 hunger → -1 hp every 3 s)
    // -------------------------------------------------------------------------

    private IEnumerator StarvationLoop()
    {
        while (_currentHunger == 0 && !_isDead)
        {
            yield return new WaitForSeconds(3f);

            if (_isDead || _currentHunger > 0) break;

            TakeDamage(1);
        }

        _starvationCoroutine = null;
    }

    // -------------------------------------------------------------------------
    // Health regeneration (+1 hp every 3 s when hunger > 10 and not full)
    // -------------------------------------------------------------------------

    private void UpdateRegenState()
    {
        bool shouldRegen = _currentHunger > 10 && _currentHealth < maxHealth && !_isDead;

        if (shouldRegen && _regenCoroutine == null)
            _regenCoroutine = StartCoroutine(RegenLoop());
        else if (!shouldRegen && _regenCoroutine != null)
        {
            StopCoroutine(_regenCoroutine);
            _regenCoroutine = null;
        }
    }

    private IEnumerator RegenLoop()
    {
        while (_currentHunger > 10 && _currentHealth < maxHealth && !_isDead)
        {
            yield return new WaitForSeconds(2f);

            if (_isDead) break;

            if (_currentHunger > 10 && _currentHealth < maxHealth)
            {
                Heal(1);
            }
            else
            {
                break; // Conditions no longer met — exit and null the handle
            }
        }

        _regenCoroutine = null;
    }

    // -------------------------------------------------------------------------
    // Eating (example helper — wire to food items as needed)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Feed the player. Restores hunger and re-evaluates regen / starvation.
    /// Call this from your food/inventory system.
    /// </summary>
    public void Eat(int hungerRestored)
    {
        if (_isDead) return;

        _currentHunger = Mathf.Min(maxHunger, _currentHunger + hungerRestored);
        RefreshHungerUI();

        // Stop starvation if hunger is above 0 now
        if (_currentHunger > 0 && _starvationCoroutine != null)
        {
            StopCoroutine(_starvationCoroutine);
            _starvationCoroutine = null;
        }

        // Re-evaluate regen
        UpdateRegenState();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetMovementEnabled(bool enabled)
    {
        if (_player != null)
        {
            // The Player component drives movement in FixedUpdate / Update.
            // Disabling the component stops all input handling and physics.
            _player.enabled = enabled;
        }
    }

    private void RefreshHealthUI()
    {
        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHealth;
            healthSlider.value = _currentHealth;
        }
    }

    private void RefreshHungerUI()
    {
        if (hungerSlider != null)
        {
            hungerSlider.maxValue = maxHunger;
            hungerSlider.value = _currentHunger;
        }
    }

    private void StopAllPassiveCoroutines()
    {
        if (_hungerCoroutine != null)   { StopCoroutine(_hungerCoroutine);    _hungerCoroutine    = null; }
        if (_starvationCoroutine != null){ StopCoroutine(_starvationCoroutine); _starvationCoroutine = null; }
        if (_regenCoroutine != null)    { StopCoroutine(_regenCoroutine);     _regenCoroutine     = null; }
    }

    // -------------------------------------------------------------------------
    // Gizmos / debug (editor only)
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 200, 60));
        GUILayout.Label($"HP: {_currentHealth} / {maxHealth}");
        GUILayout.Label($"Hunger: {_currentHunger} / {maxHunger}");
        GUILayout.EndArea();
    }
#endif
}
