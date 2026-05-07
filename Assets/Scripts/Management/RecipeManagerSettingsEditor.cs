#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for <see cref="RecipeManagerSettings"/>.
/// Groups material stats into "No Armour Protection" and "With Armour Protection"
/// sections, and shows weapon type stats in a clearly labelled block.
/// </summary>
[CustomEditor(typeof(RecipeManagerSettings))]
public class RecipeManagerSettingsEditor : Editor
{
    // Foldout state (persisted across domain reloads via EditorPrefs)
    static bool _foldMaterialBasic  = true;
    static bool _foldMaterialArmor  = true;
    static bool _foldWeaponTypes    = true;

    // Material stats — basic
    SerializedProperty _wood;
    SerializedProperty _stone;

    // Material stats — armour capable
    SerializedProperty _leather;
    SerializedProperty _metal;
    SerializedProperty _tourmaline;

    // Weapon type stats
    SerializedProperty _axeWeapon;
    SerializedProperty _sword;

    void OnEnable()
    {
        _wood       = serializedObject.FindProperty("wood");
        _stone      = serializedObject.FindProperty("stone");
        _leather    = serializedObject.FindProperty("leather");
        _metal      = serializedObject.FindProperty("metal");
        _tourmaline = serializedObject.FindProperty("tourmaline");
        _axeWeapon  = serializedObject.FindProperty("axeWeapon");
        _sword      = serializedObject.FindProperty("sword");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ── Header ──────────────────────────────────────────────────────────
        EditorGUILayout.HelpBox(
            "This component holds all crafting stat tables.\n" +
            "Place it on any persistent GameObject in your scene (e.g. GameManager).\n" +
            "Access at runtime via RecipeManagerSettings.Instance.",
            MessageType.Info);

        EditorGUILayout.Space(8);

        // ── Materials — No Armour Protection ────────────────────────────────
        _foldMaterialBasic = EditorGUILayout.BeginFoldoutHeaderGroup(
            _foldMaterialBasic, "Materials — No Armour Protection");
        if (_foldMaterialBasic)
        {
            EditorGUI.indentLevel++;
            DrawMaterialBasic(_wood,  "Wood");
            DrawMaterialBasic(_stone, "Stone");
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(4);

        // ── Materials — With Armour Protection ──────────────────────────────
        _foldMaterialArmor = EditorGUILayout.BeginFoldoutHeaderGroup(
            _foldMaterialArmor, "Materials — With Armour Protection");
        if (_foldMaterialArmor)
        {
            EditorGUI.indentLevel++;
            DrawMaterialLeather(_leather, "Leather");
            DrawMaterialArmor(_metal,      "Metal");
            DrawMaterialArmor(_tourmaline, "Tourmaline");
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        EditorGUILayout.Space(4);

        // ── Weapon Types ────────────────────────────────────────────────────
        _foldWeaponTypes = EditorGUILayout.BeginFoldoutHeaderGroup(
            _foldWeaponTypes, "Weapon Types");
        if (_foldWeaponTypes)
        {
            EditorGUI.indentLevel++;
            DrawWeaponType(_axeWeapon, "Axe");
            DrawWeaponType(_sword,     "Sword");
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        serializedObject.ApplyModifiedProperties();
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    static readonly GUIStyle _subHeader = null; // resolved lazily

    static GUIStyle SubHeaderStyle()
    {
        var s = new GUIStyle(EditorStyles.boldLabel);
        s.normal.textColor = new Color(0.75f, 0.85f, 1f);
        return s;
    }

    void DrawMaterialLeather(SerializedProperty prop, string label)
    {
        EditorGUILayout.LabelField(label, SubHeaderStyle());
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("damageReduction"), new GUIContent("Damage Reduction (0–1)"));
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(2);
    }

    void DrawMaterialBasic(SerializedProperty prop, string label)
    {
        EditorGUILayout.LabelField(label, SubHeaderStyle());
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("damage"),   new GUIContent("Damage"));
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("hardness"), new GUIContent("Hardness"));
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(2);
    }

    void DrawMaterialArmor(SerializedProperty prop, string label)
    {
        EditorGUILayout.LabelField(label, SubHeaderStyle());
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("damage"),          new GUIContent("Damage"));
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("hardness"),        new GUIContent("Hardness"));
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("damageReduction"), new GUIContent("Damage Reduction (0–1)"));
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(2);
    }

    void DrawWeaponType(SerializedProperty prop, string label)
    {
        EditorGUILayout.LabelField(label, SubHeaderStyle());
        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("damage"),      new GUIContent("Damage"));
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("reach"),       new GUIContent("Reach"));
        EditorGUILayout.PropertyField(prop.FindPropertyRelative("attackSpeed"), new GUIContent("Attack Speed"));
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(2);
    }
}
#endif
