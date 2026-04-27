using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Manages the Crafting UI.
///
/// HOW TO SET UP
/// ─────────────
/// 1. Attach this script to the root CraftingMenu GameObject.
/// 2. Assign every [SerializeField] in the Inspector (see comments below).
/// 3. Create Recipe ScriptableObjects via Assets > Create > Crafting > Recipe
///    and drag them into the `recipes` list.
/// 4. Build two prefabs:
///    • RecipeListEntry  – has a Button + icon Image + name TMP_Text
///    • MaterialEntry    – has an icon Image + name TMP_Text + amount TMP_Text
///    Assign them to the matching fields below.
/// 5. Make sure your inventory MonoBehaviour implements IInventory and is
///    referenced (or fetched) in GetInventory().
/// </summary>
public class CraftingMenu : MonoBehaviour
{
    // ───────────────────────────── Inspector fields ──────────────────────────

    [Header("Input")]
    [Tooltip("Assign a Button-type binding (default: C key)")]
    [SerializeField]
    private InputAction toggleMenuAction = new InputAction("ToggleMenu", InputActionType.Button, "<Keyboard>/c");

    [Header("Menu Root")]
    [Tooltip("The top-level panel to show / hide (can be this GameObject's own panel).")]
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
    [Tooltip("TMP_Text used to display 'Crafted x of Item' messages. Should start hidden/alpha=0.")]
    [SerializeField] private TMP_Text craftFeedbackText;

    [Tooltip("Seconds the feedback message remains visible.")]
    [SerializeField] [Range(1f, 8f)] private float feedbackDuration = 3f;

    [Header("Recipes")]
    [Tooltip("All available recipes. Drag Recipe ScriptableObjects here.")]
    [SerializeField] private List<RecipeManager> recipes = new List<RecipeManager>();

    // ───────────────────────────── Private state ─────────────────────────────

    private RecipeManager selectedRecipe;
    private bool   menuOpen;
    private Coroutine feedbackCoroutine;

    // Cached list of spawned material rows so we can clear them efficiently.
    private readonly List<GameObject> spawnedMaterialRows = new List<GameObject>();

    // ─────────────────────────────── Unity ───────────────────────────────────

    private void Start()
    {
        // Start closed.
        menuPanel.SetActive(false);
        recipeDetailPanel.SetActive(false);

        if (craftFeedbackText != null)
            craftFeedbackText.gameObject.SetActive(false);

        craftButton.onClick.AddListener(OnCraftClicked);

        PopulateRecipeList();
    }
    private void Update()
    {

        if (Keyboard.current.cKey.wasPressedThisFrame)
        {
            menuPanel.SetActive(!menuPanel.activeSelf);
        }
    }
    //private void OnEnable()
    //{
    //    toggleMenuAction.performed += _ => ToggleMenu();
    //    toggleMenuAction.Enable();
    //}

    //private void OnDisable()
    //{
    //    toggleMenuAction.performed -= _ => ToggleMenu();
    //    toggleMenuAction.Disable();
    //}

    // ─────────────────────────────── Menu ────────────────────────────────────

    private void ToggleMenu()
    {
        menuOpen = !menuOpen;
        menuPanel.SetActive(menuOpen);

        if (!menuOpen)
        {
            // Clear selection when closing.
            selectedRecipe = null;
            recipeDetailPanel.SetActive(false);
        }
    }

    // ─────────────────────────────── Left panel ───────────────────────────────

    private void PopulateRecipeList()
    {
        // Clear existing children first (useful if called at runtime to refresh).
        foreach (Transform child in recipeListContent)
            Destroy(child.gameObject);

        foreach (RecipeManager recipe in recipes)
        {
            GameObject entry = Instantiate(recipeEntryPrefab, recipeListContent);
            ConfigureRecipeEntry(entry, recipe);
        }
    }

    /// <summary>
    /// Wires up a single recipe list entry.
    /// Assumes prefab structure: root has Button; first Image child = icon;
    /// first TMP_Text child = label.
    /// </summary>
    private void ConfigureRecipeEntry(GameObject entry, RecipeManager recipe)
    {
        // Icon
        Image icon = entry.GetComponentInChildren<Image>();
        if (icon != null && recipe.itemIcon != null)
            icon.sprite = recipe.itemIcon;

        // Label
        TMP_Text label = entry.GetComponentInChildren<TMP_Text>();
        if (label != null)
            label.text = recipe.itemName;

        // Click → select recipe
        Button btn = entry.GetComponent<Button>();
        if (btn != null)
        {
            // Capture loop variable safely.
            RecipeManager captured = recipe;
            btn.onClick.AddListener(() => SelectRecipe(captured));
        }
    }

    // ─────────────────────────────── Right panel ──────────────────────────────

