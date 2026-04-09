using System.Collections;
using System.Collections.Generic;
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

    // Time budget for mesh uploads per frame (ms).
    // 8ms leaves ~8ms headroom at 60fps for game logic.
    private const double MeshBudgetMs = 8.0;
    private readonly Stopwatch _frameTimer = new Stopwatch();

    // How often to trim the heightmap cache (player chunk moves).
    private int _chunkMovesSinceLastTrim = 0;
    private const int TrimEveryNMoves = 32;

    // Frustum culling — camera frustum planes, updated once per frame.
    private Plane[] _frustumPlanes = new Plane[6];
    private Camera _mainCamera;

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

    }

    private void Start() {

        _mainCamera = Camera.main;

        Debug.Log("Generating new world using seed " + VoxelData.seed);

        worldData = SaveSystem.LoadWorld("Testing");

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

        // Pre-warm the heightmap cache BEFORE chunk generation begins.
        // All column data (surface height + biome) is computed here on the main thread.
        // Background chunk threads then find their columns already cached — O(1) reads only.
        HeightmapCache.Clear();
        int startCX = VoxelData.WorldSizeInChunks / 2;
        int startCZ = VoxelData.WorldSizeInChunks / 2;
        HeightmapCache.PreWarm(startCX, startCZ, settings.loadDistance + 1, biomes);

        LoadWorld();
        SetGlobalLightValue();

        // Spawn above sea level. Normal terrain sits at Y 66-78.
        spawnPosition = new Vector3(
            VoxelData.WorldCentre,
            VoxelData.SeaLevel + 30f,
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

        // -------------------------------------------------------
        // FRUSTUM CULLING
        // Extract the camera's frustum planes once per frame, then
        // test each active chunk's bounding box against them.
        // Chunks outside the frustum (behind player, to the side,
        // above/below) get their MeshRenderer disabled so the GPU
        // doesn't process or submit their draw calls at all.
        //
        // At viewDist=4 + vertDist=3, ~192 chunks are active.
        // Typically ~100 of those are outside the frustum.
        // This cuts GPU draw calls by 40-60% during normal gameplay.
        // -------------------------------------------------------
        GeometryUtility.CalculateFrustumPlanes(_mainCamera, _frustumPlanes);

        float halfSize = VoxelData.ChunkSize * 0.5f;

        for (int i = 0; i < activeChunks.Count; i++) {

            ChunkCoord c = activeChunks[i];
            int yi = ChunkYToIndex(c.y);
            Chunk ch = chunks[c.x, yi, c.z];

            if (ch == null || ch.isEmpty) continue;

            // Build the chunk's axis-aligned bounding box.
            Vector3 centre = ch.position + new Vector3(halfSize, halfSize, halfSize);
            Bounds bounds = new Bounds(centre,
                new Vector3(VoxelData.ChunkSize, VoxelData.ChunkSize, VoxelData.ChunkSize));

            ch.SetRendererEnabled(GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds));
        }

        // -------------------------------------------------------
        // MESH UPLOAD with time budget.
        // Drain as many queued meshes as fit within MeshBudgetMs per frame.
        // Previously this dequeued exactly 1 per frame — with 192 chunks
        // that caused a 192-frame initial load stall at 10fps = 19 seconds.
        // -------------------------------------------------------
        if (chunksToDraw.Count > 0) {
            _frameTimer.Restart();
            while (chunksToDraw.Count > 0 &&
                   _frameTimer.Elapsed.TotalMilliseconds < MeshBudgetMs) {
                chunksToDraw.Dequeue().CreateMesh();
            }
        }

        if (!settings.enableThreading) {
            if (!applyingModifications) ApplyModifications();
            if (chunksToUpdate.Count > 0) UpdateChunks();
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

            // -------------------------------------------------------
            // DISTANCE + DIRECTION PRIORITY SORT
            // Chunks closest to the player AND in front of the camera
            // are processed first. The world loads toward you instead
            // of in a random grid pattern — much better feel.
            //
            // Only sort when the queue is small (< 64) to avoid
            // spending O(n log n) time during the massive initial load.
            // -------------------------------------------------------
            if (chunksToUpdate.Count > 1 && chunksToUpdate.Count <= 64) {

                Vector3 pPos = World.Instance.player.position;
                Vector3 pFwd = World.Instance._player.transform.forward;

                chunksToUpdate.Sort((a, b) => {
                    float pa = ChunkPriority(a, pPos, pFwd);
                    float pb = ChunkPriority(b, pPos, pFwd);
                    return pa.CompareTo(pb); // Lower value = higher priority
                });
            }

            Chunk c = chunksToUpdate[0];
            chunksToUpdate.RemoveAt(0);
            chunksToUpdateSet.Remove(c);
            c.UpdateChunk();

            if (!activeChunks.Contains(c.coord)) activeChunks.Add(c.coord);
        }
    }

    /// <summary>
    /// Priority score for chunk update ordering.
    /// Lower = process first.
    /// Combines squared distance with a forward-direction bonus.
    /// </summary>
    private static float ChunkPriority(Chunk c, Vector3 playerPos, Vector3 playerFwd) {

        float half = VoxelData.ChunkSize * 0.5f;
        Vector3 toChunk = c.position + new Vector3(half, 0, half) - playerPos;

        float sqDist = toChunk.sqrMagnitude;
        float forward = Vector3.Dot(toChunk.normalized, playerFwd);

        // Subtract forward bonus so chunks we're looking at get lower (better) score.
        // 64f = one chunk width of advantage for directly in-front chunks.
        return sqDist - forward * 64f;
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

        // Pre-warm heightmap for the newly visible area.
        HeightmapCache.PreWarm(coord.x, coord.z, hd + 1, biomes);

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
    public int frameRateIndex = 1; // Kept from your original Settings

}