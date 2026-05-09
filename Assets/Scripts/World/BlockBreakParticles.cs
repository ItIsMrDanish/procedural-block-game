using UnityEngine;

/// <summary>
/// Emits small block-textured particles while the player is breaking a block.
///
/// HOW IT WORKS (no atlas/material changes):
///   1. On first use for a block type, this class crops the 16×16 tile for that
///      block's top face out of the packed atlas Texture2D at runtime, producing
///      a tiny private Texture2D per block type.
///   2. A private Material (Unlit/Transparent Cutout) is created from that crop —
///      completely separate from the world materials; nothing shared is touched.
///   3. A ParticleSystem uses that material so each particle looks like a little
///      chip of the block being broken.
///   4. Player.cs calls StartBreaking / StopBreaking / UpdateBreaking each frame.
///
/// SETUP:
///   • Add this component to the Player GameObject (or any persistent object).
///   • Assign the `atlasTex` field to your Packed_Atlas texture asset.
///     (Inspector → Player → Block Break Particles → Atlas Tex)
///   • The atlas texture MUST have Read/Write enabled in its Import Settings.
/// </summary>
public class BlockBreakParticles : MonoBehaviour
{
    [Header("Atlas Reference")]
    [Tooltip("The Packed_Atlas texture (must have Read/Write enabled in Import Settings).")]
    public Texture2D atlasTex;

    [Header("Particle Tuning")]
    [Tooltip("How many particles to emit per second while breaking.")]
    public float emitRate = 12f;

    [Tooltip("Each particle lives this many seconds.")]
    public float particleLifetime = 0.45f;

    [Tooltip("Particles spread outward at this speed.")]
    public float spreadSpeed = 2.8f;

    [Tooltip("Size of each particle in world units.")]
    public float particleSize = 0.12f;

    // ── internals ────────────────────────────────────────────────────────────

    private ParticleSystem _ps;
    private ParticleSystemRenderer _psr;

    // Cache: blockId → Material (owns its own cropped texture, never shared)
    private Material[] _matCache;

    // Current target & emit accumulator
    private Vector3 _targetCenter;
    private float   _emitAccum;
    private bool    _active;
    private byte    _currentBlockId;

    // Shared particle-system Material that we swap the texture on per block type.
    // We keep one Material per block type so the GPU never has to re-upload.

