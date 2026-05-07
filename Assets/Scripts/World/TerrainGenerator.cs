using UnityEngine;

public static class TerrainGenerator {

    private const float WarpScale     = 0.0015f;
    private const float WarpAmplitude = 18f;

    private const float ContinentScale  = 0.0006f;
    private const float ContinentOffset = 500f;
    private const float ContinentAmp    = 18f;

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

    // Cheese caves — large blobby caverns.
    private const float CaveScale               = 0.05f;
    private const float CaveThreshold           = 0.64f;
    private const int   CaveMaxDepthFromSurface = 6;

    // Spaghetti tunnels — long winding passages.
    private const float TunnelScale             = 0.02f;
    private const float TunnelThreshold         = 0.72f;

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

        float warpX = SampleRaw(worldX * WarpScale + seed * 0.001f, worldZ * WarpScale + seed * 0.002f) * WarpAmplitude;
        float warpZ = SampleRaw(worldX * WarpScale + seed * 0.003f + 0.5f, worldZ * WarpScale + seed * 0.004f + 0.5f) * WarpAmplitude;

        float wx = worldX + warpX;
        float wz = worldZ + warpZ;

        // Smooth climate over 5-point kernel — eliminates per-column biome flicker.
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
            float rawDist = Mathf.Sqrt(dt * dt + dh * dh);
            float effectiveDist = rawDist / Mathf.Max(b.rarity, 0.01f);
            float w = 1f / Mathf.Max(effectiveDist, MinClimateDist);

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

        float continent = GetContinentalness(wx, wz);
        float elevation = SampleScaled(wx, wz, ElevationOffset, ElevationScale);
        float detail    = SampleScaled(wx, wz, DetailOffset,    DetailScale);
        float ridge     = GetRidgeNoise(wx, wz);
        float erosion   = SampleScaled(wx, wz, ErosionOffset,   ErosionScale);

        float scaledElevation = elevation * blendedElevationAmp;
        float scaledDetail    = detail    * blendedElevationAmp;
        float scaledRidge     = ridge     * blendedRidgeWeight;
        float scaledErosion   = erosion   * blendedErosionWeight;

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

        if (yPos == VoxelData.WorldBottomInVoxels) return 1;

        if (yPos > surface)
            return yPos < VoxelData.SeaLevel ? (byte)14 : (byte)0;

        // Cave carving: runs from just below surface down to bedrock.
        // CaveMinY removed — caves exist at all depths below surface.
        if (yPos <= surface - CaveMaxDepthFromSurface && yPos > VoxelData.WorldBottomInVoxels + 4) {
            if (IsCave(pos) || IsTunnel(pos)) {
                // Waterlogging: only fill with water if this cavity connects upward
                // to the sea — i.e. every block above us up to SeaLevel is either
                // open ocean (above surface) or also a carved cave block.
                // Isolated underground pockets below sea level stay as air,
                // preventing the "multiple disconnected water levels" visual.
                if (yPos < VoxelData.SeaLevel)
                    return IsConnectedToSea(pos, col) ? (byte)14 : (byte)0;
                return 0;
            }
        }

        byte voxelValue;

        if (yPos == surface) {
            voxelValue = (yPos < VoxelData.SeaLevel) ? (byte)4 : biome.surfaceBlock;
        } else if (yPos >= surface - biome.subsurfaceDepth) {
            voxelValue = biome.subSurfaceBlock;
        } else {
            voxelValue = biome.undergroundBlock;
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

    // Scans upward from a carved cave block to determine if it has an unbroken
    // open path to sea level. "Open" means either above the terrain surface
    // (naturally water/air) or itself a carved cave block.
    // Returns true  → cavity is sea-connected → fill with water.
    // Returns false → cavity is sealed underground → fill with air.
    private static bool IsConnectedToSea(Vector3Int pos, ColumnData col) {

        int surface = col.surfaceHeight;

        for (int checkY = pos.y + 1; checkY < VoxelData.SeaLevel; checkY++) {

            // Above terrain surface = open ocean column, connection confirmed.
            if (checkY > surface)
                return true;

            // Inside terrain — check if this block is also carved open.
            var checkPos = new Vector3Int(pos.x, checkY, pos.z);
            bool carved = (checkY <= surface - CaveMaxDepthFromSurface &&
                           checkY > VoxelData.WorldBottomInVoxels + 4) &&
                          (IsCave(checkPos) || IsTunnel(checkPos));

            if (!carved)
                return false; // Solid block seals the column — isolated pocket
        }

        // Reached SeaLevel — connected.
        return true;
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

    // Spaghetti tunnel: uses a finer noise scale and a squashed Y axis so tunnels
    // run more horizontally than vertically, giving long winding passages.
    private static bool IsTunnel(Vector3Int pos) {

        float x  = pos.x * TunnelScale + VoxelData.seed * 0.4f;
        float y  = pos.y * TunnelScale * 0.5f + VoxelData.seed * 0.5f;  // squash Y = more horizontal tunnels
        float z  = pos.z * TunnelScale + VoxelData.seed * 0.6f;

        float AB = Mathf.PerlinNoise(x, y);
        float BC = Mathf.PerlinNoise(y, z);
        float CA = Mathf.PerlinNoise(z, x);

        return (AB + BC + CA) / 3f > TunnelThreshold;
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