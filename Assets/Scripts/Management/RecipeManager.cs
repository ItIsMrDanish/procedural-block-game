using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject representing a single crafting recipe.
/// Create via: Assets > Create > Crafting > Recipe
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

    [Header("Ingredients")]
    [Tooltip("List of materials required to craft this recipe once.")]
    public List<Ingredient> ingredients = new List<Ingredient>();

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

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

/// <summary>
/// Minimal interface so CraftingMenu / Recipe remain decoupled from
/// your concrete inventory implementation. Implement this on whatever
/// class manages the player's items.
/// </summary>
public interface IInventory
{
    int  GetAmount(string itemName);
    void AddItem(string itemName, int amount);
    void RemoveItem(string itemName, int amount);
}
