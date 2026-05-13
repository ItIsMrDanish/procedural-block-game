using UnityEngine;

/// <summary>
/// Singleton manager that spawns DroppedItem pickups when blocks are broken.
///
/// SETUP
/// ─────
///  1. Add this component to any persistent GameObject (e.g. the World object).
///  2. Assign dropItemPrefab — a small Cube with a MeshRenderer, Rigidbody,
///     BoxCollider, and the DroppedItem component already on it.
///     (Or leave it null and the manager will build a primitive cube at runtime.)
///  3. Assign playerInventory and playerTransform in the Inspector,
///     OR let it auto-find them by tag ("Player") and component on Start.
///
/// USAGE
/// ─────
///  Call ItemDropManager.Instance.SpawnDrop(blockId, worldPosition) from anywhere
///  a block is destroyed.  Player.cs calls this in UpdateBlockBreaking() via the
///  helper method BreakAndDrop().
///
/// PER-BLOCK DROPS
/// ───────────────
///  Each BlockType in World.blocktypes has a new "drop" section (added to World.cs):
///   • dropItem       – (NEW) an ItemDefinition asset; name and icon are read from it.
///                      Assign this to connect block drops to the crafting system.
///   • dropItemName   – (legacy) item name string; only used when dropItem is null
///   • dropAmount     – how many items drop (default 1)
///   • dropIcon       – (legacy) icon sprite; only used when dropItem is null
///  Air (id 0) never drops anything.
/// </summary>
public class ItemDropManager : MonoBehaviour
{
    // ──────────────────── singleton ───────────────────────────────────────────

    public static ItemDropManager Instance { get; private set; }

    // ──────────────────── Inspector ───────────────────────────────────────────

    [Tooltip("Prefab for the pickup entity.  Must have Rigidbody + DroppedItem.\n" +
             "Leave null to auto-generate a primitive cube at runtime.")]
    public GameObject dropItemPrefab;

    [Tooltip("Reference to the player Inventory.  Auto-found if left null.")]
    public Inventory playerInventory;

    [Tooltip("Reference to the player Transform.  Auto-found if left null.")]
    public Transform playerTransform;

    [Tooltip("Scale of the dropped item cube (visual only).")]
    public float dropScale = 0.4f;

    // ──────────────────── Unity lifecycle ─────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        // Auto-find inventory and player if not assigned in Inspector.
        if (playerInventory == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                playerInventory = player.GetComponentInChildren<Inventory>();
                playerTransform = player.transform;
            }
        }

        // Build a default prefab at runtime if none was assigned.
        if (dropItemPrefab == null)
            dropItemPrefab = BuildDefaultPrefab();
    }

    // ──────────────────── Public API ──────────────────────────────────────────

    /// <summary>
    /// Spawns a world pickup for the given block ID at worldPos.
    /// Call this when a block is destroyed.
    /// </summary>
    public void SpawnDrop(byte blockId, Vector3 worldPos)
    {
        if (blockId == 0) return;                               // air — no drop
        if (blockId >= World.Instance.blocktypes.Length) return;

        BlockType bt = World.Instance.blocktypes[blockId];

        // Resolve drop parameters.
        // Priority: ItemDefinition asset → legacy string fields → block name/icon.
        string dropName;
        Sprite dropIcon;

        if (bt.dropItem != null)
        {
            // ItemDefinition is assigned — use it as the single source of truth.
            // This guarantees the item name matches whatever the crafting recipes expect.
            dropName = bt.dropItem.itemName;
            dropIcon = bt.dropItem.icon != null ? bt.dropItem.icon : bt.icon;
        }
        else
        {
            // Fall back to the legacy per-block string/sprite fields.
            dropName = string.IsNullOrWhiteSpace(bt.dropItemName) ? bt.blockName : bt.dropItemName;
            dropIcon = bt.dropIcon != null ? bt.dropIcon : bt.icon;
        }

        int dropAmount = bt.dropAmount <= 0 ? 1 : bt.dropAmount;

        if (string.IsNullOrWhiteSpace(dropName)) return;       // unnamed block — skip

        // Spawn position: block centre + small upward nudge so it doesn't clip.
        Vector3 spawnPos = worldPos + new Vector3(0.5f, 0.6f, 0.5f);

        GameObject go = Instantiate(dropItemPrefab, spawnPos, Quaternion.identity);
        go.transform.localScale = Vector3.one * dropScale;
        go.name = $"Drop_{dropName}";

        // Tint the renderer with the block icon's average colour if possible.
        ApplyVisual(go, bt, dropIcon);

        // Give it a small random pop so items don't all stack on the same spot.
        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = new Vector3(
                Random.Range(-1.5f, 1.5f),
                Random.Range(2f, 4f),
                Random.Range(-1.5f, 1.5f));
        }

        var droppedItem = go.GetComponent<DroppedItem>();
        if (droppedItem == null) droppedItem = go.AddComponent<DroppedItem>();

        droppedItem.Init(dropName, dropAmount, dropIcon, playerInventory, playerTransform);
    }

    /// <summary>
    /// Spawns a single world pickup directly from an <see cref="ItemDefinition"/>.
    /// Use this for mob drops (and any other non-block source) so the item name
    /// and icon come from the same asset the crafting system uses.
    /// </summary>
    public void SpawnDropFromItem(ItemDefinition item, Vector3 worldPos)
    {
        if (item == null) return;
        if (string.IsNullOrWhiteSpace(item.itemName)) return;

        GameObject go = Instantiate(dropItemPrefab, worldPos, Quaternion.identity);
        go.transform.localScale = Vector3.one * dropScale;
        go.name = $"Drop_{item.itemName}";

        // Tint the pickup cube with the icon's centre pixel if possible.
        ApplyVisualFromSprite(go, item.icon);

        var rb = go.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = new Vector3(
                Random.Range(-1.5f, 1.5f),
                Random.Range(2f, 4f),
                Random.Range(-1.5f, 1.5f));
        }

        var droppedItem = go.GetComponent<DroppedItem>();
        if (droppedItem == null) droppedItem = go.AddComponent<DroppedItem>();

        droppedItem.Init(item.itemName, 1, item.icon, playerInventory, playerTransform);
    }

    private void ApplyVisual(GameObject go, BlockType bt, Sprite icon)
    {
        ApplyVisualFromSprite(go, icon);
    }

    private void ApplyVisualFromSprite(GameObject go, Sprite icon)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;

        // Use a simple Standard material so it looks solid in-world.
        var mat = new Material(Shader.Find("Standard"));

        if (icon != null && icon.texture != null)
        {
            // Sample the centre pixel of the icon for a representative colour.
            Texture2D tex = icon.texture;
            Color avg = Color.white;
            try
            {
                avg = tex.GetPixel(tex.width / 2, tex.height / 2);
            }
            catch { /* texture may not be readable — keep white */ }

            mat.color = avg;
        }
        else
        {
            // Fallback: use a grey so it's clearly a "block" entity.
            mat.color = new Color(0.55f, 0.55f, 0.55f);
        }

        rend.material = mat;
    }

    private static GameObject BuildDefaultPrefab()
    {
        // Build a tiny cube with all required components — used when no prefab is set.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "DroppedItemPrefab_Auto";

        // Remove the MeshCollider added by CreatePrimitive and use a BoxCollider instead
        // (already added by CreatePrimitive, so nothing to do).
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.1f;

        go.AddComponent<DroppedItem>();

        // Don't leave it active in the scene.
        go.SetActive(false);
        DontDestroyOnLoad(go);
        return go;
    }
}