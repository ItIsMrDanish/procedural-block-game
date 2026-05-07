using System;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  Enums shared across the crafting system
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Broad category of the item produced by a recipe.</summary>
public enum ItemType
{
    Item,
    Block,
    Tool,
    Weapon
}

/// <summary>Material tier applied to tools, weapons, and armour.</summary>
public enum MaterialType
{
    Wood,
    Stone,
    Leather,
    Metal,
    Tourmaline
}

/// <summary>Sub-type for tool recipes.</summary>
public enum ToolType
{
    Axe,
    Pickaxe,
    Shovel
}

/// <summary>Sub-type for weapon recipes.</summary>
public enum WeaponType
{
    Axe,
    Sword
}

/// <summary>Slot an armour item occupies.</summary>
public enum ArmorSlotType
{
    Helmet,
    Chestplate,
    Leggings,
    Boots
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Per-material stats
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Stats common to every material (applied to tools, weapons, and armour bases).
/// </summary>
[Serializable]
public class MaterialStats
{
    [Tooltip("Base damage bonus granted by this material.")]
    public float damage = 1f;

    [Tooltip("Hardness / durability multiplier for tools made from this material.")]
    public float hardness = 1f;
}

/// <summary>
/// Extended material stats for materials that also provide armour protection
/// (Leather, Metal, Tourmaline). Wood and Stone do NOT use this.
/// </summary>
[Serializable]
public class ArmorMaterialStats : MaterialStats
{
    [Tooltip("Flat damage reduction provided when this material is worn as armour.")]
    public float damageReduction = 0f;
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Per-weapon-type stats
// ═══════════════════════════════════════════════════════════════════════════════

[Serializable]
public class WeaponTypeStats
{
    [Tooltip("Base damage for this weapon type (multiplied by material damage).")]
    public float damage = 5f;

    [Tooltip("Attack reach in world units.")]
    public float reach = 2f;

    [Tooltip("Attacks per second.")]
    public float attackSpeed = 1f;
}

// ═══════════════════════════════════════════════════════════════════════════════
//  RecipeManagerSettings  (MonoBehaviour — drop on any persistent GameObject)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Central settings object for the crafting system.
/// Drop this component on a persistent GameObject (e.g. GameManager) to
/// expose all material and weapon stat tables in the Inspector.
///
/// At runtime, access the singleton via <see cref="Instance"/>.
/// </summary>
public class RecipeManagerSettings : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static RecipeManagerSettings Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Material stats ────────────────────────────────────────────────────────

    [Header("Material Stats — No Armour Protection")]
    [Tooltip("Stats for Wood-tier items. No damage reduction (Wood cannot be armour).")]
    public MaterialStats wood = new MaterialStats { damage = 1f, hardness = 1f };

    [Tooltip("Stats for Stone-tier items. No damage reduction (Stone cannot be armour).")]
    public MaterialStats stone = new MaterialStats { damage = 2f, hardness = 3f };

    [Header("Material Stats — With Armour Protection")]
    [Tooltip("Stats for Leather-tier items. Supports armour damage reduction.")]
    public ArmorMaterialStats leather = new ArmorMaterialStats { damage = 1.5f, hardness = 2f, damageReduction = 1f };

    [Tooltip("Stats for Metal-tier items. Supports armour damage reduction.")]
    public ArmorMaterialStats metal = new ArmorMaterialStats { damage = 4f, hardness = 6f, damageReduction = 3f };

    [Tooltip("Stats for Tourmaline-tier items. Supports armour damage reduction.")]
    public ArmorMaterialStats tourmaline = new ArmorMaterialStats { damage = 7f, hardness = 10f, damageReduction = 6f };

    // ── Weapon type stats ─────────────────────────────────────────────────────

    [Header("Weapon Type Stats")]
    [Tooltip("Base stats for Axe weapons (multiplied by material damage at runtime).")]
    public WeaponTypeStats axeWeapon = new WeaponTypeStats { damage = 6f, reach = 1.8f, attackSpeed = 0.8f };

    [Tooltip("Base stats for Sword weapons (multiplied by material damage at runtime).")]
    public WeaponTypeStats sword = new WeaponTypeStats { damage = 5f, reach = 2f, attackSpeed = 1.2f };

    // ── Convenience accessors ─────────────────────────────────────────────────

    /// <summary>Returns the base <see cref="MaterialStats"/> for any material type.</summary>
    public MaterialStats GetMaterialStats(MaterialType mat)
    {
        switch (mat)
        {
            case MaterialType.Wood:       return wood;
            case MaterialType.Stone:      return stone;
            case MaterialType.Leather:    return leather;
            case MaterialType.Metal:      return metal;
            case MaterialType.Tourmaline: return tourmaline;
            default:                      return wood;
        }
    }

    /// <summary>
    /// Returns the <see cref="ArmorMaterialStats"/> for materials that support armour.
    /// Returns null for Wood and Stone (which have no damage reduction).
    /// </summary>
    public ArmorMaterialStats GetArmorMaterialStats(MaterialType mat)
    {
        switch (mat)
        {
            case MaterialType.Leather:    return leather;
            case MaterialType.Metal:      return metal;
            case MaterialType.Tourmaline: return tourmaline;
            default:                      return null;
        }
    }

    /// <summary>Returns the base <see cref="WeaponTypeStats"/> for a weapon type.</summary>
    public WeaponTypeStats GetWeaponTypeStats(WeaponType wt)
    {
        switch (wt)
        {
            case WeaponType.Axe:   return axeWeapon;
            case WeaponType.Sword: return sword;
            default:               return sword;
        }
    }
}
