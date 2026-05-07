using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Drag-and-drop handler for the inventory UI.
///
/// Operates entirely on Inventory's InventorySlot data model (string itemName,
/// int amount, Sprite icon).  The "cursor slot" is a lightweight in-memory
/// InventorySlot that follows the mouse while the player is dragging an item.
///
/// INSPECTOR SETUP
/// ───────────────
///  • cursorRoot     – RectTransform that moves with the mouse (contains cursorIcon + cursorCount)
///  • cursorIcon     – Image on the cursor GameObject that shows the dragged item icon
///  • cursorCount    – TMP_Text on the cursor GameObject that shows the dragged stack count
///  • m_Raycaster    – GraphicRaycaster on the inventory Canvas
///  • m_EventSystem  – the scene's EventSystem
///  • inventory      – the Inventory component
///
/// All Inspector references fall back to FindFirstObjectByType / FindObjectOfType
/// in Awake, so the script is resilient to missing drag-and-drop assignments.
///
/// HOW CLICKS ARE IDENTIFIED
/// ─────────────────────────
///  Each hotbar/main-grid slot root GameObject spawned by Inventory must have
///  the tag "UIItemSlot" so CheckForSlot() can find it via the GraphicRaycaster.
///  The slot's flat index into Inventory is stored in an InventorySlotIndex
///  component that Inventory attaches at build time (see below).
/// </summary>
public class DragAndDropHandler : MonoBehaviour {
    // ───────────────────────────── Inspector ─────────────────────────────────

    [Header("Cursor visuals (follow the mouse while dragging)")]
    [SerializeField] private RectTransform cursorRoot = null;
    [SerializeField] private Image cursorIcon = null;
    [SerializeField] private TMP_Text cursorCount = null;

    [Header("Raycasting")]
    [SerializeField] private GraphicRaycaster m_Raycaster = null;
    [SerializeField] private EventSystem m_EventSystem = null;

    [Header("Data")]
    [SerializeField] private Inventory inventory = null;

    // ───────────────────────────── Runtime state ──────────────────────────────

    // The item currently "on the cursor" (being dragged).  Null = nothing held.
    private InventorySlot _cursorSlot = null;

    private World world;
    private InputSystem uiControls;
    private Vector2 mousePosition;

    // ───────────────────────────── Unity lifecycle ────────────────────────────

    private void Awake() {
        // Resolve all Inspector references with fallbacks so missing assignments
        // produce a clear LogError rather than a silent NullReferenceException.

        if (inventory == null)
            inventory = FindFirstObjectByType<Inventory>();
        if (inventory == null)
            Debug.LogError("DragAndDropHandler: No Inventory found! Assign it in the Inspector.");

        if (m_EventSystem == null)
            m_EventSystem = FindFirstObjectByType<EventSystem>();
        if (m_EventSystem == null)
            Debug.LogError("DragAndDropHandler: No EventSystem found in scene.");

        if (m_Raycaster == null) {
            // Try to find the GraphicRaycaster on the Canvas that contains this component.
            m_Raycaster = GetComponentInParent<GraphicRaycaster>();
            if (m_Raycaster == null)
                Debug.LogError("DragAndDropHandler: No GraphicRaycaster found. Assign it in the Inspector.");
        }
    }

    private void Start() {
        GameObject worldGO = GameObject.Find("World");
        if (worldGO != null)
            world = worldGO.GetComponent<World>();
        if (world == null)
            Debug.LogError("DragAndDropHandler: Could not find the 'World' GameObject or its World component.");

        uiControls = new InputSystem();

        uiControls.UI.MousePointer.performed += ctx =>
            mousePosition = ctx.ReadValue<Vector2>();

        uiControls.UI.Click.performed += _ =>
        {
            // Guard: world must be resolved and UI must be open.
            if (world != null && world.inUI)
                HandleSlotClick(CheckForSlot());
        };

        uiControls.UI.Enable();

        // Hide cursor visuals at startup.
        SetCursorVisible(false);
    }

    private void OnDisable() {
        uiControls?.UI.Disable();
    }

    private void Update() {
        // Guard: if world isn't resolved yet, do nothing.
        if (world == null) return;

        if (!world.inUI) {
            // Drop any held item back into the inventory when the UI closes.
            if (_cursorSlot != null)
                ReturnCursorToInventory();
            return;
        }

        // Keep the cursor graphic under the mouse.
        if (cursorRoot != null)
            cursorRoot.position = mousePosition;
    }

    // ───────────────────────────── Click handling ────────────────────────────

