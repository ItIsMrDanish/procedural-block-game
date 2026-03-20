using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ChunkData {

    // Global block position of the chunk's origin (bottom-back-left corner).
    // Stored as ints because Vector3Int is not serializable.
    int _x, _y, _z;

    public Vector3Int position {

        get { return new Vector3Int(_x, _y, _z); }
        set { _x = value.x; _y = value.y; _z = value.z; }
    }

    public ChunkData(Vector3Int pos) { position = pos; }
    public ChunkData(int x, int y, int z) { _x = x; _y = y; _z = z; }

    [System.NonSerialized] public Chunk chunk;

    // 16 x 16 x 16 voxel map.
    [HideInInspector]
    public VoxelState[,,] map = new VoxelState[
        VoxelData.ChunkSize,
        VoxelData.ChunkSize,
        VoxelData.ChunkSize];

    public void Populate() {

        for (int y = 0; y < VoxelData.ChunkSize; y++) {

            for (int x = 0; x < VoxelData.ChunkSize; x++) {

                for (int z = 0; z < VoxelData.ChunkSize; z++) {

                    // Global block position of this voxel.
                    Vector3Int globalPos = new Vector3Int(
                        x + _x,
                        y + _y,
                        z + _z);

                    map[x, y, z] = new VoxelState(
                        World.Instance.GetVoxel(globalPos),
                        this,
                        new Vector3Int(x, y, z));

                    // Set neighbours that are already in this chunk.
                    for (int p = 0; p < 6; p++) {

                        Vector3Int nv = new Vector3Int(x, y, z) + VoxelData.faceChecks[p];

                        if (IsVoxelInChunk(nv))
                            map[x, y, z].neighbours[p] = VoxelFromV3Int(nv);
                        else
                            map[x, y, z].neighbours[p] = World.Instance.worldData.GetVoxel(
                                globalPos + VoxelData.faceChecks[p]);
                    }
                }
            }
        }

        Lighting.RecalculateNaturaLight(this);
        World.Instance.worldData.AddToModifiedChunkList(this);
    }

    public void ModifyVoxel(Vector3Int pos, byte _id, int direction) {

        if (map[pos.x, pos.y, pos.z].id == _id) return;

        VoxelState voxel = map[pos.x, pos.y, pos.z];
        byte oldOpacity = voxel.properties.opacity;

        voxel.id = _id;
        voxel.orientation = direction;

        if (voxel.properties.opacity != oldOpacity) {

            // Recast sunlight downward from this voxel's column if needed.
            // We cast from this chunk's top or from the voxel above, whichever applies.
            int startY = pos.y + 1;
            if (startY >= VoxelData.ChunkSize) {

                // The voxel is at the top of this chunk — ask the chunk above.
                Lighting.CastNaturalLightFromAbove(this, pos.x, pos.z);
            } else if (map[pos.x, startY, pos.z].light == 15) {

                Lighting.CastNaturalLight(this, pos.x, pos.z, startY);
            }
        }

        if (voxel.properties.isActive && BlockBehaviour.Active(voxel))
            voxel.chunkData.chunk.AddActiveVoxel(voxel);

        for (int i = 0; i < 6; i++) {

            if (voxel.neighbours[i] != null)
                if (voxel.neighbours[i].properties.isActive && BlockBehaviour.Active(voxel.neighbours[i]))
                    voxel.neighbours[i].chunkData.chunk.AddActiveVoxel(voxel.neighbours[i]);
        }

        World.Instance.worldData.AddToModifiedChunkList(this);

        if (chunk != null)
            World.Instance.AddChunkToUpdate(chunk);
    }

    public bool IsVoxelInChunk(int x, int y, int z) {

        return x >= 0 && x < VoxelData.ChunkSize &&
               y >= 0 && y < VoxelData.ChunkSize &&
               z >= 0 && z < VoxelData.ChunkSize;
    }

    public bool IsVoxelInChunk(Vector3Int pos) {

        return IsVoxelInChunk(pos.x, pos.y, pos.z);
    }

    public VoxelState VoxelFromV3Int(Vector3Int pos) {

        return map[pos.x, pos.y, pos.z];
    }
}