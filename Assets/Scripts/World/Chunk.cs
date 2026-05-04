using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Chunk {

    public ChunkCoord coord;

    GameObject chunkObject;
    MeshRenderer meshRenderer;
    MeshFilter meshFilter;

    Material[] materials = new Material[3];

    public Vector3 position;

    private bool _isActive;

    ChunkData chunkData;

    HashSet<VoxelState> activeVoxelSet = new HashSet<VoxelState>();
    List<VoxelState> activeVoxels = new List<VoxelState>();

    // volatile: written by bg thread (UpdateChunk), read by main thread (frustum culling).
    public volatile bool isEmpty = true;

    // DOUBLE-BUFFER FIX — root cause of invisible chunks with threading ON:
    //
    // Previously UpdateChunk() wrote mesh data directly into List<> fields on this
    // Chunk object, then enqueued `this` for CreateMesh(). With threading enabled,
    // the bg thread could call UpdateChunk() again on the same chunk — calling
    // ClearMeshData() and rebuilding — while the main thread was mid-way through
    // CreateMesh() reading those same lists. The main thread would then upload an
    // empty or half-built mesh → renderer disabled → invisible chunk.
    //
    // Fix: UpdateChunk() builds into a completely isolated MeshData object. Once
    // finished it assigns _pendingMesh in one atomic reference swap. CreateMesh()
    // captures that reference once at the top and reads it exclusively — completely
    // immune to any concurrent UpdateChunk() swapping in a new MeshData afterwards.
    // volatile on _pendingMesh ensures the main thread never reads a CPU-cached
    // stale null.

    private volatile MeshData _pendingMesh = null;

    private class MeshData {

        public readonly List<Vector3> vertices        = new List<Vector3>(2048);
        public readonly List<int>     triangles       = new List<int>(4096);
        public readonly List<int>     transparentTris = new List<int>(512);
        public readonly List<int>     waterTris       = new List<int>(512);
        public readonly List<Vector2> uvs             = new List<Vector2>(2048);
        public readonly List<Color>   colors          = new List<Color>(2048);
        public readonly List<Vector3> normals         = new List<Vector3>(2048);
        public bool isEmpty    = true;
        public int  vertexIndex = 0;
    }

    public Chunk(ChunkCoord _coord) {

        coord = _coord;

        chunkObject = new GameObject();
        meshFilter  = chunkObject.AddComponent<MeshFilter>();
        meshRenderer = chunkObject.AddComponent<MeshRenderer>();

        meshFilter.mesh = new Mesh();

        materials[0] = World.Instance.material;
        materials[1] = World.Instance.transparentMaterial;
        materials[2] = World.Instance.waterMaterial;
        meshRenderer.materials = materials;

        chunkObject.transform.SetParent(World.Instance.transform);

        position = new Vector3(
            coord.x * VoxelData.ChunkSize,
            coord.y * VoxelData.ChunkSize,
            coord.z * VoxelData.ChunkSize);

        chunkObject.transform.position = position;
        chunkObject.name = $"Chunk {coord.x},{coord.y},{coord.z}";

        var blockPos = new Vector3Int(
            coord.x * VoxelData.ChunkSize,
            coord.y * VoxelData.ChunkSize,
            coord.z * VoxelData.ChunkSize);

        chunkData = World.Instance.worldData.RequestChunk(blockPos, true);
        chunkData.chunk = this;

        for (int i = 0; i < chunkData.map.Length; i++) {

            if (chunkData.map[i].properties.isActive)
                AddActiveVoxel(chunkData.map[i]);
        }

        World.Instance.AddChunkToUpdate(this);

        if (World.Instance.settings.enableAnimatedChunks)
            chunkObject.AddComponent<ChunkLoadAnimation>();
    }

    public void TickUpdate() {

        for (int i = activeVoxels.Count - 1; i >= 0; i--) {

            if (!BlockBehaviour.Active(activeVoxels[i]))
                RemoveActiveVoxel(activeVoxels[i]);
            else
                BlockBehaviour.Behave(activeVoxels[i]);
        }
    }

    public void UpdateChunk() {

        // Build a fresh MeshData in isolation — no shared state until the final swap.
        MeshData data = new MeshData();

        int size = VoxelData.ChunkSize;

        data.isEmpty = true;
        for (int i = 0; i < chunkData.map.Length; i++) {

            if (chunkData.map[i].id != 0) { data.isEmpty = false; break; }
        }

        if (!data.isEmpty) {

            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    for (int z = 0; z < size; z++) {

                        int idx = ChunkData.FlatIdx(x, y, z);
                        BlockType bt = World.Instance.blocktypes[chunkData.map[idx].id];
                        if (bt.isSolid || bt.isWater)
                            UpdateMeshData(data, x, y, z, idx);
                    }
                }
            }
        }

        // Atomic swap: main thread sees either old complete data or new complete data,
        // never a partially-built dataset.
        isEmpty      = data.isEmpty;
        _pendingMesh = data;

        World.Instance.chunksToDraw.Enqueue(this);
    }

    public void AddActiveVoxel(VoxelState voxel) {

        if (activeVoxelSet.Add(voxel)) activeVoxels.Add(voxel);
    }

    public void RemoveActiveVoxel(VoxelState voxel) {

        if (activeVoxelSet.Remove(voxel)) activeVoxels.Remove(voxel);
    }

    public bool isActive {

        get { return _isActive; }
        set {
            _isActive = value;
            if (chunkObject != null) chunkObject.SetActive(value);
        }
    }

    public void SetRendererEnabled(bool enabled) {

        if (meshRenderer != null)
            meshRenderer.enabled = enabled && !isEmpty;
    }

    public void EditVoxel(Vector3 pos, byte newID) {

        int xCheck = Mathf.FloorToInt(pos.x) - Mathf.FloorToInt(position.x);
        int yCheck = Mathf.FloorToInt(pos.y) - Mathf.FloorToInt(position.y);
        int zCheck = Mathf.FloorToInt(pos.z) - Mathf.FloorToInt(position.z);

        chunkData.ModifyVoxel(
            new Vector3Int(xCheck, yCheck, zCheck),
            newID,
            World.Instance._player.orientation);

        UpdateSurroundingVoxels(xCheck, yCheck, zCheck);
    }

    void UpdateSurroundingVoxels(int x, int y, int z) {

        for (int p = 0; p < 6; p++) {

            var currentVoxel = new Vector3Int(x, y, z) + VoxelData.faceChecks[p];

            if (!chunkData.IsVoxelInChunk(currentVoxel.x, currentVoxel.y, currentVoxel.z)) {

                var worldPos = new Vector3(
                    currentVoxel.x + position.x,
                    currentVoxel.y + position.y,
                    currentVoxel.z + position.z);

                Chunk neighbour = World.Instance.GetChunkFromVector3(worldPos);
                if (neighbour != null)
                    World.Instance.AddChunkToUpdate(neighbour, true);
            }
        }
    }

    public VoxelState GetVoxelFromGlobalVector3(Vector3 pos) {

        int xCheck = Mathf.FloorToInt(pos.x) - Mathf.FloorToInt(position.x);
        int yCheck = Mathf.FloorToInt(pos.y) - Mathf.FloorToInt(position.y);
        int zCheck = Mathf.FloorToInt(pos.z) - Mathf.FloorToInt(position.z);
        return chunkData.map[ChunkData.FlatIdx(xCheck, yCheck, zCheck)];
    }

    void UpdateMeshData(MeshData data, int x, int y, int z, int idx) {

        VoxelState voxel = chunkData.map[idx];

        float rot = 0f;
        switch (voxel.orientation) {
            case 0:  rot = 180f; break;
            case 5:  rot = 270f; break;
            case 1:  rot = 0f;   break;
            default: rot = 90f;  break;
        }

        for (int p = 0; p < 6; p++) {

            int translatedP = p;

            if (voxel.orientation != 1) {

                if (voxel.orientation == 0) {
                    if (p == 0) translatedP = 1;
                    else if (p == 1) translatedP = 0;
                    else if (p == 4) translatedP = 5;
                    else if (p == 5) translatedP = 4;
                } else if (voxel.orientation == 5) {
                    if (p == 0) translatedP = 5;
                    else if (p == 1) translatedP = 4;
                    else if (p == 4) translatedP = 0;
                    else if (p == 5) translatedP = 1;
                } else if (voxel.orientation == 4) {
                    if (p == 0) translatedP = 4;
                    else if (p == 1) translatedP = 5;
                    else if (p == 4) translatedP = 1;
                    else if (p == 5) translatedP = 0;
                }
            }

            VoxelState neighbour = voxel.neighbours[translatedP];

            bool waterOnWater = voxel.properties.isWater &&
                                y + 1 < VoxelData.ChunkSize &&
                                chunkData.map[ChunkData.FlatIdx(x, y + 1, z)].properties.isWater;

            if (neighbour != null && neighbour.properties.renderNeighborFaces && !waterOnWater) {

                float lightLevel  = neighbour.lightAsFloat;
                int   faceVertCount = 0;
                var   rotAngles   = new Vector3(0, rot, 0);
                var   pos3        = new Vector3(x, y, z);

                for (int i = 0; i < voxel.properties.meshData.faces[p].vertData.Length; i++) {

                    VertData vertData = voxel.properties.meshData.faces[p].GetVertData(i);
                    data.vertices.Add(pos3 + vertData.GetRotatedPosition(rotAngles));
                    data.normals.Add(VoxelData.faceChecks[p]);
                    data.colors.Add(new Color(0, 0, 0, lightLevel));

                    if (voxel.properties.isWater)
                        data.uvs.Add(voxel.properties.meshData.faces[p].vertData[i].uv);
                    else
                        AddTexture(data, voxel.properties.GetTextureID(p), vertData.uv);

                    faceVertCount++;
                }

                if (!voxel.properties.renderNeighborFaces) {

                    for (int i = 0; i < voxel.properties.meshData.faces[p].triangles.Length; i++)
                        data.triangles.Add(data.vertexIndex + voxel.properties.meshData.faces[p].triangles[i]);

                } else {

                    if (voxel.properties.isWater) {
                        for (int i = 0; i < voxel.properties.meshData.faces[p].triangles.Length; i++)
                            data.waterTris.Add(data.vertexIndex + voxel.properties.meshData.faces[p].triangles[i]);
                    } else {
                        for (int i = 0; i < voxel.properties.meshData.faces[p].triangles.Length; i++)
                            data.transparentTris.Add(data.vertexIndex + voxel.properties.meshData.faces[p].triangles[i]);
                    }
                }

                data.vertexIndex += faceVertCount;
            }
        }
    }

    public void CreateMesh() {

        // Capture the reference once. If the bg thread swaps _pendingMesh after this
        // line we still hold our reference and read a complete, stable dataset.
        MeshData data = _pendingMesh;
        if (data == null) return;

        Mesh mesh = meshFilter.mesh;
        mesh.Clear();

        mesh.SetVertices(data.vertices);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(data.triangles,       0);
        mesh.SetTriangles(data.transparentTris, 1);
        mesh.SetTriangles(data.waterTris,       2);
        mesh.SetUVs(0, data.uvs);
        mesh.SetColors(data.colors);
        mesh.SetNormals(data.normals);

        if (meshRenderer != null)
            meshRenderer.enabled = !data.isEmpty;
    }

    void AddTexture(MeshData data, int textureID, Vector2 uv) {

        float ty = textureID / VoxelData.TextureAtlasSizeInBlocks;
        float tx = textureID - (ty * VoxelData.TextureAtlasSizeInBlocks);

        tx *= VoxelData.NormalizedBlockTextureSize;
        ty *= VoxelData.NormalizedBlockTextureSize;
        ty = 1f - ty - VoxelData.NormalizedBlockTextureSize;

        tx += VoxelData.NormalizedBlockTextureSize * uv.x;
        ty += VoxelData.NormalizedBlockTextureSize * uv.y;

        data.uvs.Add(new Vector2(tx, ty));
    }
}

// ChunkCoord — 3D (X, Y, Z)

public class ChunkCoord {

    public int x, y, z;

    public ChunkCoord() { x = 0; y = 0; z = 0; }
    public ChunkCoord(int _x, int _y, int _z) { x = _x; y = _y; z = _z; }

    public ChunkCoord(Vector3 pos) {

        x = Mathf.FloorToInt(pos.x / VoxelData.ChunkSize);
        y = Mathf.FloorToInt(pos.y / VoxelData.ChunkSize);
        z = Mathf.FloorToInt(pos.z / VoxelData.ChunkSize);
    }

    public bool Equals(ChunkCoord other) {

        if (other == null) return false;
        return other.x == x && other.y == y && other.z == z;
    }
}