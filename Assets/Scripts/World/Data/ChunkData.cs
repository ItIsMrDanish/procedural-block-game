using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    public VoxelState[] map = new VoxelState[
        VoxelData.ChunkSize * VoxelData.ChunkSize * VoxelData.ChunkSize];

    private static readonly int S  = VoxelData.ChunkSize;
    private static readonly int S2 = VoxelData.ChunkSize * VoxelData.ChunkSize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FlatIdx(int x, int y, int z) {
        return x + y * VoxelData.ChunkSize + z * VoxelData.ChunkSize * VoxelData.ChunkSize;
    }

    public void Populate() {

        int size = VoxelData.ChunkSize;
        int chunkTopY = _y + size;

        int maxSurface = GetMaxSurfaceEstimate();
        if (_y > maxSurface + 4 && _y > VoxelData.SeaLevel) {
            FillUniform(0);
            return;
        }

        if (chunkTopY <= VoxelData.WorldBottomInVoxels + 1) {
            FillUniform(1);
            return;
        }

        BiomeAttributes[] biomes = World.Instance.biomes;

        var columnCache = new TerrainGenerator.ColumnData[size, size];
        for (int x = 0; x < size; x++) {
            for (int z = 0; z < size; z++) {
                columnCache[x, z] = HeightmapCache.GetOrCompute(x + _x, z + _z, biomes);
            }
        }

        for (int x = 0; x < size; x++) {
            for (int z = 0; z < size; z++) {

                var col = columnCache[x, z];
                int surface = col.surfaceHeight;

                for (int y = 0; y < size; y++) {

                    int worldY = y + _y;
                    var worldPos = new Vector3Int(x + _x, worldY, z + _z);
                    int idx = FlatIdx(x, y, z);

                    byte blockId = TerrainGenerator.GetVoxel(worldPos, col);

                    // Flora placement. Biome border stability is handled upstream
                    // in TerrainGenerator by smoothing climate over a kernel, so
                    // col.biome is stable across columns and no confidence gate is needed.
                    if (worldY == surface && col.biome.placeMajorFlora &&
                        blockId != 14 && blockId != 0 &&
                        worldY >= VoxelData.SeaLevel) {

                        var noisePos = new Vector2(worldPos.x, worldPos.z);

                        if (Noise.Get2DPerlin(noisePos, 0f,   col.biome.majorFloraZoneScale)      > col.biome.majorFloraZoneThreshold &&
                            Noise.Get2DPerlin(noisePos, 200f, col.biome.majorFloraPlacementScale) > col.biome.majorFloraPlacementThreshold) {

                            World.Instance.EnqueueModification(Structure.GenerateMajorFlora(
                                col.biome.majorFloraIndex,
                                new Vector3(worldPos.x, worldY, worldPos.z),
                                col.biome.minHeight, col.biome.maxHeight));
                        }
                    }

                    map[idx] = new VoxelState(blockId, this, new Vector3Int(x, y, z));

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
        ScheduleNeighbourChunkUpdates();
    }

    private void ScheduleNeighbourChunkUpdates() {

        int cs = VoxelData.ChunkSize;
        var neighbourOrigins = new Vector3Int[6] {
            new Vector3Int(_x,      _y,      _z - cs),
            new Vector3Int(_x,      _y,      _z + cs),
            new Vector3Int(_x,      _y + cs, _z),
            new Vector3Int(_x,      _y - cs, _z),
            new Vector3Int(_x - cs, _y,      _z),
            new Vector3Int(_x + cs, _y,      _z),
        };

        foreach (var origin in neighbourOrigins) {
            ChunkData neighbour = World.Instance.worldData.RequestChunk(origin, false);
            if (neighbour != null && neighbour.chunk != null)
                World.Instance.AddChunkToUpdate(neighbour.chunk);
        }
    }

    public void ModifyVoxel(Vector3Int pos, byte _id, int direction) {

        if (!IsVoxelInChunk(pos.x, pos.y, pos.z)) return;
        if (_id >= World.Instance.blocktypes.Length) return;

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

        if (voxel.properties.isActive && BlockBehaviour.Active(voxel) &&
            voxel.chunkData.chunk != null)
            voxel.chunkData.chunk.AddActiveVoxel(voxel);

        for (int i = 0; i < 6; i++) {
            if (voxel.neighbours[i] != null &&
                voxel.neighbours[i].chunkData.chunk != null &&
                voxel.neighbours[i].properties.isActive &&
                BlockBehaviour.Active(voxel.neighbours[i]))
                voxel.neighbours[i].chunkData.chunk.AddActiveVoxel(voxel.neighbours[i]);
        }

        World.Instance.worldData.AddToModifiedChunkList(this);
        if (chunk != null) World.Instance.AddChunkToUpdate(chunk);
    }

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
        ScheduleNeighbourChunkUpdates();
    }

    private int GetMaxSurfaceEstimate() {

        int max = int.MinValue;
        int last = VoxelData.ChunkSize - 1;
        int mid  = VoxelData.ChunkSize / 2;
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