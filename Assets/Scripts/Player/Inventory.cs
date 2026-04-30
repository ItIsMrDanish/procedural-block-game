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
public class Inventory : MonoBehaviour, IInventory
{
    // ───────────────────────────── Inspector ─────────────────────────────────

    [Header("Limits")]
    [Tooltip("Total regular slots (hotbar + main grid). Recommended: 36.")]
    [Min(1)] [SerializeField] private int maxSlots = 36;

    [Tooltip("Maximum stack size per slot.")]
    [Min(1)] [SerializeField] private int maxStackSize = 99;

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

    private SlotUI[] _hotbarUIs;
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

    public int  MaxSlots     => maxSlots;
    public int  MaxStackSize => maxStackSize;
    public bool IsOpen       => _inventoryOpen;

    // ───────────────────────────── Unity lifecycle ────────────────────────────

    private void Awake()
    {
        _slots = new InventorySlot[maxSlots];
    }

    private void Start()
    {
        BuildUI();
        inventoryPanel.SetActive(false);

        foreach (StartingItem entry in startingItems)
            if (!string.IsNullOrWhiteSpace(entry.itemName) && entry.amount > 0)
                AddItem(entry.itemName, entry.amount, entry.icon);

        RefreshUI();
    }

    // ─────────── Toggle — called by Player.cs, NOT by InputSystem ────────────

    /// <summary>
    /// Called by Player.cs from _controls.Player.Inventory.performed.
    /// Does NOT touch world.inUI or cursor lock — Player.cs manages those.
    /// </summary>
    public void ToggleInventory()
    {
        _inventoryOpen = !_inventoryOpen;
        inventoryPanel.SetActive(_inventoryOpen);
    }

    // ─────────────── Unstackable registry ───────────────────────────────────

    /// <summary>
    /// Marks an item name as unstackable. Called by CraftingMenu on Start
    /// for every recipe that has unstackable = true.
    /// </summary>
    public void RegisterUnstackable(string itemName)
    {
        if (!string.IsNullOrWhiteSpace(itemName))
            _unstackableItems.Add(itemName);
    }

    private bool IsUnstackable(string itemName) => _unstackableItems.Contains(itemName);

    // ───────────────────────────── UI construction ───────────────────────────

    private void BuildUI()
    {
        int mainSize = maxSlots - HotbarSize;
        _hotbarUIs = SpawnSlots(hotbarRoot,   HotbarSize);
        _mainUIs   = SpawnSlots(mainGridRoot, mainSize);
        _armorUIs  = SpawnSlots(armorRoot,    ArmorCount);

        string[] labels = { "Helmet", "Chestplate", "Leggings", "Boots" };
        for (int i = 0; i < ArmorCount; i++)
            if (_armorUIs[i] != null && _armorUIs[i].label != null)
                _armorUIs[i].label.text = labels[i];
    }

    private SlotUI[] SpawnSlots(Transform parent, int count)
    {
        SlotUI[] result = new SlotUI[count];
        if (parent == null)
        {
            Debug.LogWarning($"Inventory: slot root Transform not assigned — {count} slots skipped.");
            return result;
        }
        for (int i = 0; i < count; i++)
            result[i] = new SlotUI(Instantiate(slotUIPrefab, parent));
        return result;
    }

    // ───────────────────────────── UI refresh ────────────────────────────────

    private void RefreshUI()
    {
        for (int i = 0; i < HotbarSize && i < _hotbarUIs.Length; i++)
            ApplySlotToUI(_slots[i], _hotbarUIs[i]);

        int mainSize = maxSlots - HotbarSize;
        for (int i = 0; i < mainSize && i < _mainUIs.Length; i++)
            ApplySlotToUI(_slots[HotbarSize + i], _mainUIs[i]);

        for (int i = 0; i < ArmorCount && i < _armorUIs.Length; i++)
            ApplySlotToUI(_armorSlots[i], _armorUIs[i]);
    }

    private void ApplySlotToUI(InventorySlot slot, SlotUI ui)
    {
        if (ui == null) return;
        bool hasItem = slot != null && !string.IsNullOrEmpty(slot.itemName);

        if (ui.icon != null)
        {
            // Always keep the Image enabled so the background frame stays visible.
            // Use the item icon when available, fall back to emptySlotSprite (can be null).
            ui.icon.enabled = true;
            ui.icon.sprite  = hasItem && slot.icon != null ? slot.icon
                            : emptySlotSprite;

            // Make the image fully transparent when there's no sprite to show,
            // rather than disabling it (which would hide the slot background too).
            Color c = ui.icon.color;
            c.a = (hasItem && slot.icon != null) || emptySlotSprite != null ? 1f : 0f;
            ui.icon.color = c;
        }

        if (ui.countText != null)
        {
            ui.countText.enabled = hasItem && slot.amount > 1;
            ui.countText.text    = hasItem && slot.amount > 1 ? slot.amount.ToString() : "";
        }
    }

    // ───────────────────────────── IInventory ────────────────────────────────

    public int GetAmount(string itemName)
    {
        int total = 0;
        foreach (InventorySlot s in _slots)
            if (s != null && s.itemName == itemName) total += s.amount;
        return total;
    }

