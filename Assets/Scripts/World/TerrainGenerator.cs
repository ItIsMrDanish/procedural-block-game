using UnityEngine;

/// <summary>
/// All terrain generation logic lives here, separate from World.cs.
/// The key performance optimisation is GetColumnData() — it calculates
/// surface height AND biome for a given X,Z column ONCE, then ChunkData
/// uses it for every Y voxel in that column instead of recalculating.
/// </summary>
public static class TerrainGenerator {

    // -------------------------------------------------------
    // Height amplitudes — tuned so average terrain sits at
    // roughly Y = 68-72 (just above sea level 64).
    //
    // Perlin noise returns 0..1 so:
    //   continent avg ~0.5 → 0.5 * 15  = 7.5
    //   elevation avg ~0.5 → 0.5 * 10  = 5.0
    //   ridge^2   avg ~0.25 → 0.25 * 30 = 7.5
    //   erosion   avg ~0.5  → -0.5 * 12 = -6.0
    //   base avg  ≈ 14  → terrain at SeaLevel(64) + 14 ≈ 78
    //   biome terrainHeight adds a small amount on top
    // -------------------------------------------------------

    // Domain warp
    private const float WarpScale = 0.003f;
    private const float WarpAmplitude = 24f;

    // Continentalness — very low freq, sets ocean vs land base
    private const float ContinentScale = 0.0008f;
    private const float ContinentOffset = 500f;
    private const float ContinentAmp = 15f;

    // Elevation — medium freq, rolling hills
    private const float ElevationScale = 0.004f;
    private const float ElevationOffset = 1000f;
    private const float ElevationAmp = 10f;

    // Ridge — sharp mountain peaks (applied only where erosion is low)
    private const float RidgeScale = 0.006f;
    private const float RidgeOffset = 2000f;
    private const float RidgeAmp = 30f;

    // Erosion — suppresses ridges, creates valleys and flat plains
    private const float ErosionScale = 0.003f;
    private const float ErosionOffset = 3000f;
    private const float ErosionAmp = 12f;

    // Climate
    private const float TempScale = 0.002f;
    private const float TempOffset = 4000f;
    private const float HumidScale = 0.002f;
    private const float HumidOffset = 5000f;

    // Cave
    private const float CaveScale = 0.04f;
    private const float CaveThreshold = 0.74f;
    private const int CaveMinY = -32;

    // -------------------------------------------------------
    // Column data — calculated once per X,Z column per chunk.
    // -------------------------------------------------------
    public struct ColumnData {
        public int surfaceHeight;
        public BiomeAttributes biome;
    }

    /// <summary>
    /// Calculate everything needed for a column at (worldX, worldZ).
    /// Call this once per column, then pass the result to GetVoxel for each Y.
    /// </summary>
    public static ColumnData GetColumnData(int worldX, int worldZ, BiomeAttributes[] biomes) {

        // Domain warp
        float warpSeed = VoxelData.seed;
        float wx = worldX + SampleNoise2D(worldX * WarpScale, worldZ * WarpScale, warpSeed + 9999f) * WarpAmplitude;
        float wz = worldZ + SampleNoise2D(worldX * WarpScale, worldZ * WarpScale, warpSeed + 19999f) * WarpAmplitude;

        // Macro layers
        float continent = GetContinentalness(wx, wz);
        float elevation = SampleNoise2D(wx, wz, ElevationOffset, ElevationScale);
        float ridge = GetRidgeNoise(wx, wz);
        float erosion = SampleNoise2D(wx, wz, ErosionOffset, ErosionScale);

        // Ridge is suppressed by erosion so flat areas don't have mountains
        float ridgeMasked = ridge * Mathf.Clamp01(1f - erosion);

        float baseHeight = continent * ContinentAmp
                         + elevation * ElevationAmp
                         + (ridgeMasked * ridgeMasked) * RidgeAmp
                         - erosion * ErosionAmp;

        // Climate for biome selection
        float temp = SampleNoise2D(worldX, worldZ, TempOffset, TempScale * VoxelData.ChunkSize);
        float humid = SampleNoise2D(worldX, worldZ, HumidOffset, HumidScale * VoxelData.ChunkSize);

        // Find best biome by climate distance
        BiomeAttributes best = biomes[0];
        float bestD = float.MaxValue;
        foreach (BiomeAttributes b in biomes) {
            float dt = temp - b.temperature;
            float dh = humid - b.humidity;
            float d = dt * dt + dh * dh;
            if (d < bestD) { bestD = d; best = b; }
        }

        // Biome modifies the base height
        float biomeNoise = SampleNoise2D(worldX, worldZ, best.offset, best.terrainScale * VoxelData.ChunkSize);
        float finalOffset = baseHeight * best.heightMultiplier + best.terrainHeight * biomeNoise * 0.4f;

        int surface = Mathf.FloorToInt(VoxelData.SeaLevel + finalOffset);
        surface = Mathf.Clamp(surface,
                              VoxelData.WorldBottomInVoxels + 2,
                              VoxelData.WorldTopInVoxels - 2);

        return new ColumnData { surfaceHeight = surface, biome = best };

    }

