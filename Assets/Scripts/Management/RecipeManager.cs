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

    // ── Food settings (visible when itemType == Item && isFood) ───────────────

    [Tooltip("Check to mark this item as food. Hold right-click for ~2 s to eat it.")]
    public bool isFood = false;

    [Tooltip("How much hunger this item restores when eaten.")]
    [Min(1)]
    public int foodValue = 2;

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

    /// <summary>True when this recipe produces a food item.</summary>
    public bool IsFood       => itemType == ItemType.Item && isFood;

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

    // ── Static tool-lookup helpers (called by Player.cs) ─────────────────────

    // Registry populated explicitly by CraftingMenu.RegisterRecipesForLookup()
    // at Start(). Using a registration pattern instead of Resources.FindObjectsOfTypeAll
    // because ScriptableObjects are only guaranteed to be loaded if they are directly
    // referenced by a scene object — which CraftingMenu's serialized list already ensures.
    private static readonly Dictionary<string, RecipeManager> _recipeByItemName
        = new Dictionary<string, RecipeManager>();

    /// <summary>
    /// Called by CraftingMenu.Start() to register all its recipes into the lookup.
    /// Must be called before the player can hold any tool.
    /// </summary>
    public static void RegisterRecipes(IEnumerable<RecipeManager> recipes)
    {
        foreach (var r in recipes)
            if (r != null && !string.IsNullOrEmpty(r.ItemName))
                _recipeByItemName[r.ItemName] = r;
    }

    /// <summary>
    /// Returns (damage, hardness) for the item currently held by the player.
    /// Returns (1, 1) — bare-hands values — for non-tool items or an empty hand.
    /// </summary>
    public static (float damage, float hardness) GetToolStats(string heldItemName)
    {
        if (string.IsNullOrEmpty(heldItemName)) return (1f, 1f);

        var settings = RecipeManagerSettings.Instance;
        if (settings == null) return (1f, 1f);

        if (!_recipeByItemName.TryGetValue(heldItemName, out var recipe)) return (1f, 1f);
        if (recipe.itemType != ItemType.Tool && recipe.itemType != ItemType.Weapon) return (1f, 1f);

        MaterialStats mat = settings.GetMaterialStats(recipe.material);
        if (mat == null) return (1f, 1f);

        return (mat.damage, mat.hardness);
    }

    /// <summary>
    /// Returns the ToolType of the held item, or ToolType.None if it is not a tool.
    /// Weapons intentionally return None — they don't mine blocks.
    /// </summary>
    public static ToolType GetToolType(string heldItemName)
    {
        if (string.IsNullOrEmpty(heldItemName)) return ToolType.None;

        if (!_recipeByItemName.TryGetValue(heldItemName, out var recipe)) return ToolType.None;
        if (recipe.itemType == ItemType.Tool) return recipe.toolType;

        return ToolType.None;
    }

    /// <summary>
    /// Returns the MaterialType of the held tool, or MaterialType.None for non-tools.
    /// </summary>
    public static MaterialType GetToolMaterial(string heldItemName)
    {
        if (string.IsNullOrEmpty(heldItemName)) return MaterialType.None;

        if (!_recipeByItemName.TryGetValue(heldItemName, out var recipe)) return MaterialType.None;
        if (recipe.itemType != ItemType.Tool && recipe.itemType != ItemType.Weapon) return MaterialType.None;

        return recipe.material;
    }

    /// <summary>
    /// Returns the food value of the named item, or 0 if it is not a food item.
    /// </summary>
    public static int GetFoodValue(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return 0;
        if (!_recipeByItemName.TryGetValue(itemName, out var recipe)) return 0;
        return recipe.IsFood ? recipe.foodValue : 0;
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

        if (matStats == null)
        {
            Debug.LogWarning($"RecipeManager: {material} has no tool/weapon stats (Leather is armour-only). Damage multiplier defaulting to 1.");
            matStats = new MaterialStats { damage = 1f, hardness = 1f };
        }

        return new WeaponTypeStats
        {
            damage      = baseStats.damage + matStats.damage,
            reach       = baseStats.reach,
            attackSpeed = baseStats.attackSpeed
        };
    }

    /// <summary>
    /// Returns the damage reduction percentage (0–1) for this armour recipe.
    /// E.g. 0.05 = 5 % reduction per piece worn.
    /// Returns 0 for non-armour recipes or materials without DR (Wood / Stone).
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

        return settings.GetDamageReduction(material);
    }
}