    /// <summary>
    /// Adds items with an optional icon. Use the overload with a Sprite when you
    /// have the icon available (e.g. from BlockType.icon) so it shows in the UI.
    /// </summary>
    public int AddItem(string itemName, int amount, Sprite icon = null)
    {
        if (string.IsNullOrWhiteSpace(itemName) || amount <= 0) return amount;
        int remaining = amount;

        bool unstackable = IsUnstackable(itemName);

        // Pass 1 — stack into existing matching slots (hotbar priority).
        // Skipped entirely for unstackable items — they always get their own slot.
        if (!unstackable)
        {
            for (int i = 0; i < maxSlots && remaining > 0; i++)
            {
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
        for (int i = 0; i < maxSlots && remaining > 0; i++)
        {
            if (_slots[i] != null) continue;
            int add   = Mathf.Min(addPerSlot, remaining);
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

    public void RemoveItem(string itemName, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemName) || amount <= 0) return;
        int available = GetAmount(itemName);
        if (available < amount)
        {
            Debug.LogWarning($"Inventory: tried to remove {amount}x {itemName} but only {available} present.");
            amount = available;
        }
        int remaining = amount;
        // Remove from highest index first — preserves hotbar items longest.
        for (int i = maxSlots - 1; i >= 0 && remaining > 0; i--)
        {
            if (_slots[i] == null || _slots[i].itemName != itemName) continue;
            int take          = Mathf.Min(_slots[i].amount, remaining);
            _slots[i].amount -= take;
            remaining        -= take;
            if (_slots[i].amount == 0) _slots[i] = null;
        }
        NotifyChanged();
    }

    // ───────────────────────────── Armor ─────────────────────────────────────

    public bool EquipArmor(string itemName, ArmorSlotType slotType, Sprite icon = null)
    {
        if (slotType == ArmorSlotType.None)
        {
            Debug.LogWarning($"Cannot equip '{itemName}': ArmorSlotType is None.");
            return false;
        }
        int idx = (int)slotType - 1;
        if (_armorSlots[idx] != null) AddItem(_armorSlots[idx].itemName, 1, _armorSlots[idx].icon);
        _armorSlots[idx] = new InventorySlot(itemName, 1, icon);
        NotifyChanged();
        return true;
    }

    public void UnequipArmor(ArmorSlotType slotType)
    {
        if (slotType == ArmorSlotType.None) return;
        int idx = (int)slotType - 1;
        if (_armorSlots[idx] == null) return;
        AddItem(_armorSlots[idx].itemName, 1, _armorSlots[idx].icon);
        _armorSlots[idx] = null;
        NotifyChanged();
    }

    public InventorySlot GetArmorSlot(ArmorSlotType slotType)
        => slotType == ArmorSlotType.None ? null : _armorSlots[(int)slotType - 1];

    public bool IsArmorSlotOccupied(ArmorSlotType slotType)
        => slotType != ArmorSlotType.None && _armorSlots[(int)slotType - 1] != null;

    // ───────────────────────────── Helpers ───────────────────────────────────

    public bool HasItem(string itemName) => GetAmount(itemName) > 0;
    public bool HasFreeSlot()            => System.Array.Exists(_slots, s => s == null);

    public bool CanAdd(string itemName, int amount)
    {
        int remaining = amount;
        foreach (InventorySlot s in _slots)
        {
            if (s == null)                   remaining -= maxStackSize;
            else if (s.itemName == itemName) remaining -= (maxStackSize - s.amount);
            if (remaining <= 0) return true;
        }
        return remaining <= 0;
    }

    public void Clear()
    {
        for (int i = 0; i < maxSlots; i++) _slots[i] = null;
        NotifyChanged();
    }

    [ContextMenu("Debug: Print Inventory")]
    public void DebugPrint()
    {
        Debug.Log($"=== Inventory ({maxSlots} slots, stack {maxStackSize}) ===");
        for (int i = 0; i < maxSlots; i++)
        {
            string zone  = i < HotbarSize ? "Hotbar" : "Main";
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
public class InventorySlot
{
    public string itemName;
    public int    amount;
    public Sprite icon;

    public InventorySlot(string itemName, int amount, Sprite icon = null)
    {
        this.itemName = itemName;
        this.amount   = amount;
        this.icon     = icon;
    }
}

[System.Serializable]
public class StartingItem
{
    public string itemName;
    [Min(1)] public int amount = 1;
    [Tooltip("Optional icon shown in the inventory UI slot.")]
    public Sprite icon;
}

public class SlotUI
{
    public GameObject root;
    public Image      icon;       // The item icon Image (child of slot background)
    public TMP_Text   countText;  // Stack count text
    public TMP_Text   label;      // Armor slot label (second TMP_Text child)

    public SlotUI(GameObject go)
    {
        root = go;

        // Find the icon Image: the first Image component that lives on a CHILD
        // GameObject (not on the root go itself). This is always the item icon,
        // regardless of how many images the background/frame has on the root.
        Image rootImage = go.GetComponent<Image>(); // background — we skip this one
        icon = null;
        foreach (Image img in go.GetComponentsInChildren<Image>(true))
        {
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
        label     = texts.Length > 1 ? texts[1] : null;
    }
}
