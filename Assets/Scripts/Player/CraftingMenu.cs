using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages the Crafting UI.
///
/// INPUT
/// ─────
/// No InputSystem lives here. Player.cs calls ToggleMenu() directly.
/// This script does NOT touch world.inUI or cursor state — Player.cs owns that.
/// </summary>
public class CraftingMenu : MonoBehaviour {
    // ───────────────────────────── Inspector fields ───────────────────────────

    [Header("Inventory Reference")]
    [Tooltip("Drag the Inventory component here. Used to check/consume materials.")]
    [SerializeField] private Inventory inventory;

    [Header("Menu Root")]
    [Tooltip("The top-level panel to show / hide.")]
    [SerializeField] private GameObject menuPanel;

    [Header("Recipe List (left side)")]
    [Tooltip("Content transform inside the left ScrollView's Viewport.")]
    [SerializeField] private Transform recipeListContent;

    [Tooltip("Prefab instantiated for each recipe in the list.\n" +
             "Must have: Button, an Image child for the icon, a TMP_Text child for the name.")]
    [SerializeField] private GameObject recipeEntryPrefab;

    [Header("Recipe Detail (right side)")]
    [Tooltip("Parent panel that holds the recipe detail widgets. Hidden until a recipe is selected.")]
    [SerializeField] private GameObject recipeDetailPanel;

    [Tooltip("TMP_Text that shows 'Recipe: <ItemName>'.")]
    [SerializeField] private TMP_Text recipeDetailTitle;

    [Tooltip("Image that shows the recipe output icon in the detail panel.")]
    [SerializeField] private Image recipeDetailIcon;

    [Tooltip("Content transform inside the material list ScrollView.")]
    [SerializeField] private Transform materialListContent;

    [Tooltip("Prefab for each material row.\n" +
             "Must have: an Image child for the icon, two TMP_Text children (name, amount).")]
    [SerializeField] private GameObject materialEntryPrefab;

    [Header("Craft Controls")]
    [Tooltip("Button that triggers crafting the selected recipe.")]
    [SerializeField] private Button craftButton;

    [Tooltip("Input field for how many times to craft (empty = 1).")]
    [SerializeField] private TMP_InputField craftAmountInput;

    [Header("Feedback")]
    [Tooltip("TMP_Text used to display 'Crafted x of Item' messages.")]
    [SerializeField] private TMP_Text craftFeedbackText;

    [Tooltip("Seconds the feedback message remains visible.")]
    [SerializeField][Range(1f, 8f)] private float feedbackDuration = 3f;

    [Header("Recipes")]
    [Tooltip("All available recipes. Drag Recipe ScriptableObjects here.")]
    [SerializeField] private List<RecipeManager> recipes = new List<RecipeManager>();

    // ───────────────────────────── Private state ──────────────────────────────

    private bool _menuOpen;

    public bool IsOpen => _menuOpen;
    private RecipeManager _selectedRecipe;
    private Coroutine _feedbackCoroutine;

    private readonly List<GameObject> _spawnedMaterialRows = new List<GameObject>();

    // ─────────────────────────────── Unity ───────────────────────────────────

    private void Awake() {
        // Resolve inventory in Awake so the reference exists before any Start runs.
        // The actual item counts are NOT read here — that happens when the menu opens,
        // by which point all Start() methods (including Inventory.Start) are complete.
        if (inventory == null)
            inventory = FindFirstObjectByType<Inventory>();

        if (inventory == null)
            Debug.LogError("CraftingMenu: No Inventory found! Drag it into the Inspector slot.");
    }

    private void Start() {

        menuPanel.SetActive(false);
        recipeDetailPanel.SetActive(false);

        if (craftFeedbackText != null)
            craftFeedbackText.gameObject.SetActive(false);

        craftButton.onClick.AddListener(OnCraftClicked);
        PopulateRecipeList();

        // Register unstackable items with the inventory so it enforces 1-per-slot.
        if (inventory != null) {
            foreach (RecipeManager recipe in recipes)
                if (recipe != null && recipe.unstackable)
                    inventory.RegisterUnstackable(recipe.ItemName);
        }
    }

    // ─────────────────────── Toggle — called by Player.cs ────────────────────

    /// <summary>
    /// Called by Player.cs from _controls.Player.Crafting.performed.
    /// Does NOT touch world.inUI or cursor lock — Player.cs manages those.
    /// </summary>
    public void ToggleMenu() {
        _menuOpen = !_menuOpen;
        menuPanel.SetActive(_menuOpen);

        if (!_menuOpen) {
            _selectedRecipe = null;
            recipeDetailPanel.SetActive(false);
        } else {
            Canvas.ForceUpdateCanvases();
            if (_selectedRecipe != null)
                PopulateMaterialList(_selectedRecipe);
        }
    }

    // ─────────────────────────────── Left panel ───────────────────────────────

