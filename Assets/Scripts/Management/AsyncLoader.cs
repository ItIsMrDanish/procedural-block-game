using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class AsyncLoader : MonoBehaviour
{
    [Header("Canvas Screens")]
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private GameObject loadingScreen;

    [Header("Progress")]
    [SerializeField] private Slider progressBar;
    [SerializeField] private TextMeshProUGUI progressText;

    public void LoadScene(int sceneID)
    {
        mainMenu.SetActive(false);
        loadingScreen.SetActive(true);

        StartCoroutine(LoadAsync(sceneID));
    }

    IEnumerator LoadAsync(int sceneID)
    {
        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneID);
        
        while (!loadOperation.isDone)
        {
            float progress = Mathf.Clamp01(loadOperation.progress / 0.9f);
            progressBar.value = progress;
            progressText.text = Mathf.RoundToInt(progress * 100f) + "%";
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);
    }
}