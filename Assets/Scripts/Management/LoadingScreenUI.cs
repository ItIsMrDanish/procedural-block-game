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

        // mainMenu is optional - only set when called from the Play button directly
        if (mainMenu != null) mainMenu.SetActive(false);
        loadingScreen.SetActive(true);

        DontDestroyOnLoad(gameObject);

        SceneManager.LoadScene(sceneID);
        StartCoroutine(WaitForWorld());

    }

    private IEnumerator WaitForWorld() {

        while (!World.IsReady) {

            if (progressBar != null)
                progressBar.value = World.LoadProgress;

            if (progressText != null)
                progressText.text = Mathf.RoundToInt(World.LoadProgress * 100f) + "%";

            yield return null;
        }

        if (progressBar != null) progressBar.value = 1f;
        if (progressText != null) progressText.text = "100%";

        gameObject.SetActive(false);

    }
}