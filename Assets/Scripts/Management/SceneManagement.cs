using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

public class SceneManagement : MonoBehaviour {

    // Panel references — assign all in Inspector

    [Header("Panels")]
    public GameObject mainMenuObject;
    public GameObject settingsObject;
    public GameObject worldSelectObject;
    public GameObject createWorldObject;

    // Loading

    [Header("Loading")]
    public LoadingScreenUI loadingScreenUI;

    // World Select UI

    [Header("World Select")]
    public Transform worldListContent;
    public GameObject worldEntryPrefab;

    // Create World UI

    [Header("Create World")]
    public TMP_InputField worldNameInput;
    public TMP_InputField seedInput;

    // Settings UI

    [Header("Settings Menu UI Elements")]
    public Slider viewDstSlider;
    public TextMeshProUGUI viewDstText;
    public Slider mouseSlider;
    public TextMeshProUGUI mouseTxtSlider;
    public Toggle threadingToggle;
    public Toggle chunkAnimToggle;
    public TMP_Dropdown clouds;
    public TMP_Dropdown frameRate;

    // Private

    Settings settings;

    private string SavesRoot => Application.persistentDataPath + "/saves/";

    // Unity lifecycle

    private void Awake() {

        string cfgPath = Application.dataPath + "/settings.cfg";
        if (!File.Exists(cfgPath)) {

            settings = new Settings();
            File.WriteAllText(cfgPath, JsonUtility.ToJson(settings));
        }
        else {

            settings = JsonUtility.FromJson<Settings>(File.ReadAllText(cfgPath));
        }
    }

    // Main Menu

    public void EnterWorldSelect() {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        mainMenuObject.SetActive(false);
        worldSelectObject.SetActive(true);
        PopulateWorldList();
    }

    public void LeaveWorldSelect() {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        worldSelectObject.SetActive(false);
        mainMenuObject.SetActive(true);
    }

    // World list

    void PopulateWorldList() {

        foreach (Transform child in worldListContent)
            Destroy(child.gameObject);

        if (!Directory.Exists(SavesRoot))
            Directory.CreateDirectory(SavesRoot);

        string[] worldFolders = Directory.GetDirectories(SavesRoot);

        foreach (string folder in worldFolders) {

            if (!File.Exists(folder + "/world.world")) continue;

            string worldName = Path.GetFileName(folder);

            GameObject entry = Instantiate(worldEntryPrefab, worldListContent);

            entry.transform.Find("WorldNameText").GetComponent<TextMeshProUGUI>().text = worldName;

            string captured = worldName;
            entry.transform.Find("PlayButton").GetComponent<Button>()
                .onClick.AddListener(() => PlayWorld(captured));
            entry.transform.Find("DeleteButton").GetComponent<Button>()
                .onClick.AddListener(() => ConfirmDeleteWorld(captured));
        }
    }

    // Play existing world

    void PlayWorld(string name) {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        int seed = 0;
        string path = SavesRoot + name + "/world.world";

        if (File.Exists(path)) {

            try {

                var fmt = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                using (var stream = new FileStream(path, FileMode.Open)) {

                    WorldData wd = fmt.Deserialize(stream) as WorldData;
                    seed = wd.seed;
                }
            }
            catch (System.Exception e) {

                Debug.LogWarning("[SceneManagement] Could not read seed from save: " + e.Message);
                seed = name.GetHashCode();
            }
        }

        VoxelData.seed = seed;
        VoxelData.worldName = name;
        Debug.Log($"[SceneManagement] Loading world '{name}' seed {seed}");
        loadingScreenUI.LoadScene(1);
    }

    // Delete world

    void ConfirmDeleteWorld(string name) {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        string path = SavesRoot + name;
        if (Directory.Exists(path)) {

            Directory.Delete(path, recursive: true);
            Debug.Log($"[SceneManagement] Deleted world '{name}'");
        }

        PopulateWorldList();
    }

    // Create World panel

    public void EnterCreateWorld() {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        worldSelectObject.SetActive(false);
        createWorldObject.SetActive(true);
        if (worldNameInput != null) worldNameInput.text = "";
        if (seedInput != null) seedInput.text = "";
    }

