using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Threading;
using System.IO;
using UnityEngine.InputSystem;

public class World : MonoBehaviour {

    public Settings settings;

    [Header("World Generation Values")]
    public BiomeAttributes[] biomes;

    [Range(0f, 1f)]
    public float globalLightLevel;
    public Color day;
    public Color night;

    public Transform player;
    public Player _player;
    public Vector3 spawnPosition;

    public Material material;
    public Material transparentMaterial;
    public Material waterMaterial;

    public BlockType[] blocktypes;

    // 3D chunk array: [x, y, z] in chunk coordinates.
    // y index is offset by abs(MinChunkY) so index 0 = MinChunkY.
    Chunk[,,] chunks = new Chunk[
        VoxelData.WorldSizeInChunks,
        VoxelData.WorldHeightInChunks,
        VoxelData.WorldSizeInChunks];

    List<ChunkCoord> activeChunks = new List<ChunkCoord>();
    public ChunkCoord playerChunkCoord;
    ChunkCoord playerLastChunkCoord;

    private HashSet<Chunk> chunksToUpdateSet = new HashSet<Chunk>();
    private List<Chunk> chunksToUpdate = new List<Chunk>();

    public Queue<Chunk> chunksToDraw = new Queue<Chunk>();

    private bool applyingModifications = false;

    private readonly object _modificationsLock = new object();
    Queue<Queue<VoxelMod>> modifications = new Queue<Queue<VoxelMod>>();

    private bool _inUI = false;

    public Clouds clouds;
    public GameObject debugScreen;
    public GameObject creativeInventoryWindow;
    public GameObject cursorSlot;

    Thread ChunkUpdateThread;
    public object ChunkUpdateThreadLock = new object();
    public object ChunkListThreadLock = new object();

    private CancellationTokenSource _threadCancelSource;

    private static World _instance;
    public static World Instance { get { return _instance; } }

    public WorldData worldData;
    public string appPath;

    private InputSystem debugControls;

    private List<ChunkCoord> _previouslyActiveChunks = new List<ChunkCoord>();
    private ChunkCoord[] _tickSnapshot = new ChunkCoord[0];

    // -------------------------------------------------------
    // Converts chunk coord Y to array index.
    // -------------------------------------------------------
    private static int ChunkYToIndex(int chunkY) {
        return chunkY - VoxelData.MinChunkY;
    }

    private void Awake() {

        if (_instance != null && _instance != this)
            Destroy(this.gameObject);
        else
            _instance = this;

        appPath = Application.persistentDataPath;
        _player = player.GetComponent<Player>();

        debugControls = new InputSystem();
        debugControls.Debug.ToggleDebugScreen.performed += _ => debugScreen.SetActive(!debugScreen.activeSelf);
        debugControls.Debug.SaveWorld.performed += _ => SaveSystem.SaveWorld(worldData);
        debugControls.Debug.Enable();
    }

    private void Start() {

        Debug.Log("Generating new world using seed " + VoxelData.seed);

        worldData = SaveSystem.LoadWorld("Testing");

        string settingsPath = Application.dataPath + "/settings.cfg";
        if (File.Exists(settingsPath))
            settings = JsonUtility.FromJson<Settings>(File.ReadAllText(settingsPath));
        else {

            settings = new Settings();
            File.WriteAllText(settingsPath, JsonUtility.ToJson(settings));
        }

        Debug.Log($"Settings: H={settings.viewDistance} V={settings.verticalViewDistance} threading={settings.enableThreading}");

        Random.InitState(VoxelData.seed);

        Shader.SetGlobalFloat("minGlobalLightLevel", VoxelData.minLightLevel);
        Shader.SetGlobalFloat("maxGlobalLightLevel", VoxelData.maxLightLevel);

        LoadWorld();

        SetGlobalLightValue();

        // Spawn player above sea level.
        spawnPosition = new Vector3(
            VoxelData.WorldCentre,
            VoxelData.SeaLevel + 10f,
            VoxelData.WorldCentre);

        player.position = spawnPosition;
        CheckViewDistance();
        playerLastChunkCoord = GetChunkCoordFromVector3(player.position);

        if (settings.enableThreading) {

            _threadCancelSource = new CancellationTokenSource();
            ChunkUpdateThread = new Thread(() => ThreadedUpdate(_threadCancelSource.Token));
            ChunkUpdateThread.IsBackground = true;
            ChunkUpdateThread.Start();
        }

        StartCoroutine(Tick());
    }

