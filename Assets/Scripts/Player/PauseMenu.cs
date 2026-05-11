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

    public bool IsPaused { get; private set; } = false;

    private World _world;

    private void Start()
    {
        _world = GameObject.Find("World").GetComponent<World>();
        pauseMenuPanel.SetActive(false);
        ResumeGame();
    }

    /// <summary>
    /// Called by Player.cs via the Pause input action.
    /// Toggles the pause menu open or closed.
    /// </summary>
    public void TogglePause()
    {
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
