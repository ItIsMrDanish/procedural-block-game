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

    Chunk[,] chunks = new Chunk[VoxelData.WorldSizeInChunks, VoxelData.WorldSizeInChunks];

    List<ChunkCoord> activeChunks = new List<ChunkCoord>();
    public ChunkCoord playerChunkCoord;
    ChunkCoord playerLastChunkCoord;

    // HashSet for O(1) duplicate checks, List for ordered processing.
    private HashSet<Chunk> chunksToUpdateSet = new HashSet<Chunk>();
    private List<Chunk> chunksToUpdate = new List<Chunk>();

    public Queue<Chunk> chunksToDraw = new Queue<Chunk>();

    bool applyingModifications = false;

    // Lock protecting the modifications queue — previously unprotected,
    // causing a race between main thread (tree gen enqueue) and background thread (dequeue).
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

    // CancellationToken replaces Thread.Abort() which is unsafe in .NET 6+
    // and can leave locks permanently held, causing deadlocks.
    private CancellationTokenSource _threadCancelSource;

    private static World _instance;
    public static World Instance { get { return _instance; } }

    public WorldData worldData;
    public string appPath;

    private InputSystem debugControls;

    // Reusable collections — avoids per-frame/per-move allocations.
    private List<ChunkCoord> _previouslyActiveChunks = new List<ChunkCoord>();
    private ChunkCoord[] _tickSnapshot = new ChunkCoord[0];

    // Pre-allocated Vector2 for noise calls in GetVoxel — called millions of times on load.
    private Vector2 _noisePos = Vector2.zero;

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

        // Load settings FIRST — before LoadWorld() — so loadDistance/viewDistance
        // come from cfg, not from the scene's serialized values.
        string settingsPath = Application.dataPath + "/settings.cfg";
        if (File.Exists(settingsPath)) {
            settings = JsonUtility.FromJson<Settings>(File.ReadAllText(settingsPath));
        } else {
            settings = new Settings();
            File.WriteAllText(settingsPath, JsonUtility.ToJson(settings));
        }

        Debug.Log("Settings: loadDist=" + settings.loadDistance +
                  " viewDist=" + settings.viewDistance +
                  " threading=" + settings.enableThreading);

        Random.InitState(VoxelData.seed);

        Shader.SetGlobalFloat("minGlobalLightLevel", VoxelData.minLightLevel);
        Shader.SetGlobalFloat("maxGlobalLightLevel", VoxelData.maxLightLevel);

        LoadWorld();

        SetGlobalLightValue();
        spawnPosition = new Vector3(VoxelData.WorldCentre, VoxelData.ChunkHeight - 50f, VoxelData.WorldCentre);
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
        Color skyColor = Color.Lerp(night, day, globalLightLevel);
        skyColor.a = 1f;
        Camera.main.backgroundColor = skyColor;

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
                if (chunks[c.x, c.z] != null)
                    chunks[c.x, c.z].TickUpdate();
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

        int half = VoxelData.WorldSizeInChunks / 2;
        for (int x = half - settings.loadDistance; x < half + settings.loadDistance; x++) {
            for (int z = half - settings.loadDistance; z < half + settings.loadDistance; z++) {
                worldData.LoadChunk(new Vector2Int(x, z));
            }
        }

    }

    public void AddChunkToUpdate(Chunk chunk) {

        AddChunkToUpdate(chunk, false);

    }

    public void AddChunkToUpdate(Chunk chunk, bool insert) {

        lock (ChunkUpdateThreadLock) {

            if (chunksToUpdateSet.Add(chunk)) {
                if (insert)
                    chunksToUpdate.Insert(0, chunk);
                else
                    chunksToUpdate.Add(chunk);
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

            if (!applyingModifications)
                ApplyModifications();

            if (chunksToUpdate.Count > 0)
                UpdateChunks();
            else
                Thread.Sleep(1);

        }

    }

    private void OnDisable() {

        // Gracefully cancel the thread instead of aborting it.
        // Thread.Abort() in .NET 6+ can leave locks held permanently.
        if (settings.enableThreading && _threadCancelSource != null) {
            _threadCancelSource.Cancel();
            ChunkUpdateThread?.Join(500); // Wait up to 500ms for clean exit.
            _threadCancelSource.Dispose();
        }

        debugControls?.Debug.Disable();

    }

    void ApplyModifications() {

        applyingModifications = true;

        // Lock the modifications queue — it's enqueued from main thread (GetVoxel tree pass)
        // and dequeued from the background thread. Previously had no lock = race condition.
        lock (_modificationsLock) {

            while (modifications.Count > 0) {

                Queue<VoxelMod> queue = modifications.Dequeue();

                while (queue.Count > 0) {
                    VoxelMod v = queue.Dequeue();
                    worldData.SetVoxel(v.position, v.id, 1);
                }
            }

        }

        applyingModifications = false;

    }

    // Called from GetVoxel (main thread) — needs the same lock as ApplyModifications.
    public void EnqueueModification(Queue<VoxelMod> queue) {

        lock (_modificationsLock) {
            modifications.Enqueue(queue);
        }

    }

    ChunkCoord GetChunkCoordFromVector3(Vector3 pos) {

        int x = Mathf.FloorToInt(pos.x / VoxelData.ChunkWidth);
        int z = Mathf.FloorToInt(pos.z / VoxelData.ChunkWidth);
        return new ChunkCoord(x, z);

    }

    public Chunk GetChunkFromVector3(Vector3 pos) {

        int x = Mathf.FloorToInt(pos.x / VoxelData.ChunkWidth);
        int z = Mathf.FloorToInt(pos.z / VoxelData.ChunkWidth);
        return chunks[x, z];

    }

    void CheckViewDistance() {

        clouds.UpdateClouds();

        ChunkCoord coord = GetChunkCoordFromVector3(player.position);
        playerLastChunkCoord = playerChunkCoord;

        _previouslyActiveChunks.Clear();
        _previouslyActiveChunks.AddRange(activeChunks);
        activeChunks.Clear();

        for (int x = coord.x - settings.viewDistance; x < coord.x + settings.viewDistance; x++) {
            for (int z = coord.z - settings.viewDistance; z < coord.z + settings.viewDistance; z++) {

                ChunkCoord thisChunkCoord = new ChunkCoord(x, z);

                if (IsChunkInWorld(thisChunkCoord)) {

                    if (chunks[x, z] == null)
                        chunks[x, z] = new Chunk(thisChunkCoord);

                    chunks[x, z].isActive = true;
                    activeChunks.Add(thisChunkCoord);
                }

                for (int i = 0; i < _previouslyActiveChunks.Count; i++) {
                    if (_previouslyActiveChunks[i].Equals(thisChunkCoord))
                        _previouslyActiveChunks.RemoveAt(i);
                }

            }
        }

        foreach (ChunkCoord c in _previouslyActiveChunks)
            chunks[c.x, c.z].isActive = false;

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

    public byte GetVoxel(Vector3 pos) {

        int yPos = Mathf.FloorToInt(pos.y);

        /* IMMUTABLE PASS */

        if (!IsVoxelInWorld(pos)) return 0;
        if (yPos == 0) return 1;

        /* BIOME SELECTION PASS */

        int solidGroundHeight = 42;
        float sumOfHeights = 0f;
        int count = 0;
        float strongestWeight = 0f;
        int strongestBiomeIndex = 0;

        for (int i = 0; i < biomes.Length; i++) {

            // Reuse _noisePos instead of new Vector2 — called for every voxel on load.
            _noisePos.x = pos.x; _noisePos.y = pos.z;
            float weight = Noise.Get2DPerlin(_noisePos, biomes[i].offset, biomes[i].scale);

            if (weight > strongestWeight) {
                strongestWeight = weight;
                strongestBiomeIndex = i;
            }

            float height = biomes[i].terrainHeight *
                           Noise.Get2DPerlin(_noisePos, 0, biomes[i].terrainScale) * weight;

            if (height > 0) {
                sumOfHeights += height;
                count++;
            }

        }

        BiomeAttributes biome = biomes[strongestBiomeIndex];
        sumOfHeights /= count;
        int terrainHeight = Mathf.FloorToInt(sumOfHeights + solidGroundHeight);

        /* BASIC TERRAIN PASS */

        byte voxelValue = 0;

        if (yPos == terrainHeight)
            voxelValue = biome.surfaceBlock;
        else if (yPos < terrainHeight && yPos > terrainHeight - 4)
            voxelValue = biome.subSurfaceBlock;
        else if (yPos > terrainHeight) {
            return yPos < 51 ? (byte)14 : (byte)0;
        } else
            voxelValue = 2;

        /* SECOND PASS */

        if (voxelValue == 2) {

            foreach (Lode lode in biome.lodes) {
                if (yPos > lode.minHeight && yPos < lode.maxHeight)
                    if (Noise.Get3DPerlin(pos, lode.noiseOffset, lode.scale, lode.threshold))
                        voxelValue = lode.blockID;
            }

        }

        /* TREE PASS */

        if (yPos == terrainHeight && biome.placeMajorFlora) {

            _noisePos.x = pos.x; _noisePos.y = pos.z;

            if (Noise.Get2DPerlin(_noisePos, 0, biome.majorFloraZoneScale) > biome.majorFloraZoneThreshold) {
                if (Noise.Get2DPerlin(_noisePos, 0, biome.majorFloraPlacementScale) > biome.majorFloraPlacementThreshold) {
                    // Use thread-safe enqueue instead of direct modifications.Enqueue.
                    EnqueueModification(Structure.GenerateMajorFlora(biome.majorFloraIndex, pos, biome.minHeight, biome.maxHeight));
                }
            }

        }

        return voxelValue;

    }

    bool IsChunkInWorld(ChunkCoord coord) {

        return coord.x > 0 && coord.x < VoxelData.WorldSizeInChunks - 1 &&
               coord.z > 0 && coord.z < VoxelData.WorldSizeInChunks - 1;

    }

    bool IsVoxelInWorld(Vector3 pos) {

        return pos.x >= 0 && pos.x < VoxelData.WorldSizeInVoxels &&
               pos.y >= 0 && pos.y < VoxelData.ChunkHeight &&
               pos.z >= 0 && pos.z < VoxelData.WorldSizeInVoxels;

    }

}

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

    // Back, Front, Top, Bottom, Left, Right
    public int GetTextureID(int faceIndex) {

        switch (faceIndex) {
            case 0: return backFaceTexture;
            case 1: return frontFaceTexture;
            case 2: return topFaceTexture;
            case 3: return bottomFaceTexture;
            case 4: return leftFaceTexture;
            case 5: return rightFaceTexture;
            default:
                Debug.Log("Error in GetTextureID; invalid face index");
                return 0;
        }

    }

}

public class VoxelMod {

    public Vector3 position;
    public byte id;

    public VoxelMod() { position = new Vector3(); id = 0; }

    public VoxelMod(Vector3 _position, byte _id) { position = _position; id = _id; }

}

[System.Serializable]
public class Settings {

    [Header("Game Data")]
    public string version = "0.0.0.01";

    [Header("Performance")]
    public int loadDistance = 4;
    public int viewDistance = 4;
    public bool enableThreading = true;
    public CloudStyle clouds = CloudStyle.Fast;
    public bool enableAnimatedChunks = false;

    [Header("Controls")]
    [Range(0.1f, 10f)]
    public float mouseSensitivity = 2.0f;

}