using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Collider))]
public class PaintingInteractable : MonoBehaviour
{
    [Header("References")]
    public Transform viewPoint;
    public Transform playerCamera;
    public InputActionReference interactAction;
    public PCPlayerController playerController;

    [Header("Interaction")]
    public float maxInteractDistance = 3f;
    public LayerMask interactLayerMask = ~0;

    [Header("Transition")]
    public float transitionTime = 0.6f;
    public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0,0,1,1);
    public float targetFOV = 35f;

    [Header("Safety")]
    public float interactCooldown = 0.12f;

    bool isZoomed = false;
    Transform originalParent;
    Vector3 origLocalPos;
    Quaternion origLocalRot;
    float origFOV;
    Camera cam;
    Coroutine running;
    float lastInteractTime = -99f;

    void Awake()
    {
        cam = playerCamera.GetComponent<Camera>();
    }

    void OnEnable()
    {
        if (interactAction != null) interactAction.action.performed += OnInteractPerformed;
    }

    void OnDisable()
    {
        if (interactAction != null) interactAction.action.performed -= OnInteractPerformed;
        if (playerController != null) playerController.LockMovement(false);
        if (running != null) StopCoroutine(running);
        running = null;
    }

    void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        if (Time.time - lastInteractTime < interactCooldown) return;
        lastInteractTime = Time.time;

        if (isZoomed)
        {
            ToggleZoom();
            return;
        }

        if (IsPlayerLookingAtPicture())
        {
            ToggleZoom();
        }
    }

    bool IsPlayerLookingAtPicture()
    {
        if (Vector3.Distance(playerCamera.position, transform.position) > maxInteractDistance) return false;

        Ray r = new Ray(playerCamera.position, playerCamera.forward);
        if (Physics.Raycast(r, out RaycastHit hit, 100f, interactLayerMask))
        {
            if (hit.collider != null && hit.collider.transform.IsChildOf(transform))
                return true;
        }
        return false;
    }

    void ToggleZoom()
    {
        if (running != null) return;

        if (!isZoomed) running = StartCoroutine(MoveCameraToViewPoint());
        else running = StartCoroutine(ReturnCamera());
    }

    IEnumerator MoveCameraToViewPoint()
    {
        isZoomed = true;

        originalParent = playerCamera.parent;
        origLocalPos = playerCamera.localPosition;
        origLocalRot = playerCamera.localRotation;
        origFOV = cam != null ? cam.fieldOfView : 60f;

        playerController.LockMovement(true);

        playerCamera.SetParent(null, true);

        Vector3 startPos = playerCamera.position;
        Quaternion startRot = playerCamera.rotation;
        float startFOV = cam != null ? cam.fieldOfView : 60f;
        float endFOV = targetFOV;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, transitionTime);
            float k = transitionCurve.Evaluate(t);
            playerCamera.position = Vector3.Lerp(startPos, viewPoint.position, k);
            playerCamera.rotation = Quaternion.Slerp(startRot, viewPoint.rotation, k);
            if (cam != null) cam.fieldOfView = Mathf.Lerp(startFOV, endFOV, k);
            yield return null;
        }

        playerCamera.SetParent(viewPoint, true);
        playerController.SetCameraPitchFromTransform();

        running = null;
    }

    IEnumerator ReturnCamera()
    {
        isZoomed = false;

        playerCamera.SetParent(null, true);

        Vector3 startPos = playerCamera.position;
        Quaternion startRot = playerCamera.rotation;
        float startFOV = cam != null ? cam.fieldOfView : targetFOV;
        float endFOV = origFOV;

        Vector3 targetWorldPos;
        Quaternion targetWorldRot;
        if (originalParent != null)
        {
            targetWorldPos = originalParent.TransformPoint(origLocalPos);
            targetWorldRot = originalParent.rotation * origLocalRot;
        }
        else
        {
            targetWorldPos = origLocalPos;
            targetWorldRot = origLocalRot;
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.0001f, transitionTime);
            float k = transitionCurve.Evaluate(t);
            playerCamera.position = Vector3.Lerp(startPos, targetWorldPos, k);
            playerCamera.rotation = Quaternion.Slerp(startRot, targetWorldRot, k);
            if (cam != null) cam.fieldOfView = Mathf.Lerp(startFOV, endFOV, k);
            yield return null;
        }

        playerCamera.SetParent(originalParent, true);
        playerCamera.localPosition = origLocalPos;
        playerCamera.localRotation = origLocalRot;
        if (cam != null) cam.fieldOfView = origFOV;

        playerController.SetCameraPitchFromTransform();

        playerController.LockMovement(false);

        running = null;
    }
}
