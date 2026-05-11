using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Slot-based inventory with UI, hotbar priority, and 4 dedicated armor slots.
/// Implements IInventory so it works with CraftingMenu / Recipe out of the box.
///
/// INPUT
/// ─────
/// No InputSystem lives here. Player.cs calls ToggleInventory() directly.
/// This script does NOT touch world.inUI or cursor state — Player.cs owns that.
///
/// SLOT ORDER
/// ──────────
///  Index  0-8   → Hotbar    (blue  area, filled first, left → right)
///  Index  9-35  → Main grid (green area, filled next,  left → right, top → bottom)
///  Armor slots are separate (not part of the 36-slot pool).
///
/// INSPECTOR SETUP
/// ───────────────
///  • inventoryPanel – root panel to show / hide
///  • hotbarRoot     – parent Transform for 9  hotbar slot UIs (blue area)
///  • mainGridRoot   – parent Transform for 27 main   slot UIs (green area)
///  • armorRoot      – parent Transform for 4  armor  slot UIs (left column)
///  • slotUIPrefab   – prefab with Image (icon) + TMP_Text (stack count)
///  • maxSlots       – 36 recommended (9 hotbar + 27 main)
///  • maxStackSize   – e.g. 99
/// </summary>
public class Inventory : MonoBehaviour, IInventory {
    // ───────────────────────────── Inspector ─────────────────────────────────

    [Header("Limits")]
    [Tooltip("Total regular slots (hotbar + main grid). Recommended: 36.")]
    [Min(1)][SerializeField] private int maxSlots = 36;

    [Tooltip("Maximum stack size per slot.")]
    [Min(1)][SerializeField] private int maxStackSize = 99;

    [Header("UI – Panel")]
    [Tooltip("Root GameObject of the inventory panel.")]
    [SerializeField] private GameObject inventoryPanel;

    [Header("UI – Slot roots")]
    [Tooltip("Content parent for hotbar slot UIs (blue area).")]
    [SerializeField] private Transform hotbarRoot;

    [Tooltip("Content parent for main grid slot UIs (green area).")]
    [SerializeField] private Transform mainGridRoot;

    [Tooltip("Content parent for armor slot UIs (left column, top → bottom).")]
    [SerializeField] private Transform armorRoot;

    [Header("UI – Prefab")]
    [Tooltip("Prefab for each visual slot.\nRequired children: Image (icon), TMP_Text (stack count).")]
    [SerializeField] private GameObject slotUIPrefab;

    [Tooltip("Sprite shown in empty slots (optional).")]
    [SerializeField] private Sprite emptySlotSprite;

    [Header("Starting Items (optional – for testing)")]
    [SerializeField] private List<StartingItem> startingItems = new List<StartingItem>();

    // ───────────────────────────── Runtime data ───────────────────────────────

    private InventorySlot[] _slots;                              // null = empty slot
    private InventorySlot[] _armorSlots = new InventorySlot[4]; // Helmet=0…Boots=3

    // HUD hotbar — the 9 Toolbar child GameObjects injected by Toolbar.Awake().
    private SlotUI[] _hotbarUIs;
    // Panel hotbar — a second mirrored set spawned under hotbarRoot inside the inventory panel.
    // Both are refreshed together so they always show identical content.
    private SlotUI[] _panelHotbarUIs;
    private SlotUI[] _mainUIs;
    private SlotUI[] _armorUIs;

    private bool _inventoryOpen;

    // Set of item names that are unstackable (each always gets its own slot).
    // Populated at startup via RegisterUnstackable(), called by CraftingMenu.
    private readonly System.Collections.Generic.HashSet<string> _unstackableItems
        = new System.Collections.Generic.HashSet<string>();

    // ───────────────────────────── Constants ─────────────────────────────────

    private const int HotbarSize = 9;
    private const int ArmorCount = 4;

    // ───────────────────────────── Events ────────────────────────────────────

    /// <summary>Fired after any slot change (add, remove, equip armor).</summary>
    public event System.Action OnInventoryChanged;

    // ───────────────────────────── Properties ────────────────────────────────

    public int MaxSlots => maxSlots;
    public int MaxStackSize => maxStackSize;
    public bool IsOpen => _inventoryOpen;

    // ───────────────────────────── Unity lifecycle ────────────────────────────

    private void Awake() {
        _slots = new InventorySlot[maxSlots];
    }

