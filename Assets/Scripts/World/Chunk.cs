using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Chunk {

    public ChunkCoord coord;

    GameObject chunkObject;
    MeshRenderer meshRenderer;
    MeshFilter meshFilter;

    int vertexIndex = 0;
    List<Vector3> vertices = new List<Vector3>(2048);
    List<int> triangles = new List<int>(4096);
    List<int> transparentTriangles = new List<int>(512);
    List<int> waterTriangles = new List<int>(512);
    Material[] materials = new Material[3];
    List<Vector2> uvs = new List<Vector2>(2048);
    List<Color> colors = new List<Color>(2048);
    List<Vector3> normals = new List<Vector3>(2048);

    public Vector3 position;

    private bool _isActive;

    ChunkData chunkData;

    HashSet<VoxelState> activeVoxelSet = new HashSet<VoxelState>();
    List<VoxelState> activeVoxels = new List<VoxelState>();

    // Whether this chunk has any non-air voxels.
    // Set during UpdateChunk, used by frustum culling to skip empty chunks.
    public bool isEmpty = true;

    public Chunk(ChunkCoord _coord) {

        coord = _coord;

        chunkObject = new GameObject();
        meshFilter = chunkObject.AddComponent<MeshFilter>();
        meshRenderer = chunkObject.AddComponent<MeshRenderer>();

        // Give the mesh a persistent object we can Clear() and reuse.
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

        // Register active voxels using flat index.
        int size = VoxelData.ChunkSize;
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

        ClearMeshData();

        int size = VoxelData.ChunkSize;

        // Quick all-air check using the flat array — one loop, one comparison.
        // If the chunk is entirely air (sky chunk) we skip all mesh work.
        isEmpty = true;
        for (int i = 0; i < chunkData.map.Length; i++) {
            if (chunkData.map[i].id != 0) { isEmpty = false; break; }
        }

        if (!isEmpty) {

            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    for (int z = 0; z < size; z++) {

                        // Flat index — one bounds check, no per-dimension multiplications.
                        int idx = ChunkData.FlatIdx(x, y, z);

                        if (World.Instance.blocktypes[chunkData.map[idx].id].isSolid)
                            UpdateMeshData(x, y, z, idx);
                    }
                }
            }
        }

        World.Instance.chunksToDraw.Enqueue(this);

    }

    public void AddActiveVoxel(VoxelState voxel) {
        if (activeVoxelSet.Add(voxel)) activeVoxels.Add(voxel);
    }

    public void RemoveActiveVoxel(VoxelState voxel) {
        if (activeVoxelSet.Remove(voxel)) activeVoxels.Remove(voxel);
    }

    void ClearMeshData() {
        vertexIndex = 0;
        vertices.Clear();
        triangles.Clear();
        transparentTriangles.Clear();
        waterTriangles.Clear();
        uvs.Clear();
        colors.Clear();
        normals.Clear();
    }

    public bool isActive {
        get { return _isActive; }
        set {
            _isActive = value;
            if (chunkObject != null) chunkObject.SetActive(value);
        }
    }

    /// <summary>
    /// Enables or disables the MeshRenderer for frustum culling.
    /// Only touches the renderer, not the whole GameObject — isActive controls that.
    /// </summary>
    public void SetRendererEnabled(bool enabled) {
        if (meshRenderer != null)
            meshRenderer.enabled = enabled && !isEmpty;
    }

    public void EditVoxel(Vector3 pos, byte newID) {

        int xCheck = Mathf.FloorToInt(pos.x) - Mathf.FloorToInt(position.x);
        int yCheck = Mathf.FloorToInt(pos.y) - Mathf.FloorToInt(position.y);
        int zCheck = Mathf.FloorToInt(pos.z) - Mathf.FloorToInt(position.z);

        chunkData.ModifyVoxel(new Vector3Int(xCheck, yCheck, zCheck),
                              newID, World.Instance._player.orientation);

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

    // -------------------------------------------------------
    // Mesh generation — uses flat index throughout
    // -------------------------------------------------------

    void UpdateMeshData(int x, int y, int z, int idx) {

        VoxelState voxel = chunkData.map[idx];

        float rot = 0f;
        switch (voxel.orientation) {
            case 0: rot = 180f; break;
            case 5: rot = 270f; break;
            case 1: rot = 0f; break;
            default: rot = 90f; break;
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

            // Water-on-water check uses flat index.
            bool waterOnWater = voxel.properties.isWater &&
                                y + 1 < VoxelData.ChunkSize &&
                                chunkData.map[ChunkData.FlatIdx(x, y + 1, z)].properties.isWater;

            if (neighbour != null && neighbour.properties.renderNeighborFaces && !waterOnWater) {

                float lightLevel = neighbour.lightAsFloat;
                int faceVertCount = 0;
                var rotAngles = new Vector3(0, rot, 0);
                var pos3 = new Vector3(x, y, z);

                for (int i = 0; i < voxel.properties.meshData.faces[p].vertData.Length; i++) {
                    VertData vertData = voxel.properties.meshData.faces[p].GetVertData(i);
                    vertices.Add(pos3 + vertData.GetRotatedPosition(rotAngles));
                    normals.Add(VoxelData.faceChecks[p]);
                    colors.Add(new Color(0, 0, 0, lightLevel));

                    if (voxel.properties.isWater)
                        uvs.Add(voxel.properties.meshData.faces[p].vertData[i].uv);
                    else
                        AddTexture(voxel.properties.GetTextureID(p), vertData.uv);

                    faceVertCount++;
                }

                if (!voxel.properties.renderNeighborFaces) {
                    for (int i = 0; i < voxel.properties.meshData.faces[p].triangles.Length; i++)
                        triangles.Add(vertexIndex + voxel.properties.meshData.faces[p].triangles[i]);
                } else {
                    if (voxel.properties.isWater) {
                        for (int i = 0; i < voxel.properties.meshData.faces[p].triangles.Length; i++)
                            waterTriangles.Add(vertexIndex + voxel.properties.meshData.faces[p].triangles[i]);
                    } else {
                        for (int i = 0; i < voxel.properties.meshData.faces[p].triangles.Length; i++)
                            transparentTriangles.Add(vertexIndex + voxel.properties.meshData.faces[p].triangles[i]);
                    }
                }

                vertexIndex += faceVertCount;
            }
        }
    }

    public void CreateMesh() {

        // Reuse the existing Mesh object instead of creating a new one each time.
        Mesh mesh = meshFilter.mesh;
        mesh.Clear();

        // SetVertices(List<T>) — available since Unity 2019.3.
        // Avoids the .ToArray() copy that allocates a new managed array on every upload.
        // With 192+ chunks loading at startup, that's hundreds of extra GC allocations.
        mesh.SetVertices(vertices);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(triangles, 0);
        mesh.SetTriangles(transparentTriangles, 1);
        mesh.SetTriangles(waterTriangles, 2);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetNormals(normals);

        // Disable renderer if chunk is empty — no draw call submitted.
        if (meshRenderer != null)
            meshRenderer.enabled = !isEmpty;

    }

    void AddTexture(int textureID, Vector2 uv) {

        float ty = textureID / VoxelData.TextureAtlasSizeInBlocks;
        float tx = textureID - (ty * VoxelData.TextureAtlasSizeInBlocks);

        tx *= VoxelData.NormalizedBlockTextureSize;
        ty *= VoxelData.NormalizedBlockTextureSize;
        ty = 1f - ty - VoxelData.NormalizedBlockTextureSize;

        tx += VoxelData.NormalizedBlockTextureSize * uv.x;
        ty += VoxelData.NormalizedBlockTextureSize * uv.y;

        uvs.Add(new Vector2(tx, ty));

    }

}

// -------------------------------------------------------
// ChunkCoord — 3D (X, Y, Z)
// -------------------------------------------------------
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