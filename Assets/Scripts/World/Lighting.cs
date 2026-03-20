using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Lighting {

    // Recalculates sunlight for all columns in a single cubic chunk.
    // For a cubic chunk system we can't do a full column pass inside one chunk —
    // we instead process the top face of each chunk column and continue downward.
    public static void RecalculateNaturaLight(ChunkData chunkData) {

        for (int x = 0; x < VoxelData.ChunkSize; x++) {
            for (int z = 0; z < VoxelData.ChunkSize; z++) {

                // Check if any chunk exists directly above this one.
                // If so, we may not be at the top of the world.
                bool hasChunkAbove = HasChunkAbove(chunkData);

                if (!hasChunkAbove) {
                    // This chunk is at the top of the world in this column —
                    // start light cast from the top of this chunk.
                    CastNaturalLight(chunkData, x, z, VoxelData.ChunkSize - 1);
                } else {
                    // There is a chunk above — propagate from the top face
                    // using the light value of the voxel above if available.
                    PropagateFromAbove(chunkData, x, z);
                }
            }
        }
    }

    // Called when a block is broken/placed and we need to re-cast light from a chunk above.
    public static void CastNaturalLightFromAbove(ChunkData chunkData, int x, int z) {

        PropagateFromAbove(chunkData, x, z);
    }

    // Casts natural light downward within a single chunk starting at startY (inclusive).
    public static void CastNaturalLight(ChunkData chunkData, int x, int z, int startY) {

        if (startY >= VoxelData.ChunkSize) startY = VoxelData.ChunkSize - 1;

        // Determine if we are starting from a fully lit position.
        // A column is "unobstructed from above" if either:
        // a) this is the topmost chunk, or
        // b) the voxel directly above (in the chunk above) has light == 15.
        bool obstructed = IsObstructedFromAbove(chunkData, x, z);

        for (int y = startY; y >= 0; y--) {

            VoxelState voxel = chunkData.map[x, y, z];

            if (obstructed) {
                voxel.light = 0;
            } else if (voxel.properties.isWater) {
                voxel.light = 15; // Water lets light through (opacity must be 0).
            } else if (voxel.properties.opacity > 0) {
                voxel.light = 0;
                obstructed = true;
            } else {
                voxel.light = 15;
            }
        }

        // Continue into the chunk below if needed.
        if (!obstructed) {

            ChunkData below = GetChunkBelow(chunkData);
            if (below != null)
                CastNaturalLight(below, x, z, VoxelData.ChunkSize - 1);
        }
    }

    // -------------------------------------------------------
    // Helpers
    // -------------------------------------------------------

    static void PropagateFromAbove(ChunkData chunkData, int x, int z) {

        // Get the light value of the bottom face of the chunk above.
        ChunkData above = GetChunkAbove(chunkData);
        byte topLight = 0;

        if (above != null) {
            topLight = above.map[x, 0, z].light;
        }

        // If the voxel above is fully lit and unobstructed, treat this column as unobstructed.
        bool obstructed = topLight < 15;

        for (int y = VoxelData.ChunkSize - 1; y >= 0; y--) {

            VoxelState voxel = chunkData.map[x, y, z];

            if (obstructed) {

                voxel.light = 0;
            } else if (voxel.properties.isWater) {

                voxel.light = 15;
            } else if (voxel.properties.opacity > 0) {

                voxel.light = 0;
                obstructed = true;
            } else {

                voxel.light = 15;
            }
        }
    }

    static bool IsObstructedFromAbove(ChunkData chunkData, int x, int z) {

        ChunkData above = GetChunkAbove(chunkData);
        if (above == null) return false; // No chunk above = top of world = not obstructed.

        // The bottom row of the chunk above tells us if light came through.
        return above.map[x, 0, z].light < 15;
    }

    static bool HasChunkAbove(ChunkData chunkData) {

        return GetChunkAbove(chunkData) != null;
    }

    static ChunkData GetChunkAbove(ChunkData chunkData) {

        Vector3Int aboveOrigin = chunkData.position + new Vector3Int(0, VoxelData.ChunkSize, 0);
        return World.Instance.worldData.RequestChunk(aboveOrigin, false);
    }

    static ChunkData GetChunkBelow(ChunkData chunkData) {

        Vector3Int belowOrigin = chunkData.position - new Vector3Int(0, VoxelData.ChunkSize, 0);
        return World.Instance.worldData.RequestChunk(belowOrigin, false);
    }
}