    private void Start() {
        BuildUI();
        inventoryPanel.SetActive(false);

        foreach (StartingItem entry in startingItems)
            if (!string.IsNullOrWhiteSpace(entry.itemName) && entry.amount > 0)
                AddItem(entry.itemName, entry.amount, entry.icon);

        RefreshUI();

        // Broadcast initial state one frame later so Toolbar.Start() has already
        // subscribed to OnInventoryChanged, regardless of script execution order.
        // Invoke (not a coroutine) works even if the GameObject was inactive at startup.
        Invoke(nameof(LateStart), 0f);
    }

    private void LateStart() => NotifyChanged();

    // ─────────── Toggle — called by Player.cs, NOT by InputSystem ────────────

    /// <summary>
    /// Called by Player.cs from _controls.Player.Inventory.performed.
    /// Does NOT touch world.inUI or cursor lock — Player.cs manages those.
    /// </summary>
    public void ToggleInventory() {
        _inventoryOpen = !_inventoryOpen;
        inventoryPanel.SetActive(_inventoryOpen);

        if (_inventoryOpen) {
            Canvas.ForceUpdateCanvases();
            RefreshUI();
        }
    }

    // ─────────────── Unstackable registry ───────────────────────────────────

    /// <summary>
    /// Marks an item name as unstackable. Called by CraftingMenu on Start
    /// for every recipe that has unstackable = true.
    /// </summary>
    public void RegisterUnstackable(string itemName) {
        if (!string.IsNullOrWhiteSpace(itemName))
            _unstackableItems.Add(itemName);
    }

    private bool IsUnstackable(string itemName) => _unstackableItems.Contains(itemName);

    // ───────────────────────────── UI construction ───────────────────────────

    /// <summary>
    /// Called by Toolbar.Awake() BEFORE Inventory.Start() runs.
    /// Provides the 9 pre-existing HUD slot GameObjects so Inventory does NOT
    /// spawn a second set under hotbarRoot. Each GameObject must already have
    /// the "UIItemSlot" tag; Inventory will stamp InventorySlotIndex onto them.
    /// </summary>
    /// <summary>
    /// Called by Toolbar.Awake() BEFORE Inventory.Start() runs.
    /// Registers the 9 HUD slot GameObjects as the toolbar's SlotUI array.
    /// Inventory.BuildUI() will ALSO spawn a separate mirrored set under hotbarRoot
    /// for display inside the inventory panel — same data, two visual representations.
    /// </summary>
    public void InjectHotbarSlots(GameObject[] hotbarSlotObjects) {
        if (hotbarSlotObjects == null || hotbarSlotObjects.Length != HotbarSize) {
            Debug.LogError("Inventory.InjectHotbarSlots: expected exactly 9 slot GameObjects.");
            return;
        }

        _hotbarUIs = new SlotUI[HotbarSize];
        for (int i = 0; i < HotbarSize; i++) {
            GameObject go = hotbarSlotObjects[i];
            go.tag = "UIItemSlot";

            InventorySlotIndex idx = go.GetComponent<InventorySlotIndex>();
            if (idx == null) idx = go.AddComponent<InventorySlotIndex>();
            idx.slotIndex = i;

            _hotbarUIs[i] = new SlotUI(go);
        }

        _hotbarSlotsInjected = true;
    }

    private bool _hotbarSlotsInjected = false;

    private void BuildUI() {
        int mainSize = maxSlots - HotbarSize;

        if (!_hotbarSlotsInjected) {
            // No Toolbar injection — spawn a single hotbar set under hotbarRoot.
            _hotbarUIs = SpawnSlots(hotbarRoot, HotbarSize, startIndex: 0);
        } else {
            // Toolbar already provided the HUD slots (_hotbarUIs).
            // Spawn a SECOND mirrored set under hotbarRoot for the inventory panel.
            // Both arrays read from the same _slots[0-8] data and are refreshed together.
            _panelHotbarUIs = SpawnSlots(hotbarRoot, HotbarSize, startIndex: 0);
        }

        _mainUIs = SpawnSlots(mainGridRoot, mainSize, startIndex: HotbarSize);
        _armorUIs = SpawnSlots(armorRoot, ArmorCount, startIndex: 0);

        string[] labels = { "Helmet", "Chestplate", "Leggings", "Boots" };
        for (int i = 0; i < ArmorCount; i++)
            if (_armorUIs[i] != null && _armorUIs[i].label != null)
                _armorUIs[i].label.text = labels[i];
    }

