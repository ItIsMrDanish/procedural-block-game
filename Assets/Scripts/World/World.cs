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

    Chunk[,,] chunks = new Chunk[VoxelData.WorldSizeInChunks, VoxelData.WorldHeightInChunks, VoxelData.WorldSizeInChunks];

    // FIX (Bug 4): activeChunks is iterated on the main thread (Update frustum loop,
    // Tick coroutine) AND rebuilt by CheckViewDistance (also main thread, but called
    // from Update while Tick may be mid-iteration via coroutine). Use a snapshot list
    // for Tick iteration so a CheckViewDistance rebuild mid-tick can't corrupt it.
    // All direct access to activeChunks stays on the main thread — no cross-thread issue,
    // but the coroutine/iteration overlap was the crash source.
    private readonly List<ChunkCoord> activeChunks = new List<ChunkCoord>();
    private readonly object _activeChunksLock = new object();

    public ChunkCoord playerChunkCoord;
    ChunkCoord playerLastChunkCoord;

    // ConcurrentQueue for chunk updates: bg thread dequeues, both threads enqueue.
    private ConcurrentQueue<Chunk> chunksToUpdate = new ConcurrentQueue<Chunk>();
    private HashSet<Chunk> chunksToUpdateSet = new HashSet<Chunk>();
    private readonly object _chunksToUpdateSetLock = new object();

    // Threaded chunk creation pipeline:
    // Main thread enqueues ChunkCoords here when threading is on.
    // Bg thread dequeues, calls worldData.RequestChunk (Populate) — the expensive part.
    // After Populate, bg thread enqueues to _pendingChunkObjects.
    // Main thread dequeues _pendingChunkObjects and calls new Chunk() (must be main thread — Unity GameObject).
    private readonly Queue<ChunkCoord> _pendingChunkCreations = new Queue<ChunkCoord>();
    private readonly object _pendingChunkCreationsLock = new object();
    private readonly Queue<ChunkCoord> _pendingChunkObjects = new Queue<ChunkCoord>();
    private readonly object _pendingChunkObjectsLock = new object();

    // ConcurrentQueue: bg thread enqueues, main thread dequeues.
    public ConcurrentQueue<Chunk> chunksToDraw = new ConcurrentQueue<Chunk>();

    // FIX (Bug 2): applyingModifications was a volatile bool with a non-atomic
    // double-check pattern: if(!flag) { flag=true; ... } — two threads could both
    // pass the check before either set the flag. The fix is to do the check AND set
    // atomically inside the same lock, so only one thread can ever enter at a time.
    // The volatile keyword is removed since the lock provides the memory barrier.
    private bool _applyingModifications = false;

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

    // Static mirror of ChunkListThreadLock — assigned in Awake() immediately after
    // _instance is set.  Background threads use this instead of
    // World.Instance.ChunkListThreadLock so they never race on a null Instance.
    public static object StaticChunkListLock { get; private set; }

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

    // Loading progress — read by LoadingScreenUI
    public static float LoadProgress { get; private set; } = 0f;
    public static bool IsReady { get; private set; } = false;

    // -------------------------------------------------------

    private static int ChunkYToIndex(int chunkY) {
        return chunkY - VoxelData.MinChunkY;
    }

    // Unity lifecycle

    private void Awake() {

        if (_instance != null && _instance != this) {
            Destroy(this.gameObject);
        } else {
            _instance = this;
            // Assign the static lock immediately so background threads can use it
            // without ever dereferencing Instance, eliminating the Windows race.
            StaticChunkListLock = ChunkListThreadLock;
        }

        appPath = Application.persistentDataPath;
        _player = player.GetComponent<Player>();

        debugControls = new InputSystem();
        debugControls.Debug.ToggleDebugScreen.performed += _ => debugScreen.SetActive(!debugScreen.activeSelf);
        debugControls.Debug.SaveWorld.performed += _ => SaveSystem.SaveWorld(worldData);
        debugControls.Debug.Enable();

        LoadProgress = 0f;
        IsReady = false;

        mainCanvas.gameObject.SetActive(false);
    }

    private void Start() {

        _mainCamera = Camera.main;
        StartCoroutine(InitWorld());
    }

    private IEnumerator InitWorld() {

        Debug.Log($"Loading world '{VoxelData.worldName}' with seed {VoxelData.seed}");

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

        LoadProgress = 0.1f;
        yield return null;

        HeightmapCache.Clear();
        int startCX = VoxelData.WorldSizeInChunks / 2;
        int startCZ = VoxelData.WorldSizeInChunks / 2;
        HeightmapCache.PreWarm(startCX, startCZ, settings.loadDistance + 1, biomes);

        LoadProgress = 0.25f;
        yield return null;

        yield return StartCoroutine(LoadWorldAsync());

        SetGlobalLightValue();

        LoadProgress = 0.8f;
        yield return null;

        spawnPosition = FindLandSpawn();

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

        LoadProgress = 1f;
        IsReady = true;

        player.gameObject.SetActive(true);
        mainCanvas.gameObject.SetActive(true);

        // Enable player input AFTER the canvas is active. This prevents the canvas
        // SetActive() from triggering OnEnable on child UI components while Input System
        // actions are already live — which caused stale action phases requiring 2-3
        // key presses before UI toggles would register correctly.
        _player.EnableControls();

        StartCoroutine(Tick());
    }

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

            // FIX (Bug 4 part 2): Take a snapshot of activeChunks under the lock
            // before iterating. CheckViewDistance can rebuild activeChunks mid-tick
            // if the coroutine yields; iterating a list while it's being modified
            // throws InvalidOperationException.
            int count;
            lock (_activeChunksLock) {

                count = activeChunks.Count;
                if (_tickSnapshot.Length != count)
                    _tickSnapshot = new ChunkCoord[count];
                activeChunks.CopyTo(_tickSnapshot);
            }

            for (int i = 0; i < count; i++) {

                ChunkCoord c = _tickSnapshot[i];
                int yi = ChunkYToIndex(c.y);
                if (chunks[c.x, yi, c.z] != null)
                    chunks[c.x, yi, c.z].TickUpdate();
            }
        }
    }

    private void Update() {

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

        // Frustum culling
        GeometryUtility.CalculateFrustumPlanes(_mainCamera, _frustumPlanes);
        float halfSize = VoxelData.ChunkSize * 0.5f;

        // FIX (Bug 4 part 3): Read activeChunks under lock for the frustum loop too.
        // CheckViewDistance (called above in same Update) rebuilds the list, so we
        // need a stable view of it for this loop.
        int activeCount;
        ChunkCoord[] frustumSnapshot;
        lock (_activeChunksLock) {

            activeCount = activeChunks.Count;
            frustumSnapshot = activeChunks.ToArray();
        }

        for (int i = 0; i < activeCount; i++) {

            ChunkCoord c = frustumSnapshot[i];
            int yi = ChunkYToIndex(c.y);
            Chunk ch = chunks[c.x, yi, c.z];

            if (ch == null || ch.isEmpty) continue;

            Vector3 centre = ch.position + new Vector3(halfSize, halfSize, halfSize);
            Bounds bounds = new Bounds(centre,
                new Vector3(VoxelData.ChunkSize, VoxelData.ChunkSize, VoxelData.ChunkSize));

            ch.SetRendererEnabled(GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds));
        }

        // Time-budgeted mesh upload
        if (!chunksToDraw.IsEmpty) {

            _frameTimer.Restart();
            while (!chunksToDraw.IsEmpty &&
                   _frameTimer.Elapsed.TotalMilliseconds < MeshBudgetMs) {

                if (chunksToDraw.TryDequeue(out Chunk toDraw))
                    toDraw.CreateMesh();
            }
        }

        // Drain pending chunk GameObject creations (threaded mode).
        // The bg thread did the expensive Populate(); we just create the GameObject here.
        if (settings.enableThreading) {

            _frameTimer.Restart();
            while (_frameTimer.Elapsed.TotalMilliseconds < 4.0) {

                ChunkCoord cc;
                lock (_pendingChunkObjectsLock) {

                    if (_pendingChunkObjects.Count == 0) break;
                    cc = _pendingChunkObjects.Dequeue();
                }

                int yi = ChunkYToIndex(cc.y);
                if (chunks[cc.x, yi, cc.z] == null) {

                    chunks[cc.x, yi, cc.z] = new Chunk(cc);

                    // Activate if still within current view. Without this, async-created
                    // chunks are never made visible because CheckViewDistance already ran.
                    bool inView;
                    lock (_activeChunksLock) { inView = activeChunks.Exists(a => a.Equals(cc)); }
                    if (inView) chunks[cc.x, yi, cc.z].isActive = true;
                }
            }
        }

        if (!settings.enableThreading) {

            if (!_applyingModifications) {
                _applyingModifications = true;
                ApplyModifications();
            }
            if (!chunksToUpdate.IsEmpty) UpdateChunks();
        }
    }

    public void AddChunkToUpdate(Chunk chunk) { AddChunkToUpdate(chunk, false); }
    public void AddChunkToUpdate(Chunk chunk, bool insert) {

        lock (_chunksToUpdateSetLock) {

            if (chunksToUpdateSet.Add(chunk))
                chunksToUpdate.Enqueue(chunk);
        }
    }

    void UpdateChunks() {

        // Process multiple chunks per frame with a time budget to reduce visual
        // pop-in while keeping frame times bounded.
        _frameTimer.Restart();
        const double budgetMs = 4.0;

        while (!chunksToUpdate.IsEmpty &&
               _frameTimer.Elapsed.TotalMilliseconds < budgetMs) {

            if (!chunksToUpdate.TryDequeue(out Chunk c)) break;
            lock (_chunksToUpdateSetLock) { chunksToUpdateSet.Remove(c); }
            c.UpdateChunk();
        }
    }

    void ThreadedUpdate(CancellationToken token) {

        while (!token.IsCancellationRequested) {

            // Pre-populate chunk data for coords queued by CheckViewDistance.
            // This is the expensive Populate() work — done here off the main thread.
            // Once data is ready, hand off to main thread for GameObject creation.
            ChunkCoord ccToPrep;
            bool hadPrep = false;
            lock (_pendingChunkCreationsLock) {

                hadPrep = _pendingChunkCreations.Count > 0;
                ccToPrep = hadPrep ? _pendingChunkCreations.Dequeue() : default;
            }

            if (hadPrep) {

                // Don't read chunks[] array here — it's main-thread-only.
                // RequestChunk is safe to call from bg thread; it checks internally
                // if ChunkData already exists and skips Populate() if so.
                var blockOrigin = new UnityEngine.Vector3Int(
                    ccToPrep.x * VoxelData.ChunkSize,
                    ccToPrep.y * VoxelData.ChunkSize,
                    ccToPrep.z * VoxelData.ChunkSize);
                worldData.RequestChunk(blockOrigin, true);

                // Hand off to main thread for GameObject creation.
                lock (_pendingChunkObjectsLock) { _pendingChunkObjects.Enqueue(ccToPrep); }

                // FIX: drain modifications here too, not only when the prep queue is empty.
                // Previously the `continue` skipped ApplyModifications() entirely while
                // _pendingChunkCreations was non-empty.  Flora mods (trees, void trees) are
                // enqueued by Populate() itself, so they were never applied before the chunk
                // mesh was built → trees missing on all platforms, most visibly on Windows.
                bool shouldApplyMid;
                lock (_modificationsLock) {
                    shouldApplyMid = !_applyingModifications && modifications.Count > 0;
                    if (shouldApplyMid) _applyingModifications = true;
                }
                if (shouldApplyMid) ApplyModifications();

                continue; // Keep draining prep queue before sleeping.
            }

            bool shouldApply;
            lock (_modificationsLock) {

                shouldApply = !_applyingModifications && modifications.Count > 0;
                if (shouldApply) _applyingModifications = true;
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

        // Drain the queue. _applyingModifications was already set to true by the caller
        // (either ThreadedUpdate or the non-threaded Update path).
        // We only need the lock to safely dequeue batches.
        try {

            while (true) {

                Queue<VoxelMod> q;
                lock (_modificationsLock) {

                    if (modifications.Count == 0) break;
                    q = modifications.Dequeue();
                }

                while (q.Count > 0) {

                    VoxelMod v = q.Dequeue();
                    worldData.SetVoxel(v.position, v.id, 1);
                }
            }
        } finally {

            lock (_modificationsLock) { _applyingModifications = false; }
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

        int hd = settings.viewDistance;
        int vd = settings.verticalViewDistance;

        // --- Phase 1: figure out which coords are needed (no locks, no allocation) ---
        // We read `chunks` array here — this is safe because only the main thread
        // writes to `chunks[]` (new Chunk() calls only happen here on the main thread).
        var newActiveChunks = new List<ChunkCoord>();
        var chunksToCreate = new List<ChunkCoord>();

        _previouslyActiveChunks.Clear();
        lock (_activeChunksLock) { _previouslyActiveChunks.AddRange(activeChunks); }

        for (int x = coord.x - hd; x < coord.x + hd; x++) {
            for (int z = coord.z - hd; z < coord.z + hd; z++) {
                for (int y = coord.y - vd; y < coord.y + vd; y++) {

                    if (!IsChunkInWorld(x, y, z)) continue;

                    ChunkCoord cc = new ChunkCoord(x, y, z);
                    newActiveChunks.Add(cc);

                    if (chunks[x, ChunkYToIndex(y), z] == null)
                        chunksToCreate.Add(cc);
                }
            }
        }

        // Compute deactivations before creating new chunks.
        _previouslyActiveChunks.RemoveAll(old =>
            newActiveChunks.Exists(n => n.Equals(old)));

        // --- Phase 2: create new Chunk objects ---
        // With threading ON and bg thread running: enqueue for bg thread to Populate(),
        // then main thread creates the GameObject. This keeps Populate() off the main thread.
        // IMPORTANT: during initial world load (InitWorld), the bg thread hasn't started yet,
        // so we always create synchronously if the thread isn't running.
        bool bgThreadRunning = settings.enableThreading &&
                               ChunkUpdateThread != null &&
                               ChunkUpdateThread.IsAlive;

        if (bgThreadRunning) {

            // Enqueue for bg thread to Populate() then hand back to main thread.
            lock (_pendingChunkCreationsLock) {
                foreach (ChunkCoord cc in chunksToCreate)
                    _pendingChunkCreations.Enqueue(cc);
            }
        } else {

            // Synchronous creation: main thread does everything.
            // This handles both: threading disabled, and the initial load before bg thread starts.
            foreach (ChunkCoord cc in chunksToCreate) {

                int yi = ChunkYToIndex(cc.y);
                if (chunks[cc.x, yi, cc.z] == null)
                    chunks[cc.x, yi, cc.z] = new Chunk(cc);
            }
        }

        // --- Phase 3: swap activeChunks under lock, activate/deactivate ---
        lock (_activeChunksLock) {

            activeChunks.Clear();
            activeChunks.AddRange(newActiveChunks);
        }

        // Only activate chunks that actually exist. Chunks enqueued in _pendingChunkCreations
        // don't exist yet — they'll call AddChunkToUpdate themselves when created.
        foreach (ChunkCoord cc in newActiveChunks) {

            int yi = ChunkYToIndex(cc.y);
            if (chunks[cc.x, yi, cc.z] != null)
                chunks[cc.x, yi, cc.z].isActive = true;
        }

        foreach (ChunkCoord c in _previouslyActiveChunks) {

            int yi = ChunkYToIndex(c.y);
            if (chunks[c.x, yi, c.z] != null)
                chunks[c.x, yi, c.z].isActive = false;
        }

        // PreWarm heightmap on a fire-and-forget thread.
        int pwX = coord.x, pwZ = coord.z, pwR = settings.viewDistance + 1;
        BiomeAttributes[] pwBiomes = biomes;
        System.Threading.Tasks.Task.Run(() =>
            HeightmapCache.PreWarm(pwX, pwZ, pwR, pwBiomes));
    }


    // Spirals outward from the world centre until it finds a column whose surface
    // is at or above sea level (not ocean). Spawns the player 2 blocks above
    // that surface so they land cleanly on solid ground.
    private Vector3 FindLandSpawn() {

        int centre = VoxelData.WorldCentre;
        const int stepSize = 8;
        const int maxRadius = 400;

        for (int radius = 0; radius <= maxRadius; radius += stepSize) {

            if (radius == 0) {
                var col = HeightmapCache.GetOrCompute(centre, centre, biomes);
                if (col.surfaceHeight >= VoxelData.SeaLevel)
                    return new Vector3(centre, col.surfaceHeight + 2f, centre);
                continue;
            }

            for (int i = -radius; i <= radius; i += stepSize) {
                int[] xs = { centre + i, centre + i, centre - radius, centre + radius };
                int[] zs = { centre - radius, centre + radius, centre + i, centre + i };
                for (int side = 0; side < 4; side++) {
                    int wx = xs[side];
                    int wz = zs[side];
                    if (wx < 0 || wx >= VoxelData.WorldSizeInVoxels) continue;
                    if (wz < 0 || wz >= VoxelData.WorldSizeInVoxels) continue;
                    var col = HeightmapCache.GetOrCompute(wx, wz, biomes);
                    if (col.surfaceHeight >= VoxelData.SeaLevel)
                        return new Vector3(wx, col.surfaceHeight + 2f, wz);
                }
            }
        }

        Debug.LogWarning("FindLandSpawn: no land found, using fallback.");
        return new Vector3(centre, VoxelData.SeaLevel + 30f, centre);
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
            // Only manage cursor lock here.
            // Each UI panel (inventory, crafting) manages its own visibility.
            // The creative inventory is opened separately via SetCreativeInventory().
            Cursor.lockState = _inUI ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _inUI;
        }
    }

    /// <summary>
    /// Opens or closes the creative inventory window specifically.
    /// Also sets inUI so player input is blocked while it is open.
    /// </summary>
    public void SetCreativeInventory(bool open) {
        creativeInventoryWindow.SetActive(open);
        cursorSlot.SetActive(open);
        inUI = open;
    }

    bool IsChunkInWorld(int cx, int cy, int cz) {

        return cx > 0 && cx < VoxelData.WorldSizeInChunks - 1 &&
               cy >= VoxelData.MinChunkY && cy < VoxelData.MaxChunkY &&
               cz > 0 && cz < VoxelData.WorldSizeInChunks - 1;
    }
}

// Supporting data types

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

    [Header("Breaking")]
    [Tooltip("Seconds of continuous holding required to break this block. " +
             "Set to 0 for instant-break (e.g. air, grass, flowers).")]
    public float blockHealth;

    [Tooltip("Which tool type mines this block at full speed.\n" +
             "A mismatched tool always falls back to bare-hands speed (damage = 1).\n" +
             "Leave as None for blocks that any tool mines at full efficiency (e.g. dirt with a shovel is faster, but any tool works the same — adjust to taste).")]
    public ToolType preferredTool = ToolType.None;

    [Tooltip("Minimum material tier required to obtain a drop from this block.\n" +
             "Breaking it with a lower-tier tool destroys the block but yields nothing.\n" +
             "Leave as None to allow bare-hands harvesting.")]
    public MaterialType minimumMaterial = MaterialType.None;

    [Header("Drop")]
    [Tooltip("Item name added to the player's inventory on break.\n" +
             "Leave empty to use blockName.")]
    public string dropItemName;

    [Tooltip("How many items drop when this block is broken. Default 1.")]
    [Min(0)]
    public int dropAmount = 1;

    [Tooltip("Icon sprite for the dropped item in the inventory UI.\n" +
             "Leave null to use the block's own icon.")]
    public Sprite dropIcon;

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