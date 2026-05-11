using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// Displays the held item's icon in the bottom-right corner of the
/// screen with a Minecraft-style look: the icon is tilted on X/Y axes
/// and a dark offset copy sits behind it to sell the depth illusion.
public class HeldItemDisplay : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static HeldItemDisplay Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Images")]
    [Tooltip("The foreground Image that shows the item icon.")]
    [SerializeField] private Image iconImage;

    [Tooltip("The background Image used as the depth shadow (same sprite, dark tint).")]
    [SerializeField] private Image shadowImage;

    [Header("3-D Tilt")]
    [Tooltip("Euler rotation applied to both images to fake an isometric 3-D look.\n" +
             "X tilts the top away / bottom toward the viewer.\n" +
             "Y rotates left/right.\n" +
             "Z adds roll (the classic Minecraft diagonal).\n" +
             "Default: (15, -25, 15) — tweak freely.")]
    [SerializeField] private Vector3 iconRotation = new Vector3(15f, -25f, 15f);

    [Tooltip("Pixel offset of the shadow behind the icon (UI local space).\n" +
             "Negative X + negative Y pushes it down-left, selling the raised look.")]
    [SerializeField] private Vector2 shadowOffset = new Vector2(-10f, -10f);

    [Tooltip("Colour of the shadow Image. Dark + semi-transparent works best.")]
    [SerializeField] private Color shadowColor = new Color(0f, 0f, 0f, 0.55f);

    [Header("Wiggle (Break)")]
    [Tooltip("Angle in degrees the icon rocks back and forth while the player holds left-click.")]
    [SerializeField] private float wiggleAngle = 12f;

    [Tooltip("How many full rocks per second during the wiggle.")]
    [SerializeField] private float wiggleSpeed = 12f;
    [Tooltip("Duration in seconds of the dip-and-return animation when the item changes.\n" +
             "Set to 0 to disable.")]
    [SerializeField] [Range(0f, 0.5f)] private float swapAnimDuration = 0.18f;

    [Tooltip("How many pixels the icon drops during the dip.")]
    [SerializeField] private float swapDipPixels = 24f;

    // ── Private state ─────────────────────────────────────────────────────────

    private RectTransform _iconRect;
    private RectTransform _shadowRect;
    private Vector2       _iconRestPos;
    private Vector2       _shadowRestPos;
    private Coroutine     _swapCoroutine;
    private Coroutine     _wiggleCoroutine;
    private string        _currentItemName;
    private bool          _wiggling;
    private bool          _foodWiggling;
    private Coroutine     _foodWiggleCoroutine;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (iconImage   != null) _iconRect   = iconImage.rectTransform;
        if (shadowImage != null) _shadowRect = shadowImage.rectTransform;
    }

    private void Start()
    {
        // Bake the tilt into both RectTransforms.
        if (_iconRect   != null) _iconRect.localEulerAngles   = iconRotation;
        if (_shadowRect != null) _shadowRect.localEulerAngles = iconRotation;

        // Offset the shadow.
        if (_iconRect != null && _shadowRect != null)
            _shadowRect.anchoredPosition = _iconRect.anchoredPosition + shadowOffset;

        if (shadowImage != null) shadowImage.color = shadowColor;

        // Cache rest positions for the swap animation.
        if (_iconRect   != null) _iconRestPos   = _iconRect.anchoredPosition;
        if (_shadowRect != null) _shadowRestPos = _shadowRect.anchoredPosition;

        // Hide until the first slot notification arrives from Toolbar.
        SetVisible(false);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by Toolbar.NotifyPlayer() every time the hotbar selection changes.
    /// Passing null or an empty/icon-less slot hides the display.
    /// </summary>
    public void Show(InventorySlot slot)
    {
        bool empty = slot == null
                  || string.IsNullOrEmpty(slot.itemName)
                  || slot.icon == null;

        if (empty)
        {
            SetVisible(false);
            _currentItemName = string.Empty;
            return;
        }

        bool itemChanged = slot.itemName != _currentItemName;
        _currentItemName = slot.itemName;

        iconImage.sprite   = slot.icon;
        shadowImage.sprite = slot.icon;
        SetVisible(true);

        // Only animate when the item actually changes.
        if (itemChanged && swapAnimDuration > 0f)
        {
            // Cancel any food-wiggle that was in progress for the previous item.
            StopFoodWiggle();

            if (_swapCoroutine != null) StopCoroutine(_swapCoroutine);
            _swapCoroutine = StartCoroutine(SwapAnim());
        }
    }

    // ── Wiggle API (called by Player.cs on attack press/release) ─────────────

    /// <summary>Call when the player presses left-click (break held).</summary>
    public void StartWiggle()
    {
        if (_wiggling) return;
        _wiggling = true;
        if (_wiggleCoroutine != null) StopCoroutine(_wiggleCoroutine);
        _wiggleCoroutine = StartCoroutine(WiggleCoroutine());
    }

    /// <summary>Call when the player releases left-click.</summary>
    public void StopWiggle()
    {
        _wiggling = false;
        // WiggleCoroutine checks _wiggling each cycle and exits cleanly,
        // then snaps rotation back to the baked tilt.
    }

    // ── Food wiggle API (called by Player.cs on right-click with food) ────────

    /// <summary>
    /// Starts a slower, gentler right-to-left rock animation while the player
    /// holds right-click on a food item. Mirrors the left-click wiggle API.
    /// </summary>
    public void StartFoodWiggle()
    {
        if (_foodWiggling) return;
        _foodWiggling = true;
        if (_foodWiggleCoroutine != null) StopCoroutine(_foodWiggleCoroutine);
        _foodWiggleCoroutine = StartCoroutine(FoodWiggleCoroutine());
    }

    /// <summary>Call when the player releases right-click or finishes eating.</summary>
    public void StopFoodWiggle()
    {
        _foodWiggling = false;
        // FoodWiggleCoroutine checks _foodWiggling each cycle and exits cleanly.
    }

    private IEnumerator FoodWiggleCoroutine()
    {
        // Slightly slower and shallower than the break wiggle — feels like chewing.
        float angle = wiggleAngle * 0.6f;
        float speed = wiggleSpeed * 0.55f;

        while (_foodWiggling)
        {
            float t      = 0f;
            float period = 1f / speed;
            while (t < period && _foodWiggling)
            {
                t += Time.deltaTime;
                float zOffset = Mathf.Sin(t / period * Mathf.PI * 2f) * angle;
                ApplyRotationWithZ(iconRotation.z + zOffset);
                yield return null;
            }
        }

        // Snap back to rest rotation cleanly.
        ApplyRotationWithZ(iconRotation.z);
        _foodWiggleCoroutine = null;
    }

    private IEnumerator WiggleCoroutine()
    {
        while (_wiggling)
        {
            // Oscillate the Z component of the rotation around the baked tilt value.
            float t = 0f;
            float period = 1f / wiggleSpeed;
            while (t < period && _wiggling)
            {
                t += Time.deltaTime;
                float zOffset = Mathf.Sin(t / period * Mathf.PI * 2f) * wiggleAngle;
                ApplyRotationWithZ(iconRotation.z + zOffset);
                yield return null;
            }
        }

        // Snap back to the rest rotation cleanly.
        ApplyRotationWithZ(iconRotation.z);
        _wiggleCoroutine = null;
    }

    /// <summary>Applies iconRotation with a custom Z override to both images.</summary>
    private void ApplyRotationWithZ(float z)
    {
        Vector3 r = new Vector3(iconRotation.x, iconRotation.y, z);
        if (_iconRect   != null) _iconRect.localEulerAngles   = r;
        if (_shadowRect != null) _shadowRect.localEulerAngles = r;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void SetVisible(bool on)
    {
        if (iconImage   != null) iconImage.enabled   = on;
        if (shadowImage != null) shadowImage.enabled = on;
    }

    /// <summary>
    /// Dips the icon (and shadow) down by swapDipPixels then returns to rest.
    /// Gives the feel of the item being pulled up into the hand slot.
    /// </summary>
    private IEnumerator SwapAnim()
    {
        if (_iconRect == null) yield break;

        Vector2 iconDip   = _iconRestPos   + Vector2.down * swapDipPixels;
        Vector2 shadowDip = _shadowRestPos + Vector2.down * swapDipPixels;
        float   half      = swapAnimDuration * 0.5f;

        // Down.
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            float p = t / half;
            _iconRect.anchoredPosition   = Vector2.Lerp(_iconRestPos,   iconDip,   p);
            if (_shadowRect != null)
                _shadowRect.anchoredPosition = Vector2.Lerp(_shadowRestPos, shadowDip, p);
            yield return null;
        }

        // Back up.
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            float p = t / half;
            _iconRect.anchoredPosition   = Vector2.Lerp(iconDip,   _iconRestPos,   p);
            if (_shadowRect != null)
                _shadowRect.anchoredPosition = Vector2.Lerp(shadowDip, _shadowRestPos, p);
            yield return null;
        }

        _iconRect.anchoredPosition = _iconRestPos;
        if (_shadowRect != null) _shadowRect.anchoredPosition = _shadowRestPos;
        _swapCoroutine = null;
    }
}
