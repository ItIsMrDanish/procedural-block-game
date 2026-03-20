using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Noise {

    public static float Get2DPerlin(Vector2 position, float offset, float scale) {

        position.x += (offset + VoxelData.seed + 0.1f);
        position.y += (offset + VoxelData.seed + 0.1f);

        // Was ChunkWidth — now ChunkSize since chunks are cubic.
        return Mathf.PerlinNoise(
            position.x / VoxelData.ChunkSize * scale,
            position.y / VoxelData.ChunkSize * scale);
    }

    public static bool Get3DPerlin(Vector3 position, float offset, float scale, float threshold) {

        float x = (position.x + offset + VoxelData.seed + 0.1f) * scale;
        float y = (position.y + offset + VoxelData.seed + 0.1f) * scale;
        float z = (position.z + offset + VoxelData.seed + 0.1f) * scale;

        float AB = Mathf.PerlinNoise(x, y);
        float BC = Mathf.PerlinNoise(y, z);
        float AC = Mathf.PerlinNoise(x, z);
        float BA = Mathf.PerlinNoise(y, x);
        float CB = Mathf.PerlinNoise(z, y);
        float CA = Mathf.PerlinNoise(z, x);

        return (AB + BC + AC + BA + CB + CA) / 6f > threshold;
    }

    // Integer overload — avoids Vector3 allocation when called with Vector3Int positions.
    public static bool Get3DPerlin(Vector3Int position, float offset, float scale, float threshold) {

        return Get3DPerlin(new Vector3(position.x, position.y, position.z), offset, scale, threshold);
    }
}

// Original for simplex

//using UnityEngine;

//public static class Noise {

//    public static float Get2DSimplex(Vector2 position, float offset, float scale) {

//        return SimplexNoise.Noise((position.x + 1f) / VoxelData.ChunkWidth * scale + offset, (position.y + 1f) / VoxelData.ChunkWidth * scale + offset) * 0.5f;
//    }

//    public static bool Get3DSimplex(Vector3 position, float offset, float scale, float threshold) {

//        float x = (position.x + offset + 0.1f) * scale;
//        float y = (position.y + offset + 0.1f) * scale;
//        float z = (position.z + offset + 0.1f) * scale;

//        float noise = (SimplexNoise.Noise(x, y, z) + 1f) * 0.5f;
//        return noise > threshold;
//    }
//}