    private SlotUI[] SpawnSlots(Transform parent, int count, int startIndex = 0) {
        SlotUI[] result = new SlotUI[count];
        if (parent == null) {
            Debug.LogWarning($"Inventory: slot root Transform not assigned — {count} slots skipped.");
            return result;
        }
        for (int i = 0; i < count; i++) {
            GameObject go = Instantiate(slotUIPrefab, parent);

            // Tag the root so DragAndDropHandler's raycaster can identify it.
            go.tag = "UIItemSlot";

            // Stamp the flat inventory index so the handler knows which slot was clicked.
            InventorySlotIndex idx = go.AddComponent<InventorySlotIndex>();
            idx.slotIndex = startIndex + i;

            result[i] = new SlotUI(go);
        }
        return result;
    }

    // ───────────────────────────── UI refresh ────────────────────────────────

    private void RefreshUI() {
        // HUD hotbar (Toolbar children).
        if (_hotbarUIs != null)
            for (int i = 0; i < HotbarSize && i < _hotbarUIs.Length; i++)
                ApplySlotToUI(_slots[i], _hotbarUIs[i]);

        // Panel hotbar mirror — only exists when Toolbar injection was used.
        if (_panelHotbarUIs != null)
            for (int i = 0; i < HotbarSize && i < _panelHotbarUIs.Length; i++)
                ApplySlotToUI(_slots[i], _panelHotbarUIs[i]);

        int mainSize = maxSlots - HotbarSize;
        if (_mainUIs != null)
            for (int i = 0; i < mainSize && i < _mainUIs.Length; i++)
                ApplySlotToUI(_slots[HotbarSize + i], _mainUIs[i]);

        if (_armorUIs != null)
            for (int i = 0; i < ArmorCount && i < _armorUIs.Length; i++)
                ApplySlotToUI(_armorSlots[i], _armorUIs[i]);
    }

    private void ApplySlotToUI(InventorySlot slot, SlotUI ui) {
        if (ui == null) return;
        bool hasItem = slot != null && !string.IsNullOrEmpty(slot.itemName);

        if (ui.icon != null) {
            // Always keep the Image enabled so the background frame stays visible.
            // Use the item icon when available, fall back to emptySlotSprite (can be null).
            ui.icon.enabled = true;
            ui.icon.sprite = hasItem && slot.icon != null ? slot.icon
                            : emptySlotSprite;

            // Make the image fully transparent when there's no sprite to show,
            // rather than disabling it (which would hide the slot background too).
            Color c = ui.icon.color;
            c.a = (hasItem && slot.icon != null) || emptySlotSprite != null ? 1f : 0f;
            ui.icon.color = c;
        }

        if (ui.countText != null) {
            ui.countText.enabled = hasItem && slot.amount > 1;
            ui.countText.text = hasItem && slot.amount > 1 ? slot.amount.ToString() : "";
        }
    }

    // ───────────────────────────── IInventory ────────────────────────────────

    public int GetAmount(string itemName) {
        if (_slots == null) return 0;
        int total = 0;
        foreach (InventorySlot s in _slots)
            if (s != null && s.itemName == itemName) total += s.amount;
        return total;
    }

    /// <summary>
    /// Adds items with an optional icon. Use the overload with a Sprite when you
    /// have the icon available (e.g. from BlockType.icon) so it shows in the UI.
    /// </summary>
    public int AddItem(string itemName, int amount, Sprite icon = null) {
        if (string.IsNullOrWhiteSpace(itemName) || amount <= 0) return amount;

        int remaining = amount;

        bool unstackable = IsUnstackable(itemName);

        // Pass 1 — stack into existing matching slots (hotbar priority).
        // Skipped entirely for unstackable items — they always get their own slot.
        if (!unstackable) {
            for (int i = 0; i < maxSlots && remaining > 0; i++) {
                if (_slots[i] == null || _slots[i].itemName != itemName) continue;
                int space = maxStackSize - _slots[i].amount;
                if (space <= 0) continue;
                int add = Mathf.Min(space, remaining);
                _slots[i].amount += add;
                if (icon != null && _slots[i].icon == null) _slots[i].icon = icon;
                remaining -= add;
            }
        }

        // Pass 2 — open new slots (hotbar first, then main grid).
        // Unstackable items always add 1 per slot.
        int addPerSlot = unstackable ? 1 : maxStackSize;
        for (int i = 0; i < maxSlots && remaining > 0; i++) {
            if (_slots[i] != null) continue;
            int add = Mathf.Min(addPerSlot, remaining);
            _slots[i] = new InventorySlot(itemName, add, icon);
            remaining -= add;
        }

        if (remaining > 0)
            Debug.LogWarning($"Inventory full! Could not add {remaining}x {itemName}.");

        NotifyChanged();
        return remaining;
    }

