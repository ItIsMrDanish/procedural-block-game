using UnityEngine;

/// <summary>
/// ScriptableObject representing a single item in the game world.
/// Assign one of these to an Ingredient slot so the recipe reads
/// the item name and icon from a single authoritative source.
///
/// Create via: Assets > Create > Bloxels > Item Definition
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "Bloxels/Item Definition", order = 0)]
public class ItemDefinition : ScriptableObject
{
    [Tooltip("Unique item identifier used by the inventory system.")]
    public string itemName;

    [Tooltip("Icon shown in the inventory, recipe list, and material panel.")]
    public Sprite icon;
}
