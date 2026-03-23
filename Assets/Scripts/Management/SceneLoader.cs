using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.UI;
using TMPro;

public class SceneLoader : MonoBehaviour {

    public static SceneLoader Instance { get; private set; }
    public string TargetScene { get; private set; }

    private void Awake() {

        if (Instance != null) {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void LoadWithLoadingScreen(string sceneName) {

        TargetScene = sceneName;
        SceneManager.LoadScene("Loading", LoadSceneMode.Single);
    }

    public IEnumerator LoadTargetSceneAsync(Slider progressBar, TextMeshProUGUI progressText) {

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(TargetScene, LoadSceneMode.Single);
        asyncLoad.allowSceneActivation = false;

        while (!asyncLoad.isDone) {

            float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);

            if (progressBar != null)
                progressBar.value = progress;

            if (progressText != null)
                progressText.text = "Loading... " + Mathf.RoundToInt(progress * 100f) + "%";

            if (asyncLoad.progress >= 0.9f) {

                if (progressText != null)
                    progressText.text = "Loading... 100%";

                yield return new WaitForSeconds(0.5f);
                asyncLoad.allowSceneActivation = true;
            }

            yield return null;
        }
    }
}
