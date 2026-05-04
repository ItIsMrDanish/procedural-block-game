using UnityEngine;
using UnityEngine.UI;

// Setup in Unity:
// 1. In mainCanvas, create UI > Image. Name it "UnderwaterOverlay".
// 2. Set Rect Transform to stretch full screen (anchor min 0,0 / max 1,1, all offsets 0).
// 3. Set the Image color to roughly (R:0, G:0.25, B:0.55, A:0.5) — adjust to taste.
// 4. Turn OFF "Raycast Target" on the Image so it doesn't eat mouse clicks.
// 5. Attach this script to that GameObject.

[RequireComponent(typeof(Image))]
public class UnderwaterOverlay : MonoBehaviour {

    [Tooltip("How fast the panel fades in and out (units per second of alpha blend).")]
    public float transitionSpeed = 8f;

    private Image _image;
    private Color _fullColor;   // the color set in the inspector — shown when submerged
    private float _blend = 0f;  // 0 = hidden, 1 = fully visible

    private void Awake() {

        _image     = GetComponent<Image>();
        _fullColor = _image.color;

        // Start invisible.
        SetAlpha(0f);
    }

    private void Update() {

        if (!World.IsReady) return;

        bool submerged = IsSubmerged();
        float target   = submerged ? 1f : 0f;

        _blend = Mathf.MoveTowards(_blend, target, Time.deltaTime * transitionSpeed);
        SetAlpha(_blend * _fullColor.a);
    }

    private bool IsSubmerged() {

        VoxelState voxel = World.Instance.GetVoxelState(Camera.main.transform.position);
        if (voxel == null) return false;
        return World.Instance.blocktypes[voxel.id].isWater;
    }

    private void SetAlpha(float a) {

        Color c = _fullColor;
        c.a = a;
        _image.color = c;
    }
}