    private void SelectRecipe(RecipeManager recipe)
    {
        selectedRecipe = recipe;
        recipeDetailPanel.SetActive(true);

        // Header
        if (recipeDetailTitle != null)
            recipeDetailTitle.text = $"Recipe: {recipe.itemName}";

        if (recipeDetailIcon != null && recipe.itemIcon != null)
            recipeDetailIcon.sprite = recipe.itemIcon;

        PopulateMaterialList(recipe);
    }

    private void PopulateMaterialList(RecipeManager recipe)
    {
        // Destroy previous rows.
        foreach (GameObject row in spawnedMaterialRows)
            Destroy(row);
        spawnedMaterialRows.Clear();

        IInventory inventory = GetInventory();

        foreach (Ingredient ingredient in recipe.ingredients)
        {
            GameObject row = Instantiate(materialEntryPrefab, materialListContent);
            spawnedMaterialRows.Add(row);
            ConfigureMaterialRow(row, ingredient, inventory);
        }
    }

    /// <summary>
    /// Wires up a material row.
    /// Assumes prefab structure: first Image = icon; TMP_Text[0] = name;
    /// TMP_Text[1] = amount (e.g. "2 / 5").
    /// </summary>
    private void ConfigureMaterialRow(GameObject row, Ingredient ingredient, IInventory inventory)
    {
        Image icon = row.GetComponentInChildren<Image>();
        if (icon != null && ingredient.icon != null)
            icon.sprite = ingredient.icon;

        TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>();

        if (texts.Length > 0)
            texts[0].text = ingredient.itemName;

        if (texts.Length > 1)
        {
            int have = inventory != null ? inventory.GetAmount(ingredient.itemName) : 0;
            texts[1].text = $"{have} / {ingredient.amount}";

            // Tint red when the player doesn't have enough.
            texts[1].color = have >= ingredient.amount ? Color.white : Color.red;
        }
    }

    // ─────────────────────────────── Crafting ─────────────────────────────────

    private void OnCraftClicked()
    {
        if (selectedRecipe == null) return;

        int amount = ParseCraftAmount();
        IInventory inventory = GetInventory();

        if (inventory == null)
        {
            Debug.LogWarning("CraftingMenu: No IInventory found. Override GetInventory().");
            return;
        }

        if (!selectedRecipe.CanCraft(inventory, amount))
        {
            ShowFeedback($"Not enough materials to craft {selectedRecipe.itemName}!");
            return;
        }

        selectedRecipe.Craft(inventory, amount);

        int totalProduced = selectedRecipe.outputAmount * amount;
        ShowFeedback($"Crafted {totalProduced}x {selectedRecipe.itemName}");

        // Refresh material counts to reflect the new inventory state.
        PopulateMaterialList(selectedRecipe);
    }

    /// <summary>Returns the craft amount from the input field, defaulting to 1.</summary>
    private int ParseCraftAmount()
    {
        if (craftAmountInput == null || string.IsNullOrWhiteSpace(craftAmountInput.text))
            return 1;

        if (int.TryParse(craftAmountInput.text, out int parsed) && parsed >= 1)
            return parsed;

        Debug.LogWarning($"CraftingMenu: '{craftAmountInput.text}' is not a valid positive integer. Defaulting to 1.");
        return 1;
    }

    // ─────────────────────────────── Feedback ─────────────────────────────────

    private void ShowFeedback(string message)
    {
        if (craftFeedbackText == null) return;

        if (feedbackCoroutine != null)
            StopCoroutine(feedbackCoroutine);

        feedbackCoroutine = StartCoroutine(FeedbackRoutine(message));
    }

    private IEnumerator FeedbackRoutine(string message)
    {
        craftFeedbackText.text    = message;
        craftFeedbackText.gameObject.SetActive(true);

        // Fade in
        yield return StartCoroutine(FadeTo(craftFeedbackText, 0f, 1f, 0.2f));

        // Hold
        yield return new WaitForSeconds(feedbackDuration);

        // Fade out
        yield return StartCoroutine(FadeTo(craftFeedbackText, 1f, 0f, 0.4f));

        craftFeedbackText.gameObject.SetActive(false);
        feedbackCoroutine = null;
    }

    private IEnumerator FadeTo(TMP_Text text, float from, float to, float duration)
    {
        float elapsed = 0f;
        Color c = text.color;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            c.a = Mathf.Lerp(from, to, elapsed / duration);
            text.color = c;
            yield return null;
        }

        c.a = to;
        text.color = c;
    }

    // ─────────────────────────────── Inventory ────────────────────────────────

    /// <summary>
    /// Override or modify this method to return your concrete IInventory.
    /// By default it searches the scene for any MonoBehaviour implementing IInventory.
    /// </summary>
    protected virtual IInventory GetInventory()
    {
        // Searches all MonoBehaviours in the scene for one that implements IInventory.
        // For better performance, cache this result or assign it in the Inspector.
        foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb is IInventory inv)
                return inv;
        }

        return null;
    }
}
