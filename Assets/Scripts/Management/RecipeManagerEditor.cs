#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for <see cref="RecipeManager"/>.
/// Shows / hides fields based on <see cref="ItemType"/>, <see cref="isArmor"/>, etc.
/// </summary>
[CustomEditor(typeof(RecipeManager))]
public class RecipeManagerEditor : Editor
{
    // Serialized property references
    SerializedProperty _outputItem;
    SerializedProperty _outputAmount;
    SerializedProperty _unstackable;
    SerializedProperty _itemType;
    SerializedProperty _toolType;
    SerializedProperty _weaponType;
    SerializedProperty _material;
    SerializedProperty _isArmor;
    SerializedProperty _armorSlot;
    SerializedProperty _ingredients;

    void OnEnable()
    {
        _outputItem   = serializedObject.FindProperty("outputItem");
        _outputAmount = serializedObject.FindProperty("outputAmount");
        _unstackable  = serializedObject.FindProperty("unstackable");
        _itemType     = serializedObject.FindProperty("itemType");
        _toolType     = serializedObject.FindProperty("toolType");
        _weaponType   = serializedObject.FindProperty("weaponType");
        _material     = serializedObject.FindProperty("material");
        _isArmor      = serializedObject.FindProperty("isArmor");
        _armorSlot    = serializedObject.FindProperty("armorSlot");
        _ingredients  = serializedObject.FindProperty("ingredients");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ── Output ──────────────────────────────────────────────────────────
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_outputItem,   new GUIContent("Output Item"));
        EditorGUILayout.PropertyField(_outputAmount, new GUIContent("Output Amount"));
        EditorGUILayout.PropertyField(_unstackable,  new GUIContent("Unstackable"));

        EditorGUILayout.Space(6);

        // ── Type ────────────────────────────────────────────────────────────
        EditorGUILayout.LabelField("Type", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_itemType, new GUIContent("Type"));

        var type = (ItemType)_itemType.enumValueIndex;

        EditorGUILayout.Space(4);

        switch (type)
        {
            // ── Tool ────────────────────────────────────────────────────────
            case ItemType.Tool:
                EditorGUILayout.PropertyField(_toolType, new GUIContent("Tool Type"));
                DrawMaterialDropdown();
                break;

            // ── Weapon ──────────────────────────────────────────────────────
            case ItemType.Weapon:
                EditorGUILayout.PropertyField(_weaponType, new GUIContent("Weapon Type"));
                DrawMaterialDropdown();
                DrawWeaponStatPreview();
                break;

            // ── Item (may be armour) ─────────────────────────────────────────
            case ItemType.Item:
                EditorGUILayout.PropertyField(_isArmor, new GUIContent("Is Armor?"));
                if (_isArmor.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_armorSlot, new GUIContent("Armor Type"));
                    DrawMaterialDropdown();
                    DrawArmorStatPreview();
                    EditorGUI.indentLevel--;
                }
                break;

            // ── Block — no extra fields ──────────────────────────────────────
            case ItemType.Block:
                break;
        }

        EditorGUILayout.Space(6);

        // ── Ingredients ─────────────────────────────────────────────────────
        EditorGUILayout.LabelField("Ingredients", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_ingredients, new GUIContent("Ingredients"), true);

        serializedObject.ApplyModifiedProperties();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    void DrawMaterialDropdown()
    {
        EditorGUILayout.PropertyField(_material, new GUIContent("Material"));
    }

    /// <summary>
    /// Shows a read-only stat preview box for weapons, pulling from the settings singleton.
    /// Only displayed when the application is playing (singleton exists) or when a settings
    /// asset is found in the scene.
    /// </summary>
    void DrawWeaponStatPreview()
    {
        var settings = RecipeManagerSettings.Instance
                       ?? Object.FindObjectOfType<RecipeManagerSettings>();
        if (settings == null) return;

        var recipe   = (RecipeManager)target;
        var wt       = (WeaponType)_weaponType.enumValueIndex;
        var mat      = (MaterialType)_material.enumValueIndex;
        var baseW    = settings.GetWeaponTypeStats(wt);
        var baseMat  = settings.GetMaterialStats(mat);

        EditorGUILayout.Space(2);
        EditorGUILayout.HelpBox(
            $"Resolved Weapon Stats (preview)\n" +
            $"  Damage      : {baseW.damage + baseMat.damage:F2}  ({baseW.damage:F2} base + {baseMat.damage:F2} material)\n" +
            $"  Reach       : {baseW.reach:F2}\n" +
            $"  Attack Speed: {baseW.attackSpeed:F2}",
            MessageType.None);
    }

    void DrawArmorStatPreview()
    {
        var settings = RecipeManagerSettings.Instance
                       ?? Object.FindObjectOfType<RecipeManagerSettings>();
        if (settings == null) return;

        var mat = (MaterialType)_material.enumValueIndex;
        float dr = settings.GetDamageReduction(mat);

        if (dr <= 0f)
        {
            EditorGUILayout.HelpBox(
                $"{mat} does not provide Damage Reduction (only Leather, Metal, Tourmaline do).",
                MessageType.Info);
            return;
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.HelpBox(
            $"Resolved Armor Stats (preview)\n" +
            $"  Damage Reduction: {dr * 100f:F0} % per piece",
            MessageType.None);
    }
}
#endif
