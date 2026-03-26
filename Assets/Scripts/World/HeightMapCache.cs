using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// Caches terrain column data (surface height + biome) keyed by world XZ position.
///
/// WHY THIS EXISTS:
/// In the cubic chunk system, every vertical chunk layer (e.g. Y=0, Y=16, Y=32...)
/// sharing the same XZ footprint would each call GetColumnData independently —
/// paying the full noise cost 6+ times for the identical X,Z position.
/// This cache ensures the noise is computed exactly ONCE per unique world column,
/// regardless of how many vertical chunks sit on top of each other.
///
/// THREAD SAFETY:
/// Chunk population runs on a background thread. The cache uses a
/// ReaderWriterLockSlim so multiple threads can read simultaneously
/// while only one thread writes at a time.
/// </summary>
public static class HeightmapCache {

    private static readonly Dictionary<Vector2Int, TerrainGenerator.ColumnData> _cache
        = new Dictionary<Vector2Int, TerrainGenerator.ColumnData>(4096);

    private static readonly ReaderWriterLockSlim _lock
        = new ReaderWriterLockSlim();

    // -------------------------------------------------------
    // Public API
    // -------------------------------------------------------

    /// <summary>
    /// Returns column data for the given world XZ position.
    /// Reads from cache if already computed, otherwise computes and stores.
    /// </summary>
    public static TerrainGenerator.ColumnData GetOrCompute(
        int worldX, int worldZ, BiomeAttributes[] biomes) {

        var key = new Vector2Int(worldX, worldZ);

        // Try read first — fast path, no write lock needed.
        _lock.EnterReadLock();
        try {
            if (_cache.TryGetValue(key, out var cached))
                return cached;
        } finally {
            _lock.ExitReadLock();
        }

        // Not cached — compute and store.
        var data = TerrainGenerator.ComputeColumnData(worldX, worldZ, biomes);

        _lock.EnterWriteLock();
        try {
            // Double-check in case another thread computed it while we waited.
            if (!_cache.ContainsKey(key))
                _cache[key] = data;
            else
                data = _cache[key]; // Use the version that was written first.
        } finally {
            _lock.ExitWriteLock();
        }

        return data;

    }

    /// <summary>
    /// Pre-warms the cache for a rectangular region around the given world chunk centre.
    /// Call this once at startup before chunk population begins so threads never
    /// have to compute the same column twice.
    /// </summary>
    public static void PreWarm(int centreChunkX, int centreChunkZ,
                               int horizontalChunks, BiomeAttributes[] biomes) {

        int blockMin_X = (centreChunkX - horizontalChunks) * VoxelData.ChunkSize;
        int blockMax_X = (centreChunkX + horizontalChunks) * VoxelData.ChunkSize;
        int blockMin_Z = (centreChunkZ - horizontalChunks) * VoxelData.ChunkSize;
        int blockMax_Z = (centreChunkZ + horizontalChunks) * VoxelData.ChunkSize;

        for (int x = blockMin_X; x < blockMax_X; x++) {
            for (int z = blockMin_Z; z < blockMax_Z; z++) {
                GetOrCompute(x, z, biomes);
            }
        }

    }

    /// <summary>
    /// Removes cached columns outside a given radius from a centre.
    /// Call periodically to avoid unbounded memory growth as the player moves.
    /// </summary>
    public static void Trim(int centreChunkX, int centreChunkZ, int keepChunkRadius) {

        int keepRadius = keepChunkRadius * VoxelData.ChunkSize;

        int cx = centreChunkX * VoxelData.ChunkSize;
        int cz = centreChunkZ * VoxelData.ChunkSize;

        var toRemove = new System.Collections.Generic.List<Vector2Int>();

        _lock.EnterReadLock();
        try {
            foreach (var kv in _cache) {
                int dx = Mathf.Abs(kv.Key.x - cx);
                int dz = Mathf.Abs(kv.Key.y - cz);
                if (dx > keepRadius || dz > keepRadius)
                    toRemove.Add(kv.Key);
            }
        } finally {
            _lock.ExitReadLock();
        }

        if (toRemove.Count == 0) return;

        _lock.EnterWriteLock();
        try {
            foreach (var key in toRemove)
                _cache.Remove(key);
        } finally {
            _lock.ExitWriteLock();
        }

    }

    /// <summary>Clears the entire cache. Call when loading a new world.</summary>
    public static void Clear() {

        _lock.EnterWriteLock();
        try { _cache.Clear(); } finally { _lock.ExitWriteLock(); }

    }

}