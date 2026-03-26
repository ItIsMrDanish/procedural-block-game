using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ChunkData {

    int _x, _y, _z;

    public Vector3Int position {
        get { return new Vector3Int(_x, _y, _z); }
        set { _x = value.x; _y = value.y; _z = value.z; }
    }

    public ChunkData(Vector3Int pos) { position = pos; }
    public ChunkData(int x, int y, int z) { _x = x; _y = y; _z = z; }

    [System.NonSerialized] public Chunk chunk;

    [HideInInspector]
    public VoxelState[,,] map = new VoxelState[
        VoxelData.ChunkSize,
        VoxelData.ChunkSize,
        VoxelData.ChunkSize];

    public void Populate() {

        int chunkTopY = _y + VoxelData.ChunkSize; // Exclusive top Y of this chunk
        int chunkBottomY = _y;                        // Inclusive bottom Y

        // -------------------------------------------------------
        // EARLY EXIT 1: Chunk is entirely ABOVE the world surface for all columns.
        // Find the max possible surface height in this chunk's footprint.
        // If even the highest nearby surface is below this chunk's bottom,
        // fill everything with air (or water) — zero noise calls needed.
        //
        // We check a sampled grid (every 4 blocks) rather than every column
        // to avoid doing full noise on sky chunks.
        // -------------------------------------------------------
        int maxSurfaceInFootprint = GetMaxSurfaceEstimate();

        if (chunkBottomY > maxSurfaceInFootprint + 4 &&
            chunkBottomY > VoxelData.SeaLevel) {

            // Entirely above terrain and above water — fill with air.
            FillUniform(0);
            return;
        }

        // -------------------------------------------------------
        // EARLY EXIT 2: Chunk is entirely below world bottom.
        // -------------------------------------------------------
        if (chunkTopY <= VoxelData.WorldBottomInVoxels + 1) {
            FillUniform(1); // Bedrock
            return;
        }

        // -------------------------------------------------------
        // NORMAL POPULATE PATH:
        // Build column cache from HeightmapCache (O(1) if pre-warmed).
        // -------------------------------------------------------
        BiomeAttributes[] biomes = World.Instance.biomes;
        var columnCache = new TerrainGenerator.ColumnData[
            VoxelData.ChunkSize, VoxelData.ChunkSize];

        for (int x = 0; x < VoxelData.ChunkSize; x++) {
            for (int z = 0; z < VoxelData.ChunkSize; z++) {
                columnCache[x, z] = HeightmapCache.GetOrCompute(
                    x + _x, z + _z, biomes);
            }
        }

        // -------------------------------------------------------
        // Fill voxels using cached column data.
        // -------------------------------------------------------
        for (int x = 0; x < VoxelData.ChunkSize; x++) {
            for (int z = 0; z < VoxelData.ChunkSize; z++) {

                TerrainGenerator.ColumnData col = columnCache[x, z];
                int surface = col.surfaceHeight;

                for (int y = 0; y < VoxelData.ChunkSize; y++) {

                    int worldY = y + _y;
                    var worldPos = new Vector3Int(x + _x, worldY, z + _z);

                    byte blockId = TerrainGenerator.GetVoxel(worldPos, col);

                    // Flora — only at the surface level voxel
                    if (worldY == surface &&
                        worldY >= VoxelData.SeaLevel &&
                        col.biome.placeMajorFlora) {

                        var noisePos = new Vector2(worldPos.x, worldPos.z);

                        if (Noise.Get2DPerlin(noisePos, 0f, col.biome.majorFloraZoneScale)
                                > col.biome.majorFloraZoneThreshold &&
                            Noise.Get2DPerlin(noisePos, 0f, col.biome.majorFloraPlacementScale)
                                > col.biome.majorFloraPlacementThreshold) {

                            World.Instance.EnqueueModification(
                                Structure.GenerateMajorFlora(
                                    col.biome.majorFloraIndex,
                                    new Vector3(worldPos.x, worldY, worldPos.z),
                                    col.biome.minHeight,
                                    col.biome.maxHeight));
                        }
                    }

                    map[x, y, z] = new VoxelState(blockId, this, new Vector3Int(x, y, z));

                    // Link neighbours that are inside this chunk.
                    // Border neighbours are looked up from WorldData.
                    for (int p = 0; p < 6; p++) {

                        var nv = new Vector3Int(x, y, z) + VoxelData.faceChecks[p];

                        if (IsVoxelInChunk(nv))
                            map[x, y, z].neighbours[p] = map[nv.x, nv.y, nv.z];
                        else
                            map[x, y, z].neighbours[p] =
                                World.Instance.worldData.GetVoxel(worldPos + VoxelData.faceChecks[p]);
                    }
                }
            }
        }

        Lighting.RecalculateNaturaLight(this);
        World.Instance.worldData.AddToModifiedChunkList(this);

    }

    public void ModifyVoxel(Vector3Int pos, byte _id, int direction) {

        if (map[pos.x, pos.y, pos.z].id == _id) return;

        VoxelState voxel = map[pos.x, pos.y, pos.z];
        byte oldOpacity = voxel.properties.opacity;

        voxel.id = _id;
        voxel.orientation = direction;

        if (voxel.properties.opacity != oldOpacity) {
            int startY = pos.y + 1;
            if (startY >= VoxelData.ChunkSize)
                Lighting.CastNaturalLightFromAbove(this, pos.x, pos.z);
            else if (map[pos.x, startY, pos.z].light == 15)
                Lighting.CastNaturalLight(this, pos.x, pos.z, startY);
        }

        if (voxel.properties.isActive && BlockBehaviour.Active(voxel))
            voxel.chunkData.chunk.AddActiveVoxel(voxel);

        for (int i = 0; i < 6; i++) {
            if (voxel.neighbours[i] != null)
                if (voxel.neighbours[i].properties.isActive &&
                    BlockBehaviour.Active(voxel.neighbours[i]))
                    voxel.neighbours[i].chunkData.chunk.AddActiveVoxel(voxel.neighbours[i]);
        }

        World.Instance.worldData.AddToModifiedChunkList(this);
        if (chunk != null) World.Instance.AddChunkToUpdate(chunk);

    }

    // -------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------

    /// <summary>
    /// Fills every voxel in the chunk with a single block ID.
    /// Much faster than the normal populate loop.
    /// </summary>
    private void FillUniform(byte id) {

        for (int x = 0; x < VoxelData.ChunkSize; x++) {
            for (int y = 0; y < VoxelData.ChunkSize; y++) {
                for (int z = 0; z < VoxelData.ChunkSize; z++) {

                    var worldPos = new Vector3Int(x + _x, y + _y, z + _z);
                    map[x, y, z] = new VoxelState(id, this, new Vector3Int(x, y, z));

                    for (int p = 0; p < 6; p++) {
                        var nv = new Vector3Int(x, y, z) + VoxelData.faceChecks[p];
                        if (IsVoxelInChunk(nv))
                            map[x, y, z].neighbours[p] = map[nv.x, nv.y, nv.z];
                        else
                            map[x, y, z].neighbours[p] =
                                World.Instance.worldData.GetVoxel(worldPos + VoxelData.faceChecks[p]);
                    }
                }
            }
        }

        Lighting.RecalculateNaturaLight(this);
        World.Instance.worldData.AddToModifiedChunkList(this);

    }

    /// <summary>
    /// Samples a coarse grid of columns to estimate the maximum surface height
    /// in this chunk's XZ footprint. Used for early-exit sky chunk detection.
    /// Only samples 4 corners + centre — 5 calls vs 256.
    /// </summary>
    private int GetMaxSurfaceEstimate() {

        int max = int.MinValue;
        int step = VoxelData.ChunkSize - 1; // Just check corners
        BiomeAttributes[] biomes = World.Instance.biomes;

        for (int x = 0; x <= step; x += step > 0 ? step : 1) {
            for (int z = 0; z <= step; z += step > 0 ? step : 1) {
                var col = HeightmapCache.GetOrCompute(x + _x, z + _z, biomes);
                if (col.surfaceHeight > max) max = col.surfaceHeight;
            }
        }

        // Also check centre
        int mid = VoxelData.ChunkSize / 2;
        var midCol = HeightmapCache.GetOrCompute(mid + _x, mid + _z, biomes);
        if (midCol.surfaceHeight > max) max = midCol.surfaceHeight;

        return max;

    }

    public bool IsVoxelInChunk(int x, int y, int z) {
        return x >= 0 && x < VoxelData.ChunkSize &&
               y >= 0 && y < VoxelData.ChunkSize &&
               z >= 0 && z < VoxelData.ChunkSize;
    }

    public bool IsVoxelInChunk(Vector3Int pos) {
        return IsVoxelInChunk(pos.x, pos.y, pos.z);
    }

    public VoxelState VoxelFromV3Int(Vector3Int pos) {
        return map[pos.x, pos.y, pos.z];
    }

}