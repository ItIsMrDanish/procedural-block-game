using UnityEngine;

/// <summary>
/// Toolbar (hotbar) controller.
///
/// The Toolbar GameObject has 9 child ItemSlot GameObjects that ARE the visual
/// hotbar both in the HUD and (via injection) inside the Inventory panel.
/// In Awake it hands those GameObjects to Inventory.InjectHotbarSlots() so
/// Inventory does NOT spawn a duplicate set — one set of slots, two panels.
///
/// INSPECTOR SETUP
/// ───────────────
///  • inventory  – the Inventory component
///  • highlight  – the RectTransform used as the selection highlight (child of Toolbar)
///  • player     – the Player component
///
/// The 9 child ItemSlot GameObjects are found automatically via GetComponentsInChildren.
/// They must each have a UIItemSlot component.
///
/// EXECUTION ORDER
/// ───────────────
/// Toolbar must Awake() BEFORE Inventory.Start() so the injection is complete when
/// Inventory.BuildUI() runs. Since Toolbar sits above Inventory in the hierarchy,
/// Unity's default top-down Awake order handles this. If you ever move them,
/// set Toolbar's Script Execution Order lower (earlier) than Inventory in
/// Edit → Project Settings → Script Execution Order.
/// </summary>
public class Toolbar : MonoBehaviour
{
    // ───────────────────────────── Inspector ─────────────────────────────────

    [Tooltip("The Inventory component.")]
    public Inventory inventory;

    [Tooltip("RectTransform used as the visual selection highlight.")]
    public RectTransform highlight;

    [Tooltip("Player reference — receives the currently selected InventorySlot.")]
    public Player player;

    // ───────────────────────────── State ─────────────────────────────────────

    /// <summary>Currently selected hotbar index (0–8).</summary>
    public int slotIndex { get; private set; } = 0;

    private const int HotbarSize = 9;

    private InputSystem input;

    // The 9 HUD slot GameObjects (children of this Toolbar GameObject).
    private GameObject[] _slotObjects;

    // ───────────────────────────── Unity lifecycle ────────────────────────────

    private void Awake()
    {
        // ── Collect the 9 child slot GameObjects ──────────────────────────────
        // UIItemSlot components live on the direct children (ItemSlot 0–8).
        UIItemSlot[] childSlots = GetComponentsInChildren<UIItemSlot>(true);
        if (childSlots.Length != HotbarSize)
            Debug.LogWarning($"Toolbar: expected {HotbarSize} UIItemSlot children, found {childSlots.Length}.");

        _slotObjects = new GameObject[HotbarSize];
        for (int i = 0; i < HotbarSize && i < childSlots.Length; i++)
            _slotObjects[i] = childSlots[i].gameObject;

        // ── Inject into Inventory BEFORE Inventory.Start() runs ───────────────
        // Inventory.BuildUI() skips spawning a duplicate hotbar row when slots
        // have already been injected here.
        if (inventory != null)
            inventory.InjectHotbarSlots(_slotObjects);
        else
            Debug.LogError("Toolbar: Inventory reference not assigned in Inspector.");

        // ── Wire input ────────────────────────────────────────────────────────
        input = new InputSystem();

        input.Player.NextItemToolbelt.performed     += _ => ScrollSlot(-1);
        input.Player.PreviousItemToolbelt.performed += _ => ScrollSlot(1);

        input.Player.SelectSlot1.performed += _ => SetSlot(0);
        input.Player.SelectSlot2.performed += _ => SetSlot(1);
        input.Player.SelectSlot3.performed += _ => SetSlot(2);
        input.Player.SelectSlot4.performed += _ => SetSlot(3);
        input.Player.SelectSlot5.performed += _ => SetSlot(4);
        input.Player.SelectSlot6.performed += _ => SetSlot(5);
        input.Player.SelectSlot7.performed += _ => SetSlot(6);
        input.Player.SelectSlot8.performed += _ => SetSlot(7);
        input.Player.SelectSlot9.performed += _ => SetSlot(8);
    }

    private void OnEnable()  => input.Enable();
    private void OnDisable() => input.Disable();

    private void Start()
    {
        if (inventory == null) { enabled = false; return; }

        inventory.OnInventoryChanged += OnInventoryChanged;

        UpdateHighlight();
        NotifyPlayer();
    }

    private void OnDestroy()
    {
        if (inventory != null)
            inventory.OnInventoryChanged -= OnInventoryChanged;
    }

    // ───────────────────────────── Inventory callback ────────────────────────

    private void OnInventoryChanged() => NotifyPlayer();

    // ───────────────────────────── Input handlers ────────────────────────────

    private void ScrollSlot(int direction)
    {
        slotIndex += direction;
        if (slotIndex > HotbarSize - 1) slotIndex = 0;
        if (slotIndex < 0)              slotIndex = HotbarSize - 1;

        UpdateHighlight();
        NotifyPlayer();
    }

    private void SetSlot(int index)
    {
        if (index < 0 || index >= HotbarSize) return;
        slotIndex = index;
        UpdateHighlight();
        NotifyPlayer();
    }

    // ───────────────────────────── Helpers ───────────────────────────────────

    private void UpdateHighlight()
    {
        if (highlight == null || _slotObjects == null) return;
        if (slotIndex < 0 || slotIndex >= _slotObjects.Length) return;
        if (_slotObjects[slotIndex] == null) return;

        // Both the highlight and the slots are children of the same Canvas, so
        // their RectTransform.position values share the same screen-space coordinate
        // system and can be copied directly — no conversion needed.
        highlight.position = _slotObjects[slotIndex].transform.position;
    }

    private void NotifyPlayer()
    {
        if (player == null || inventory == null) return;
        player.SetSelectedItem(inventory.GetSlot(slotIndex));
    }

    // ───────────────────────────── Public API ────────────────────────────────

    /// <summary>Returns the InventorySlot currently selected (may be null if empty).</summary>
    public InventorySlot GetSelectedSlot() => inventory != null ? inventory.GetSlot(slotIndex) : null;
}
