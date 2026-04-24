using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using UnityEngine;
using System.Threading;
using System.IO;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

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

    Chunk[,,] chunks = new Chunk[
        VoxelData.WorldSizeInChunks,
        VoxelData.WorldHeightInChunks,
        VoxelData.WorldSizeInChunks];

    List<ChunkCoord> activeChunks = new List<ChunkCoord>();
    public ChunkCoord playerChunkCoord;
    ChunkCoord playerLastChunkCoord;

    // ConcurrentQueue for chunk updates: background thread dequeues, both threads enqueue.
    // Eliminates the ChunkUpdateThreadLock entirely — no more deadlock risk.
    private ConcurrentQueue<Chunk> chunksToUpdate = new ConcurrentQueue<Chunk>();
    // HashSet to prevent duplicate enqueues — only touched under _chunksToUpdateSetLock.
    private HashSet<Chunk> chunksToUpdateSet = new HashSet<Chunk>();
    private readonly object _chunksToUpdateSetLock = new object();

    // ConcurrentQueue: background thread enqueues, main thread dequeues — no lock needed.
    public ConcurrentQueue<Chunk> chunksToDraw = new ConcurrentQueue<Chunk>();

    private volatile bool applyingModifications = false;

    private readonly object _modificationsLock = new object();
    Queue<Queue<VoxelMod>> modifications = new Queue<Queue<VoxelMod>>();

    private bool _inUI = false;

    public Clouds clouds;
    public GameObject debugScreen;
    public GameObject creativeInventoryWindow;
    public GameObject cursorSlot;
    public GameObject mainCanvas;

    Thread ChunkUpdateThread;
    public object ChunkListThreadLock = new object();

    private CancellationTokenSource _threadCancelSource;

    private static World _instance;
    public static World Instance { get { return _instance; } }

    public WorldData worldData;
    public string appPath;

    private InputSystem debugControls;

    private List<ChunkCoord> _previouslyActiveChunks = new List<ChunkCoord>();
    private ChunkCoord[] _tickSnapshot = new ChunkCoord[0];

    // Time budget for mesh uploads per frame (ms).
    private const double MeshBudgetMs = 8.0;
    private readonly Stopwatch _frameTimer = new Stopwatch();

    // How often to trim the heightmap cache (player chunk moves).
    private int _chunkMovesSinceLastTrim = 0;
    private const int TrimEveryNMoves = 32;

    // Frustum culling — camera frustum planes, updated once per frame.
    private Plane[] _frustumPlanes = new Plane[6];
    private Camera _mainCamera;

    // -------------------------------------------------------
    // Loading progress — read by LoadingScreenUI
    // -------------------------------------------------------

    public static float LoadProgress { get; private set; } = 0f;
    public static bool IsReady { get; private set; } = false;

    // -------------------------------------------------------

    private static int ChunkYToIndex(int chunkY) {
        return chunkY - VoxelData.MinChunkY;
    }

    // -------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------

    private void Awake() {

        if (_instance != null && _instance != this)
            Destroy(this.gameObject);
        else
            _instance = this;

        appPath = Application.persistentDataPath;
        _player = player.GetComponent<Player>();

        debugControls = new InputSystem();
        debugControls.Debug.ToggleDebugScreen.performed += _ =>
            debugScreen.SetActive(!debugScreen.activeSelf);
        debugControls.Debug.SaveWorld.performed += _ =>
            SaveSystem.SaveWorld(worldData);
        debugControls.Debug.Enable();

        LoadProgress = 0f;
        IsReady = false;

        // Hide the player and UI until the world is fully ready
        //player.gameObject.SetActive(false);
        mainCanvas.gameObject.SetActive(false);

    }

    private void Start() {

        _mainCamera = Camera.main;
        StartCoroutine(InitWorld());

    }

    // -------------------------------------------------------
    // Coroutine: initialise world across multiple frames
    // so the loading screen can update its progress bar
    // -------------------------------------------------------

    private IEnumerator InitWorld() {

        Debug.Log($"Loading world '{VoxelData.worldName}' with seed {VoxelData.seed}");

        // Step 1: Basic setup (0–5%)
        LoadProgress = 0f;
        yield return null;

        worldData = SaveSystem.LoadWorld(VoxelData.worldName, VoxelData.seed);

        string settingsPath = Application.dataPath + "/settings.cfg";
        if (File.Exists(settingsPath))
            settings = JsonUtility.FromJson<Settings>(File.ReadAllText(settingsPath));
        else {
            settings = new Settings();
            File.WriteAllText(settingsPath, JsonUtility.ToJson(settings));
        }

        Random.InitState(VoxelData.seed);

        Shader.SetGlobalFloat("minGlobalLightLevel", VoxelData.minLightLevel);
        Shader.SetGlobalFloat("maxGlobalLightLevel", VoxelData.maxLightLevel);

        LoadProgress = 0.05f;
        yield return null;

        // Step 2: Pre-warm heightmap cache (5–25%)
        // This is a single blocking call so we yield before and after
        LoadProgress = 0.1f;
        yield return null;

        HeightmapCache.Clear();
        int startCX = VoxelData.WorldSizeInChunks / 2;
        int startCZ = VoxelData.WorldSizeInChunks / 2;
        HeightmapCache.PreWarm(startCX, startCZ, settings.loadDistance + 1, biomes);

        LoadProgress = 0.25f;
        yield return null;

        // Step 3: Load chunks (25–75%) — spread across frames
        yield return StartCoroutine(LoadWorldAsync());

        // Step 4: Final setup (75–100%)
        SetGlobalLightValue();

        LoadProgress = 0.8f;
        yield return null;

        spawnPosition = new Vector3(
            VoxelData.WorldCentre,
            VoxelData.SeaLevel + 30f,
            VoxelData.WorldCentre);

        player.position = spawnPosition;
        CheckViewDistance();
        playerLastChunkCoord = GetChunkCoordFromVector3(player.position);

        LoadProgress = 0.95f;
        yield return null;

        if (settings.enableThreading) {
            _threadCancelSource = new CancellationTokenSource();
            ChunkUpdateThread = new Thread(() => ThreadedUpdate(_threadCancelSource.Token));
            ChunkUpdateThread.IsBackground = true;
            ChunkUpdateThread.Start();
        }

        // Done — unhide player and UI then signal the loading screen to hide
        LoadProgress = 1f;
        IsReady = true;

        player.gameObject.SetActive(true);
        mainCanvas.gameObject.SetActive(true);
        StartCoroutine(Tick());

    }

    // Yields once per X-row so the loading bar moves smoothly
    private IEnumerator LoadWorldAsync() {

        int hd = settings.loadDistance;
        int vd = settings.verticalViewDistance;
        int cx = VoxelData.WorldSizeInChunks / 2;
        int cy = Mathf.FloorToInt((float)VoxelData.SeaLevel / VoxelData.ChunkSize);

        int totalColumns = (hd * 2) * (hd * 2);
        int done = 0;

        for (int x = cx - hd; x < cx + hd; x++) {
            for (int z = cx - hd; z < cx + hd; z++) {
                for (int y = cy - vd; y < cy + vd; y++) {
                    if (!IsChunkInWorld(x, y, z)) continue;
                    worldData.LoadChunk(new Vector3Int(
                        x * VoxelData.ChunkSize,
                        y * VoxelData.ChunkSize,
                        z * VoxelData.ChunkSize));
                }
                done++;
            }

            // Yield once per X-row to keep the UI responsive
            LoadProgress = Mathf.Lerp(0.25f, 0.75f, (float)done / totalColumns);
            yield return null;
        }

    }

    // -------------------------------------------------------

    public void SetGlobalLightValue() {

        Shader.SetGlobalFloat("GlobalLightLevel", globalLightLevel);
        Color sky = Color.Lerp(night, day, globalLightLevel);
        sky.a = 1f;
        _mainCamera.backgroundColor = sky;

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

        // Don't run game logic until the world is fully initialised
        if (!IsReady) return;

        playerChunkCoord = GetChunkCoordFromVector3(player.position);

        if (!playerChunkCoord.Equals(playerLastChunkCoord)) {

            CheckViewDistance();

            _chunkMovesSinceLastTrim++;
            if (_chunkMovesSinceLastTrim >= TrimEveryNMoves) {
                _chunkMovesSinceLastTrim = 0;
                HeightmapCache.Trim(
                    playerChunkCoord.x,
                    playerChunkCoord.z,
                    settings.viewDistance + 4);
            }
        }

        // Frustum culling — disable renderers on chunks outside the camera view
        GeometryUtility.CalculateFrustumPlanes(_mainCamera, _frustumPlanes);

        float halfSize = VoxelData.ChunkSize * 0.5f;

        for (int i = 0; i < activeChunks.Count; i++) {

            ChunkCoord c = activeChunks[i];
            int yi = ChunkYToIndex(c.y);
            Chunk ch = chunks[c.x, yi, c.z];

            if (ch == null || ch.isEmpty) continue;

            Vector3 centre = ch.position + new Vector3(halfSize, halfSize, halfSize);
            Bounds bounds = new Bounds(centre,
                new Vector3(VoxelData.ChunkSize, VoxelData.ChunkSize, VoxelData.ChunkSize));

            ch.SetRendererEnabled(GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds));
        }

        // Time-budgeted mesh upload — ConcurrentQueue: TryDequeue instead of Dequeue
        if (!chunksToDraw.IsEmpty) {
            _frameTimer.Restart();
            while (!chunksToDraw.IsEmpty &&
                   _frameTimer.Elapsed.TotalMilliseconds < MeshBudgetMs) {
                if (chunksToDraw.TryDequeue(out Chunk toDraw))
                    toDraw.CreateMesh();
            }
        }

        if (!settings.enableThreading) {
            if (!applyingModifications) ApplyModifications();
            if (!chunksToUpdate.IsEmpty) UpdateChunks();
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

        // The set prevents duplicates. The actual queue is a ConcurrentQueue —
        // safe to enqueue from any thread without blocking the background worker.
        lock (_chunksToUpdateSetLock) {
            if (chunksToUpdateSet.Add(chunk))
                chunksToUpdate.Enqueue(chunk);
        }
    }

    void UpdateChunks() {

        if (!chunksToUpdate.TryDequeue(out Chunk c)) return;

        // Remove from dedup set so it can be re-queued later if needed.
        lock (_chunksToUpdateSetLock) { chunksToUpdateSet.Remove(c); }

        c.UpdateChunk();

        // activeChunks is only touched on the main thread so this is safe here
        // only when threading is disabled. In threaded mode UpdateChunks runs on
        // the bg thread — activeChunks tracking happens in CheckViewDistance instead.
    }

    void ThreadedUpdate(CancellationToken token) {

        while (!token.IsCancellationRequested) {

            bool shouldApply;
            lock (_modificationsLock) {
                shouldApply = !applyingModifications && modifications.Count > 0;
            }

            if (shouldApply) ApplyModifications();

            if (!chunksToUpdate.IsEmpty) UpdateChunks();
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
        HeightmapCache.Clear();

    }

    void ApplyModifications() {

        lock (_modificationsLock) {
            if (applyingModifications) return;
            applyingModifications = true;

            while (modifications.Count > 0) {
                Queue<VoxelMod> q = modifications.Dequeue();
                while (q.Count > 0) {
                    VoxelMod v = q.Dequeue();
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

        // PreWarm on a fire-and-forget thread so it never blocks the main thread.
        // The cache is thread-safe (ReaderWriterLockSlim), so this is safe.
        int pwX = coord.x, pwZ = coord.z, pwR = hd + 1;
        BiomeAttributes[] pwBiomes = biomes;
        System.Threading.Tasks.Task.Run(() =>
            HeightmapCache.PreWarm(pwX, pwZ, pwR, pwBiomes));

    }

    public bool CheckForVoxel(Vector3 pos) {

        VoxelState voxel = worldData.GetVoxel(pos);
        if (voxel == null) return false;
        return blocktypes[voxel.id].isSolid;

    }

    public VoxelState GetVoxelState(Vector3 pos) { return worldData.GetVoxel(pos); }

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

    bool IsChunkInWorld(int cx, int cy, int cz) {
        return cx > 0 && cx < VoxelData.WorldSizeInChunks - 1 &&
               cy >= VoxelData.MinChunkY && cy < VoxelData.MaxChunkY &&
               cz > 0 && cz < VoxelData.WorldSizeInChunks - 1;
    }

}

// -------------------------------------------------------
// Supporting data types
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
    public int backFaceTexture, frontFaceTexture, topFaceTexture;
    public int bottomFaceTexture, leftFaceTexture, rightFaceTexture;

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
    public VoxelMod(Vector3 _pos, byte _id) { position = _pos; id = _id; }
}

[System.Serializable]
public class Settings {

    [Header("Game Data")]
    public string version = "0.0.0.01";

    [Header("Performance")]
    public int loadDistance = 4;
    public int viewDistance = 4;
    public int verticalViewDistance = 3;
    public bool enableThreading = true;
    public CloudStyle clouds = CloudStyle.Fast;
    public bool enableAnimatedChunks = false;

    [Header("Controls")]
    [Range(0.1f, 10f)]
    public float mouseSensitivity = 2.0f;
    public int frameRateIndex = 1;

}