using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.UI;
using TMPro;

public class LoadingSceneManager : MonoBehaviour {

    [Header("Loading UI")]
    public Slider progressBar;               // Optional progress bar
    public TextMeshProUGUI progressText;     // Optional "Loading... 73%" label

    private void Start() {

        if (string.IsNullOrEmpty(Scenemanage.TargetScene)) {

            Debug.LogError("LoadingSceneManager: No target scene set. Returning to main menu.");
            SceneManager.LoadScene(0);
            return;
        }

        StartCoroutine(LoadTargetScene());
    }

    private IEnumerator LoadTargetScene() {

        // Begin loading the target scene in the background, but don't activate it yet
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(Scenemanage.TargetScene, LoadSceneMode.Single);
        asyncLoad.allowSceneActivation = false;

        while (!asyncLoad.isDone) {

            // Progress goes from 0 to 0.9 while loading, then jumps to 1 on activation
            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);

            if (progressBar != null)
                progressBar.value = progress;

            if (progressText != null)
                progressText.text = "Loading... " + Mathf.RoundToInt(progress * 100f) + "%";

            // Once fully loaded, allow the scene to activate
            if (asyncLoad.progress >= 0.9f) {

                if (progressText != null)
                    progressText.text = "Loading... 100%";

                // Optional: small delay so the player sees 100% before switching
                yield return new WaitForSeconds(0.5f);

                asyncLoad.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}
