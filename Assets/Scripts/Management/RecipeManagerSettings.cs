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
    None = 0,
    Wood,
    Stone,
    Leather,
    Metal,
    Tourmaline
}

/// <summary>Sub-type for tool recipes.</summary>
public enum ToolType
{
    None = 0,
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
/// Values are additive: the final stat = weapon/tool base + material value.
/// </summary>
[Serializable]
public class MaterialStats
{
    [Tooltip("Damage added to the weapon/tool base damage. Base (no bonus) = 0.")]
    public float damage = 0f;

    [Tooltip("Hardness added to the tool base hardness. Base (no bonus) = 0.")]
    public float hardness = 0f;
}

/// <summary>
/// Stats for Leather — armour-only material.
/// Has no Damage or Hardness since Leather cannot be used for tools or weapons.
/// </summary>
[Serializable]
public class LeatherStats
{
    [Tooltip("Percentage of incoming damage blocked per armour piece worn (0–1). Base = 0 %.")]
    [Range(0f, 0.25f)]
    public float damageReduction = 0f;
}

/// <summary>
/// Extended material stats for Metal and Tourmaline — materials used for tools/weapons
/// that also provide percentage-based armour damage reduction when worn.
/// </summary>
[Serializable]
public class ArmorMaterialStats : MaterialStats
{
    [Tooltip("Percentage of incoming damage blocked per armour piece worn (0–1). Base = 0 %.")]
    [Range(0f, 0.25f)]
    public float damageReduction = 0f;
}

// ═══════════════════════════════════════════════════════════════════════════════
//  Per-weapon-type stats
// ═══════════════════════════════════════════════════════════════════════════════

[Serializable]
public class WeaponTypeStats
{
    [Tooltip("Base damage for this weapon type. Material damage is added on top.")]
    public float damage = 1f;

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

    [Header("Material Stats")]
    [Tooltip("Stats for Wood-tier items. No damage reduction (Wood cannot be armour).")]
    public MaterialStats wood = new MaterialStats { damage = 1f, hardness = 1f };

    [Tooltip("Stats for Stone-tier items. No damage reduction (Stone cannot be armour).")]
    public MaterialStats stone = new MaterialStats { damage = 2f, hardness = 2f };

    [Tooltip("Stats for Leather-tier items. Armour-only material — no damage or hardness.")]
    public LeatherStats leather = new LeatherStats { damageReduction = 0.05f };

    [Tooltip("Stats for Metal-tier items. Supports armour damage reduction.")]
    public ArmorMaterialStats metal = new ArmorMaterialStats { damage = 2f, hardness = 2f, damageReduction = 0.1f };

    [Tooltip("Stats for Tourmaline-tier items. Supports armour damage reduction.")]
    public ArmorMaterialStats tourmaline = new ArmorMaterialStats { damage = 2f, hardness = 2f, damageReduction = 0.15f };

    // ── Weapon type stats ─────────────────────────────────────────────────────

    [Header("Weapon Type Stats")]
    [Tooltip("Base stats for Axe weapons (multiplied by material damage at runtime).")]
    public WeaponTypeStats axeWeapon = new WeaponTypeStats { damage = 6f, reach = 2f, attackSpeed = 1f };

    [Tooltip("Base stats for Sword weapons (multiplied by material damage at runtime).")]
    public WeaponTypeStats sword = new WeaponTypeStats { damage = 5f, reach = 2f, attackSpeed = 1.5f };

    // ── Convenience accessors ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the base <see cref="MaterialStats"/> for tool/weapon materials.
    /// NOTE: Leather is armour-only and has no MaterialStats — returns null for Leather.
    /// </summary>
    public MaterialStats GetMaterialStats(MaterialType mat)
    {
        switch (mat)
        {
            case MaterialType.Wood:       return wood;
            case MaterialType.Stone:      return stone;
            case MaterialType.Metal:      return metal;
            case MaterialType.Tourmaline: return tourmaline;
            default:                      return null; // Leather has no tool/weapon stats
        }
    }

    /// <summary>
    /// Returns the damage reduction percentage (0–1) for armour materials.
    /// Returns 0 for Wood and Stone (no armour protection).
    /// </summary>
    public float GetDamageReduction(MaterialType mat)
    {
        switch (mat)
        {
            case MaterialType.Leather:    return leather.damageReduction;
            case MaterialType.Metal:      return metal.damageReduction;
            case MaterialType.Tourmaline: return tourmaline.damageReduction;
            default:                      return 0f;
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