    /// <summary>
    /// Core slot-click logic.
    ///  • Nothing held + empty slot  → nothing
    ///  • Nothing held + filled slot → pick up the whole stack
    ///  • Holding item + empty slot  → place the whole stack
    ///  • Holding item + same item   → merge stacks (up to maxStackSize)
    ///  • Holding item + diff item   → swap
    /// </summary>
    private void HandleSlotClick(SlotHit hit) {
        if (hit == null) return;
        if (inventory == null) return;  // guard: inventory not resolved

        bool cursorHasItem = _cursorSlot != null;
        InventorySlot targetSlot = inventory.GetSlot(hit.slotIndex);
        bool targetHasItem = targetSlot != null;

        // ── Nothing held, nothing in target → no-op ──────────────────────────
        if (!cursorHasItem && !targetHasItem) return;

        // ── Nothing held → pick up entire stack ──────────────────────────────
        if (!cursorHasItem && targetHasItem) {
            _cursorSlot = new InventorySlot(targetSlot.itemName, targetSlot.amount, targetSlot.icon);
            inventory.RemoveItem(targetSlot.itemName, targetSlot.amount);
            UpdateCursorVisual();
            return;
        }

        // ── Holding item → place into empty slot ─────────────────────────────
        if (cursorHasItem && !targetHasItem) {
            // Use the direct slot-set path to honour the player's chosen position.
            DirectSetSlot(hit.slotIndex, _cursorSlot);
            _cursorSlot = null;
            UpdateCursorVisual();
            return;
        }

        // ── Both cursor and target have items ─────────────────────────────────
        if (cursorHasItem && targetHasItem) {
            // Same item → merge up to maxStackSize.
            if (_cursorSlot.itemName == targetSlot.itemName) {
                int space = inventory.MaxStackSize - targetSlot.amount;
                if (space > 0) {
                    int transfer = Mathf.Min(space, _cursorSlot.amount);
                    DirectAddToSlot(hit.slotIndex, transfer);
                    _cursorSlot.amount -= transfer;
                    if (_cursorSlot.amount <= 0)
                        _cursorSlot = null;
                }
                // If no space, do nothing (cursor keeps the stack).
            } else {
                // Different items → swap.
                InventorySlot saved = new InventorySlot(targetSlot.itemName, targetSlot.amount, targetSlot.icon);
                DirectSetSlot(hit.slotIndex, _cursorSlot);
                _cursorSlot = saved;
            }

            UpdateCursorVisual();
        }
    }

    // ───────────────────────────── Raycasting ────────────────────────────────

    private SlotHit CheckForSlot() {
        if (m_Raycaster == null || m_EventSystem == null) return null;

        PointerEventData ped = new PointerEventData(m_EventSystem) { position = mousePosition };
        List<RaycastResult> results = new List<RaycastResult>();
        m_Raycaster.Raycast(ped, results);

        foreach (RaycastResult result in results) {
            if (result.gameObject.CompareTag("UIItemSlot")) {
                InventorySlotIndex idx = result.gameObject.GetComponent<InventorySlotIndex>();
                if (idx != null)
                    return new SlotHit(idx.slotIndex);
            }
        }
        return null;
    }

    // ───────────────────────────── Direct slot writes ────────────────────────

    /// <summary>
    /// Writes an InventorySlot directly into inventory slot [index], bypassing
    /// AddItem's fill-first logic.
    /// </summary>
    private void DirectSetSlot(int index, InventorySlot source) {
        inventory.SetSlotDirect(index, source);
    }

    private void DirectAddToSlot(int index, int amount) {
        inventory.AddToSlotDirect(index, amount);
    }

    // ───────────────────────────── Cursor visual ─────────────────────────────

    private void UpdateCursorVisual() {
        bool hasCursor = _cursorSlot != null;
        SetCursorVisible(hasCursor);

        if (!hasCursor) return;

        if (cursorIcon != null) cursorIcon.sprite = _cursorSlot.icon;
        if (cursorCount != null) cursorCount.text = _cursorSlot.amount > 1
                                                        ? _cursorSlot.amount.ToString() : "";
    }

    private void SetCursorVisible(bool visible) {
        if (cursorRoot != null) cursorRoot.gameObject.SetActive(visible);
    }

    private void ReturnCursorToInventory() {
        if (_cursorSlot == null || inventory == null) return;
        inventory.AddItem(_cursorSlot.itemName, _cursorSlot.amount, _cursorSlot.icon);
        _cursorSlot = null;
        UpdateCursorVisual();
    }

    // ───────────────────────────── Inner types ───────────────────────────────

    private class SlotHit {
        public readonly int slotIndex;
        public SlotHit(int i) { slotIndex = i; }
    }
}