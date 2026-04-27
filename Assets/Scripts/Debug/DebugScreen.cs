using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebugScreen : MonoBehaviour {

    World world;
    TextMeshProUGUI text;

    float frameRate;
    float timer;

    int halfWorldSizeInVoxels;
    int halfWorldSizeInChunks;

    private void Start() {
        
        world = GameObject.Find("World").GetComponent<World>();
        text = GetComponent<TextMeshProUGUI>();

        halfWorldSizeInVoxels = VoxelData.WorldSizeInVoxels / 2;
        halfWorldSizeInChunks = VoxelData.WorldSizeInChunks / 2;
    }

    private void Update() {

        string debugText = "Debug Screen � Press F3 to close/open";
        debugText += "\n";
        string debugText = "Victor er fed";
        debugText += "\n";
        string debugText = "Miku #1";
        debugText += "\n";
        debugText += frameRate + " FPS";
        debugText += "\n\n";
        debugText += "XYZ: " + (world.player.transform.position.x - halfWorldSizeInVoxels) + "x" + " / " + world.player.transform.position.y + "y" + " / " + (world.player.transform.position.z - halfWorldSizeInVoxels) + "z";
        debugText += "\n";
        debugText += "Block: " + ((int)world.player.transform.position.x - halfWorldSizeInVoxels) + "x" + " / " + (int)world.player.transform.position.y + "y" + " / " + ((int)world.player.transform.position.z - halfWorldSizeInVoxels) + "z";
        debugText += "\n";
        debugText += "Chunk: " + (world.playerChunkCoord.x - halfWorldSizeInChunks) + "x" + " / " + (world.playerChunkCoord.z - halfWorldSizeInChunks) + "z";

        string direction = "";
        switch (world._player.orientation) {

            case 0:
                direction = "South";
                break;
            case 5:
                direction = "East";
                break;
            case 1:
                direction = "North";
                break;
            default:
                direction = "West";
                break;
        }

        debugText += "\n";
        debugText += "Direction Facing: " + direction;

        text.text = debugText;

        if(timer > 1f) {

            frameRate = (int)(1f / Time.unscaledDeltaTime);
            timer = 0;
        } else {

            timer += Time.deltaTime;
        }
    }
}