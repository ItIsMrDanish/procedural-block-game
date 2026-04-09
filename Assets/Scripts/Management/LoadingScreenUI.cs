using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class LoadingScreenUI : MonoBehaviour {

    [Header("Canvas Screens")]
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject loadingScreen;

    [Header("Progress UI")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI progressText;

    public void LoadScene(int sceneID) {

        mainMenu.SetActive(false);
        loadingScreen.SetActive(true);

        SceneManager.LoadScene(sceneID);

    }

    private void Start() {

        // Only run the progress tracker if we're already in the game scene
        if (World.Instance != null)
            StartCoroutine(WaitForWorld());

    }

    private IEnumerator WaitForWorld() {

        while (!World.IsReady) {

            float p = World.LoadProgress;

            if (progressBar != null)
                progressBar.value = p;

            if (progressText != null)
                progressText.text = Mathf.RoundToInt(p * 100f) + "%";

            yield return null;
        }

        if (progressBar != null) progressBar.value = 1f;
        if (progressText != null) progressText.text = "100%";

        gameObject.SetActive(false);

    }
}
