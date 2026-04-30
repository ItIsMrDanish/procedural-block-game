using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// CowSpawner — attach to the World GameObject (or any persistent manager).
//
// Handles two spawn passes:
//   1. Initial spawn  — runs once after the world finishes loading (IsReady).
//      Scatters a batch of cows around the player's spawn area using the
//      HeightmapCache so every cow lands exactly on the terrain surface.
//
//   2. Ambient respawn — runs on a slow timer; adds new cows if the current
//      count is below maxCows. Cows are placed in a ring around the player
//      (between minSpawnDist and maxSpawnDist) so they appear naturally while
//      exploring rather than popping into existence right in front of the player.
//
// Setup:
//   1. Attach to World (or a Scene Manager GameObject).
//   2. Assign 'cowPrefab' — your Cow GameObject with Cow + MobHitbox + Collider.
//   3. Assign 'world' if not on the World object itself.
//   4. Tune the numbers below in the Inspector.
//
// Surface detection:
//   Uses HeightmapCache.GetOrCompute() (same data TerrainGenerator uses) so cows
//   always land on the correct voxel surface height without any raycasts.
//   Only spawns on non-ocean biomes (surfaceY >= SeaLevel) to avoid water spawns.
// ─────────────────────────────────────────────────────────────────────────────

public class CowSpawner : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The Cow prefab (must have Cow + MobHitbox + Collider).")]
    public GameObject cowPrefab;

    [Tooltip("The World component. Auto-found if left empty.")]
    public World world;

    [Header("Initial Spawn (on world load)")]
    [Tooltip("How many cows to spawn when the world first loads.")]
    public int initialSpawnCount = 12;

    [Tooltip("Radius around the player spawn point to scatter initial cows.")]
    public float initialSpawnRadius = 48f;

    [Header("Ambient Respawn")]
    [Tooltip("Maximum number of cows alive at the same time.")]
    public int maxCows = 20;

    [Tooltip("Seconds between each ambient respawn check.")]
    public float respawnInterval = 30f;

    [Tooltip("Minimum distance from the player to spawn a new cow.")]
    public float minSpawnDist = 24f;

    [Tooltip("Maximum distance from the player to spawn a new cow.")]
    public float maxSpawnDist = 64f;

    [Tooltip("How many cows to try to add per respawn tick (if below maxCows).")]
    public int respawnBatchSize = 3;

    [Header("Biome Filter")]
    [Tooltip("Cows won't spawn if surface height is below sea level (avoids oceans).")]
    public bool avoidOceans = true;

    // ── Private ──────────────────────────────────────────────────────────────

    private readonly List<GameObject> _liveCows = new List<GameObject>();
    private bool _initialSpawnDone = false;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        if (world == null)
            world = World.Instance;

        StartCoroutine(WaitForWorldThenSpawn());
        StartCoroutine(AmbientRespawnLoop());
    }

    // ── Initial spawn ────────────────────────────────────────────────────────

    private IEnumerator WaitForWorldThenSpawn()
    {
        // Wait until World.IsReady (set at end of InitWorld coroutine).
        while (!World.IsReady)
            yield return new WaitForSeconds(0.5f);

        // One extra frame so chunks have had a chance to draw.
        yield return new WaitForSeconds(1f);

        SpawnInitialHerd();
        _initialSpawnDone = true;
    }

    private void SpawnInitialHerd()
    {
        if (cowPrefab == null) { Debug.LogWarning("[CowSpawner] No cowPrefab assigned!"); return; }

        Vector3 centre = world.spawnPosition;
        int spawned = 0;
        int attempts = initialSpawnCount * 6; // Allow plenty of retries

        for (int i = 0; i < attempts && spawned < initialSpawnCount; i++)
        {
            Vector2 offset = Random.insideUnitCircle * initialSpawnRadius;
            int worldX = Mathf.RoundToInt(centre.x + offset.x);
            int worldZ = Mathf.RoundToInt(centre.z + offset.y);

            if (TryGetSpawnPosition(worldX, worldZ, out Vector3 spawnPos))
            {
                SpawnCow(spawnPos);
                spawned++;
            }
        }

        Debug.Log($"[CowSpawner] Initial spawn: {spawned}/{initialSpawnCount} cows placed.");
    }

    // ── Ambient respawn ──────────────────────────────────────────────────────

    private IEnumerator AmbientRespawnLoop()
    {
        // Don't start until initial spawn is done.
        while (!_initialSpawnDone)
            yield return new WaitForSeconds(1f);

        while (true)
        {
            yield return new WaitForSeconds(respawnInterval);

            PruneDead();

            if (_liveCows.Count >= maxCows) continue;

            int needed = Mathf.Min(respawnBatchSize, maxCows - _liveCows.Count);
            int spawned = 0;
            int attempts = needed * 8;

            Vector3 playerPos = world.player != null ? world.player.position : world.spawnPosition;

            for (int i = 0; i < attempts && spawned < needed; i++)
            {
                // Pick a random angle and distance in the spawn ring.
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = Random.Range(minSpawnDist, maxSpawnDist);

                int worldX = Mathf.RoundToInt(playerPos.x + Mathf.Cos(angle) * dist);
                int worldZ = Mathf.RoundToInt(playerPos.z + Mathf.Sin(angle) * dist);

                if (TryGetSpawnPosition(worldX, worldZ, out Vector3 spawnPos))
                {
                    SpawnCow(spawnPos);
                    spawned++;
                }
            }

            if (spawned > 0)
                Debug.Log($"[CowSpawner] Ambient respawn: +{spawned} cows (total {_liveCows.Count}).");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Looks up the terrain surface at (worldX, worldZ) via HeightmapCache.
    /// Returns true and sets spawnPos if the location is valid for a cow spawn.
    /// spawnPos.y is set to the top face of the surface block (surfaceHeight + 1)
    /// so the cow stands ON the block rather than inside it.
    /// </summary>
    private bool TryGetSpawnPosition(int worldX, int worldZ, out Vector3 spawnPos)
    {
        spawnPos = Vector3.zero;

        if (world == null || world.biomes == null || world.biomes.Length == 0)
            return false;

        TerrainGenerator.ColumnData col =
            HeightmapCache.GetOrCompute(worldX, worldZ, world.biomes);

        int surfaceY = col.surfaceHeight;

        // Skip ocean / underwater surfaces.
        if (avoidOceans && surfaceY < VoxelData.SeaLevel)
            return false;

        // Place the cow's feet at the TOP of the surface block.
        // surfaceY is the voxel index of the surface block, so +1 puts us
        // at the bottom face of the air block directly above it.
        spawnPos = new Vector3(worldX + 0.5f, surfaceY + 1f, worldZ + 0.5f);
        return true;
    }

    private void SpawnCow(Vector3 position)
    {
        GameObject cow = Instantiate(cowPrefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        _liveCows.Add(cow);
    }

    /// <summary>Remove destroyed/dead cows from the tracking list.</summary>
    private void PruneDead()
    {
        _liveCows.RemoveAll(c => c == null);
    }

    // ── Editor Gizmos ────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (world == null) return;

        Vector3 centre = Application.isPlaying ? world.spawnPosition :
                         new Vector3(VoxelData.WorldCentre, VoxelData.SeaLevel, VoxelData.WorldCentre);

        // Initial spawn radius
        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.3f);
        DrawCircle(centre, initialSpawnRadius);

        // Ambient spawn ring
        if (world.player != null)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.3f);
            DrawCircle(world.player.position, minSpawnDist);
            DrawCircle(world.player.position, maxSpawnDist);
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