using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections;
using UnityEngine.UI;
using TMPro;

public class Scenemanage : MonoBehaviour
{

    public GameObject mainMenuObject;
    public GameObject settingsObject;

    [Header("Main Menu UI Elements")]
    public TextMeshProUGUI seedField;

    [Header("Settings Menu UI Elements")]
    public Slider viewDstSlider;
    public TextMeshProUGUI viewDstText;
    public Slider mouseSlider;
    public TextMeshProUGUI mouseTxtSlider;
    public Toggle threadingToggle;
    public Toggle chunkAnimToggle;
    public TMP_Dropdown clouds;

    // Name of your loading scene (must be added to Build Settings)
    private const string LoadingSceneName = "Loading";

    // Static so the loading scene can read it
    public static string TargetScene { get; private set; }

    Settings settings;

    private void Awake()
    {

        if (!File.Exists(Application.dataPath + "/settings.cfg"))
        {

            Debug.Log("No settings file found, creating new one.");

            settings = new Settings();
            string jsonExport = JsonUtility.ToJson(settings);
            File.WriteAllText(Application.dataPath + "/settings.cfg", jsonExport);

        }
        else
        {

            Debug.Log("Settings file found, loading settings.");

            string jsonImport = File.ReadAllText(Application.dataPath + "/settings.cfg");
            settings = JsonUtility.FromJson<Settings>(jsonImport);
        }
    }

    public void LoadScene(int sceneID)
    {

        TargetScene = SceneUtility.GetScenePathByBuildIndex(sceneID);
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Single);
    }

    public void StartGame()
    {

        VoxelData.seed = Mathf.Abs(seedField.text.GetHashCode()) / VoxelData.WorldSizeInChunks;
        TargetScene = "main";
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Single);
    }

    public void EnterSettings()
    {

        viewDstSlider.value = settings.viewDistance;
        UpdateViewDstSlider();
        mouseSlider.value = settings.mouseSensitivity;
        UpdateMouseSlider();
        threadingToggle.isOn = settings.enableThreading;
        chunkAnimToggle.isOn = settings.enableAnimatedChunks;
        clouds.value = (int)settings.clouds;

        mainMenuObject.SetActive(false);
        settingsObject.SetActive(true);
    }

    public void LeaveSettings()
    {

        settings.viewDistance = (int)viewDstSlider.value;
        settings.mouseSensitivity = mouseSlider.value;
        settings.enableThreading = threadingToggle.isOn;
        settings.enableAnimatedChunks = chunkAnimToggle.isOn;
        settings.clouds = (CloudStyle)clouds.value;

        string jsonExport = JsonUtility.ToJson(settings);
        File.WriteAllText(Application.dataPath + "/settings.cfg", jsonExport);

        mainMenuObject.SetActive(true);
        settingsObject.SetActive(false);
    }

    public void QuitGame()
    {

        Application.Quit();
    }

    public void UpdateViewDstSlider()
    {

        viewDstText.text = "View Distance: " + viewDstSlider.value;
    }

    public void UpdateMouseSlider()
    {

        mouseTxtSlider.text = "Mouse Sensitivity: " + mouseSlider.value.ToString("F1");
    }
}