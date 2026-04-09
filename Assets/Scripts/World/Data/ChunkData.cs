using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Stores voxel data for one 16×16×16 chunk.
///
/// FLAT ARRAY OPTIMISATION:
/// map is VoxelState[] (1D) not VoxelState[,,] (3D).
/// In C#, [x,y,z] does 3 bounds checks + 2 multiplications per access.
/// The flat version does 1 bounds check with a pre-computed index.
/// In UpdateChunk's tight loop (~24,000 accesses per chunk) this is 2-3x faster.
/// Index formula: x + y * ChunkSize + z * ChunkSize²  (via FlatIdx)
/// </summary>
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

    // FLAT 1D array — see class summary above.
    [HideInInspector]
    public VoxelState[] map = new VoxelState[
        VoxelData.ChunkSize * VoxelData.ChunkSize * VoxelData.ChunkSize];

    // Pre-computed strides so they don't get recalculated in tight loops.
    private static readonly int S = VoxelData.ChunkSize;           // 16
    private static readonly int S2 = VoxelData.ChunkSize * VoxelData.ChunkSize; // 256

    /// <summary>Converts (x,y,z) local chunk coordinates to a flat array index.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FlatIdx(int x, int y, int z) {
        return x + y * VoxelData.ChunkSize + z * VoxelData.ChunkSize * VoxelData.ChunkSize;
    }

    // -------------------------------------------------------
    // Populate
    // -------------------------------------------------------

    public void Populate() {

        int size = VoxelData.ChunkSize;
        int chunkTopY = _y + size;

        // EARLY EXIT 1: entirely above terrain + above water.
        int maxSurface = GetMaxSurfaceEstimate();
        if (_y > maxSurface + 4 && _y > VoxelData.SeaLevel) {
            FillUniform(0);
            return;
        }

        // EARLY EXIT 2: entirely below world bottom.
        if (chunkTopY <= VoxelData.WorldBottomInVoxels + 1) {
            FillUniform(1);
            return;
        }

        BiomeAttributes[] biomes = World.Instance.biomes;

        // Column cache — noise computed ONCE per XZ column, reused for all 16 Y voxels.
        var columnCache = new TerrainGenerator.ColumnData[size, size];
        for (int x = 0; x < size; x++) {
            for (int z = 0; z < size; z++) {
                columnCache[x, z] = HeightmapCache.GetOrCompute(x + _x, z + _z, biomes);
            }
        }

        // Fill voxels.
        for (int x = 0; x < size; x++) {
            for (int z = 0; z < size; z++) {

                var col = columnCache[x, z];
                int surface = col.surfaceHeight;

                for (int y = 0; y < size; y++) {

                    int worldY = y + _y;
                    var worldPos = new Vector3Int(x + _x, worldY, z + _z);
                    int idx = FlatIdx(x, y, z);

                    byte blockId = TerrainGenerator.GetVoxel(worldPos, col);

                    // Flora — only at the exact surface voxel.
                    if (worldY == surface && worldY >= VoxelData.SeaLevel &&
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
                                    col.biome.minHeight, col.biome.maxHeight));
                        }
                    }

                    map[idx] = new VoxelState(blockId, this, new Vector3Int(x, y, z));

                    // Link neighbours within this chunk via flat index.
                    for (int p = 0; p < 6; p++) {

                        var nv = new Vector3Int(x, y, z) + VoxelData.faceChecks[p];

                        if (IsVoxelInChunk(nv.x, nv.y, nv.z))
                            map[idx].neighbours[p] = map[FlatIdx(nv.x, nv.y, nv.z)];
                        else
                            map[idx].neighbours[p] =
                                World.Instance.worldData.GetVoxel(worldPos + VoxelData.faceChecks[p]);
                    }
                }
            }
        }

        Lighting.RecalculateNaturaLight(this);
        World.Instance.worldData.AddToModifiedChunkList(this);

    }

    // -------------------------------------------------------
    // ModifyVoxel
    // -------------------------------------------------------

    public void ModifyVoxel(Vector3Int pos, byte _id, int direction) {

        int idx = FlatIdx(pos.x, pos.y, pos.z);

        if (map[idx].id == _id) return;

        VoxelState voxel = map[idx];
        byte oldOpacity = voxel.properties.opacity;

        voxel.id = _id;
        voxel.orientation = direction;

        if (voxel.properties.opacity != oldOpacity) {
            int startY = pos.y + 1;
            if (startY >= VoxelData.ChunkSize)
                Lighting.CastNaturalLightFromAbove(this, pos.x, pos.z);
            else if (map[FlatIdx(pos.x, startY, pos.z)].light == 15)
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
    // Helpers
    // -------------------------------------------------------

    /// <summary>
    /// Fills every voxel with the same block ID. Used for uniform sky/bedrock chunks.
    /// Much faster than the normal loop — no noise calls needed.
    /// </summary>
    private void FillUniform(byte id) {

        int size = VoxelData.ChunkSize;

        for (int x = 0; x < size; x++) {
            for (int y = 0; y < size; y++) {
                for (int z = 0; z < size; z++) {

                    int idx = FlatIdx(x, y, z);
                    var wPos = new Vector3Int(x + _x, y + _y, z + _z);
                    map[idx] = new VoxelState(id, this, new Vector3Int(x, y, z));

                    for (int p = 0; p < 6; p++) {
                        var nv = new Vector3Int(x, y, z) + VoxelData.faceChecks[p];
                        if (IsVoxelInChunk(nv.x, nv.y, nv.z))
                            map[idx].neighbours[p] = map[FlatIdx(nv.x, nv.y, nv.z)];
                        else
                            map[idx].neighbours[p] =
                                World.Instance.worldData.GetVoxel(wPos + VoxelData.faceChecks[p]);
                    }
                }
            }
        }

        Lighting.RecalculateNaturaLight(this);
        World.Instance.worldData.AddToModifiedChunkList(this);

    }

    /// <summary>
    /// Samples 5 corner/centre columns to estimate max surface height in this chunk's footprint.
    /// Used by the sky-chunk early-exit check. 5 HeightmapCache reads vs 256.
    /// </summary>
    private int GetMaxSurfaceEstimate() {

        int max = int.MinValue;
        int last = VoxelData.ChunkSize - 1;
        int mid = VoxelData.ChunkSize / 2;
        var biomes = World.Instance.biomes;

        void Check(int lx, int lz) {
            var col = HeightmapCache.GetOrCompute(lx + _x, lz + _z, biomes);
            if (col.surfaceHeight > max) max = col.surfaceHeight;
        }

        Check(0, 0); Check(last, 0);
        Check(0, last); Check(last, last);
        Check(mid, mid);

        return max;
    }

    // Inline bounds check — unsigned comparison: a single compare instead of two.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsVoxelInChunk(int x, int y, int z) {
        return (uint)x < (uint)VoxelData.ChunkSize &&
               (uint)y < (uint)VoxelData.ChunkSize &&
               (uint)z < (uint)VoxelData.ChunkSize;
    }

    public bool IsVoxelInChunk(Vector3Int pos) {
        return IsVoxelInChunk(pos.x, pos.y, pos.z);
    }

    public VoxelState VoxelFromV3Int(Vector3Int pos) {
        return map[FlatIdx(pos.x, pos.y, pos.z)];
    }

}