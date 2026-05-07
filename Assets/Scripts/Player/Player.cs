using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class Player : MonoBehaviour
{

    public bool isGrounded;
    public bool isSprinting;
    public int orientation;

    [Header("Movement")]
    public float walkSpeed = 4.5f;
    public float sprintSpeed = 7.5f;
    public float jumpForce = 6.5f;

    // Stronger gravity = snappier jumps, less floaty feel.
    public float gravity = -22f;

    // How fast horizontal velocity ramps up / decelerates (units/s²).
    public float groundAccel = 60f;   // Responsive on the ground
    public float groundDecel = 50f;   // Stops quickly when key released
    public float airAccel = 18f;   // Less control mid-air — feels natural

    // Brief window after walking off a ledge where you can still jump.
    public float coyoteTime = 0.12f;

    [Header("Body")]
    // Player is 2 blocks tall. Feet at transform.position, head top at +playerHeight.
    // 1.8 fits through a 2-block gap; raise to 1.95 for a tighter fit.
    public float playerHeight = 1.8f;
    public float playerWidth = 0.25f;  // Half-width of the AABB

    [Header("Look")]
    public float maxLookAngle = 89f;

    [Header("Block Interaction")]
    public Transform highlightBlock;
    public Transform placeBlock;
    public float checkIncrement = 0.1f;
    public float reach = 8f;

    [Header("Block Breaking UI")]
    // Assign a UI Image (set Image Type = Filled, Fill Method = Horizontal) in the Inspector.
    // It will fill left-to-right as the player holds the break button.
    // Leave null to disable the progress bar entirely.
    public Image breakProgressBar;

    [Header("References")]
    public Toolbar toolbar;
    public Inventory inventory;
    public CraftingMenu craftingMenu;

    private Transform _cam;
    private World _world;
    private InputSystem _controls;

    private float _cameraPitch = 0f;   // Accumulated pitch angle, clamped

    private Vector2 _moveInput;
    private Vector2 _lookInput;

    // Separate horizontal and vertical so we can handle them independently.
    private Vector3 _horizontalVelocity;   // XZ movement
    private float _verticalVelocity;     // Y (gravity + jump)

    private float _coyoteTimer;
    private bool _jumpRequest;

    // ── Block-breaking state ──────────────────────────────────────────────────
    // _breakHeld:      true while the Attack button is physically held down.
    // _breakProgress:  how many seconds of damage have been dealt to the current block.
    // _breakTarget:    world-space floor position of the block currently being broken.
    //                  Stored as Vector3Int so we can compare cheaply each frame.
    // _breakTargetSet: guards against comparing an uninitialised _breakTarget.

    private bool _breakHeld = false;
    private float _breakProgress = 0f;
    private Vector3Int _breakTarget = Vector3Int.zero;
    private bool _breakTargetSet = false;
    // ─────────────────────────────────────────────────────────────────────────

    // Unity lifecycle

    private void Awake()
    {

        _controls = new InputSystem();

        _controls.Player.Move.performed += ctx => _moveInput = ctx.ReadValue<Vector2>();
        _controls.Player.Move.canceled += _ => _moveInput = Vector2.zero;

        // Raw mouse delta — smoothing is NOT applied here, it belongs in rendering not input.
        _controls.Player.Look.performed += ctx => _lookInput = ctx.ReadValue<Vector2>();
        _controls.Player.Look.canceled += _ => _lookInput = Vector2.zero;

        _controls.Player.Jump.performed += _ => { if (CanJump()) _jumpRequest = true; };
        _controls.Player.Sprint.performed += _ => isSprinting = true;
        _controls.Player.Sprint.canceled += _ => isSprinting = false;

        // Attack: track held/released rather than single-frame callbacks.
        _controls.Player.Attack.performed += _ => _breakHeld = true;
        _controls.Player.Attack.canceled += _ => OnBreakReleased();

        _controls.Player.Use.performed += _ => PlaceBlock();
        _controls.Player.Inventory.performed += _ => ToggleUI(inventory.ToggleInventory, isInventoryToggle: true);
        _controls.Player.Crafting.performed += _ => ToggleUI(craftingMenu.ToggleMenu, isInventoryToggle: false);
    }

    private void OnDisable() => _controls.Disable();

    private void Start()
    {

        _cam = GameObject.Find("Main Camera").transform;
        _world = GameObject.Find("World").GetComponent<World>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _world.inUI = false;

        // Sync pitch tracker to camera's actual starting angle.
        _cameraPitch = _cam.localEulerAngles.x;
        if (_cameraPitch > 180f) _cameraPitch -= 360f;

        SetBreakBarVisible(false);

        // NOTE: _controls.Enable() is intentionally NOT called here.
        // World.InitWorld() calls _player.EnableControls() after mainCanvas.SetActive(true)
        // so the Input System action states are clean when input first becomes live.
    }

    /// <summary>
    /// Called by World.InitWorld() immediately after the main canvas is activated.
    /// Enabling controls here guarantees no Input System action-phase weirdness caused
    /// by the canvas activation re-triggering OnEnable on child UI components.
    /// </summary>
    public void EnableControls() => _controls.Enable();

    private void Update()
    {

        // Always run movement so gravity and collision keep working in UI.
        // Only skip look and block-interaction when a UI is open.
        UpdateMovement();
        if (!_world.inUI)
        {

            ApplyLook();
            PlaceCursorBlocks();
            PushCameraOutOfBlocks();
            UpdateBlockBreaking();   // ← new: advance break timer each frame
        }

        // Block-placement orientation (which cardinal direction the player faces).
        Vector3 xzFwd = transform.forward;
        xzFwd.y = 0f;
        if (Vector3.Angle(xzFwd, Vector3.forward) <= 45f) orientation = 0;
        else if (Vector3.Angle(xzFwd, Vector3.right) <= 45f) orientation = 5;
        else if (Vector3.Angle(xzFwd, Vector3.back) <= 45f) orientation = 1;
        else orientation = 4;
    }

    // Look

    private void ApplyLook()
    {

        float sens = _world.settings.mouseSensitivity;

        // Horizontal: rotate the whole player body. No smoothing — crisp response.
        transform.Rotate(Vector3.up * _lookInput.x * sens);

        // Vertical: accumulate and hard-clamp so you can never look upside-down.
        _cameraPitch -= _lookInput.y * sens;
        _cameraPitch = Mathf.Clamp(_cameraPitch, -maxLookAngle, maxLookAngle);

        // Write X directly to avoid drift from repeated Rotate() calls.
        // We preserve Y and Z so HealthAndHunger camera tilt (Z) is not clobbered.
        Vector3 angles = _cam.localEulerAngles;
        angles.x = _cameraPitch;
        _cam.localEulerAngles = angles;
    }

    // Camera — prevent seeing through blocks

    // The camera sits at eye-level inside the player.
    // If that voxel is solid (e.g. squeezed into a 2-block gap, or a block placed
    // right next to the player), we nudge the camera down until it is in free air.
    // This completely eliminates the "see through block" glitch.

    private void PushCameraOutOfBlocks()
    {

        Vector3 eyePos = _cam.position;

        if (_world.CheckForVoxel(eyePos))
        {

            // Step downward in 1/8-block increments until we find free space.
            float offset = 0f;
            for (int i = 0; i < 16; i++)
            {

                offset -= 0.125f;
                Vector3 candidate = eyePos + Vector3.up * offset;

                if (!_world.CheckForVoxel(candidate))
                {

                    Vector3 localPos = _cam.localPosition;
                    localPos.y = Mathf.Max(0.1f, localPos.y + offset);
                    _cam.localPosition = localPos;
                    return;
                }
            }

        }
        else
        {

            // Camera is free — restore natural eye height smoothly so it doesn't pop.
            float naturalEye = playerHeight - 0.15f;
            Vector3 localPos = _cam.localPosition;

            if (Mathf.Abs(localPos.y - naturalEye) > 0.001f)
                localPos.y = Mathf.MoveTowards(localPos.y, naturalEye, Time.deltaTime * 10f);

            _cam.localPosition = localPos;
        }
    }

    // Movement — all in Update, axis-separated AABB collision

    private void UpdateMovement()
    {

        float dt = Time.deltaTime;

        // Coyote timer: decays when airborne, resets when grounded.
        if (isGrounded)
            _coyoteTimer = coyoteTime;
        else
            _coyoteTimer -= dt;

        // Gravity: small constant negative when grounded keeps ground detection reliable.
        if (isGrounded && _verticalVelocity < 0f)
        {

            _verticalVelocity = -2f;
        }

        if (_jumpRequest)
        {

            _verticalVelocity = jumpForce;
            _jumpRequest = false;
        }

        _verticalVelocity += gravity * dt;

        // Resolve target horizontal velocity from input.
        Vector3 wishDir = (transform.right * _moveInput.x + transform.forward * _moveInput.y).normalized;
        float targetSpeed = isSprinting ? sprintSpeed : walkSpeed;
        Vector3 targetHVel = wishDir * targetSpeed;

        // Accelerate toward target, decelerate when no input.
        float accel = isGrounded
            ? (wishDir.sqrMagnitude > 0.01f ? groundAccel : groundDecel)
            : airAccel;

        _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, targetHVel, accel * dt);

        // Collision resolution — split axes so we slide along walls.
        float dx = _horizontalVelocity.x * dt;
        float dz = _horizontalVelocity.z * dt;
        float dy = _verticalVelocity * dt;

        if (CheckSideX(dx)) { dx = 0f; _horizontalVelocity.x = 0f; }
        if (CheckSideZ(dz)) { dz = 0f; _horizontalVelocity.z = 0f; }

        if (dy < 0f) dy = CheckDownSpeed(dy);
        else dy = CheckUpSpeed(dy);

        transform.position += new Vector3(dx, dy, dz);
    }

    private float CheckDownSpeed(float downSpeed)
    {

        float targetY = transform.position.y + downSpeed;
        float px = transform.position.x;
        float pz = transform.position.z;
        float w = playerWidth;

        Vector3 p = new Vector3(px - w, targetY, pz - w);
        if (_world.CheckForVoxel(p)) { isGrounded = true; return 0f; }
        p.x = px + w;
        if (_world.CheckForVoxel(p)) { isGrounded = true; return 0f; }
        p.z = pz + w;
        if (_world.CheckForVoxel(p)) { isGrounded = true; return 0f; }
        p.x = px - w;
        if (_world.CheckForVoxel(p)) { isGrounded = true; return 0f; }

        isGrounded = false;
        return downSpeed;
    }

    // Checks the 4 top corners at the target Y (feet + height).
    private float CheckUpSpeed(float upSpeed)
    {

        float targetY = transform.position.y + playerHeight + upSpeed;
        float px = transform.position.x;
        float pz = transform.position.z;
        float w = playerWidth;

        Vector3 p = new Vector3(px - w, targetY, pz - w);
        if (_world.CheckForVoxel(p)) return 0f;
        p.x = px + w;
        if (_world.CheckForVoxel(p)) return 0f;
        p.z = pz + w;
        if (_world.CheckForVoxel(p)) return 0f;
        p.x = px - w;
        if (_world.CheckForVoxel(p)) return 0f;

        return upSpeed;
    }

    // Sweep the player's AABB edge along X by deltaX and check all corner/height probes.
    // Probing the *destination* position (current + delta) means we only block if the
    // player would actually enter a block — grazing a corner along the other axis never
    // triggers this, so the player slides smoothly rather than stopping dead.

    private bool CheckSideX(float deltaX)
    {

        float px = transform.position.x + deltaX;  // candidate X edge
        float py = transform.position.y;
        float pz = transform.position.z;
        float w = playerWidth;
        float edgeX = px + Mathf.Sign(deltaX) * w;  // leading face in X

        // Sample the two Z corners at three heights: feet, mid, just-below-head.
        for (int i = 0; i < 3; i++)
        {

            float h = py + (i == 0 ? 0f : i == 1 ? 1f : playerHeight - 0.1f);
            if (_world.CheckForVoxel(new Vector3(edgeX, h, pz - w))) return true;
            if (_world.CheckForVoxel(new Vector3(edgeX, h, pz + w))) return true;
        }

        return false;
    }

    // Same idea for Z.
    private bool CheckSideZ(float deltaZ)
    {

        float px = transform.position.x;
        float py = transform.position.y;
        float pz = transform.position.z + deltaZ; // candidate Z edge
        float w = playerWidth;
        float edgeZ = pz + Mathf.Sign(deltaZ) * w; // leading face in Z

        for (int i = 0; i < 3; i++)
        {

            float h = py + (i == 0 ? 0f : i == 1 ? 1f : playerHeight - 0.1f);
            if (_world.CheckForVoxel(new Vector3(px - w, h, edgeZ))) return true;
            if (_world.CheckForVoxel(new Vector3(px + w, h, edgeZ))) return true;
        }

        return false;
    }

    // Jump helper

    private bool CanJump()
    {

        return isGrounded || _coyoteTimer > 0f;
    }

    // ── Block breaking (hold-to-break) ────────────────────────────────────────
    //
    // Design:
    //   • blockHealth on BlockType is the number of seconds needed to break
    //     the block.  Set it to 0 (or ≤ 0) for instant-break (air, etc.).
    //   • Each frame we advance _breakProgress by Time.deltaTime while the
    //     attack button is held AND the cursor is on the same block.
    //   • If the player looks away, the target changes → progress resets.
    //   • If the button is released, progress resets.
    //   • When progress >= blockHealth the block is destroyed.

    private void UpdateBlockBreaking()
    {

        // Nothing to do if button not held or no block targeted.
        if (!_breakHeld || !highlightBlock.gameObject.activeSelf)
        {
            ResetBreak();
            return;
        }

        // Compute integer world position of the highlighted block.
        Vector3Int currentTarget = new Vector3Int(
            Mathf.FloorToInt(highlightBlock.position.x),
            Mathf.FloorToInt(highlightBlock.position.y),
            Mathf.FloorToInt(highlightBlock.position.z));

        // If the player has re-aimed at a different block, reset and start fresh.
        if (_breakTargetSet && currentTarget != _breakTarget)
        {
            ResetBreak(showBar: false);
        }

        _breakTarget = currentTarget;
        _breakTargetSet = true;

        // Look up the health of the targeted block.
        Chunk chunk = _world.GetChunkFromVector3(highlightBlock.position);
        if (chunk == null) { ResetBreak(); return; }

        VoxelState voxel = chunk.GetVoxelFromGlobalVector3(highlightBlock.position);
        if (voxel == null) { ResetBreak(); return; }

        float health = _world.blocktypes[voxel.id].blockHealth;

        // Instant-break: health of 0 means no hold required.
        if (health <= 0f)
        {
            SpawnDrop(voxel.id, highlightBlock.position);
            chunk.EditVoxel(highlightBlock.position, 0);
            ResetBreak();
            return;
        }

        // Advance progress.
        _breakProgress += Time.deltaTime;

        // Update progress bar fill (0 → 1).
        float fill = Mathf.Clamp01(_breakProgress / health);
        SetBreakBarFill(fill);
        SetBreakBarVisible(true);

        // Block broken!
        if (_breakProgress >= health)
        {
            SpawnDrop(voxel.id, highlightBlock.position);
            chunk.EditVoxel(highlightBlock.position, 0);
            ResetBreak();
        }
    }

    // Spawns a world drop for the given block at its world position.
    // Routes through ItemDropManager so all drop logic lives in one place.
    private void SpawnDrop(byte blockId, Vector3 blockWorldPos)
    {
        if (ItemDropManager.Instance != null)
            ItemDropManager.Instance.SpawnDrop(blockId, blockWorldPos);
    }

    // Called when the Attack button is released.
    private void OnBreakReleased()
    {
        _breakHeld = false;
        ResetBreak();
    }

    // Resets breaking state and optionally hides the bar immediately.
    private void ResetBreak(bool showBar = false)
    {
        _breakProgress = 0f;
        _breakTargetSet = false;
        if (!showBar) SetBreakBarVisible(false);
    }

    // Progress-bar helpers — safe to call when breakProgressBar is null.
    private void SetBreakBarVisible(bool visible)
    {
        if (breakProgressBar != null)
            breakProgressBar.gameObject.SetActive(visible);
    }

    private void SetBreakBarFill(float fill)
    {
        if (breakProgressBar != null)
            breakProgressBar.fillAmount = fill;
    }

    // ─────────────────────────────────────────────────────────────────────────

    // The InventorySlot currently selected in the hotbar.
    // Kept in sync by Toolbar via SetSelectedItem() every time the selection
    // changes or the inventory contents change.
    private InventorySlot _selectedItem;

    /// <summary>
    /// Called by Toolbar whenever the selected hotbar slot or its contents change.
    /// May receive null when the selected slot is empty.
    /// </summary>
    public void SetSelectedItem(InventorySlot slot) => _selectedItem = slot;

    private void PlaceBlock()
    {

        if (_world.inUI) return;
        if (!highlightBlock.gameObject.activeSelf) return;

        // Nothing selected or the selected slot is empty — can't place.
        if (_selectedItem == null) return;

        // Resolve the item name to a block-type byte ID by searching World.blocktypes.
        // Index 0 is always Air, so a match at 0 is treated as non-placeable.
        byte blockID = 0;
        for (int i = 1; i < _world.blocktypes.Length; i++)
        {
            if (_world.blocktypes[i].blockName == _selectedItem.itemName)
            {
                blockID = (byte)i;
                break;
            }
        }

        // Item name didn't match any placeable block type.
        if (blockID == 0) return;

        // Reject placement if the target block overlaps the player's AABB.
        Vector3 bMin = placeBlock.position; // Block min corner (floor-snapped)
        Vector3 bMax = bMin + Vector3.one;  // Block max corner (1×1×1 voxel)

        float px = transform.position.x;
        float py = transform.position.y;
        float pz = transform.position.z;
        float w = playerWidth + 0.05f;  // Tiny margin so you can place flush against yourself

        bool ox = (px + w) > bMin.x && (px - w) < bMax.x;
        bool oy = (py + playerHeight) > bMin.y && py < bMax.y;
        bool oz = (pz + w) > bMin.z && (pz - w) < bMax.z;

        if (ox && oy && oz) return;  // Block would be inside the player — refuse

        // Place the block and consume one from the inventory stack.
        Chunk chunk = _world.GetChunkFromVector3(placeBlock.position);
        if (chunk != null)
        {
            chunk.EditVoxel(placeBlock.position, blockID);
            inventory.RemoveItem(_selectedItem.itemName, 1);
            // _selectedItem is refreshed automatically:
            // inventory.RemoveItem → OnInventoryChanged → Toolbar.NotifyPlayer → SetSelectedItem
        }
    }

    // Cursor blocks (highlight + place preview)

    private void PlaceCursorBlocks()
    {

        float step = checkIncrement;
        Vector3 camPos = _cam.position;
        Vector3 camFwd = _cam.forward;

        Vector3 lastFloor = Vector3.zero;
        bool hasLast = false;

        while (step < reach)
        {

            Vector3 pos = camPos + camFwd * step;

            if (_world.CheckForVoxel(pos))
            {

                highlightBlock.position = new Vector3(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));

                placeBlock.position = hasLast ? lastFloor : highlightBlock.position;

                highlightBlock.gameObject.SetActive(true);
                placeBlock.gameObject.SetActive(true);
                return;
            }

            lastFloor = new Vector3(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));
            hasLast = true;

            step += checkIncrement;
        }

        highlightBlock.gameObject.SetActive(false);
        placeBlock.gameObject.SetActive(false);
    }

    // UI toggle helper — single place that owns inUI and cursor state.
    //
    // Rules:
    //  • Only one UI can be open at a time.  If the *other* panel is already
    //    open, the key press is silently ignored.
    //  • inUI and cursor state are synced after every successful toggle.
    private void ToggleUI(System.Action panelToggle, bool isInventoryToggle)
    {
        // Mutual exclusion: block open attempts while the other panel is up.
        if (isInventoryToggle && craftingMenu.IsOpen) return;
        if (!isInventoryToggle && inventory.IsOpen) return;

        panelToggle();

        // Derive inUI from actual panel states (both checked in case of edge cases).
        bool anyOpen = inventory.IsOpen || craftingMenu.IsOpen;
        _world.inUI = anyOpen;
        Cursor.lockState = anyOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = anyOpen;
    }

    // Compatibility shim - World.cs accesses _player.orientation directly.
    // The public field above satisfies that. This property lets any code
    // that held a reference to the old "world" field still compile.

    public World world => _world;
}