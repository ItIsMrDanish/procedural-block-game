using UnityEngine;

// Terrain generation logic.
// ComputeColumnData() is the expensive function — it runs 8 Perlin calls per
// unique world XZ position and is called through HeightmapCache, so each
// position is computed exactly once regardless of vertical chunk count.

// GetVoxel() is the cheap per-voxel function — it only does up to 3 Perlin
// calls (cave check, underground only) and is otherwise pure arithmetic.

// HEIGHT TARGETS:
//   Normal terrain:   Y 66–75  (just above sea level 64)
//   Ocean floor:      Y 40–58  (fills with water up to 64)
//   Mountain peaks:   Y 90–130 (rare, where ridge + continent both high)
//   Absolute limits:  Y 37–145 (clamped in code)

public static class TerrainGenerator {

    // Global noise parameters.
    // These define the SCALE of each layer — biome weights scale
    // the AMPLITUDE at the point of combination.

    // Domain warp — distorts coordinates to break grid look
    private const float WarpScale     = 0.0015f;
    private const float WarpAmplitude = 18f;

    // Continentalness — very low freq, land-mass vs ocean
    private const float ContinentScale  = 0.0006f;
    private const float ContinentOffset = 500f;
    private const float ContinentAmp    = 18f;

    // Elevation — medium freq, rolling hills
    private const float ElevationScale  = 0.003f;
    private const float ElevationOffset = 1000f;
    private const float ElevationAmp    = 12f;

    // fBm detail — second octave of elevation for natural bumps
    private const float DetailScale  = 0.008f;
    private const float DetailOffset = 1500f;
    private const float DetailAmp    = 5f;

    // Ridge — sharp mountain peaks
    private const float RidgeScale  = 0.005f;
    private const float RidgeOffset = 2000f;
    private const float RidgeAmp    = 35f;

    // Erosion — flattens terrain, suppresses ridges in low areas
    private const float ErosionScale  = 0.0025f;
    private const float ErosionOffset = 3000f;
    private const float ErosionAmp    = 14f;

    // Climate
    private const float TempScale   = 0.0015f;
    private const float TempOffset  = 4000f;
    private const float HumidScale  = 0.0015f;
    private const float HumidOffset = 5000f;

    // Cave
    private const float CaveScale              = 0.05f;
    private const float CaveThreshold          = 0.69f;
    private const int   CaveMinY               = -40;
    private const int   CaveMaxDepthFromSurface = 5;

    // Minimum climate distance to avoid division-by-zero.
    private const float MinClimateDist = 0.0001f;

    // Column data struct — returned by ComputeColumnData

    public struct ColumnData {

        public int surfaceHeight;
        public BiomeAttributes biome;
    }

    // ComputeColumnData — expensive, call via HeightmapCache

    public static ColumnData ComputeColumnData(int worldX, int worldZ, BiomeAttributes[] biomes) {

        float seed = VoxelData.seed;

        // Step 1: Domain warp
        float warpX = SampleRaw(worldX * WarpScale + seed * 0.001f, worldZ * WarpScale + seed * 0.002f) * WarpAmplitude;
        float warpZ = SampleRaw(worldX * WarpScale + seed * 0.003f + 0.5f, worldZ * WarpScale + seed * 0.004f + 0.5f) * WarpAmplitude;

        float wx = worldX + warpX;
        float wz = worldZ + warpZ;

        // Step 2: Climate (un-warped for stability)
        float temp  = SampleScaled(worldX, worldZ, TempOffset,  TempScale);
        float humid = SampleScaled(worldX, worldZ, HumidOffset, HumidScale);

        // Step 3: Biome selection + blending.
        //
        // Each biome's effective distance is divided by its rarity so that
        // high-rarity biomes appear closer in climate space and win over a
        // larger area. Low-rarity biomes only win right at their climate point.
        //
        // The same rarity-adjusted distance drives the blend weights, so
        // terrain shape also transitions according to rarity.

        float totalWeight        = 0f;
        float blendedElevationAmp  = 0f;
        float blendedRidgeWeight   = 0f;
        float blendedErosionWeight = 0f;
        float blendedHeightOffset  = 0f;

        BiomeAttributes biome = biomes[0];
        float bestD = float.MaxValue;

        for (int i = 0; i < biomes.Length; i++) {

            BiomeAttributes b = biomes[i];
            float dt = temp  - b.temperature;
            float dh = humid - b.humidity;

            // Raw Euclidean distance in climate space.
            float rawDist = Mathf.Sqrt(dt * dt + dh * dh);

            // Rarity shrinks the effective distance — common biomes feel closer.
            float effectiveDist = rawDist / Mathf.Max(b.rarity, 0.01f);

            // Inverse-distance weight for blending terrain shape.
            float w = 1f / Mathf.Max(effectiveDist, MinClimateDist);

            totalWeight          += w;
            blendedElevationAmp  += b.elevationAmplitude * w;
            blendedRidgeWeight   += b.ridgeWeight        * w;
            blendedErosionWeight += b.erosionWeight      * w;
            blendedHeightOffset  += b.heightOffset       * w;

            // Nearest biome (by effective distance) wins for surface blocks + flora.
            if (effectiveDist < bestD) { bestD = effectiveDist; biome = b; }
        }

        blendedElevationAmp  /= totalWeight;
        blendedRidgeWeight   /= totalWeight;
        blendedErosionWeight /= totalWeight;
        blendedHeightOffset  /= totalWeight;

        // Step 4: Macro noise layers (warped coords)
        float continent = GetContinentalness(wx, wz);
        float elevation = SampleScaled(wx, wz, ElevationOffset, ElevationScale);
        float detail    = SampleScaled(wx, wz, DetailOffset,    DetailScale);
        float ridge     = GetRidgeNoise(wx, wz);
        float erosion   = SampleScaled(wx, wz, ErosionOffset,   ErosionScale);

        // Step 5: Apply blended biome weights
        float scaledElevation = elevation * blendedElevationAmp;
        float scaledDetail    = detail    * blendedElevationAmp;
        float scaledRidge     = ridge     * blendedRidgeWeight;
        float scaledErosion   = erosion   * blendedErosionWeight;

        float ridgeMasked = scaledRidge * Mathf.Clamp01(1.2f - scaledErosion);

        // Step 6: Combine into height offset from sea level
        float heightOffset =
              continent       * ContinentAmp
            + scaledElevation * ElevationAmp
            + scaledDetail    * DetailAmp
            + (ridgeMasked * ridgeMasked) * RidgeAmp
            - scaledErosion   * ErosionAmp
            + blendedHeightOffset;

        // Step 7: Final surface height
        int surface = Mathf.RoundToInt(VoxelData.SeaLevel + heightOffset);
        surface = Mathf.Clamp(surface,
            VoxelData.WorldBottomInVoxels + 4,
            VoxelData.WorldTopInVoxels    - 4);

        return new ColumnData { surfaceHeight = surface, biome = biome };
    }

