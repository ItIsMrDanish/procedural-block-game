using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

public class Player : MonoBehaviour {

    public bool isGrounded;
    public bool isSprinting;

    private Transform cam;
    private World world;

    public float walkSpeed = 3f;
    public float sprintSpeed = 6f;
    public float jumpForce = 5f;
    public float gravity = -9.8f;

    public float playerWidth = 0.15f;
    public float boundsTolerance = 0.1f;

    public int orientation;

    private Vector2 moveInput;
    private Vector2 _rawLookInput;
    private Vector2 _smoothedLook;
    private Vector2 _lookVelocity;

    // How quickly look input catches up. 0 = instant, 0.05 = slight smoothing.
    // Tweak in Inspector. Keeps mouse from feeling floaty after stopping.
    public float lookSmoothTime = 0.03f;

    // Tracks cumulative vertical angle so we can clamp it properly.
    // This is what prevents looking upside down.
    private float _cameraPitch = 0f;
    public float maxLookAngle = 89f;

    private Vector3 velocity;
    private float verticalMomentum = 0;
    private bool jumpRequest;

    public Transform highlightBlock;
    public Transform placeBlock;
    public float checkIncrement = 0.1f;
    public float reach = 8f;

    public Toolbar toolbar;
    public byte selectedBlockIndex = 1;

    private InputSystem controls;

    // Pre-allocated to avoid per-frame heap allocations.
    private Vector3 _checkPos = Vector3.zero;
    private Vector3 _cursorPos = Vector3.zero;
    private Vector3 _lastCursorPos = Vector3.zero;

    #region Unity

    private void Awake() {

        controls = new InputSystem();

        controls.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        controls.Player.Move.canceled += _ => moveInput = Vector2.zero;

        // Store raw look delta smoothing applied in ApplyLook().
        controls.Player.Look.performed += ctx => _rawLookInput = ctx.ReadValue<Vector2>();
        controls.Player.Look.canceled += _ => _rawLookInput = Vector2.zero;

        controls.Player.Jump.performed += _ => { if (isGrounded) jumpRequest = true; };
        controls.Player.Sprint.performed += _ => isSprinting = true;
        controls.Player.Sprint.canceled += _ => isSprinting = false;
        controls.Player.Attack.performed += _ => BreakBlock();
        controls.Player.Use.performed += _ => PlaceBlock();
        controls.Player.Inventory.performed += _ => { world.inUI = !world.inUI; };
    }

    private void OnEnable() => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void Start() {

        cam = GameObject.Find("Main Camera").transform;
        world = GameObject.Find("World").GetComponent<World>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        world.inUI = false;

        // Sync pitch tracker with camera's actual starting angle.
        _cameraPitch = cam.localEulerAngles.x;
        if (_cameraPitch > 180f) _cameraPitch -= 360f;
    }

    private void FixedUpdate() {

        if (!world.inUI) {

            CalculateVelocity();

            if (jumpRequest)
                Jump();

            ApplyLook();

            transform.Translate(velocity, Space.World);
        }
    }

    private void Update() {

        if (!world.inUI)
            placeCursorBlocks();

        // Orientation tracking for block placement direction.
        Vector3 XZDirection = transform.forward;
        XZDirection.y = 0;

        if (Vector3.Angle(XZDirection, Vector3.forward) <= 45)
            orientation = 0;
        else if (Vector3.Angle(XZDirection, Vector3.right) <= 45)
            orientation = 5;
        else if (Vector3.Angle(XZDirection, Vector3.back) <= 45)
            orientation = 1;
        else
            orientation = 4;
    }

    #endregion

    #region Look

    private void ApplyLook() {

        float sensitivity = world.settings.mouseSensitivity;

        // SmoothDamp brings look toward the raw input value each frame.
        // When the mouse stops (_rawLookInput = 0), _smoothedLook decays to 0
        // instead of stopping instantly gives a tiny bit of inertia feel.
        // Set lookSmoothTime = 0 in Inspector for instant snap-stop response.
        _smoothedLook.x = Mathf.SmoothDamp(_smoothedLook.x, _rawLookInput.x * sensitivity, ref _lookVelocity.x, lookSmoothTime);

        _smoothedLook.y = Mathf.SmoothDamp(_smoothedLook.y, _rawLookInput.y * sensitivity, ref _lookVelocity.y, lookSmoothTime);

        // Horizontal rotate the whole player body left/right.
        transform.Rotate(Vector3.up * _smoothedLook.x);

        // Vertical accumulate pitch and clamp it hard.
        // This is the fix for looking upside down: we track the angle ourselves
        // and never let it go past maxLookAngle.
        _cameraPitch -= _smoothedLook.y;
        _cameraPitch = Mathf.Clamp(_cameraPitch, -maxLookAngle, maxLookAngle);

        // Set the camera rotation directly from the clamped value.
        // Using localEulerAngles directly avoids any drift from repeated Rotate() calls.
        cam.localEulerAngles = new Vector3(_cameraPitch, 0f, 0f);
    }

    #endregion

    #region Movement

    void Jump() {

        verticalMomentum = jumpForce;
        isGrounded = false;
        jumpRequest = false;
    }

    private void CalculateVelocity() {

        if (verticalMomentum > gravity)
            verticalMomentum += Time.fixedDeltaTime * gravity;

        float speed = isSprinting ? sprintSpeed : walkSpeed;

        velocity = ((transform.forward * moveInput.y) + (transform.right * moveInput.x))
                   * Time.fixedDeltaTime * speed;

        velocity += Vector3.up * verticalMomentum * Time.fixedDeltaTime;

        if ((velocity.z > 0 && front) || (velocity.z < 0 && back))
            velocity.z = 0;
        if ((velocity.x > 0 && right) || (velocity.x < 0 && left))
            velocity.x = 0;

        if (velocity.y < 0)
            velocity.y = checkDownSpeed(velocity.y);
        else if (velocity.y > 0)
            velocity.y = checkUpSpeed(velocity.y);
    }

