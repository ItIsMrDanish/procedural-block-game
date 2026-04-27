using UnityEngine;

[CreateAssetMenu(fileName = "BiomeAttributes", menuName = "Bloxels/Biome Attribute")]
public class BiomeAttributes : ScriptableObject {

    [Header("Identity")]
    public string biomeName = "New Biome";

    // Climate determines which biome is chosen at any XZ position.

    [Header("Climate (0=cold/dry, 1=hot/wet)")]
    [Range(0f, 1f)] public float temperature = 0.5f;
    [Range(0f, 1f)] public float humidity = 0.5f;

    // Terrain shape � multipliers applied to global noise layers.
    // Default of 1 means "use global noise as-is".

    [Header("Terrain Shape")]

    [Tooltip("Scales mountain ridge peaks. 0 = plains/flat, 1 = default, 2 = big mountains.")]
    [Range(0f, 3f)] public float ridgeWeight = 1f;

    [Tooltip("Scales erosion (flattening). 0 = rugged jagged terrain, 2 = very smooth flat.")]
    [Range(0f, 3f)] public float erosionWeight = 1f;

    [Tooltip("Scales general elevation noise. 0 = no hills, 2 = exaggerated rolling hills.")]
    [Range(0f, 3f)] public float elevationAmplitude = 1f;

    [Tooltip("Flat block offset applied to final surface height. Negative = lowlands, positive = plateaus.")]
    [Range(-48f, 48f)] public float heightOffset = 0f;

    // Surface blocks

    [Header("Surface Blocks")]
    public byte surfaceBlock = 3; // Grass
    public byte subSurfaceBlock = 5; // Dirt

    [Tooltip("How many layers of subSurfaceBlock appear below the surface before stone.")]
    [Range(1, 12)] public int subsurfaceDepth = 4;

    // Flora

    [Header("Flora")]
    public bool placeMajorFlora = true;
    public int majorFloraIndex = 0;
    public float majorFloraZoneScale = 1.3f;
    [Range(0.1f, 1f)]
    public float majorFloraZoneThreshold = 0.6f;
    public float majorFloraPlacementScale = 15f;
    [Range(0.1f, 1f)]
    public float majorFloraPlacementThreshold = 0.8f;
    public int minHeight = 5;
    public int maxHeight = 12;

    // Ore lodes

    [Header("Lodes")]
    public Lode[] lodes;

    // Legacy kept so existing ScriptableObjects don't break.
    // These are no longer used in height calculation.

    [HideInInspector] public int offset = 0;
    [HideInInspector] public float scale = 1f;
    [HideInInspector] public int terrainHeight = 20;
    [HideInInspector] public float terrainScale = 1f;
}

[System.Serializable]
public class Lode {
    
    public string nodeName;
    public byte blockID;
    public int minHeight;
    public int maxHeight;
    public float scale;
    public float threshold;
    public float noiseOffset;
}