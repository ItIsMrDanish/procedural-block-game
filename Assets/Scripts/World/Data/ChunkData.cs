using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

// Stores voxel data for one 16x16x16 chunk.
//
// FLAT ARRAY OPTIMISATION:
// map is VoxelState[] (1D) not VoxelState[,,] (3D).
// In C#, [x,y,z] does 3 bounds checks + 2 multiplications per access.
// The flat version does 1 bounds check with a pre-computed index.
// In UpdateChunk's tight loop (~24,000 accesses per chunk) this is 2-3x faster.
// Index formula: x + y * ChunkSize + z * ChunkSize^2  (via FlatIdx)

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
    private static readonly int S  = VoxelData.ChunkSize;
    private static readonly int S2 = VoxelData.ChunkSize * VoxelData.ChunkSize;

    /// <summary>Converts (x,y,z) local chunk coordinates to a flat array index.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FlatIdx(int x, int y, int z) {

        return x + y * VoxelData.ChunkSize + z * VoxelData.ChunkSize * VoxelData.ChunkSize;
    }

    // Populate
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

                        if (Noise.Get2DPerlin(noisePos, 0f,   col.biome.majorFloraZoneScale)      > col.biome.majorFloraZoneThreshold &&
                            Noise.Get2DPerlin(noisePos, 200f, col.biome.majorFloraPlacementScale) > col.biome.majorFloraPlacementThreshold) {

                            World.Instance.EnqueueModification(Structure.GenerateMajorFlora(
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

        // FIX — INVISIBLE SEAMS WITH THREADING:
        //
        // When this chunk was populated, its border voxels wired cross-chunk neighbours
        // via GetVoxel(). Those neighbour voxels now have a back-reference to this chunk's
        // border voxels (set by VoxelNeighbours.ReturnNeighbour). But the NEIGHBOUR CHUNKS'
        // mesh was already built before this chunk existed — their border faces saw null
        // neighbours and were skipped (correctly, no face to render against air/void).
        // Now that this chunk is populated, those neighbour chunks need to rebuild their
        // meshes so the newly-visible faces appear.
        //
        // The null guard in VoxelState.light/PropogateLight("if chunk != null") prevented
        // AddChunkToUpdate from firing during Populate because chunkData.chunk is set by
        // the Chunk MonoBehaviour AFTER ChunkData construction. So we schedule the
        // neighbour re-meshes here explicitly, after Populate is fully done.
        //
        // We only need to trigger the 6 face-adjacent neighbour chunks (not this chunk —
        // new Chunk() will call AddChunkToUpdate on itself in its constructor).
        ScheduleNeighbourChunkUpdates();
    }

    // After this chunk is populated, tell each of the 6 face-adjacent neighbour chunks
    // to rebuild their mesh. Their border faces may have been skipped when they were
    // first built because this chunk didn't exist yet.
    private void ScheduleNeighbourChunkUpdates() {

        int cs = VoxelData.ChunkSize;

        // The 6 neighbour chunk origins (one per face direction).
        var neighbourOrigins = new Vector3Int[6] {
            new Vector3Int(_x,      _y,      _z - cs), // Back
            new Vector3Int(_x,      _y,      _z + cs), // Front
            new Vector3Int(_x,      _y + cs, _z),      // Top
            new Vector3Int(_x,      _y - cs, _z),      // Bottom
            new Vector3Int(_x - cs, _y,      _z),      // Left
            new Vector3Int(_x + cs, _y,      _z),      // Right
        };

        foreach (var origin in neighbourOrigins) {

            // RequestChunk with create=false: only look up, never populate.
            ChunkData neighbour = World.Instance.worldData.RequestChunk(origin, false);

            // Only schedule if the neighbour exists AND has a Chunk MonoBehaviour.
            // If chunk is null, new Chunk() hasn't run yet for that coord —
            // the Chunk constructor calls AddChunkToUpdate itself, so we don't need to.
            if (neighbour != null && neighbour.chunk != null)
                World.Instance.AddChunkToUpdate(neighbour.chunk);
        }
    }

    // ModifyVoxel

    public void ModifyVoxel(Vector3Int pos, byte _id, int direction) {

        // Guard 1: position must be within this chunk's bounds.
        if (!IsVoxelInChunk(pos.x, pos.y, pos.z)) return;

        // Guard 2: block ID must be within blocktypes array.
        // Structure mods (VoidTree etc.) use IDs 28/32/33. If those blocktypes entries
        // don't exist yet, voxel.properties crashes with IndexOutOfRangeException.
        if (_id >= World.Instance.blocktypes.Length) return;

        int idx = FlatIdx(pos.x, pos.y, pos.z);

        if (map[idx].id == _id) return;

        VoxelState voxel = map[idx];
        // Cache opacity BEFORE changing id, so we can detect opacity changes.
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

        // FIX — NULL CHUNK GUARD:
        // chunkData.chunk is null if the Chunk MonoBehaviour hasn't been assigned yet
        // (during Populate or when called from bg thread before new Chunk() runs).
        // Calling AddActiveVoxel on a null chunk would NullReferenceException.
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

    // Helpers

    // Fills every voxel with the same block ID. Used for uniform sky/bedrock chunks.
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

    // Samples 5 corner/centre columns to estimate max surface height in this chunk's footprint.
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