    private void PopulateRecipeList() {
        foreach (Transform child in recipeListContent)
            Destroy(child.gameObject);

        foreach (RecipeManager recipe in recipes) {
            GameObject entry = Instantiate(recipeEntryPrefab, recipeListContent);
            ConfigureRecipeEntry(entry, recipe);
        }
    }

    private void ConfigureRecipeEntry(GameObject entry, RecipeManager recipe) {
        Image icon = entry.GetComponentInChildren<Image>();
        if (icon != null && recipe.ItemIcon != null) icon.sprite = recipe.ItemIcon;

        TMP_Text label = entry.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = recipe.ItemName;

        Button btn = entry.GetComponent<Button>();
        if (btn != null) {
            RecipeManager captured = recipe;
            btn.onClick.AddListener(() => SelectRecipe(captured));
        }
    }

    // ─────────────────────────────── Right panel ──────────────────────────────

    private void SelectRecipe(RecipeManager recipe) {
        _selectedRecipe = recipe;
        recipeDetailPanel.SetActive(true);

        if (recipeDetailTitle != null) recipeDetailTitle.text = $"Recipe: {recipe.ItemName}";
        if (recipeDetailIcon != null && recipe.ItemIcon != null) recipeDetailIcon.sprite = recipe.ItemIcon;

        PopulateMaterialList(recipe);
    }

    private void PopulateMaterialList(RecipeManager recipe) {
        foreach (GameObject row in _spawnedMaterialRows) Destroy(row);
        _spawnedMaterialRows.Clear();

        foreach (Ingredient ingredient in recipe.ingredients) {
            GameObject row = Instantiate(materialEntryPrefab, materialListContent);
            _spawnedMaterialRows.Add(row);
            ConfigureMaterialRow(row, ingredient);
        }
    }

    private void ConfigureMaterialRow(GameObject row, Ingredient ingredient) {
        Image icon = row.GetComponentInChildren<Image>();
        if (icon != null && ingredient.Icon != null) icon.sprite = ingredient.Icon;

        TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>();
        if (texts.Length > 0) texts[0].text = ingredient.ItemName;
        if (texts.Length > 1) {
            int have = inventory != null ? inventory.GetAmount(ingredient.ItemName) : 0;
            texts[1].text = $"{have} / {ingredient.amount}";
            texts[1].color = have >= ingredient.amount ? Color.white : Color.red;
        }
    }

    // ─────────────────────────────── Crafting ─────────────────────────────────

    private void OnCraftClicked() {
        if (_selectedRecipe == null) return;
        if (inventory == null) { Debug.LogWarning("CraftingMenu: No Inventory assigned."); return; }

        int amount = ParseCraftAmount();

        if (!_selectedRecipe.CanCraft(inventory, amount)) {
            ShowFeedback($"Not enough materials to craft {_selectedRecipe.ItemName}!");
            return;
        }

        _selectedRecipe.Craft(inventory, amount);

        // Craft() adds items via IInventory.AddItem(string, int) which carries no Sprite.
        // Backfill the icon from the recipe onto any slot that is still missing it.
        if (_selectedRecipe.ItemIcon != null)
            inventory.SetIconForItem(_selectedRecipe.ItemName, _selectedRecipe.ItemIcon);

        ShowFeedback($"Crafted {_selectedRecipe.outputAmount * amount}x {_selectedRecipe.ItemName}");

        // Refresh material counts to reflect new inventory state.
        PopulateMaterialList(_selectedRecipe);
    }

    private int ParseCraftAmount() {
        if (craftAmountInput == null || string.IsNullOrWhiteSpace(craftAmountInput.text)) return 1;
        if (int.TryParse(craftAmountInput.text, out int parsed) && parsed >= 1) return parsed;
        Debug.LogWarning($"CraftingMenu: '{craftAmountInput.text}' is not a valid positive integer. Defaulting to 1.");
        return 1;
    }

    // ─────────────────────────────── Feedback ─────────────────────────────────

    private void ShowFeedback(string message) {
        if (craftFeedbackText == null) return;
        if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
        _feedbackCoroutine = StartCoroutine(FeedbackRoutine(message));
    }

    private IEnumerator FeedbackRoutine(string message) {
        craftFeedbackText.text = message;
        craftFeedbackText.gameObject.SetActive(true);
        yield return StartCoroutine(FadeTo(craftFeedbackText, 0f, 1f, 0.2f));
        yield return new WaitForSeconds(feedbackDuration);
        yield return StartCoroutine(FadeTo(craftFeedbackText, 1f, 0f, 0.4f));
        craftFeedbackText.gameObject.SetActive(false);
        _feedbackCoroutine = null;
    }

    private IEnumerator FadeTo(TMP_Text text, float from, float to, float duration) {
        float elapsed = 0f;
        Color c = text.color;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(from, to, elapsed / duration);
            text.color = c;
            yield return null;
        }
        c.a = to;
        text.color = c;
    }
}