using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// MonsterSpawnManager
//
// Companion to DayNightCycle. Handles spawning hostile mobs at night and
// despawning (or disabling) them when day returns.
//
// Currently a READY STUB — it has all the plumbing but spawns nothing because
// monster prefabs haven't been made yet. When you create a monster prefab:
//   1. Add it to the 'monsterPrefabs' array in the Inspector.
//   2. The rest works automatically.
//
// Spawn logic mirrors CowSpawner:
//   • Uses HeightmapCache to find the terrain surface (no raycasts).
//   • Places mobs in a ring around the player (minSpawnDist → maxSpawnDist).
//   • Won't spawn in ocean biomes or above sea level caves.
//   • Caps live monsters at maxMonsters.
//   • Despawns all monsters when dawn arrives.
//
// Setup:
//   Attach to the World GameObject (or any persistent manager in the scene).
// ─────────────────────────────────────────────────────────────────────────────

public class MonsterSpawnManager : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("World component. Auto-found if left null.")]
    public World world;

    [Header("Monster Prefabs")]
    [Tooltip("Add monster prefabs here once they exist. Each must have a Collider " +
             "and ideally a MobHitbox-style component.")]
    public GameObject[] monsterPrefabs;

    [Header("Spawn Settings")]
    [Tooltip("Maximum monsters alive simultaneously.")]
    public int maxMonsters = 15;

    [Tooltip("Seconds between spawn ticks (checked each night).")]
    public float spawnInterval = 20f;

    [Tooltip("How many monsters to try to spawn per tick.")]
    public int spawnBatchSize = 3;

    [Tooltip("Minimum distance from the player to spawn a monster.")]
    public float minSpawnDist = 16f;

    [Tooltip("Maximum distance from the player to spawn a monster.")]
    public float maxSpawnDist = 48f;

    [Tooltip("Monsters won't spawn on tiles below sea level.")]
    public bool avoidOceans = true;

    [Header("Despawn")]
    [Tooltip("If true, all monsters are immediately destroyed at sunrise.")]
    public bool despawnAtDawn = true;

    [Tooltip("If true, monsters that wander further than this from the player are culled.")]
    public bool cullingEnabled = true;

    [Tooltip("Distance at which monsters are culled (should be > maxSpawnDist).")]
    public float cullDistance = 80f;

    // ── Private ──────────────────────────────────────────────────────────────

    private readonly List<GameObject> _liveMonsters = new List<GameObject>();
    private bool _wasNightLastFrame = false;
    private bool _spawnLoopRunning  = false;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        if (world == null) world = World.Instance;
        StartCoroutine(WaitThenRun());
    }

    private IEnumerator WaitThenRun()
    {
        while (!World.IsReady)
            yield return new WaitForSeconds(0.5f);

        StartCoroutine(SpawnLoop());
        StartCoroutine(CullLoop());
    }

    private void Update()
    {
        if (!World.IsReady) return;

        bool isNight = DayNightCycle.IsNight;

        // Despawn everything when dawn breaks.
        if (despawnAtDawn && _wasNightLastFrame && !isNight)
            DespawnAll();

        _wasNightLastFrame = isNight;
    }

    // ── Spawn loop ────────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);

            if (!DayNightCycle.IsNight) continue;

            // Nothing to spawn yet — stub guard.
            if (monsterPrefabs == null || monsterPrefabs.Length == 0)
            {
                // Uncomment for noisy debugging:
                // Debug.Log("[MonsterSpawnManager] No monster prefabs assigned — skipping spawn tick.");
                continue;
            }

            PruneDead();
            if (_liveMonsters.Count >= maxMonsters) continue;

            int needed   = Mathf.Min(spawnBatchSize, maxMonsters - _liveMonsters.Count);
            int spawned  = 0;
            int attempts = needed * 8;

            Vector3 playerPos = world.player != null
                ? world.player.position
                : world.spawnPosition;

            for (int i = 0; i < attempts && spawned < needed; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float dist  = Random.Range(minSpawnDist, maxSpawnDist);

                int wx = Mathf.RoundToInt(playerPos.x + Mathf.Cos(angle) * dist);
                int wz = Mathf.RoundToInt(playerPos.z + Mathf.Sin(angle) * dist);

                if (TryGetSpawnPosition(wx, wz, out Vector3 spawnPos))
                {
                    GameObject prefab = monsterPrefabs[Random.Range(0, monsterPrefabs.Length)];
                    GameObject mob    = Instantiate(prefab, spawnPos,
                                            Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    _liveMonsters.Add(mob);
                    spawned++;
                }
            }

            if (spawned > 0)
                Debug.Log($"[MonsterSpawnManager] Spawned {spawned} monster(s) — total {_liveMonsters.Count}.");
        }
    }

    // ── Cull loop ─────────────────────────────────────────────────────────────

    // Periodically removes monsters that have wandered too far from the player.
    private IEnumerator CullLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(10f);

            if (!cullingEnabled) continue;

            Vector3 playerPos = world.player != null
                ? world.player.position
                : Vector3.zero;

            float cullSqr = cullDistance * cullDistance;

            for (int i = _liveMonsters.Count - 1; i >= 0; i--)
            {
                GameObject mob = _liveMonsters[i];
                if (mob == null) { _liveMonsters.RemoveAt(i); continue; }

                if ((mob.transform.position - playerPos).sqrMagnitude > cullSqr)
                {
                    Destroy(mob);
                    _liveMonsters.RemoveAt(i);
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up the terrain surface at (worldX, worldZ) via HeightmapCache.
    /// Returns true and sets spawnPos if the location is valid for a monster.
    /// </summary>
    private bool TryGetSpawnPosition(int worldX, int worldZ, out Vector3 spawnPos)
    {
        spawnPos = Vector3.zero;

        if (world == null || world.biomes == null || world.biomes.Length == 0)
            return false;

        TerrainGenerator.ColumnData col =
            HeightmapCache.GetOrCompute(worldX, worldZ, world.biomes);

        int surfaceY = col.surfaceHeight;

        if (avoidOceans && surfaceY < VoxelData.SeaLevel)
            return false;

        spawnPos = new Vector3(worldX + 0.5f, surfaceY + 1f, worldZ + 0.5f);
        return true;
    }

    private void DespawnAll()
    {
        foreach (GameObject mob in _liveMonsters)
            if (mob != null) Destroy(mob);

        _liveMonsters.Clear();
        Debug.Log("[MonsterSpawnManager] Dawn — all monsters despawned.");
    }

    private void PruneDead()
    {
        _liveMonsters.RemoveAll(m => m == null);
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (world == null) return;

        Vector3 centre = Application.isPlaying && world.player != null
            ? world.player.position
            : new Vector3(VoxelData.WorldCentre, VoxelData.SeaLevel, VoxelData.WorldCentre);

        // Spawn ring
        Gizmos.color = new Color(0.9f, 0.15f, 0.15f, 0.35f);
        DrawCircle(centre, minSpawnDist);
        DrawCircle(centre, maxSpawnDist);

        // Cull ring
        if (cullingEnabled)
        {
            Gizmos.color = new Color(0.6f, 0.0f, 0.0f, 0.2f);
            DrawCircle(centre, cullDistance);
        }
    }

    private static void DrawCircle(Vector3 centre, float radius)
    {
        int steps = 48;
        Vector3 prev = centre + new Vector3(radius, 0, 0);
        for (int i = 1; i <= steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}
