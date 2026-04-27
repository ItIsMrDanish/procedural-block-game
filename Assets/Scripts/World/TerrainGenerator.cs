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
    private const float WarpScale = 0.0015f;
    private const float WarpAmplitude = 18f;

    // Continentalness — very low freq, land-mass vs ocean
    private const float ContinentScale = 0.0006f;
    private const float ContinentOffset = 500f;
    private const float ContinentAmp = 18f;  // ±18 blocks influence

    // Elevation — medium freq, rolling hills
    private const float ElevationScale = 0.003f;
    private const float ElevationOffset = 1000f;
    private const float ElevationAmp = 12f;  // ±6 blocks average

    // fBm detail — second octave of elevation for natural bumps
    // This reuses the elevation noise at 2× frequency with half amplitude
    // No extra Perlin call count — same noise, different input coords
    private const float DetailScale = 0.008f;
    private const float DetailOffset = 1500f;
    private const float DetailAmp = 5f;

    // Ridge — sharp mountain peaks
    private const float RidgeScale = 0.005f;
    private const float RidgeOffset = 2000f;
    private const float RidgeAmp = 35f;  // Mountains up to ~35 blocks above base

    // Erosion — flattens terrain, suppresses ridges in low areas
    private const float ErosionScale = 0.0025f;
    private const float ErosionOffset = 3000f;
    private const float ErosionAmp = 14f;

    // Climate
    private const float TempScale = 0.0015f;
    private const float TempOffset = 4000f;
    private const float HumidScale = 0.0015f;
    private const float HumidOffset = 5000f;

    // Cave — 3-axis approximation (was 6-axis = 6 Perlin calls, now 3 = half cost)
    // Quality is very similar — the 6-axis version is only marginally more varied
    private const float CaveScale = 0.05f;
    private const float CaveThreshold = 0.69f; // Adjusted for 3-axis (was 0.74)
    private const int CaveMinY = -40;
    private const int CaveMaxDepthFromSurface = 5; // Don't carve within 5 of surface

    // Column data struct — returned by ComputeColumnData

    public struct ColumnData {

        public int surfaceHeight;
        public BiomeAttributes biome;
    }

    // ComputeColumnData — expensive, call via HeightmapCache

    // Computes surface height and biome for a world XZ position.
    // This is the expensive function (~9 Perlin calls).
    // ALWAYS call via HeightmapCache.GetOrCompute() — never directly.

    public static ColumnData ComputeColumnData(int worldX, int worldZ, BiomeAttributes[] biomes) {

        float seed = VoxelData.seed;

        // Step 1: Domain warp — offset coords to break grid artifacts
        float warpX = SampleRaw(worldX * WarpScale + seed * 0.001f, worldZ * WarpScale + seed * 0.002f) * WarpAmplitude;
        float warpZ = SampleRaw(worldX * WarpScale + seed * 0.003f + 0.5f, worldZ * WarpScale + seed * 0.004f + 0.5f) * WarpAmplitude;

        float wx = worldX + warpX;
        float wz = worldZ + warpZ;

        // Step 2: Climate — biome selection (uses un-warped coords for stability)
        float temp = SampleScaled(worldX, worldZ, TempOffset, TempScale);
        float humid = SampleScaled(worldX, worldZ, HumidOffset, HumidScale);

        // Find best biome by climate distance
        BiomeAttributes biome = biomes[0];
        float bestD = float.MaxValue;
        for (int i = 0; i < biomes.Length; i++) {

            BiomeAttributes b = biomes[i];
            float dt = temp - b.temperature;
            float dh = humid - b.humidity;
            float d = dt * dt + dh * dh;
            if (d < bestD) { bestD = d; biome = b; }
        }

        // Step 3: Macro noise layers (warped coords for terrain)
        float continent = GetContinentalness(wx, wz);
        float elevation = SampleScaled(wx, wz, ElevationOffset, ElevationScale);
        float detail = SampleScaled(wx, wz, DetailOffset, DetailScale);
        float ridge = GetRidgeNoise(wx, wz);
        float erosion = SampleScaled(wx, wz, ErosionOffset, ErosionScale);

        // Step 4: Apply biome weights to each layer
        // Each biome can amplify or suppress individual terrain features
        float scaledElevation = elevation * biome.elevationAmplitude;
        float scaledDetail = detail * biome.elevationAmplitude; // Same scale as elev
        float scaledRidge = ridge * biome.ridgeWeight;
        float scaledErosion = erosion * biome.erosionWeight;

        // Ridge masked by erosion — mountains only form where erosion is low
        float ridgeMasked = scaledRidge * Mathf.Clamp01(1.2f - scaledErosion);

        // Step 5: Combine into height offset from sea level
        float heightOffset =
              continent * ContinentAmp // Ocean vs land base
            + scaledElevation * ElevationAmp // Rolling hills
            + scaledDetail * DetailAmp // Fine detail bumps
            + (ridgeMasked * ridgeMasked) * RidgeAmp // Mountain peaks (squared = sharp)
            - scaledErosion * ErosionAmp // Valley/plain erosion
            + biome.heightOffset; // Biome flat offset (plateaus etc.)

        // Step 6: Final surface height
        int surface = Mathf.RoundToInt(VoxelData.SeaLevel + heightOffset);
        surface = Mathf.Clamp(surface,
            VoxelData.WorldBottomInVoxels + 4,
            VoxelData.WorldTopInVoxels - 4);

        return new ColumnData { surfaceHeight = surface, biome = biome };
    }

    // GetVoxel — cheap per-voxel call

    // Returns the block ID at a world position using pre-computed column data.
    // Only does cave noise (3 Perlin calls) for underground voxels.
    // Everything else is arithmetic.

    public static byte GetVoxel(Vector3Int pos, ColumnData col) {

        int yPos = pos.y;
        int surface = col.surfaceHeight;
        BiomeAttributes biome = col.biome;

        // Absolute bedrock floor
        if (yPos == VoxelData.WorldBottomInVoxels) return 1;

        // Above terrain surface
        if (yPos > surface)
            return yPos < VoxelData.SeaLevel ? (byte)14 : (byte)0;

        // Cave carving — only underground, not near surface
        if (yPos <= surface - CaveMaxDepthFromSurface && yPos >= CaveMinY) {
            if (IsCave(pos)) return 0;
        }

        // Surface layers
        byte voxelValue;

        if (yPos == surface) {

            // Underwater surface = sand regardless of biome
            voxelValue = (yPos < VoxelData.SeaLevel) ? (byte)4 : biome.surfaceBlock;
        } else if (yPos >= surface - biome.subsurfaceDepth) {

            voxelValue = biome.subSurfaceBlock;
        } else {

            voxelValue = 2; // Stone
        }

        // Ore lodes — only in stone
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

    // 3-axis cave check. Uses AB, BC, CA instead of all 6 permutations.
    // 3 Perlin calls vs 6 — same visual quality, half the cost.

    private static bool IsCave(Vector3Int pos) {

        float x = pos.x * CaveScale + VoxelData.seed * 0.1f;
        float y = pos.y * CaveScale + VoxelData.seed * 0.2f;
        float z = pos.z * CaveScale + VoxelData.seed * 0.3f;

        float AB = Mathf.PerlinNoise(x, y);
        float BC = Mathf.PerlinNoise(y, z);
        float CA = Mathf.PerlinNoise(z, x);

        return (AB + BC + CA) / 3f > CaveThreshold;
    }

    /// <summary>Reuses cave noise logic for ore lode generation.</summary>
    private static bool IsCaveNoise(Vector3Int pos, float offset, float scale, float threshold) {

        float x = (pos.x + offset + VoxelData.seed) * scale;
        float y = (pos.y + offset + VoxelData.seed) * scale;
        float z = (pos.z + offset + VoxelData.seed) * scale;

        float AB = Mathf.PerlinNoise(x, y);
        float BC = Mathf.PerlinNoise(y, z);
        float CA = Mathf.PerlinNoise(z, x);

        return (AB + BC + CA) / 3f > threshold;
    }

    // Continentalness: remapped so ~40% of the world is ocean (negative value).
    
    private static float GetContinentalness(float x, float z) {

        float v = SampleScaled(x, z, ContinentOffset, ContinentScale);
        // Remap 0..1 → -1..1.2, clamped.
        // Breakpoint at v≈0.46: below = ocean, above = land
        return Mathf.Clamp(v * 2.2f - 1.0f, -1f, 1f);
    }

    // Ridge noise: folded Perlin that produces sharp mountain ridges.
    // 1 - |2v - 1| gives value 0 at 0 and 1, peaks at v=0.5.
   
    private static float GetRidgeNoise(float x, float z) {

        float v = SampleScaled(x, z, RidgeOffset, RidgeScale);
        return 1f - Mathf.Abs(2f * v - 1f);
    }

    // Samples Perlin noise with a seed-offset and a world-space scale.
    // The scale is in "cycles per ChunkSize blocks" so it's independent
    // of chunk size and matches old Noise.Get2DPerlin behavior.

    // IMPORTANT: px and pz must differ — PerlinNoise(t, t) samples along
    // the noise diagonal where output is nearly constant (~0.5 everywhere),
    // which killed all biome variation. The Z input uses a large prime offset
    // (9371f) to break the symmetry and produce genuine 2D variation.
    // This also fixes elevation, detail, ridge, erosion and continentalness —
    // they all went through the same broken formula.
    
    private static float SampleScaled(float x, float z, float offset, float scale) {

        float px = (x + offset + VoxelData.seed + 0.1f) * scale;
        float pz = (z + offset + VoxelData.seed + 9371.1f) * scale;
        return Mathf.PerlinNoise(px, pz);
    }

    // Samples raw Perlin noise (for domain warp where we want raw coordinates).

    private static float SampleRaw(float x, float z) {

        return Mathf.PerlinNoise(x, z) * 2f - 1f; // Returns -1..1
    }
}