    void Awake()
    {
        BuildParticleSystem();
        _matCache = new Material[256];
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Call each frame while the player is holding the break button on a block.</summary>
    public void UpdateBreaking(byte blockId, Vector3 blockWorldPos, float progressFraction)
    {
        _targetCenter    = blockWorldPos + new Vector3(0.5f, 0.5f, 0.5f);
        _currentBlockId  = blockId;
        _active          = true;

        // Swap material if block type changed mid-break (edge case: shouldn't happen,
        // but safe to handle).
        Material mat = GetOrCreateMaterial(blockId);
        if (_psr.sharedMaterial != mat)
            _psr.sharedMaterial = mat;

        // Accumulate fractional emit count so we're frame-rate independent.
        _emitAccum += emitRate * Time.deltaTime;
        int toEmit = Mathf.FloorToInt(_emitAccum);
        _emitAccum -= toEmit;

        if (toEmit > 0)
            EmitBurst(toEmit);
    }

    /// <summary>Call when breaking stops (button released, block destroyed, aim changed).</summary>
    public void StopBreaking()
    {
        _active    = false;
        _emitAccum = 0f;
    }

    // ── Particle emission ────────────────────────────────────────────────────

    void EmitBurst(int count)
    {
        var emitParams = new ParticleSystem.EmitParams();

        for (int i = 0; i < count; i++)
        {
            // Pick a random point on the unit sphere — gives uniform burst in all directions.
            Vector3 outDir = Random.onUnitSphere;

            // Spawn on the surface of the block in that direction, slightly inset
            // so particles don't clip through adjacent blocks.
            emitParams.position = _targetCenter + outDir * Random.Range(0.1f, 0.45f);

            // Pure outward velocity — no upward bias so above/below/beside all work equally.
            emitParams.velocity = outDir * Random.Range(spreadSpeed * 0.4f, spreadSpeed);

            emitParams.startLifetime = particleLifetime * Random.Range(0.7f, 1.3f);
            emitParams.startSize     = particleSize     * Random.Range(0.7f, 1.3f);

            // Random UV frame within the particle sheet (we're using a single tile,
            // so this just picks a sub-region of the 16×16 crop for variety).
            emitParams.randomSeed = (uint)Random.Range(0, int.MaxValue);

            _ps.Emit(emitParams, 1);
        }
    }

    // ── Material / texture helpers ────────────────────────────────────────────

    Material GetOrCreateMaterial(byte blockId)
    {
        if (_matCache[blockId] != null)
            return _matCache[blockId];

        Texture2D crop = CropBlockTile(blockId);

        // Use Unlit/Transparent so particles are flat colours, not shaded by lights.
        // This shader is always present in Unity.
        Shader shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material mat = new Material(shader);
        mat.mainTexture     = crop;
        mat.color           = Color.white;

        _matCache[blockId] = mat;
        return mat;
    }

    /// <summary>
    /// Crops the top-face texture tile for <paramref name="blockId"/> from the atlas.
    /// Returns a small 16×16 Texture2D owned by this system (not the world atlas).
    /// </summary>
    Texture2D CropBlockTile(byte blockId)
    {
        if (atlasTex == null)
        {
            // Fallback: solid magenta so it's obvious the atlas wasn't assigned.
            var fallback = new Texture2D(4, 4);
            Color[] cols = new Color[16];
            for (int i = 0; i < 16; i++) cols[i] = Color.magenta;
            fallback.SetPixels(cols);
            fallback.Apply();
            return fallback;
        }

        // Resolve which texture atlas tile the top face of this block uses.
        // VoxelData.TextureAtlasSizeInBlocks == 16, blockSize == 16 px.
        int atlasBlocks = VoxelData.TextureAtlasSizeInBlocks; // 16
        int blockPx     = atlasTex.width / atlasBlocks;       // typically 16

        // GetTextureID(2) = top face (face index 2 = Top in VoxelData.faceChecks).
        // blocktypes is accessed through World.Instance — safe at runtime.
        int textureID = 0;
        if (World.Instance != null && blockId < World.Instance.blocktypes.Length)
            textureID = World.Instance.blocktypes[blockId].GetTextureID(2);

        // Convert linear texture ID → atlas column/row.
        int tileX = textureID % atlasBlocks;
        int tileY = textureID / atlasBlocks;

        // Atlas UV origin is top-left for the ID grid, but Texture2D pixels are
        // bottom-left, so flip Y.
        int pixelX = tileX * blockPx;
        int pixelY = atlasTex.height - (tileY + 1) * blockPx;
        pixelY     = Mathf.Clamp(pixelY, 0, atlasTex.height - blockPx);

        Color[] pixels;
        try
        {
            pixels = atlasTex.GetPixels(pixelX, pixelY, blockPx, blockPx);
        }
        catch
        {
            // Atlas not readable — return a grey tile and log a hint.
            Debug.LogWarning("[BlockBreakParticles] Could not read atlas pixels. " +
                             "Enable Read/Write on the Packed_Atlas texture in Import Settings.");
            pixels = new Color[blockPx * blockPx];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.grey;
        }

        Texture2D tile = new Texture2D(blockPx, blockPx, TextureFormat.RGBA32, false);
        tile.filterMode = FilterMode.Point; // Keep pixel art crisp
        tile.SetPixels(pixels);
        tile.Apply();
        return tile;
    }

    // ── ParticleSystem setup ─────────────────────────────────────────────────

    void BuildParticleSystem()
    {
        GameObject go = new GameObject("BlockBreakParticles");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        _ps  = go.AddComponent<ParticleSystem>();
        _psr = go.GetComponent<ParticleSystemRenderer>();

        // Stop the auto-play loop; we drive emission manually via Emit().
        var main = _ps.main;
        main.loop           = false;
        main.playOnAwake    = false;
        main.maxParticles   = 200;
        main.startLifetime  = particleLifetime;
        main.startSpeed     = 0f;          // We set velocity per-particle in EmitParams
        main.startSize      = particleSize;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.8f;       // Particles arc downward naturally

        // No shape — we position each particle manually.
        var shape = _ps.shape;
        shape.enabled = false;

        // Fade out towards end of lifetime.
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[]  { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[]  { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 0.85f) });
        col.color = grad;

        // Shrink slightly over lifetime.
        var size = _ps.sizeOverLifetime;
        size.enabled = true;
        AnimationCurve sizeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.4f);
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Renderer: billboard quads so they always face the camera.
        _psr.renderMode = ParticleSystemRenderMode.Billboard;
        _psr.sortingOrder = 1; // Draw on top of block faces

        // Start it once so the system is warm.
        _ps.Play();
    }
}