    /// <summary>
    /// Determine the block ID at an absolute world position.
    /// Requires pre-computed ColumnData so this is just a lookup — no noise calls.
    /// Cave noise IS called here (it's 3D so must be per-voxel), but only
    /// for voxels that are actually underground.
    /// </summary>
    public static byte GetVoxel(Vector3Int pos, ColumnData col) {

        int yPos = pos.y;

        // Absolute bedrock floor
        if (yPos == VoxelData.WorldBottomInVoxels) return 1;

        int surface = col.surfaceHeight;
        BiomeAttributes biome = col.biome;

        if (yPos > surface) {
            // Air or water
            return yPos < VoxelData.SeaLevel ? (byte)14 : (byte)0;
        }

        // Cave carving — only underground, avoids expensive 3D call near surface
        if (yPos <= surface - 5 && yPos >= CaveMinY) {
            if (IsCave(pos)) return 0;
        }

        byte voxelValue;

        if (yPos == surface) {
            // Sand under water, biome surface block above
            voxelValue = yPos < VoxelData.SeaLevel ? (byte)4 : biome.surfaceBlock;
        } else if (yPos >= surface - 4) {
            voxelValue = biome.subSurfaceBlock;
        } else {
            voxelValue = 2; // Stone
        }

        // Ore lodes
        if (voxelValue == 2) {
            foreach (Lode lode in biome.lodes) {
                if (yPos > lode.minHeight && yPos < lode.maxHeight)
                    if (Noise.Get3DPerlin(new Vector3(pos.x, pos.y, pos.z),
                                         lode.noiseOffset, lode.scale, lode.threshold))
                        voxelValue = lode.blockID;
            }
        }

        return voxelValue;

    }

    // -------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------

    private static bool IsCave(Vector3Int pos) {

        return Noise.Get3DPerlin(
            new Vector3(pos.x, pos.y, pos.z),
            0f, CaveScale, CaveThreshold);

    }

    private static float GetContinentalness(float x, float z) {

        float v = SampleNoise2D(x, z, ContinentOffset, ContinentScale * VoxelData.ChunkSize);
        // Remap: values below ~0.4 push negative (ocean), above push positive (land)
        return Mathf.Clamp(v * 2f - 0.8f, -1f, 1f);

    }

    private static float GetRidgeNoise(float x, float z) {

        float v = SampleNoise2D(x, z, RidgeOffset, RidgeScale * VoxelData.ChunkSize);
        // Fold around 0.5 to produce ridges
        return 1f - Mathf.Abs(2f * v - 1f);

    }

    // Inline noise helper — avoids allocating Vector2 on every call
    private static float SampleNoise2D(float x, float z, float offset, float scale) {

        float px = (x + offset + VoxelData.seed + 0.1f) / VoxelData.ChunkSize * scale;
        float pz = (z + offset + VoxelData.seed + 0.1f) / VoxelData.ChunkSize * scale;
        return Mathf.PerlinNoise(px, pz);

    }

    // Overload for warp (no chunk-relative scaling needed)
    private static float SampleNoise2D(float x, float z, float offset) {

        return Mathf.PerlinNoise(x + offset, z + offset);

    }

}