    public void LeaveCreateWorld() {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        createWorldObject.SetActive(false);
        worldSelectObject.SetActive(true);
        PopulateWorldList();
    }

    public void CreateWorld() {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();

        string name     = worldNameInput != null ? worldNameInput.text.Trim() : "";
        string seedText = seedInput != null ? seedInput.text.Trim() : "";

        if (string.IsNullOrEmpty(name))
            name = "World " + System.DateTime.Now.ToString("yyyy-MM-dd HH-mm");

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "");

        if (string.IsNullOrEmpty(name))
            name = "World " + System.Environment.TickCount;

        int seed;
        if (string.IsNullOrEmpty(seedText))
            seed = System.Environment.TickCount;
        else if (int.TryParse(seedText, out int parsed))
            seed = parsed;
        else
            seed = seedText.GetHashCode();

        VoxelData.seed = seed;
        VoxelData.worldName = name;

        Debug.Log($"[SceneManagement] Creating world '{name}' seed {seed}");
        loadingScreenUI.LoadScene(1);
    }

    // Settings

    public void EnterSettings() {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        viewDstSlider.value = settings.viewDistance;
        UpdateViewDstSlider();
        mouseSlider.value = settings.mouseSensitivity;
        UpdateMouseSlider();
        threadingToggle.isOn = settings.enableThreading;
        chunkAnimToggle.isOn = settings.enableAnimatedChunks;
        clouds.value = (int)settings.clouds;
        frameRate.value = settings.frameRateIndex;

        // Register click sounds on every interactive control.
        // RemoveListener first so re-opening the panel never double-registers.
        threadingToggle.onValueChanged.RemoveListener(OnSettingsToggleChanged);
        threadingToggle.onValueChanged.AddListener(OnSettingsToggleChanged);

        chunkAnimToggle.onValueChanged.RemoveListener(OnSettingsToggleChanged);
        chunkAnimToggle.onValueChanged.AddListener(OnSettingsToggleChanged);

        clouds.onValueChanged.RemoveListener(OnSettingsDropdownChanged);
        clouds.onValueChanged.AddListener(OnSettingsDropdownChanged);

        frameRate.onValueChanged.RemoveListener(OnSettingsDropdownChanged);
        frameRate.onValueChanged.AddListener(OnSettingsDropdownChanged);

        viewDstSlider.onValueChanged.RemoveListener(OnSettingsSliderChanged);
        viewDstSlider.onValueChanged.AddListener(OnSettingsSliderChanged);

        mouseSlider.onValueChanged.RemoveListener(OnSettingsSliderChanged);
        mouseSlider.onValueChanged.AddListener(OnSettingsSliderChanged);

        mainMenuObject.SetActive(false);
        settingsObject.SetActive(true);
    }

    // Unified callbacks for settings controls — each just plays the click sound.
    private void OnSettingsToggleChanged(bool _)   { if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick(); }
    private void OnSettingsDropdownChanged(int _)  { if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick(); }
    private void OnSettingsSliderChanged(float _)  { if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick(); }

    public void LeaveSettings() {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        settings.viewDistance = (int)viewDstSlider.value;
        settings.mouseSensitivity = mouseSlider.value;
        settings.enableThreading = threadingToggle.isOn;
        settings.enableAnimatedChunks = chunkAnimToggle.isOn;
        settings.clouds = (CloudStyle)clouds.value;
        settings.frameRateIndex = frameRate.value;

        File.WriteAllText(Application.dataPath + "/settings.cfg", JsonUtility.ToJson(settings));

        mainMenuObject.SetActive(true);
        settingsObject.SetActive(false);
    }

    public void QuitGame() {

        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        Application.Quit();
    }

    public void UpdateViewDstSlider() {

        viewDstText.text = "View Distance: " + viewDstSlider.value;
    }

    public void UpdateMouseSlider() {

        mouseTxtSlider.text = "Mouse Sensitivity: " + mouseSlider.value.ToString("F1");
    }

    public void ReturnToMainMenu() {
        if (SoundManager.Instance != null) SoundManager.Instance.PlayMenuClick();
        SceneManager.LoadScene(0);
    }
}