    #endregion

    #region Block Interaction

    void BreakBlock() {

        if (!world.inUI && highlightBlock.gameObject.activeSelf)
            world.GetChunkFromVector3(highlightBlock.position).EditVoxel(highlightBlock.position, 0);
    }

    void PlaceBlock() {

        if (!world.inUI && highlightBlock.gameObject.activeSelf) {

            if (toolbar.slots[toolbar.slotIndex].HasItem) {
                
                world.GetChunkFromVector3(placeBlock.position).EditVoxel(placeBlock.position, toolbar.slots[toolbar.slotIndex].itemSlot.stack.id);
                toolbar.slots[toolbar.slotIndex].itemSlot.Take(1);
            }
        }
    }

    private void placeCursorBlocks() {

        float step = checkIncrement;

        _lastCursorPos.x = 0; _lastCursorPos.y = 0; _lastCursorPos.z = 0;

        Vector3 camPos = cam.position;
        Vector3 camFwd = cam.forward;

        while (step < reach) {

            _cursorPos.x = camPos.x + camFwd.x * step;
            _cursorPos.y = camPos.y + camFwd.y * step;
            _cursorPos.z = camPos.z + camFwd.z * step;

            if (world.CheckForVoxel(_cursorPos)) {

                highlightBlock.position = new Vector3(Mathf.FloorToInt(_cursorPos.x), Mathf.FloorToInt(_cursorPos.y), Mathf.FloorToInt(_cursorPos.z));
                placeBlock.position = _lastCursorPos;

                highlightBlock.gameObject.SetActive(true);
                placeBlock.gameObject.SetActive(true);
                return;
            }

            _lastCursorPos.x = Mathf.FloorToInt(_cursorPos.x);
            _lastCursorPos.y = Mathf.FloorToInt(_cursorPos.y);
            _lastCursorPos.z = Mathf.FloorToInt(_cursorPos.z);

            step += checkIncrement;
        }

        highlightBlock.gameObject.SetActive(false);
        placeBlock.gameObject.SetActive(false);
    }

    #endregion

    #region Collision Checks

    private float checkDownSpeed(float downSpeed) {

        float targetY = transform.position.y + downSpeed;
        float px = transform.position.x;
        float pz = transform.position.z;
        _checkPos.y = targetY;

        _checkPos.x = px - playerWidth; _checkPos.z = pz - playerWidth;
        if (world.CheckForVoxel(_checkPos)) { isGrounded = true; return 0; }

        _checkPos.x = px + playerWidth; _checkPos.z = pz - playerWidth;
        if (world.CheckForVoxel(_checkPos)) { isGrounded = true; return 0; }

        _checkPos.x = px + playerWidth; _checkPos.z = pz + playerWidth;
        if (world.CheckForVoxel(_checkPos)) { isGrounded = true; return 0; }

        _checkPos.x = px - playerWidth; _checkPos.z = pz + playerWidth;
        if (world.CheckForVoxel(_checkPos)) { isGrounded = true; return 0; }

        isGrounded = false;
        return downSpeed;
    }

    private float checkUpSpeed(float upSpeed) {

        float targetY = transform.position.y + 2f + upSpeed;
        float px = transform.position.x;
        float pz = transform.position.z;
        _checkPos.y = targetY;

        _checkPos.x = px - playerWidth; _checkPos.z = pz - playerWidth;
        if (world.CheckForVoxel(_checkPos)) return 0;

        _checkPos.x = px + playerWidth; _checkPos.z = pz - playerWidth;
        if (world.CheckForVoxel(_checkPos)) return 0;

        _checkPos.x = px + playerWidth; _checkPos.z = pz + playerWidth;
        if (world.CheckForVoxel(_checkPos)) return 0;

        _checkPos.x = px - playerWidth; _checkPos.z = pz + playerWidth;
        if (world.CheckForVoxel(_checkPos)) return 0;

        return upSpeed;
    }

    public bool front {

        get {

            float px = transform.position.x, py = transform.position.y, pz = transform.position.z;
            _checkPos.x = px; _checkPos.z = pz + playerWidth;
            _checkPos.y = py; if (world.CheckForVoxel(_checkPos)) return true;
            _checkPos.y = py + 1f; if (world.CheckForVoxel(_checkPos)) return true;
            return false;
        }
    }

    public bool back {

        get {

            float px = transform.position.x, py = transform.position.y, pz = transform.position.z;
            _checkPos.x = px; _checkPos.z = pz - playerWidth;
            _checkPos.y = py; if (world.CheckForVoxel(_checkPos)) return true;
            _checkPos.y = py + 1f; if (world.CheckForVoxel(_checkPos)) return true;
            return false;
        }
    }

    public bool left {

        get {

            float px = transform.position.x, py = transform.position.y, pz = transform.position.z;
            _checkPos.x = px - playerWidth; _checkPos.z = pz;
            _checkPos.y = py; if (world.CheckForVoxel(_checkPos)) return true;
            _checkPos.y = py + 1f; if (world.CheckForVoxel(_checkPos)) return true;
            return false;
        }
    }

    public bool right {

        get {

            float px = transform.position.x, py = transform.position.y, pz = transform.position.z;
            _checkPos.x = px + playerWidth; _checkPos.z = pz;
            _checkPos.y = py; if (world.CheckForVoxel(_checkPos)) return true;
            _checkPos.y = py + 1f; if (world.CheckForVoxel(_checkPos)) return true;
            return false;
        }
    }

    #endregion
}