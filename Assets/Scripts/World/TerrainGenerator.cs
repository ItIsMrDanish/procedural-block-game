using UnityEngine;

public static class TerrainGenerator {

    private const float WarpScale     = 0.0015f;
    private const float WarpAmplitude = 18f;

    private const float ContinentScale  = 0.0006f;
    private const float ContinentOffset = 500f;
    private const float ContinentAmp    = 32f;   // was 18 — needs to be 32 so ocean floors sit well below sea level

    private const float ElevationScale  = 0.003f;
    private const float ElevationOffset = 1000f;
    private const float ElevationAmp    = 12f;

    private const float DetailScale  = 0.008f;
    private const float DetailOffset = 1500f;
    private const float DetailAmp    = 5f;

    private const float RidgeScale  = 0.005f;
    private const float RidgeOffset = 2000f;
    private const float RidgeAmp    = 35f;

    private const float ErosionScale  = 0.0025f;
    private const float ErosionOffset = 3000f;
    private const float ErosionAmp    = 14f;

    private const float TempScale   = 0.0015f;
    private const float TempOffset  = 4000f;
    private const float HumidScale  = 0.0015f;
    private const float HumidOffset = 5000f;

    private const float CaveScale               = 0.05f;
    private const float CaveThreshold           = 0.69f;
    private const int   CaveMinY                = -40;
    private const int   CaveMaxDepthFromSurface = 5;

    private const float MinClimateDist = 0.0001f;

    // Climate is averaged over a 5-point kernel to prevent column-by-column
    // biome flickering caused by domain warp at borders.
    private const float ClimateKernelRadius = 32f;

    public struct ColumnData {
        public int surfaceHeight;
        public BiomeAttributes biome;
    }

    public static ColumnData ComputeColumnData(int worldX, int worldZ, BiomeAttributes[] biomes) {

        float seed = VoxelData.seed;

        float warpX = SampleRaw(worldX * WarpScale + seed * 0.001f,        worldZ * WarpScale + seed * 0.002f)        * WarpAmplitude;
        float warpZ = SampleRaw(worldX * WarpScale + seed * 0.003f + 0.5f, worldZ * WarpScale + seed * 0.004f + 0.5f) * WarpAmplitude;

        float wx = worldX + warpX;
        float wz = worldZ + warpZ;

        // Smooth climate over 5-point cross kernel — eliminates per-column biome flicker.
        float r = ClimateKernelRadius;
        float temp =
            ( SampleScaled(worldX,   worldZ,   TempOffset, TempScale)
            + SampleScaled(worldX+r, worldZ,   TempOffset, TempScale)
            + SampleScaled(worldX-r, worldZ,   TempOffset, TempScale)
            + SampleScaled(worldX,   worldZ+r, TempOffset, TempScale)
            + SampleScaled(worldX,   worldZ-r, TempOffset, TempScale) ) * 0.2f;

        float humid =
            ( SampleScaled(worldX,   worldZ,   HumidOffset, HumidScale)
            + SampleScaled(worldX+r, worldZ,   HumidOffset, HumidScale)
            + SampleScaled(worldX-r, worldZ,   HumidOffset, HumidScale)
            + SampleScaled(worldX,   worldZ+r, HumidOffset, HumidScale)
            + SampleScaled(worldX,   worldZ-r, HumidOffset, HumidScale) ) * 0.2f;

        float totalWeight          = 0f;
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
            float rawDist       = Mathf.Sqrt(dt * dt + dh * dh);
            float effectiveDist = rawDist / Mathf.Max(b.rarity, 0.01f);
            float w             = 1f / Mathf.Max(effectiveDist, MinClimateDist);

            totalWeight          += w;
            blendedElevationAmp  += b.elevationAmplitude * w;
            blendedRidgeWeight   += b.ridgeWeight        * w;
            blendedErosionWeight += b.erosionWeight      * w;
            blendedHeightOffset  += b.heightOffset       * w;

            if (effectiveDist < bestD) {
                bestD = effectiveDist;
                biome = b;
            }
        }