    public void SetGlobalLightValue() {

        Shader.SetGlobalFloat("GlobalLightLevel", globalLightLevel);
        Color sky = Color.Lerp(night, day, globalLightLevel);
        sky.a = 1f;
        Camera.main.backgroundColor = sky;
    }

    IEnumerator Tick() {

        while (true) {

            yield return new WaitForSeconds(VoxelData.tickLength);

            int count = activeChunks.Count;

            if (_tickSnapshot.Length != count)
                _tickSnapshot = new ChunkCoord[count];

            activeChunks.CopyTo(_tickSnapshot);

            for (int i = 0; i < count; i++) {
                ChunkCoord c = _tickSnapshot[i];
                int yi = ChunkYToIndex(c.y);
                if (chunks[c.x, yi, c.z] != null)
                    chunks[c.x, yi, c.z].TickUpdate();
            }
        }
    }

    private void Update() {

        playerChunkCoord = GetChunkCoordFromVector3(player.position);

        if (!playerChunkCoord.Equals(playerLastChunkCoord))
            CheckViewDistance();

        if (chunksToDraw.Count > 0)
            chunksToDraw.Dequeue().CreateMesh();

        if (!settings.enableThreading) {

            if (!applyingModifications)
                ApplyModifications();

            if (chunksToUpdate.Count > 0)
                UpdateChunks();
        }
    }

    void LoadWorld() {

        int hd = settings.loadDistance;
        int vd = settings.verticalViewDistance;
        int cx = VoxelData.WorldSizeInChunks / 2;
        int cy = Mathf.FloorToInt((float)VoxelData.SeaLevel / VoxelData.ChunkSize);

        for (int x = cx - hd; x < cx + hd; x++) {

            for (int z = cx - hd; z < cx + hd; z++) {

                for (int y = cy - vd; y < cy + vd; y++) {

                    if (!IsChunkInWorld(x, y, z)) continue;
                    worldData.LoadChunk(new Vector3Int(
                        x * VoxelData.ChunkSize,
                        y * VoxelData.ChunkSize,
                        z * VoxelData.ChunkSize));
                }
            }
        }
    }

    public void AddChunkToUpdate(Chunk chunk) { AddChunkToUpdate(chunk, false); }

    public void AddChunkToUpdate(Chunk chunk, bool insert) {

        lock (ChunkUpdateThreadLock) {

            if (chunksToUpdateSet.Add(chunk)) {

                if (insert) chunksToUpdate.Insert(0, chunk);
                else chunksToUpdate.Add(chunk);
            }
        }
    }

    void UpdateChunks() {

        lock (ChunkUpdateThreadLock) {

            if (chunksToUpdate.Count == 0) return;

            Chunk c = chunksToUpdate[0];
            chunksToUpdate.RemoveAt(0);
            chunksToUpdateSet.Remove(c);
            c.UpdateChunk();

            if (!activeChunks.Contains(c.coord))
                activeChunks.Add(c.coord);
        }
    }

    void ThreadedUpdate(CancellationToken token) {

        while (!token.IsCancellationRequested) {

            bool shouldApply;
            lock (_modificationsLock) {

                shouldApply = !applyingModifications && modifications.Count > 0;
            }

            if (shouldApply) ApplyModifications();
            if (chunksToUpdate.Count > 0) UpdateChunks();
            else Thread.Sleep(1);
        }
    }

    private void OnDisable() {

        if (settings.enableThreading && _threadCancelSource != null) {

            _threadCancelSource.Cancel();
            ChunkUpdateThread?.Join(500);
            _threadCancelSource.Dispose();
        }

        debugControls?.Debug.Disable();
    }

    void ApplyModifications() {

        lock (_modificationsLock) {

            if (applyingModifications) return;
            applyingModifications = true;

            while (modifications.Count > 0) {
                Queue<VoxelMod> queue = modifications.Dequeue();
                while (queue.Count > 0) {
                    VoxelMod v = queue.Dequeue();
                    worldData.SetVoxel(v.position, v.id, 1);
                }
            }

            applyingModifications = false;
        }
    }

    public void EnqueueModification(Queue<VoxelMod> queue) {

        lock (_modificationsLock) {
            modifications.Enqueue(queue);
        }
    }