    // Explicit void implementation for IInventory compatibility (no icon).
    void IInventory.AddItem(string itemName, int amount) => AddItem(itemName, amount);

    public void RemoveItem(string itemName, int amount) {
        if (string.IsNullOrWhiteSpace(itemName) || amount <= 0) return;
        int available = GetAmount(itemName);
        if (available < amount) {
            Debug.LogWarning($"Inventory: tried to remove {amount}x {itemName} but only {available} present.");
            amount = available;
        }
        int remaining = amount;
        // Remove from highest index first — preserves hotbar items longest.
        for (int i = maxSlots - 1; i >= 0 && remaining > 0; i--) {
            if (_slots[i] == null || _slots[i].itemName != itemName) continue;
            int take = Mathf.Min(_slots[i].amount, remaining);
            _slots[i].amount -= take;
            remaining -= take;
            if (_slots[i].amount == 0) _slots[i] = null;
        }
        NotifyChanged();
    }

    // ───────────────────────────── Armor ─────────────────────────────────────

    public bool EquipArmor(string itemName, ArmorSlotType slotType, Sprite icon = null) {
        int idx = (int)slotType; // Helmet=0, Chestplate=1, Leggings=2, Boots=3
        if (idx < 0 || idx >= ArmorCount) {
            Debug.LogWarning($"Cannot equip '{itemName}': ArmorSlotType index {idx} is out of range.");
            return false;
        }
        if (_armorSlots[idx] != null) AddItem(_armorSlots[idx].itemName, 1, _armorSlots[idx].icon);
        _armorSlots[idx] = new InventorySlot(itemName, 1, icon);
        NotifyChanged();
        return true;
    }

    public void UnequipArmor(ArmorSlotType slotType) {
        int idx = (int)slotType;
        if (idx < 0 || idx >= ArmorCount) return;
        if (_armorSlots[idx] == null) return;
        AddItem(_armorSlots[idx].itemName, 1, _armorSlots[idx].icon);
        _armorSlots[idx] = null;
        NotifyChanged();
    }

    public InventorySlot GetArmorSlot(ArmorSlotType slotType) {
        int idx = (int)slotType;
        return (idx >= 0 && idx < ArmorCount) ? _armorSlots[idx] : null;
    }

    public bool IsArmorSlotOccupied(ArmorSlotType slotType) {
        int idx = (int)slotType;
        return idx >= 0 && idx < ArmorCount && _armorSlots[idx] != null;
    }

    // ───────────────────────────── Drag-and-drop direct writes ──────────────

    /// <summary>
    /// Overwrites slot [index] with a copy of <paramref name="source"/>.
    /// Used by DragAndDropHandler so the player can drop an item into a
    /// specific slot rather than always letting AddItem choose the position.
    /// Fires OnInventoryChanged.
    /// </summary>
    public void SetSlotDirect(int index, InventorySlot source) {
        if (source == null || index < 0 || index >= maxSlots) return;
        _slots[index] = new InventorySlot(source.itemName, source.amount, source.icon);
        NotifyChanged();
    }

    /// <summary>
    /// Adds <paramref name="amount"/> to the stack already in slot [index].
    /// Does NOT create a new slot if the slot is empty.
    /// Fires OnInventoryChanged.
    /// </summary>
    public void AddToSlotDirect(int index, int amount) {
        if (index < 0 || index >= maxSlots || _slots[index] == null || amount <= 0) return;
        _slots[index].amount = Mathf.Min(_slots[index].amount + amount, maxStackSize);
        NotifyChanged();
    }

    // ───────────────────────────── Toolbar accessors ─────────────────────────

    /// <summary>
    /// Returns the InventorySlot at the given flat index (0–maxSlots-1).
    /// Returns null if the slot is empty or the index is out of range.
    /// Used by Toolbar to read hotbar slots 0–8 without exposing the full array.
    /// </summary>
    public InventorySlot GetSlot(int index) {
        if (_slots == null || index < 0 || index >= _slots.Length) return null;
        return _slots[index];
    }