        blendedElevationAmp  /= totalWeight;
        blendedRidgeWeight   /= totalWeight;
        blendedErosionWeight /= totalWeight;
        blendedHeightOffset  /= totalWeight;

        // Continentalness: [-1, 1] where negative = ocean, positive = land.
        // v * 2.8 - 1.2 means ~43% of Perlin space maps to ocean (negative),
        // and ocean values go as low as -1 (32 blocks below sea level with ContinentAmp=32).
        // Previously was v * 2.2 - 1.0 which barely pushed terrain below sea level
        // once the other positive terms (elevation, ridge) were added in.
        float continent = GetContinentalness(wx, wz);

        float elevation = SampleScaled(wx, wz, ElevationOffset, ElevationScale);
        float detail    = SampleScaled(wx, wz, DetailOffset,    DetailScale);
        float ridge     = GetRidgeNoise(wx, wz);
        float erosion   = SampleScaled(wx, wz, ErosionOffset,   ErosionScale);

        float scaledElevation = elevation * blendedElevationAmp;
        float scaledDetail    = detail    * blendedElevationAmp;
        float scaledRidge     = ridge     * blendedRidgeWeight;
        float scaledErosion   = erosion   * blendedErosionWeight;

        // When continent is strongly negative (deep ocean), suppress ridge/elevation
        // so ocean floors are flat rather than having underwater mountain spikes.
        // oceanFlatten = 1 in deep ocean, 0 on land. Smoothstep over [-0.6, 0] continent range.
        float oceanFlatten = Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, (-continent - 0f) / 0.6f));

        scaledElevation *= (1f - oceanFlatten);
        scaledDetail    *= (1f - oceanFlatten);
        scaledRidge     *= (1f - oceanFlatten);

        float ridgeMasked = scaledRidge * Mathf.Clamp01(1.2f - scaledErosion);

        float heightOffset =
              continent       * ContinentAmp
            + scaledElevation * ElevationAmp
            + scaledDetail    * DetailAmp
            + (ridgeMasked * ridgeMasked) * RidgeAmp
            - scaledErosion   * ErosionAmp
            + blendedHeightOffset;

        int surface = Mathf.RoundToInt(VoxelData.SeaLevel + heightOffset);
        surface = Mathf.Clamp(surface,
            VoxelData.WorldBottomInVoxels + 4,
            VoxelData.WorldTopInVoxels    - 4);

        return new ColumnData { surfaceHeight = surface, biome = biome };
    }

    public static byte GetVoxel(Vector3Int pos, ColumnData col) {

        int yPos    = pos.y;
        int surface = col.surfaceHeight;
        BiomeAttributes biome = col.biome;

        if (yPos == VoxelData.WorldBottomInVoxels) return 1; // Bedrock

        // Above surface: water if below sea level, air otherwise.
        if (yPos > surface)
            return yPos < VoxelData.SeaLevel ? (byte)14 : (byte)0;

        // Cave carving — only underground, not too close to surface.
        if (yPos <= surface - CaveMaxDepthFromSurface && yPos >= CaveMinY) {
            if (IsCave(pos)) return 0;
        }

        byte voxelValue;

        if (yPos == surface) {
            // Underwater surface is always sand regardless of biome.
            voxelValue = (yPos < VoxelData.SeaLevel) ? (byte)4 : biome.surfaceBlock;
        } else if (yPos >= surface - biome.subsurfaceDepth) {
            voxelValue = biome.subSurfaceBlock;
        } else {
            voxelValue = 2; // Stone
        }

        // Ore lode injection.
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
        // v * 2.8 - 1.2: ocean (negative) covers ~43% of the world.
        // Deep ocean reaches -1.0, giving -32 blocks with ContinentAmp=32.
        // Previously v * 2.2 - 1.0 barely pushed terrain below sea level
        // because elevation/ridge/detail terms always added ~+10 back.
        return Mathf.Clamp(v * 2.8f - 1.2f, -1f, 1f);
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