    ChunkCoord GetChunkCoordFromVector3(Vector3 pos) {

        return new ChunkCoord(
            Mathf.FloorToInt(pos.x / VoxelData.ChunkSize),
            Mathf.FloorToInt(pos.y / VoxelData.ChunkSize),
            Mathf.FloorToInt(pos.z / VoxelData.ChunkSize));
    }

    public Chunk GetChunkFromVector3(Vector3 pos) {

        int cx = Mathf.FloorToInt(pos.x / VoxelData.ChunkSize);
        int cy = Mathf.FloorToInt(pos.y / VoxelData.ChunkSize);
        int cz = Mathf.FloorToInt(pos.z / VoxelData.ChunkSize);

        if (!IsChunkInWorld(cx, cy, cz)) return null;

        return chunks[cx, ChunkYToIndex(cy), cz];
    }

    void CheckViewDistance() {

        clouds.UpdateClouds();

        ChunkCoord coord = GetChunkCoordFromVector3(player.position);
        playerLastChunkCoord = playerChunkCoord;

        _previouslyActiveChunks.Clear();
        _previouslyActiveChunks.AddRange(activeChunks);
        activeChunks.Clear();

        int hd = settings.viewDistance;
        int vd = settings.verticalViewDistance;

        for (int x = coord.x - hd; x < coord.x + hd; x++) {
            for (int z = coord.z - hd; z < coord.z + hd; z++) {
                for (int y = coord.y - vd; y < coord.y + vd; y++) {

                    if (!IsChunkInWorld(x, y, z)) continue;

                    ChunkCoord cc = new ChunkCoord(x, y, z);
                    int yi = ChunkYToIndex(y);

                    if (chunks[x, yi, z] == null)
                        chunks[x, yi, z] = new Chunk(cc);

                    chunks[x, yi, z].isActive = true;
                    activeChunks.Add(cc);

                    for (int i = 0; i < _previouslyActiveChunks.Count; i++) {
                        if (_previouslyActiveChunks[i].Equals(cc))
                            _previouslyActiveChunks.RemoveAt(i);
                    }
                }
            }
        }

        foreach (ChunkCoord c in _previouslyActiveChunks)
            chunks[c.x, ChunkYToIndex(c.y), c.z].isActive = false;
    }

    public bool CheckForVoxel(Vector3 pos) {

        VoxelState voxel = worldData.GetVoxel(pos);
        if (voxel == null) return false;
        return blocktypes[voxel.id].isSolid;

    }

    public VoxelState GetVoxelState(Vector3 pos) {
        return worldData.GetVoxel(pos);
    }

    public bool inUI {

        get { return _inUI; }
        set {
            _inUI = value;
            if (_inUI) {

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                creativeInventoryWindow.SetActive(true);
                cursorSlot.SetActive(true);
            } else {

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                creativeInventoryWindow.SetActive(false);
                cursorSlot.SetActive(false);
            }
        }
    }

