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

        // -------------------------------------------------------
        // KEY PERFORMANCE FIX:
        // Cache column data (surface height + biome) per X,Z pair.
        // Previously GetSurfaceHeight + GetBiome were called for every
        // single voxel = 4096 calls per chunk, each doing 7+ noise samples.
        // Now we calculate once per column = 256 calls, then reuse for
        // all 16 Y voxels in that column.
        // -------------------------------------------------------
        var columnCache = new TerrainGenerator.ColumnData[VoxelData.ChunkSize, VoxelData.ChunkSize];

        for (int x = 0; x < VoxelData.ChunkSize; x++) {
            for (int z = 0; z < VoxelData.ChunkSize; z++) {

                int worldX = x + _x;
                int worldZ = z + _z;

                columnCache[x, z] = TerrainGenerator.GetColumnData(
                    worldX, worldZ, World.Instance.biomes);

            }
        }

        // Now iterate all voxels, using cached column data for each Y.
        for (int x = 0; x < VoxelData.ChunkSize; x++) {
            for (int z = 0; z < VoxelData.ChunkSize; z++) {

                TerrainGenerator.ColumnData col = columnCache[x, z];

                for (int y = 0; y < VoxelData.ChunkSize; y++) {

                    Vector3Int globalPos = new Vector3Int(x + _x, y + _y, z + _z);

                    byte blockId = TerrainGenerator.GetVoxel(globalPos, col);

                    // Handle flora — queue it from main thread later via EnqueueModification
                    if (y + _y == col.surfaceHeight &&
                        y + _y >= VoxelData.SeaLevel &&
                        col.biome.placeMajorFlora) {

                        Vector2 noisePos = new Vector2(globalPos.x, globalPos.z);

                        if (Noise.Get2DPerlin(noisePos, 0f, col.biome.majorFloraZoneScale) > col.biome.majorFloraZoneThreshold &&
                            Noise.Get2DPerlin(noisePos, 0f, col.biome.majorFloraPlacementScale) > col.biome.majorFloraPlacementThreshold) {

                            World.Instance.EnqueueModification(
                                Structure.GenerateMajorFlora(
                                    col.biome.majorFloraIndex,
                                    new Vector3(globalPos.x, globalPos.y, globalPos.z),
                                    col.biome.minHeight,
                                    col.biome.maxHeight));
                        }
                    }

                    map[x, y, z] = new VoxelState(blockId, this, new Vector3Int(x, y, z));

                    // Set neighbours already in this chunk
                    for (int p = 0; p < 6; p++) {

                        Vector3Int nv = new Vector3Int(x, y, z) + VoxelData.faceChecks[p];

                        if (IsVoxelInChunk(nv))
                            map[x, y, z].neighbours[p] = VoxelFromV3Int(nv);
                        else
                            map[x, y, z].neighbours[p] = World.Instance.worldData.GetVoxel(
                                globalPos + VoxelData.faceChecks[p]);
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
                if (voxel.neighbours[i].properties.isActive && BlockBehaviour.Active(voxel.neighbours[i]))
                    voxel.neighbours[i].chunkData.chunk.AddActiveVoxel(voxel.neighbours[i]);
        }

        World.Instance.worldData.AddToModifiedChunkList(this);

        if (chunk != null)
            World.Instance.AddChunkToUpdate(chunk);

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