using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Lighting {

    public static void RecalculateNaturaLight(ChunkData chunkData) {

        for (int x = 0; x < VoxelData.ChunkSize; x++) {
            for (int z = 0; z < VoxelData.ChunkSize; z++) {

                bool hasChunkAbove = GetChunkAbove(chunkData) != null;

                if (!hasChunkAbove)
                    CastNaturalLight(chunkData, x, z, VoxelData.ChunkSize - 1);
                else
                    PropagateFromAbove(chunkData, x, z);

            }
        }

    }

    public static void CastNaturalLightFromAbove(ChunkData chunkData, int x, int z) {
        PropagateFromAbove(chunkData, x, z);
    }

    public static void CastNaturalLight(ChunkData chunkData, int x, int z, int startY) {

        if (startY >= VoxelData.ChunkSize) startY = VoxelData.ChunkSize - 1;

        bool obstructed = IsObstructedFromAbove(chunkData, x, z);

        for (int y = startY; y >= 0; y--) {

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

        if (!obstructed) {
            ChunkData below = GetChunkBelow(chunkData);
            if (below != null)
                CastNaturalLight(below, x, z, VoxelData.ChunkSize - 1);
        }

    }

    static void PropagateFromAbove(ChunkData chunkData, int x, int z) {

        ChunkData above = GetChunkAbove(chunkData);
        byte topLight = 0;

        if (above != null)
            topLight = above.map[x, 0, z].light;

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
        if (above == null) return false;
        return above.map[x, 0, z].light < 15;

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