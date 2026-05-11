using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Pause Menu — integrates with the Player / World inUI pattern.
///
/// Setup:
///  1. Assign this component to any persistent GameObject.
///  2. Drag your PauseMenuPanel into pauseMenuPanel.
///  3. Drag the Player component into the player field.
///  4. In your InputSystem asset, add a "Pause" action to the Player map
///     bound to Escape (or Back on gamepad). Player.cs will call TogglePause().
///  5. Wire any Resume button's OnClick to PauseMenu.OnResumeButtonClicked().
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Pause Menu")]
    [Tooltip("The root Panel GameObject of your pause menu UI.")]
    [SerializeField] private GameObject pauseMenuPanel;

    [Header("References")]
    [Tooltip("Assign the Player component so the pause menu can sync inUI and cursor state.")]
    [SerializeField] private Player player;

    [Tooltip("Assign the Inventory component. Esc will close it before the pause menu can open.")]
    [SerializeField] private Inventory inventory;

    [Tooltip("Assign the CraftingMenu component. Esc will close it before the pause menu can open.")]
    [SerializeField] private CraftingMenu craftingMenu;

    public bool IsPaused { get; private set; } = false;

    private World _world;

    private void Start()
    {
        _world = GameObject.Find("World").GetComponent<World>();
        pauseMenuPanel.SetActive(false);

        // Auto-resolve optional references if not wired in the Inspector.
        if (player == null)
            player = FindFirstObjectByType<Player>();
        if (inventory == null)
            inventory = FindFirstObjectByType<Inventory>();
        if (craftingMenu == null)
            craftingMenu = FindFirstObjectByType<CraftingMenu>();

        if (player == null)
            Debug.LogError("PauseMenu: No Player found — ForceCloseUI() will not work. Drag the Player into the Inspector slot.");
        if (craftingMenu == null)
            Debug.LogWarning("PauseMenu: No CraftingMenu found — Esc will not close it before pausing.");
        if (inventory == null)
            Debug.LogWarning("PauseMenu: No Inventory found — Esc will not close it before pausing.");

        ResumeGame();
    }

    /// <summary>
    /// Called by Player.cs via the Pause input action.
    /// If Inventory or CraftingMenu is open, Esc closes that UI first and does NOT
    /// open the pause menu. A second Esc press (with both UIs already closed) pauses.
    /// </summary>
    public void TogglePause()
    {
        // Close whichever gameplay UI is open and bail out — don't pause yet.
        bool closedSomething = false;

        if (inventory != null && inventory.IsOpen)
        {
            inventory.CloseInventory();
            player?.ForceCloseUI(); // Player owns inUI + cursor state
            closedSomething = true;
        }

        if (craftingMenu != null && craftingMenu.IsOpen)
        {
            craftingMenu.CloseMenu();
            player?.ForceCloseUI();
            closedSomething = true;
        }

        if (closedSomething) return;

        // No gameplay UI was open — toggle the pause menu as normal.
        if (IsPaused)
            ResumeGame();
        else
            PauseGame();
    }

    private void PauseGame()
    {
        IsPaused = true;
        pauseMenuPanel.SetActive(true);
        Time.timeScale = 0f;

        // Unlock cursor so the player can click menu buttons.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Tell World a UI is open so Player skips look / block interaction.
        if (_world != null)
            _world.inUI = true;
    }

    private void ResumeGame()
    {
        IsPaused = false;
        pauseMenuPanel.SetActive(false);
        Time.timeScale = 1f;

        // Only restore gameplay cursor state if no other UI panel is open.
        // (Player.ToggleUI owns cursor state for inventory / crafting.)
        if (_world != null)
            _world.inUI = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ExitToMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
    }

    /// Wire this to your Resume Button's OnClick event in the Inspector.
    public void OnResumeButtonClicked() => ResumeGame();
}
