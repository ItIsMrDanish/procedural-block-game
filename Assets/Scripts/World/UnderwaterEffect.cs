using UnityEngine;

// Attach to Main Camera.
// Drag the UnderwaterOverlay UI panel into the 'overlayPanel' field in the inspector.
public class UnderwaterEffect : MonoBehaviour {

    public GameObject overlayPanel;

    private void Update() {

        if (!World.IsReady || overlayPanel == null) return;

        VoxelState voxel = World.Instance.GetVoxelState(transform.position);
        bool submerged = voxel != null && World.Instance.blocktypes[voxel.id].isWater;

        overlayPanel.SetActive(submerged);
    }
}