    // -------------------------------------------------------
    // Terrain generation — multi-biome, multi-layer noise.
    // Uses absolute Y coordinates (not chunk-relative).
    // -------------------------------------------------------
    public byte GetVoxel(Vector3Int pos) {

        int yPos = pos.y;

        /* IMMUTABLE PASS */

        if (!IsVoxelInWorld(pos)) return 0;

        // Absolute bedrock floor.
        if (yPos == VoxelData.WorldBottomInVoxels) return 1;

        /* BIOME SELECTION PASS */

        Vector2 noisePos = new Vector2(pos.x, pos.z);

        float sumOfHeights = 0f;
        int count = 0;
        float strongestWeight = 0f;
        int strongestBiome = 0;

        for (int i = 0; i < biomes.Length; i++) {

            float weight = Noise.Get2DPerlin(noisePos, biomes[i].offset, biomes[i].scale);

            if (weight > strongestWeight) {

                strongestWeight = weight;
                strongestBiome = i;
            }

            float h = biomes[i].terrainHeight *
                      Noise.Get2DPerlin(noisePos, 0, biomes[i].terrainScale) * weight;

            if (h > 0) { sumOfHeights += h; count++; }

        }

        BiomeAttributes biome = biomes[strongestBiome];
        if (count > 0) sumOfHeights /= count;

        // Terrain height is now relative to sea level.
        int terrainHeight = VoxelData.SeaLevel + Mathf.FloorToInt(sumOfHeights);

        /* BASIC TERRAIN PASS */

        byte voxelValue;

        if (yPos == terrainHeight) voxelValue = biome.surfaceBlock;
        else if (yPos < terrainHeight && yPos > terrainHeight - 4) voxelValue = biome.subSurfaceBlock;
        else if (yPos > terrainHeight) {

            // Water fills below sea level.
            return yPos < VoxelData.SeaLevel ? (byte)14 : (byte)0;
        } else voxelValue = 2; // Stone

        /* ORE / LODE PASS */

        if (voxelValue == 2) {

            foreach (Lode lode in biome.lodes) {
                if (yPos > lode.minHeight && yPos < lode.maxHeight)
                    if (Noise.Get3DPerlin(pos, lode.noiseOffset, lode.scale, lode.threshold))
                        voxelValue = lode.blockID;
            }
        }

        /* FLORA PASS */

        if (yPos == terrainHeight && biome.placeMajorFlora) {

            if (Noise.Get2DPerlin(noisePos, 0, biome.majorFloraZoneScale) > biome.majorFloraZoneThreshold &&
                Noise.Get2DPerlin(noisePos, 0, biome.majorFloraPlacementScale) > biome.majorFloraPlacementThreshold) {

                EnqueueModification(Structure.GenerateMajorFlora(
                    biome.majorFloraIndex, pos, biome.minHeight, biome.maxHeight));
            }
        }

        return voxelValue;
    }

    // Float overload for backward compatibility with VoxelMod positions.
    public byte GetVoxel(Vector3 pos) {

        return GetVoxel(new Vector3Int(
            Mathf.FloorToInt(pos.x),
            Mathf.FloorToInt(pos.y),
            Mathf.FloorToInt(pos.z)));
    }

    bool IsChunkInWorld(int cx, int cy, int cz) {

        return cx > 0 && cx < VoxelData.WorldSizeInChunks - 1 &&
               cy >= VoxelData.MinChunkY && cy < VoxelData.MaxChunkY &&
               cz > 0 && cz < VoxelData.WorldSizeInChunks - 1;
    }

    bool IsVoxelInWorld(Vector3Int pos) {

        return pos.x >= 0 && pos.x < VoxelData.WorldSizeInVoxels &&
               pos.y >= VoxelData.WorldBottomInVoxels && pos.y < VoxelData.WorldTopInVoxels &&
               pos.z >= 0 && pos.z < VoxelData.WorldSizeInVoxels;
    }
}

// -------------------------------------------------------
// Supporting types
// -------------------------------------------------------

[System.Serializable]
public class BlockType {

    public string blockName;
    public bool isSolid;
    public VoxelMeshData meshData;
    public bool renderNeighborFaces;
    public bool isWater;
    public byte opacity;
    public Sprite icon;
    public bool isActive;

    [Header("Texture Values")]
    public int backFaceTexture;
    public int frontFaceTexture;
    public int topFaceTexture;
    public int bottomFaceTexture;
    public int leftFaceTexture;
    public int rightFaceTexture;

    public int GetTextureID(int faceIndex) {

        switch (faceIndex) {

            case 0: return backFaceTexture;
            case 1: return frontFaceTexture;
            case 2: return topFaceTexture;
            case 3: return bottomFaceTexture;
            case 4: return leftFaceTexture;
            case 5: return rightFaceTexture;
            default: Debug.Log("Error in GetTextureID; invalid face index"); return 0;
        }
    }
}

public class VoxelMod {

    public Vector3 position;
    public byte id;

    public VoxelMod() { position = Vector3.zero; id = 0; }
    public VoxelMod(Vector3 _position, byte _id) { position = _position; id = _id; }
}

[System.Serializable]
public class Settings {

    [Header("Game Data")]
    public string version = "0.0.0.01";

    [Header("Performance")]
    public int loadDistance = 4;
    public int viewDistance = 4;
    public int verticalViewDistance = 3;  // Chunk layers up and down from player.
    public bool enableThreading = true;
    public CloudStyle clouds = CloudStyle.Fast;
    public bool enableAnimatedChunks = false;

    [Header("Controls")]
    [Range(0.1f, 10f)]
    public float mouseSensitivity = 2.0f;
}