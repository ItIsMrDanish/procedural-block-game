using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Attach to your LoadingCanvas in the MainMenu scene.
// When LoadScene() is called, the canvas marks itself DontDestroyOnLoad so it
// survives into the World scene, where it reads World.LoadProgress each frame
// and hides itself once World.IsReady is true.

public class LoadingScreenUI : MonoBehaviour {

    [Header("Canvas Screens")]
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject loadingScreen;

    [Header("Progress UI")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI progressText;

    // Called by your Play button — pass the World scene build index (1)
    public void LoadScene(int sceneID) {

        mainMenu.SetActive(false);
        loadingScreen.SetActive(true);

        // Persist this canvas into the next scene so it can read World.LoadProgress
        DontDestroyOnLoad(gameObject);

        SceneManager.LoadScene(sceneID);
        StartCoroutine(WaitForWorld());

    }

    private IEnumerator WaitForWorld() {

        // Wait until World exists and has finished initialising
        while (!World.IsReady) {

            if (progressBar != null)
                progressBar.value = World.LoadProgress;

            if (progressText != null)
                progressText.text = Mathf.RoundToInt(World.LoadProgress * 100f) + "%";

            yield return null;
        }

        // Snap to 100% then hide
        if (progressBar != null) progressBar.value = 1f;
        if (progressText != null) progressText.text = "100%";

        gameObject.SetActive(false);

    }
}
