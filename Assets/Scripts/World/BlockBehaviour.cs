using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class BlockBehaviour {

    // Cached reusable list for grass spread neighbours.
    // Previously allocated new List<VoxelState>() every Behave() call,
    // which happens every tick for every active grass block.
    private static readonly List<VoxelState> _grassNeighbours = new List<VoxelState>(4);

    public static bool Active(VoxelState voxel) {

        switch (voxel.id) {

            case 3: // Grass
                if ((voxel.neighbours[0] != null && voxel.neighbours[0].id == 5) ||
                    (voxel.neighbours[1] != null && voxel.neighbours[1].id == 5) ||
                    (voxel.neighbours[4] != null && voxel.neighbours[4].id == 5) ||
                    (voxel.neighbours[5] != null && voxel.neighbours[5].id == 5)) {
                    return true;
                }

                break;
        }

        return false;
    }

    public static void Behave(VoxelState voxel) {

        switch (voxel.id) {

            case 3: // Grass
                if (voxel.neighbours[2] != null && voxel.neighbours[2].id != 0) {

                    voxel.chunkData.chunk.RemoveActiveVoxel(voxel);
                    voxel.chunkData.ModifyVoxel(voxel.position, 5, 0);
                    return;
                }

                _grassNeighbours.Clear();
                if (voxel.neighbours[0] != null && voxel.neighbours[0].id == 5) _grassNeighbours.Add(voxel.neighbours[0]);
                if (voxel.neighbours[1] != null && voxel.neighbours[1].id == 5) _grassNeighbours.Add(voxel.neighbours[1]);
                if (voxel.neighbours[4] != null && voxel.neighbours[4].id == 5) _grassNeighbours.Add(voxel.neighbours[4]);
                if (voxel.neighbours[5] != null && voxel.neighbours[5].id == 5) _grassNeighbours.Add(voxel.neighbours[5]);

                if (_grassNeighbours.Count == 0) return;

                int index = Random.Range(0, _grassNeighbours.Count);
                _grassNeighbours[index].chunkData.ModifyVoxel(_grassNeighbours[index].position, 3, 0);

                break;
        }
    }
}