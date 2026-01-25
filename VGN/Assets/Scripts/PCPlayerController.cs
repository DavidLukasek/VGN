using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PCPlayerController : MonoBehaviour
{
    public float moveSpeed = 4f;
    public float gravity = -9.81f;
    public float jumpForce = 5f;
    public float fallMultiplier = 2.5f;
    public float lowJumpMultiplier = 2f;

    public Transform cameraTransform;
    public float mouseSensitivity = 0.1f;
    public float maxLookAngle = 80f;

    CharacterController cc;
    Vector2 moveInput;
    Vector2 lookInput;
    float verticalVelocity;
    float cameraPitch = 0f;
    bool isSprinting;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (cameraTransform == null)
            cameraTransform = Camera.main.transform;
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnLook(InputValue value)
    {
        lookInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (value.isPressed && cc.isGrounded)
            verticalVelocity = jumpForce;
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        float yaw = lookInput.x * mouseSensitivity;
        transform.Rotate(0f, yaw, 0f);

        cameraPitch -= lookInput.y * mouseSensitivity;
        cameraPitch = Mathf.Clamp(cameraPitch, -maxLookAngle, maxLookAngle);
        cameraTransform.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);

        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        if (move.magnitude > 1f) move.Normalize();

        if (cc.isGrounded && verticalVelocity < 0)
            verticalVelocity = -2f;

        if (verticalVelocity > 0)
        {
            verticalVelocity += gravity * Time.deltaTime;
        }
        else
        {
            verticalVelocity += gravity * fallMultiplier * Time.deltaTime;
        }
        
        Vector3 velocity = move * moveSpeed;
        velocity.y = verticalVelocity;

        cc.Move(velocity * Time.deltaTime);
    }
}
