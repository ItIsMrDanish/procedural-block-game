using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────── Armor slot enum ─────────────────────────────

/// <summary>
/// Which armor slot an item occupies.
/// None  = regular item (goes into main / hotbar inventory).
/// Other values restrict the item to the matching armor slot.
/// </summary>
public enum ArmorSlotType
{
    None,       // Not an armor piece
    Helmet,     // Top armor slot         (armor index 0)
    Chestplate, // Second armor slot      (armor index 1)
    Leggings,   // Third armor slot       (armor index 2)
    Boots       // Bottom armor slot      (armor index 3)
}

// ─────────────────────────────── IInventory ──────────────────────────────────

/// <summary>
/// Minimal interface keeping CraftingMenu / Recipe decoupled from the
/// concrete Inventory implementation.
/// </summary>
public interface IInventory
{
    int  GetAmount(string itemName);
    void AddItem(string itemName, int amount);
    void RemoveItem(string itemName, int amount);
}

// ─────────────────────────────── Ingredient ──────────────────────────────────

/// <summary>One material entry inside a Recipe.</summary>
[System.Serializable]
public class Ingredient
{
    [Tooltip("Must match the item name used by your inventory system.")]
    public string itemName;

    [Tooltip("Icon shown in the material list (optional).")]
    public Sprite icon;

    [Min(1)]
    public int amount = 1;
}

// ─────────────────────────────── Recipe ──────────────────────────────────────

/// <summary>
/// ScriptableObject representing a single crafting recipe.
/// Create via: Assets > Create > Bloxels > Recipe
/// </summary>
[CreateAssetMenu(fileName = "NewRecipe", menuName = "Bloxels/Recipe", order = 1)]
public class RecipeManager : ScriptableObject
{
    [Header("Output")]
    [Tooltip("The item produced by this recipe.")]
    public string itemName;

    [Tooltip("Icon displayed in the recipe list and detail panel.")]
    public Sprite itemIcon;

    [Tooltip("How many of the item are produced per single craft.")]
    [Min(1)]
    public int outputAmount = 1;

    [Tooltip("When checked, this item always occupies its own slot and cannot be stacked.\n" +
            "Useful for tools, weapons, and unique items.")]
    public bool unstackable = false;

    [Header("Armor")]
    [Tooltip("Set to anything other than None to mark this item as armor.\n" +
             "Armor items are placed directly into the matching armor slot\n" +
             "and cannot stack in regular inventory slots.")]
    public ArmorSlotType armorSlot = ArmorSlotType.None;

    /// <summary>Convenience: true when this recipe produces an armor item.</summary>
    public bool IsArmor => armorSlot != ArmorSlotType.None;

    [Header("Ingredients")]
    [Tooltip("List of materials required to craft this recipe once.")]
    public List<Ingredient> ingredients = new List<Ingredient>();

    // ──────────────────────────── Helpers ────────────────────────────────────

    /// <summary>Returns true when the inventory contains enough of every ingredient.</summary>
    public bool CanCraft(IInventory inventory, int times = 1)
    {
        foreach (Ingredient ingredient in ingredients)
        {
            if (inventory.GetAmount(ingredient.itemName) < ingredient.amount * times)
                return false;
        }
        return true;
    }

    /// <summary>Removes ingredients and adds output items. Call only after CanCraft.</summary>
    public void Craft(IInventory inventory, int times = 1)
    {
        foreach (Ingredient ingredient in ingredients)
            inventory.RemoveItem(ingredient.itemName, ingredient.amount * times);

        inventory.AddItem(itemName, outputAmount * times);
    }
}
