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
    private Vector2 lookInput;

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

    #region Unity

    private void Awake() {

        controls = new InputSystem();

        // ===================== MOVEMENT =====================
        controls.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        controls.Player.Move.canceled += _ => moveInput = Vector2.zero;

        controls.Player.Look.performed += ctx => lookInput = ctx.ReadValue<Vector2>();

        controls.Player.Jump.performed += _ => {
            if (isGrounded) jumpRequest = true;
        };

        controls.Player.Sprint.performed += _ => isSprinting = true;
        controls.Player.Sprint.canceled += _ => isSprinting = false;

        // ===================== MOUSE BUTTONS =====================
        controls.Player.Attack.performed += _ => BreakBlock();
        controls.Player.Use.performed += _ => PlaceBlock();

        // ===================== INVENTORY =====================
        controls.Player.Inventory.performed += _ => {
            world.inUI = !world.inUI;
        };
    }

    private void OnEnable() => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void Start() {

        cam = GameObject.Find("Main Camera").transform;
        world = GameObject.Find("World").GetComponent<World>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        world.inUI = false;
    }

    private void FixedUpdate() {

        if (!world.inUI) {

            CalculateVelocity();
            if (jumpRequest)
                Jump();

            transform.Rotate(Vector3.up * lookInput.x * world.settings.mouseSensitivity);
            cam.Rotate(Vector3.right * -lookInput.y * world.settings.mouseSensitivity);
            transform.Translate(velocity, Space.World);
        }
    }

    private void Update() {

        if (!world.inUI) {
            placeCursorBlocks();
        }

        // ===================== ORIENTATION TRACKING =====================
        Vector3 XZDirection = transform.forward;
        XZDirection.y = 0;
        if (Vector3.Angle(XZDirection, Vector3.forward) <= 45)
            orientation = 0;        // South
        else if (Vector3.Angle(XZDirection, Vector3.right) <= 45)
            orientation = 5;        // East
        else if (Vector3.Angle(XZDirection, Vector3.back) <= 45)
            orientation = 1;        // North
        else
            orientation = 4;        // West
    }

    #endregion

    #region Movement

    void Jump() {

        verticalMomentum = jumpForce;
        isGrounded = false;
        jumpRequest = false;
    }

    private void CalculateVelocity() {

        // Affect vertical momentum with gravity.
        if (verticalMomentum > gravity)
            verticalMomentum += Time.fixedDeltaTime * gravity;

        // If we're sprinting, use the sprint multiplier.
        if (isSprinting)
            velocity = ((transform.forward * moveInput.y) + (transform.right * moveInput.x)) * Time.fixedDeltaTime * sprintSpeed;
        else
            velocity = ((transform.forward * moveInput.y) + (transform.right * moveInput.x)) * Time.fixedDeltaTime * walkSpeed;

        // Apply vertical momentum (falling/jumping).
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
        Vector3 lastPos = new Vector3();

        while (step < reach) {

            Vector3 pos = cam.position + (cam.forward * step);

            if (world.CheckForVoxel(pos)) {

                highlightBlock.position = new Vector3(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));
                placeBlock.position = lastPos;

                highlightBlock.gameObject.SetActive(true);
                placeBlock.gameObject.SetActive(true);

                return;
            }

            lastPos = new Vector3(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y), Mathf.FloorToInt(pos.z));

            step += checkIncrement;
        }

        highlightBlock.gameObject.SetActive(false);
        placeBlock.gameObject.SetActive(false);
    }

    #endregion

    #region Collision Checks

    private float checkDownSpeed(float downSpeed) {

        if (
            world.CheckForVoxel(new Vector3(transform.position.x - playerWidth, transform.position.y + downSpeed, transform.position.z - playerWidth)) ||
            world.CheckForVoxel(new Vector3(transform.position.x + playerWidth, transform.position.y + downSpeed, transform.position.z - playerWidth)) ||
            world.CheckForVoxel(new Vector3(transform.position.x + playerWidth, transform.position.y + downSpeed, transform.position.z + playerWidth)) ||
            world.CheckForVoxel(new Vector3(transform.position.x - playerWidth, transform.position.y + downSpeed, transform.position.z + playerWidth))
           ) {

            isGrounded = true;
            return 0;

        } else {

            isGrounded = false;
            return downSpeed;
        }
    }

    private float checkUpSpeed(float upSpeed) {

        if (
            world.CheckForVoxel(new Vector3(transform.position.x - playerWidth, transform.position.y + 2f + upSpeed, transform.position.z - playerWidth)) ||
            world.CheckForVoxel(new Vector3(transform.position.x + playerWidth, transform.position.y + 2f + upSpeed, transform.position.z - playerWidth)) ||
            world.CheckForVoxel(new Vector3(transform.position.x + playerWidth, transform.position.y + 2f + upSpeed, transform.position.z + playerWidth)) ||
            world.CheckForVoxel(new Vector3(transform.position.x - playerWidth, transform.position.y + 2f + upSpeed, transform.position.z + playerWidth))
           ) {

            return 0;

        } else {

            return upSpeed;
        }
    }

    public bool front {

        get {
            if (
                world.CheckForVoxel(new Vector3(transform.position.x, transform.position.y, transform.position.z + playerWidth)) ||
                world.CheckForVoxel(new Vector3(transform.position.x, transform.position.y + 1f, transform.position.z + playerWidth))
                )
                return true;
            else
                return false;
        }
    }

    public bool back {

        get {
            if (
                world.CheckForVoxel(new Vector3(transform.position.x, transform.position.y, transform.position.z - playerWidth)) ||
                world.CheckForVoxel(new Vector3(transform.position.x, transform.position.y + 1f, transform.position.z - playerWidth))
                )
                return true;
            else
                return false;
        }
    }

    public bool left {

        get {
            if (
                world.CheckForVoxel(new Vector3(transform.position.x - playerWidth, transform.position.y, transform.position.z)) ||
                world.CheckForVoxel(new Vector3(transform.position.x - playerWidth, transform.position.y + 1f, transform.position.z))
                )
                return true;
            else
                return false;
        }
    }

    public bool right {

        get {
            if (
                world.CheckForVoxel(new Vector3(transform.position.x + playerWidth, transform.position.y, transform.position.z)) ||
                world.CheckForVoxel(new Vector3(transform.position.x + playerWidth, transform.position.y + 1f, transform.position.z))
                )
                return true;
            else
                return false;
        }
    }

    #endregion
}