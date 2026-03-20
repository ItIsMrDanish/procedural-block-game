using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WorldData {

    public string worldName = "Prototype";
    public int seed;

    // Key: block-space origin of chunk (always a multiple of ChunkSize).
    [System.NonSerialized]
    public Dictionary<Vector3Int, ChunkData> chunks = new Dictionary<Vector3Int, ChunkData>();

    [System.NonSerialized]
    public List<ChunkData> modifiedChunks = new List<ChunkData>();

    public void AddToModifiedChunkList(ChunkData chunk) {

        if (!modifiedChunks.Contains(chunk))
            modifiedChunks.Add(chunk);
    }

    public WorldData(string _worldName, int _seed) {

        worldName = _worldName;
        seed = _seed;
    }

    public WorldData(WorldData wD) {

        worldName = wD.worldName;
        seed = wD.seed;
    }

    // -------------------------------------------------------
    // Converts any block position to the chunk origin key.
    // -------------------------------------------------------
    private static Vector3Int BlockToChunkOrigin(Vector3Int pos) {

        return new Vector3Int(
            Mathf.FloorToInt((float)pos.x / VoxelData.ChunkSize) * VoxelData.ChunkSize,
            Mathf.FloorToInt((float)pos.y / VoxelData.ChunkSize) * VoxelData.ChunkSize,
            Mathf.FloorToInt((float)pos.z / VoxelData.ChunkSize) * VoxelData.ChunkSize);
    }

    private static Vector3Int BlockToChunkOrigin(Vector3 pos) {

        return new Vector3Int(
            Mathf.FloorToInt(pos.x / VoxelData.ChunkSize) * VoxelData.ChunkSize,
            Mathf.FloorToInt(pos.y / VoxelData.ChunkSize) * VoxelData.ChunkSize,
            Mathf.FloorToInt(pos.z / VoxelData.ChunkSize) * VoxelData.ChunkSize);
    }

    public ChunkData RequestChunk(Vector3Int blockOrigin, bool create) {

        ChunkData c;

        lock (World.Instance.ChunkListThreadLock) {

            if (chunks.TryGetValue(blockOrigin, out c))
                return c;

            if (!create)
                return null;

            LoadChunk(blockOrigin);
            chunks.TryGetValue(blockOrigin, out c);
        }

        return c;
    }

    public void LoadChunk(Vector3Int blockOrigin) {

        if (chunks.ContainsKey(blockOrigin)) return;

        ChunkData chunk = SaveSystem.LoadChunk(worldName, blockOrigin);
        if (chunk != null) {

            chunks.Add(blockOrigin, chunk);
            return;
        }

        chunks.Add(blockOrigin, new ChunkData(blockOrigin));
        chunks[blockOrigin].Populate();
    }

    bool IsVoxelInWorld(Vector3Int pos) {

        return pos.x >= 0 && pos.x < VoxelData.WorldSizeInVoxels &&
               pos.y >= VoxelData.WorldBottomInVoxels && pos.y < VoxelData.WorldTopInVoxels &&
               pos.z >= 0 && pos.z < VoxelData.WorldSizeInVoxels;
    }

    bool IsVoxelInWorld(Vector3 pos) {

        return pos.x >= 0 && pos.x < VoxelData.WorldSizeInVoxels &&
               pos.y >= VoxelData.WorldBottomInVoxels && pos.y < VoxelData.WorldTopInVoxels &&
               pos.z >= 0 && pos.z < VoxelData.WorldSizeInVoxels;
    }

    public void SetVoxel(Vector3 pos, byte value, int direction) {

        if (!IsVoxelInWorld(pos)) return;

        Vector3Int origin = BlockToChunkOrigin(pos);
        ChunkData chunk = RequestChunk(origin, true);

        Vector3Int local = new Vector3Int(
            Mathf.FloorToInt(pos.x) - origin.x,
            Mathf.FloorToInt(pos.y) - origin.y,
            Mathf.FloorToInt(pos.z) - origin.z);

        chunk.ModifyVoxel(local, value, direction);
    }

    public VoxelState GetVoxel(Vector3 pos) {

        if (!IsVoxelInWorld(pos)) return null;

        Vector3Int origin = BlockToChunkOrigin(pos);
        ChunkData chunk = RequestChunk(origin, false);

        if (chunk == null) return null;

        Vector3Int local = new Vector3Int(
            Mathf.FloorToInt(pos.x) - origin.x,
            Mathf.FloorToInt(pos.y) - origin.y,
            Mathf.FloorToInt(pos.z) - origin.z);

        return chunk.map[local.x, local.y, local.z];
    }

    // Integer overload — avoids float conversion for internal calls.
    public VoxelState GetVoxel(Vector3Int pos) {

        if (!IsVoxelInWorld(pos)) return null;

        Vector3Int origin = BlockToChunkOrigin(pos);
        ChunkData chunk = RequestChunk(origin, false);

        if (chunk == null) return null;

        return chunk.map[pos.x - origin.x, pos.y - origin.y, pos.z - origin.z];
    }
}