using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Structure {

    public static Queue<VoxelMod> GenerateMajorFlora(int index, Vector3 position, int minTrunkHeight, int maxTrunkHeight) {

        switch (index) {
            case 0: return MakeTree(position, minTrunkHeight, maxTrunkHeight);
            case 1: return MakeCacti(position, minTrunkHeight, maxTrunkHeight);
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

        // Trunk.
        for (int i = 1; i < height; i++)
            queue.Enqueue(new VoxelMod(new Vector3(baseX, baseY + i, baseZ), 6));

        // Leaf canopy.
        for (int x = -3; x < 4; x++) {
            for (int y = 0; y < 7; y++) {
                for (int z = -3; z < 4; z++) {
                    queue.Enqueue(new VoxelMod(
                        new Vector3(baseX + x, baseY + height + y, baseZ + z), 11));
                }
            }
        }

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

        for (int i = 1; i <= height; i++)
            queue.Enqueue(new VoxelMod(new Vector3(baseX, baseY + i, baseZ), 12));

        return queue;

    }

}