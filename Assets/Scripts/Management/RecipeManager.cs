using System.Collections.Generic;
using UnityEngine;

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

/// <summary>
/// One material entry inside a Recipe.
/// Assign an <see cref="ItemDefinition"/> ScriptableObject; the ingredient
/// reads its item name and icon from that single source of truth.
/// </summary>
[System.Serializable]
public class Ingredient
{
    [Tooltip("Drag an ItemDefinition asset here. The item name and icon are read from it automatically.")]
    public ItemDefinition item;

    [Min(1)]
    public int amount = 1;

    // ── Convenience passthrough properties ────────────────────────────────────

    /// <summary>Item name as defined in the assigned ItemDefinition.</summary>
    public string ItemName => item != null ? item.itemName : string.Empty;

    /// <summary>Icon as defined in the assigned ItemDefinition.</summary>
    public Sprite Icon => item != null ? item.icon : null;
}

// ─────────────────────────────── RecipeManager ───────────────────────────────

/// <summary>
/// ScriptableObject representing a single crafting recipe.
///
/// • Set <see cref="itemType"/> to Item / Block / Tool / Weapon.
/// • Tool and Weapon recipes expose a <see cref="material"/> dropdown.
/// • Tool recipes also expose a <see cref="toolType"/> dropdown.
/// • Weapon recipes also expose a <see cref="weaponType"/> dropdown.
/// • Item recipes expose an <see cref="isArmor"/> toggle; when checked,
///   an <see cref="armorSlot"/> dropdown and <see cref="material"/> dropdown appear.
///
/// Create via: Assets > Create > Bloxels > Recipe
/// </summary>
[CreateAssetMenu(fileName = "NewRecipe", menuName = "Bloxels/Recipe", order = 1)]
public class RecipeManager : ScriptableObject
{
    // ── Output ────────────────────────────────────────────────────────────────

    [Header("Output")]
    [Tooltip("The ItemDefinition that represents the item produced by this recipe.")]
    public ItemDefinition outputItem;

    [Tooltip("How many of the item are produced per single craft.")]
    [Min(1)]
    public int outputAmount = 1;

    [Tooltip("When checked, this item always occupies its own slot and cannot be stacked.\n" +
             "Useful for tools, weapons, and unique items.")]
    public bool unstackable = false;

    // ── Type ──────────────────────────────────────────────────────────────────

    [Header("Type")]
    [Tooltip("Broad category of the item this recipe produces.")]
    public ItemType itemType = ItemType.Item;

    // ── Tool settings (visible when itemType == Tool) ─────────────────────────

    [Tooltip("Which kind of tool this recipe produces.")]
    public ToolType toolType = ToolType.Axe;

    // ── Weapon settings (visible when itemType == Weapon) ─────────────────────

    [Tooltip("Which kind of weapon this recipe produces.")]
    public WeaponType weaponType = WeaponType.Sword;

    // ── Material (visible when itemType == Tool, Weapon, or Armor Item) ───────

    [Tooltip("Material tier used for this tool, weapon, or armour piece.\n" +
             "Stats are pulled from RecipeManagerSettings at runtime.")]
    public MaterialType material = MaterialType.Wood;

    // ── Armour settings (visible when itemType == Item && isArmor) ────────────

    [Tooltip("Check to mark this item as armour. Reveals the Armor Slot and Material dropdowns.")]
    public bool isArmor = false;

    [Tooltip("Which armour slot this piece occupies.")]
    public ArmorSlotType armorSlot = ArmorSlotType.Helmet;

    // ── Ingredients ───────────────────────────────────────────────────────────

    [Header("Ingredients")]
    [Tooltip("List of ItemDefinition assets required to craft this recipe once.")]
    public List<Ingredient> ingredients = new List<Ingredient>();

    // ── Convenience properties ────────────────────────────────────────────────

    /// <summary>Item name read from the assigned output ItemDefinition.</summary>
    public string ItemName   => outputItem != null ? outputItem.itemName : string.Empty;

    /// <summary>Icon read from the assigned output ItemDefinition.</summary>
    public Sprite ItemIcon   => outputItem != null ? outputItem.icon    : null;

    /// <summary>True when this recipe produces an armour item.</summary>
    public bool IsArmor      => itemType == ItemType.Item && isArmor;

    /// <summary>True when a material dropdown is relevant for this recipe.</summary>
    public bool HasMaterial  => itemType == ItemType.Tool
                             || itemType == ItemType.Weapon
                             || IsArmor;

    // ── Crafting logic ────────────────────────────────────────────────────────

    /// <summary>Returns true when the inventory contains enough of every ingredient.</summary>
    public bool CanCraft(IInventory inventory, int times = 1)
    {
        foreach (Ingredient ingredient in ingredients)
        {
            if (string.IsNullOrEmpty(ingredient.ItemName)) continue;
            if (inventory.GetAmount(ingredient.ItemName) < ingredient.amount * times)
                return false;
        }
        return true;
    }

    /// <summary>Removes ingredients and adds output items. Call only after CanCraft.</summary>
    public void Craft(IInventory inventory, int times = 1)
    {
        foreach (Ingredient ingredient in ingredients)
        {
            if (string.IsNullOrEmpty(ingredient.ItemName)) continue;
            inventory.RemoveItem(ingredient.ItemName, ingredient.amount * times);
        }

        if (!string.IsNullOrEmpty(ItemName))
            inventory.AddItem(ItemName, outputAmount * times);
    }

    // ── Runtime stat helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Resolves the final weapon stats by combining weapon-type base values
    /// with the chosen material's damage multiplier.
    /// Requires a <see cref="RecipeManagerSettings"/> singleton in the scene.
    /// </summary>
    public WeaponTypeStats GetResolvedWeaponStats()
    {
        if (itemType != ItemType.Weapon) return null;
        var settings = RecipeManagerSettings.Instance;
        if (settings == null)
        {
            Debug.LogWarning("RecipeManagerSettings singleton not found in scene.");
            return null;
        }

        WeaponTypeStats baseStats = settings.GetWeaponTypeStats(weaponType);
        MaterialStats   matStats  = settings.GetMaterialStats(material);

        return new WeaponTypeStats
        {
            damage      = baseStats.damage * matStats.damage,
            reach       = baseStats.reach,
            attackSpeed = baseStats.attackSpeed
        };
    }

    /// <summary>
    /// Resolves the armour damage-reduction value for armour items.
    /// Returns 0 for materials that don't support armour (Wood / Stone).
    /// Requires a <see cref="RecipeManagerSettings"/> singleton in the scene.
    /// </summary>
    public float GetResolvedDamageReduction()
    {
        if (!IsArmor) return 0f;
        var settings = RecipeManagerSettings.Instance;
        if (settings == null)
        {
            Debug.LogWarning("RecipeManagerSettings singleton not found in scene.");
            return 0f;
        }

        ArmorMaterialStats armorMat = settings.GetArmorMaterialStats(material);
        return armorMat?.damageReduction ?? 0f;
    }
}
