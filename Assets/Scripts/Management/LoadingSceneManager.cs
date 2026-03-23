using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoadingSceneManager : MonoBehaviour {

    [Header("Loading UI (both optional)")]
    public Slider progressBar;
    public TextMeshProUGUI progressText;

    private void Start() {

        if (SceneLoader.Instance == null) {
            Debug.LogError("LoadingSceneManager: SceneLoader instance not found!");
            return;
        }

        StartCoroutine(SceneLoader.Instance.LoadTargetSceneAsync(progressBar, progressText));
    }
}
