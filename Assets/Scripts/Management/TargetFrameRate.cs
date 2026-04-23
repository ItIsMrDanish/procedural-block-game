using UnityEngine;
public class TargetFrameRate : MonoBehaviour
{
    private int _lastIndex = -1;

    void Start()
    {
        // Vi fjerner vSync for at tillade custom frame rates
        QualitySettings.vSyncCount = 0;
        ApplyFrameRate();
    }

    void Update()
    {
        // Tjekker om indstillingen er �ndret i World.settings midt i spillet
        if (World.Instance != null && World.Instance.settings.frameRateIndex != _lastIndex)
        {
            ApplyFrameRate();
        }
    }

    void ApplyFrameRate()
    {
        if (World.Instance == null) return;

        int index = World.Instance.settings.frameRateIndex;
        _lastIndex = index;

        // Her mapper vi dropdown-indekset til faktiske tal. 
        // S�rg for at disse matcher r�kkef�lgen i din TMP_Dropdown i Unity!
        switch (index)
        {
            case 0: Application.targetFrameRate = 30; break;
            case 1: Application.targetFrameRate = 60; break;
            case 2: Application.targetFrameRate = 120; break;
            case 3: Application.targetFrameRate = 144; break;
            case 4: Application.targetFrameRate = 165; break;
            case 5: Application.targetFrameRate = 240; break;
            case 6: Application.targetFrameRate = -1; break; // -1 betyder "Unlimited"
            default: Application.targetFrameRate = 60; break;
        }

        Debug.Log("Target FPS sat til: " + Application.targetFrameRate);
    }
}