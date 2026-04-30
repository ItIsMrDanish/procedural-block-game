using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Structure {

    public static Queue<VoxelMod> GenerateMajorFlora(int index, Vector3 position, int minTrunkHeight, int maxTrunkHeight) {

        switch (index) {

            case 0: return MakeTree(position, minTrunkHeight, maxTrunkHeight);
            case 1: return MakeCacti(position, minTrunkHeight, maxTrunkHeight);
            case 2: return MakeVoidTree(position, minTrunkHeight, maxTrunkHeight);
        }

        return new Queue<VoxelMod>();

    }

    public static Queue<VoxelMod> MakeTree(Vector3 position, int minTrunkHeight, int maxTrunkHeight) {

        Queue<VoxelMod> queue = new Queue<VoxelMod>();
        int height = (int)(maxTrunkHeight * Noise.Get2DPerlin(
            new Vector2(position.x, position.z), 250f, 3f));

        if (height < minTrunkHeight) height = minTrunkHeight;

        int baseX = Mathf.FloorToInt(position.x);
        int baseY = Mathf.FloorToInt(position.y);
        int baseZ = Mathf.FloorToInt(position.z);

        // Trunk — extends 2 blocks up into the canopy.
        for (int i = 1; i <= height + 2; i++)
            queue.Enqueue(new VoxelMod(new Vector3(baseX, baseY + i, baseZ), 6));

        // Bottom two canopy layers (large, 5x5 with corners trimmed).
        for (int layer = 0; layer < 2; layer++) {
            int y = height - 1 + layer;
            for (int x = -2; x <= 2; x++) {
                for (int z = -2; z <= 2; z++) {
                    if (Mathf.Abs(x) == 2 && Mathf.Abs(z) == 2) continue;
                    queue.Enqueue(new VoxelMod(new Vector3(baseX + x, baseY + y, baseZ + z), 11));
                }
            }
        }

        // Middle two canopy layers (medium, 3x3 full).
        for (int layer = 0; layer < 2; layer++) {
            int y = height + 1 + layer;
            for (int x = -1; x <= 1; x++) {
                for (int z = -1; z <= 1; z++) {
                    queue.Enqueue(new VoxelMod(new Vector3(baseX + x, baseY + y, baseZ + z), 11));
                }
            }
        }

        // Top layer (plus/cross shape).
        int topY = height + 3;
        queue.Enqueue(new VoxelMod(new Vector3(baseX,     baseY + topY, baseZ),     11));
        queue.Enqueue(new VoxelMod(new Vector3(baseX + 1, baseY + topY, baseZ),     11));
        queue.Enqueue(new VoxelMod(new Vector3(baseX - 1, baseY + topY, baseZ),     11));
        queue.Enqueue(new VoxelMod(new Vector3(baseX,     baseY + topY, baseZ + 1), 11));
        queue.Enqueue(new VoxelMod(new Vector3(baseX,     baseY + topY, baseZ - 1), 11));

        return queue;
    }

    public static Queue<VoxelMod> MakeCacti(Vector3 position, int minTrunkHeight, int maxTrunkHeight) {

        Queue<VoxelMod> queue = new Queue<VoxelMod>();
        int height = (int)(maxTrunkHeight * Noise.Get2DPerlin(
            new Vector2(position.x, position.z), 23456f, 2f));

        if (height < minTrunkHeight) height = minTrunkHeight;

        int baseX = Mathf.FloorToInt(position.x);
        int baseY = Mathf.FloorToInt(position.y);
        int baseZ = Mathf.FloorToInt(position.z);

        // Body blocks (block 12).
        for (int i = 1; i < height; i++)
            queue.Enqueue(new VoxelMod(new Vector3(baseX, baseY + i, baseZ), 12));

        // Top cap block (block 13).
        queue.Enqueue(new VoxelMod(new Vector3(baseX, baseY + height, baseZ), 13));

        return queue;
    }

        public static Queue<VoxelMod> MakeVoidTree(Vector3 position, int minTrunkHeight, int maxTrunkHeight) {

        Queue<VoxelMod> queue = new Queue<VoxelMod>();
        int height = (int)(maxTrunkHeight * Noise.Get2DPerlin(
            new Vector2(position.x, position.z), 250f, 3f));

        if (height < minTrunkHeight) height = minTrunkHeight;

        int baseX = Mathf.FloorToInt(position.x);
        int baseY = Mathf.FloorToInt(position.y);
        int baseZ = Mathf.FloorToInt(position.z);

        // Trunk — extends 2 blocks up into the canopy.
        for (int i = 1; i <= height + 2; i++)
            queue.Enqueue(new VoxelMod(new Vector3(baseX, baseY + i, baseZ), 28));

        // Bottom two canopy layers (large, 5x5 with corners trimmed).
        for (int layer = 0; layer < 2; layer++) {
            int y = height - 1 + layer;
            for (int x = -2; x <= 2; x++) {
                for (int z = -2; z <= 2; z++) {
                    if (Mathf.Abs(x) == 2 && Mathf.Abs(z) == 2) continue;
                    queue.Enqueue(new VoxelMod(new Vector3(baseX + x, baseY + y, baseZ + z), 32));
                }
            }
        }

        // Middle two canopy layers (medium, 3x3 full).
        for (int layer = 0; layer < 2; layer++) {
            int y = height + 1 + layer;
            for (int x = -1; x <= 1; x++) {
                for (int z = -1; z <= 1; z++) {
                    queue.Enqueue(new VoxelMod(new Vector3(baseX + x, baseY + y, baseZ + z), 32));
                }
            }
        }

        // Top layer (plus/cross shape).
        int topY = height + 3;
        queue.Enqueue(new VoxelMod(new Vector3(baseX,     baseY + topY, baseZ),     32));
        queue.Enqueue(new VoxelMod(new Vector3(baseX + 1, baseY + topY, baseZ),     32));
        queue.Enqueue(new VoxelMod(new Vector3(baseX - 1, baseY + topY, baseZ),     32));
        queue.Enqueue(new VoxelMod(new Vector3(baseX,     baseY + topY, baseZ + 1), 32));
        queue.Enqueue(new VoxelMod(new Vector3(baseX,     baseY + topY, baseZ - 1), 32));

        return queue;
    }
}