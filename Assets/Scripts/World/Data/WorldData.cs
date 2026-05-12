using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class WorldData {

    public string worldName = "Prototype";
    public int seed;

    [System.NonSerialized]
    public Dictionary<Vector3Int, ChunkData> chunks = new Dictionary<Vector3Int, ChunkData>();

    [System.NonSerialized]
    public List<ChunkData> modifiedChunks = new List<ChunkData>();

    private readonly object _modifiedChunksLock = new object();

    public void AddToModifiedChunkList(ChunkData chunk) {

        lock (_modifiedChunksLock) {

            if (!modifiedChunks.Contains(chunk))
                modifiedChunks.Add(chunk);
        }
    }

    public List<ChunkData> GetAndClearModifiedChunks() {

        lock (_modifiedChunksLock) {

            var copy = new List<ChunkData>(modifiedChunks);
            modifiedChunks.Clear();
            return copy;
        }
    }

    public WorldData(string _worldName, int _seed) {
        worldName = _worldName;
        seed = _seed;
    }

    public WorldData(WorldData wD) {
        worldName = wD.worldName;
        seed = wD.seed;
    }

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

    // Tracks which chunk origins are currently mid-Populate() on any thread.
    [System.NonSerialized]
    private readonly HashSet<Vector3Int> _populatingSet = new HashSet<Vector3Int>();
    private readonly object _populatingLock = new object();

    // RequestChunk - safe from any thread.
    public ChunkData RequestChunk(Vector3Int blockOrigin, bool create) {

        while (true) {

            ChunkData shell = null;
            bool shouldPopulate = false;
            bool needsPopulate = false;

            lock (World.StaticChunkListLock) {

                if (chunks.TryGetValue(blockOrigin, out ChunkData existing))
                    return existing;

                if (!create)
                    return null;

                lock (_populatingLock) {

                    if (!_populatingSet.Contains(blockOrigin)) {

                        _populatingSet.Add(blockOrigin);
                        shouldPopulate = true;

                        shell = SaveSystem.LoadChunk(worldName, blockOrigin);

                        if (shell == null) {

                            shell = new ChunkData(blockOrigin);
                            needsPopulate = true;
                        }

                        // Insert shell before releasing lock so neighbours find it.
                        chunks[blockOrigin] = shell;
                    }
                }
            }

            if (shouldPopulate) {

                if (needsPopulate)
                    shell.Populate();

                lock (_populatingLock) { _populatingSet.Remove(blockOrigin); }

                return shell;
            }

            // Another thread is populating this origin — yield CPU and retry.
            System.Threading.Thread.Sleep(0);
        }
    }

    // LoadChunk: used by LoadWorldAsync (main thread, before threading starts).
    public void LoadChunk(Vector3Int blockOrigin) {

        lock (World.StaticChunkListLock) {
            if (chunks.ContainsKey(blockOrigin)) return;
        }

        ChunkData chunk = SaveSystem.LoadChunk(worldName, blockOrigin);

        if (chunk == null) {
            chunk = new ChunkData(blockOrigin);
            chunk.Populate();
        }

        lock (World.StaticChunkListLock) {
            if (!chunks.ContainsKey(blockOrigin))
                chunks[blockOrigin] = chunk;
        }
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

        // Guard: block ID must be valid. Structure mods (VoidTree etc.) use high IDs
        // (28, 32, 33). If blocktypes array is shorter, voxel.properties crashes.
        if (value >= World.Instance.blocktypes.Length) {
            UnityEngine.Debug.LogWarning($"SetVoxel: block ID {value} out of blocktypes range ({World.Instance.blocktypes.Length}). Skipping.");
            return;
        }

        // FIX — float precision: VoxelMod.position is Vector3. FloorToInt(31.9999f)
        // gives 31 but origin is 32, producing local = -1. Clamp to [0, ChunkSize-1].
        Vector3Int origin = BlockToChunkOrigin(pos);
        ChunkData chunk = RequestChunk(origin, true);

        int cs = VoxelData.ChunkSize;
        int lx = Mathf.Clamp(Mathf.FloorToInt(pos.x) - origin.x, 0, cs - 1);
        int ly = Mathf.Clamp(Mathf.FloorToInt(pos.y) - origin.y, 0, cs - 1);
        int lz = Mathf.Clamp(Mathf.FloorToInt(pos.z) - origin.z, 0, cs - 1);

        chunk.ModifyVoxel(new Vector3Int(lx, ly, lz), value, direction);
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

        return chunk.map[ChunkData.FlatIdx(local.x, local.y, local.z)];
    }

    public VoxelState GetVoxel(Vector3Int pos) {

        if (!IsVoxelInWorld(pos)) return null;

        Vector3Int origin = BlockToChunkOrigin(pos);
        ChunkData chunk = RequestChunk(origin, false);

        if (chunk == null) return null;

        return chunk.map[ChunkData.FlatIdx(pos.x - origin.x, pos.y - origin.y, pos.z - origin.z)];
    }
}