    // GetVoxel — cheap per-voxel call

    public static byte GetVoxel(Vector3Int pos, ColumnData col) {

        int yPos    = pos.y;
        int surface = col.surfaceHeight;
        BiomeAttributes biome = col.biome;

        if (yPos == VoxelData.WorldBottomInVoxels) return 1;

        if (yPos > surface)
            return yPos < VoxelData.SeaLevel ? (byte)14 : (byte)0;

        if (yPos <= surface - CaveMaxDepthFromSurface && yPos >= CaveMinY) {
            if (IsCave(pos)) return 0;
        }

        byte voxelValue;

        if (yPos == surface) {
            voxelValue = (yPos < VoxelData.SeaLevel) ? (byte)4 : biome.surfaceBlock;
        } else if (yPos >= surface - biome.subsurfaceDepth) {
            voxelValue = biome.subSurfaceBlock;
        } else {
            voxelValue = 2; // Stone
        }

        if (voxelValue == 2 && biome.lodes != null) {

            for (int i = 0; i < biome.lodes.Length; i++) {

                Lode lode = biome.lodes[i];
                if (yPos > lode.minHeight && yPos < lode.maxHeight)
                    if (IsCaveNoise(pos, lode.noiseOffset, lode.scale, lode.threshold))
                        voxelValue = lode.blockID;
            }
        }

        return voxelValue;
    }

    // Private helpers

    private static bool IsCave(Vector3Int pos) {

        float x = pos.x * CaveScale + VoxelData.seed * 0.1f;
        float y = pos.y * CaveScale + VoxelData.seed * 0.2f;
        float z = pos.z * CaveScale + VoxelData.seed * 0.3f;

        float AB = Mathf.PerlinNoise(x, y);
        float BC = Mathf.PerlinNoise(y, z);
        float CA = Mathf.PerlinNoise(z, x);

        return (AB + BC + CA) / 3f > CaveThreshold;
    }

    private static bool IsCaveNoise(Vector3Int pos, float offset, float scale, float threshold) {

        float x = (pos.x + offset + VoxelData.seed) * scale;
        float y = (pos.y + offset + VoxelData.seed) * scale;
        float z = (pos.z + offset + VoxelData.seed) * scale;

        float AB = Mathf.PerlinNoise(x, y);
        float BC = Mathf.PerlinNoise(y, z);
        float CA = Mathf.PerlinNoise(z, x);

        return (AB + BC + CA) / 3f > threshold;
    }

    private static float GetContinentalness(float x, float z) {

        float v = SampleScaled(x, z, ContinentOffset, ContinentScale);
        return Mathf.Clamp(v * 2.2f - 1.0f, -1f, 1f);
    }

    private static float GetRidgeNoise(float x, float z) {

        float v = SampleScaled(x, z, RidgeOffset, RidgeScale);
        return 1f - Mathf.Abs(2f * v - 1f);
    }

    private static float SampleScaled(float x, float z, float offset, float scale) {

        float px = (x + offset + VoxelData.seed + 0.1f)    * scale;
        float pz = (z + offset + VoxelData.seed + 9371.1f) * scale;
        return Mathf.PerlinNoise(px, pz);
    }

    private static float SampleRaw(float x, float z) {

        return Mathf.PerlinNoise(x, z) * 2f - 1f;
    }
}