    /// <summary>
    /// Returns the Transform of a hotbar slot's root GameObject so Toolbar can
    /// position its highlight. Works regardless of whether slots were injected
    /// (Toolbar children) or spawned under hotbarRoot.
    /// </summary>
    public Transform GetHotbarSlotTransform(int hotbarIndex) {
        if (_hotbarUIs == null || hotbarIndex < 0 || hotbarIndex >= _hotbarUIs.Length)
            return null;
        SlotUI ui = _hotbarUIs[hotbarIndex];
        return ui?.root != null ? ui.root.transform : null;
    }

    // ───────────────────────────── Helpers ───────────────────────────────────

    public bool HasItem(string itemName) => GetAmount(itemName) > 0;
    public bool HasFreeSlot() => System.Array.Exists(_slots, s => s == null);

    /// <summary>
    /// Backfills the icon on every slot that holds itemName but has no icon assigned.
    /// Called by CraftingMenu after Craft() so crafted items display their recipe icon.
    /// </summary>
    public void SetIconForItem(string itemName, Sprite icon) {
        if (string.IsNullOrWhiteSpace(itemName) || icon == null || _slots == null) return;
        bool changed = false;
        foreach (InventorySlot s in _slots) {
            if (s != null && s.itemName == itemName && s.icon == null) {
                s.icon = icon;
                changed = true;
            }
        }
        if (changed) NotifyChanged();
    }

    public bool CanAdd(string itemName, int amount) {
        int remaining = amount;
        foreach (InventorySlot s in _slots) {
            if (s == null) remaining -= maxStackSize;
            else if (s.itemName == itemName) remaining -= (maxStackSize - s.amount);
            if (remaining <= 0) return true;
        }
        return remaining <= 0;
    }

    public void Clear() {
        for (int i = 0; i < maxSlots; i++) _slots[i] = null;
        NotifyChanged();
    }

    [ContextMenu("Debug: Print Inventory")]
    public void DebugPrint() {
        Debug.Log($"=== Inventory ({maxSlots} slots, stack {maxStackSize}) ===");
        for (int i = 0; i < maxSlots; i++) {
            string zone = i < HotbarSize ? "Hotbar" : "Main";
            string entry = _slots[i] == null ? "(empty)" : $"{_slots[i].itemName} x{_slots[i].amount}";
            Debug.Log($"  [{zone} {i}] {entry}");
        }
        for (int i = 0; i < ArmorCount; i++)
            Debug.Log($"  [Armor {i}] {(_armorSlots[i] == null ? "(empty)" : _armorSlots[i].itemName)}");
    }

    private void NotifyChanged() { RefreshUI(); OnInventoryChanged?.Invoke(); }
}

// ─────────────────────────────── Data types ───────────────────────────────────

[System.Serializable]
public class InventorySlot {
    public string itemName;
    public int amount;
    public Sprite icon;

    public InventorySlot(string itemName, int amount, Sprite icon = null) {
        this.itemName = itemName;
        this.amount = amount;
        this.icon = icon;
    }
}

[System.Serializable]
public class StartingItem {
    public string itemName;
    [Min(1)] public int amount = 1;
    [Tooltip("Optional icon shown in the inventory UI slot.")]
    public Sprite icon;
}

public class SlotUI {
    public GameObject root;
    public Image icon;       // The item icon Image (child of slot background)
    public TMP_Text countText;  // Stack count text
    public TMP_Text label;      // Armor slot label (second TMP_Text child)

    public SlotUI(GameObject go) {
        root = go;

        // Find the icon Image: the first Image component that lives on a CHILD
        // GameObject (not on the root go itself). This is always the item icon,
        // regardless of how many images the background/frame has on the root.
        Image rootImage = go.GetComponent<Image>(); // background — we skip this one
        icon = null;
        foreach (Image img in go.GetComponentsInChildren<Image>(true)) {
            if (img.gameObject != go) // not the root background
            {
                icon = img;
                break;
            }
        }
        // Fallback: if the prefab has only one Image (on the root), use it.
        if (icon == null) icon = rootImage;

        TMP_Text[] texts = go.GetComponentsInChildren<TMP_Text>(true);
        countText = texts.Length > 0 ? texts[0] : null;
        label = texts.Length > 1 ? texts[1] : null;
    }
}

/// <summary>
/// Tiny component stamped onto every slot GameObject by Inventory.SpawnSlots().
/// Lets DragAndDropHandler resolve a raycaster hit back to a flat inventory index.
/// </summary>
public class InventorySlotIndex : MonoBehaviour {
    /// <summary>Flat index into Inventory._slots (0 = first hotbar slot).</summary>
    public int slotIndex;
}