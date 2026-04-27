using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// General-purpose slot-based inventory that implements IInventory,
/// making it compatible with CraftingMenu / Recipe out of the box.
///
/// HOW TO SET UP
/// ─────────────
/// 1. Attach this script to your Player (or a dedicated Inventory GameObject).
/// 2. Set Max Slots and Max Stack Size in the Inspector.
/// 3. Optionally pre-populate Starting Items for testing.
/// 4. CraftingMenu will auto-discover this component via IInventory.
/// </summary>
public class Inventory : MonoBehaviour, IInventory
{
    // ───────────────────────────── Inspector ─────────────────────────────────

    [Header("Limits")]
    [Tooltip("Total number of distinct item slots available.")]
    [Min(1)] [SerializeField] private int maxSlots = 20;

    [Tooltip("Maximum number of items that can stack in a single slot.")]
    [Min(1)] [SerializeField] private int maxStackSize = 99;

    [Header("Starting Items (optional)")]
    [Tooltip("Items the inventory is pre-filled with at Start (useful for testing).")]
    [SerializeField] private List<StartingItem> startingItems = new List<StartingItem>();

    // ───────────────────────────── Data ──────────────────────────────────────

    // Each slot holds one item type and its current stack count.
    private List<InventorySlot> slots = new List<InventorySlot>();

    // ───────────────────────────── Events ────────────────────────────────────

    /// <summary>Fired whenever any slot changes (add, remove, stack update).</summary>
    public event System.Action OnInventoryChanged;

    // ───────────────────────────── Properties ────────────────────────────────

    public int MaxSlots     => maxSlots;
    public int MaxStackSize => maxStackSize;

    /// <summary>Read-only view of all slots (includes empty/null slots up to MaxSlots).</summary>
    public IReadOnlyList<InventorySlot> Slots => slots;

    /// <summary>Number of slots currently occupied by an item.</summary>
    public int UsedSlots => slots.Count;

    // ───────────────────────────── Unity ─────────────────────────────────────

    private void Awake()
    {
        foreach (StartingItem entry in startingItems)
        {
            if (!string.IsNullOrWhiteSpace(entry.itemName) && entry.amount > 0)
                AddItem(entry.itemName, entry.amount);
        }
    }

    // ───────────────────────────── IInventory ────────────────────────────────

    /// <summary>Total count of an item across all slots.</summary>
    public int GetAmount(string itemName)
    {
        int total = 0;
        foreach (InventorySlot slot in slots)
        {
            if (slot.itemName == itemName)
                total += slot.amount;
        }
        return total;
    }

    /// <summary>
    /// Adds items, stacking into existing slots first, then opening new ones.
    /// Returns the number of items that could NOT be added (overflow).
    /// </summary>
    public int AddItem(string itemName, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemName) || amount <= 0) return amount;

        int remaining = amount;

        // 1. Fill existing slots that have room.
        foreach (InventorySlot slot in slots)
        {
            if (slot.itemName != itemName) continue;

            int space = maxStackSize - slot.amount;
            if (space <= 0) continue;

            int toAdd = Mathf.Min(space, remaining);
            slot.amount += toAdd;
            remaining   -= toAdd;

            if (remaining == 0) break;
        }

        // 2. Open new slots for whatever is left.
        while (remaining > 0)
        {
            if (slots.Count >= maxSlots)
            {
                Debug.LogWarning($"Inventory full! Could not add {remaining}x {itemName}.");
                break;
            }

            int toAdd = Mathf.Min(maxStackSize, remaining);
            slots.Add(new InventorySlot(itemName, toAdd));
            remaining -= toAdd;
        }

        OnInventoryChanged?.Invoke();
        return remaining; // 0 = everything fit; >0 = overflow
    }

    // Explicit interface implementation so the void signature matches IInventory.
    void IInventory.AddItem(string itemName, int amount) => AddItem(itemName, amount);

    /// <summary>
    /// Removes a total count of an item, consuming slots from the most-filled
    /// first (LIFO stack behaviour). Logs a warning if there isn't enough.
    /// </summary>
    public void RemoveItem(string itemName, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemName) || amount <= 0) return;

        int available = GetAmount(itemName);
        if (available < amount)
        {
            Debug.LogWarning($"Inventory: tried to remove {amount}x {itemName} but only {available} present.");
            amount = available; // Remove as much as possible.
        }

        int remaining = amount;

        // Iterate backwards so we can safely remove empty slots.
        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventorySlot slot = slots[i];
            if (slot.itemName != itemName) continue;

            int toRemove = Mathf.Min(slot.amount, remaining);
            slot.amount -= toRemove;
            remaining   -= toRemove;

            if (slot.amount == 0)
                slots.RemoveAt(i);
        }

        OnInventoryChanged?.Invoke();
    }

    // ───────────────────────────── Extra helpers ──────────────────────────────

    /// <summary>Returns true if at least one item of this name exists.</summary>
    public bool HasItem(string itemName) => GetAmount(itemName) > 0;

    /// <summary>Returns true if the inventory has room for at least one more item.</summary>
    public bool HasFreeSlot() => slots.Count < maxSlots;

    /// <summary>
    /// Returns true if `amount` of `itemName` can be added without overflow.
    /// </summary>
    public bool CanAdd(string itemName, int amount)
    {
        int remaining = amount;

        foreach (InventorySlot slot in slots)
        {
            if (slot.itemName != itemName) continue;
            remaining -= (maxStackSize - slot.amount);
            if (remaining <= 0) return true;
        }

        // Need new slots?
        int slotsNeeded = Mathf.CeilToInt((float)remaining / maxStackSize);
        return (slots.Count + slotsNeeded) <= maxSlots;
    }

    /// <summary>Removes all items from the inventory.</summary>
    public void Clear()
    {
        slots.Clear();
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Prints the full inventory contents to the console (handy for debugging).</summary>
    [ContextMenu("Debug: Print Inventory")]
    public void DebugPrint()
    {
        Debug.Log($"=== Inventory ({slots.Count}/{maxSlots} slots) ===");
        foreach (InventorySlot slot in slots)
            Debug.Log($"  {slot.itemName}: {slot.amount}/{maxStackSize}");
    }
}

// ─────────────────────────────── Data types ───────────────────────────────────

/// <summary>One slot in the inventory.</summary>
[System.Serializable]
public class InventorySlot
{
    public string itemName;
    public int    amount;

    public InventorySlot(string itemName, int amount)
    {
        this.itemName = itemName;
        this.amount   = amount;
    }
}

/// <summary>Used only to pre-populate the inventory in the Inspector.</summary>
[System.Serializable]
public class StartingItem
{
    public string itemName;
    [Min(1)] public int amount = 1;
}
