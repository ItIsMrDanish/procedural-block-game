using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class VoxelData {

    // All chunks are now 16x16x16 cubes.
    public static readonly int ChunkSize = 16;

    // World size in chunks horizontally (X and Z).
    public static readonly int WorldSizeInChunks = 100;

    // Vertical world range in chunk coordinates.
    // MinChunkY=-4  → bottom of world at Y = -4 * 16 = -64 blocks
    // MaxChunkY=20  → top of world at    Y =  20 * 16 = 320 blocks
    // Total height = 24 chunk columns = 384 blocks
    public static readonly int MinChunkY = -4;
    public static readonly int MaxChunkY = 20;

    // Number of chunk layers vertically.
    public static int WorldHeightInChunks { get { return MaxChunkY - MinChunkY; } }

    // Lighting Values
    public static float minLightLevel = 0.1f;
    public static float maxLightLevel = 0.9f;

    public static float unitOfLight {

        get { return 1f / 16f; }
    }

    public static float tickLength = 1f;

    public static int seed;

    // World centre in blocks (horizontal).
    public static int WorldCentre {

        get { return (WorldSizeInChunks * ChunkSize) / 2; }
    }

    // Total world width/depth in blocks.
    public static int WorldSizeInVoxels {

        get { return WorldSizeInChunks * ChunkSize; }
    }

    // Absolute Y of the very bottom of the world in blocks.
    public static int WorldBottomInVoxels {

        get { return MinChunkY * ChunkSize; }
    }

    // Absolute Y of the very top of the world in blocks.
    public static int WorldTopInVoxels {

        get { return MaxChunkY * ChunkSize; }
    }

    // Sea level in absolute block Y coordinates.
    public static readonly int SeaLevel = 64;

    public static readonly int TextureAtlasSizeInBlocks = 16;
    public static float NormalizedBlockTextureSize {

        get { return 1f / (float)TextureAtlasSizeInBlocks; }
    }

    public static readonly Vector3[] voxelVerts = new Vector3[8] {

        new Vector3(0.0f, 0.0f, 0.0f),
        new Vector3(1.0f, 0.0f, 0.0f),
        new Vector3(1.0f, 1.0f, 0.0f),
        new Vector3(0.0f, 1.0f, 0.0f),
        new Vector3(0.0f, 0.0f, 1.0f),
        new Vector3(1.0f, 0.0f, 1.0f),
        new Vector3(1.0f, 1.0f, 1.0f),
        new Vector3(0.0f, 1.0f, 1.0f),
    };

    public static readonly Vector3Int[] faceChecks = new Vector3Int[6] {

        new Vector3Int( 0,  0, -1), // Back
        new Vector3Int( 0,  0,  1), // Front
        new Vector3Int( 0,  1,  0), // Top
        new Vector3Int( 0, -1,  0), // Bottom
        new Vector3Int(-1,  0,  0), // Left
        new Vector3Int( 1,  0,  0)  // Right
    };

    public static readonly int[] revFaceCheckIndex = new int[6] { 1, 0, 3, 2, 5, 4 };

    public static readonly int[,] voxelTris = new int[6, 4] {

        // Back, Front, Top, Bottom, Left, Right
        {0, 3, 1, 2}, // Back Face
        {5, 6, 4, 7}, // Front Face
        {3, 7, 2, 6}, // Top Face
        {1, 5, 0, 4}, // Bottom Face
        {4, 7, 0, 3}, // Left Face
        {1, 2, 5, 6}  // Right Face
    };

    public static readonly Vector2[] voxelUvs = new Vector2[4] {

        new Vector2(0.0f, 0.0f),
        new Vector2(0.0f, 1.0f),
        new Vector2(1.0f, 0.0f),
        new Vector2(1.0f, 1.0f)
    };
}