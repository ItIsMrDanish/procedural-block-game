using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BiomeAttributes", menuName = "MinecraftTutorial/Biome Attribute")]
public class BiomeAttributes : ScriptableObject {

    [Header("Identity")]
    public string biomeName;

    // -------------------------------------------------------
    // Climate classification — used by TerrainGenerator to
    // select the best-matching biome for a given position.
    // 0 = cold/dry,  1 = hot/wet
    // -------------------------------------------------------
    [Header("Climate")]
    [Range(0f, 1f)] public float temperature = 0.5f;
    [Range(0f, 1f)] public float humidity = 0.5f;

    // -------------------------------------------------------
    // Terrain shape — how much this biome amplifies or
    // flattens the base terrain height.
    // 1.0 = neutral, >1 = mountains, <1 = flat plains/ocean
    // -------------------------------------------------------
    [Header("Terrain Shape")]
    public float heightMultiplier = 1.0f;  // Scales the terrain amplitude.
    public int terrainHeight = 40;    // Maximum height variation above sea level.
    public float terrainScale = 1.0f;  // Noise frequency for this biome's terrain.

    // -------------------------------------------------------
    // Surface blocks
    // -------------------------------------------------------
    [Header("Surface")]
    public byte surfaceBlock = 3; // Grass by default
    public byte subSurfaceBlock = 5; // Dirt by default

    // -------------------------------------------------------
    // Flora
    // -------------------------------------------------------
    [Header("Major Flora")]
    public int majorFloraIndex = 0;
    public float majorFloraZoneScale = 1.3f;
    [Range(0.1f, 1f)]
    public float majorFloraZoneThreshold = 0.6f;
    public float majorFloraPlacementScale = 15f;
    [Range(0.1f, 1f)]
    public float majorFloraPlacementThreshold = 0.8f;
    public bool placeMajorFlora = true;
    public int maxHeight = 12;
    public int minHeight = 5;

    // -------------------------------------------------------
    // Ore lodes
    // -------------------------------------------------------
    [Header("Lodes")]
    public Lode[] lodes;

    // Legacy fields kept for backward compatibility with
    // existing ScriptableObjects that may still reference them.
    [HideInInspector] public int offset = 0;
    [HideInInspector] public float scale = 1f;

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