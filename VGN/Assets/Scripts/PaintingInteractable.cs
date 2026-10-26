using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
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
    public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float targetFOV = 35f;

    [Header("Inspection")]
    [Min(1f)] public float maxZoomMultiplier = 6f;
    [Min(0.01f)] public float zoomSpeed = 0.18f;

    [Header("Safety")]
    public float interactCooldown = 0.12f;

    // only one painting can own the shared camera and interaction click
    static PaintingInteractable activePainting;

    // let the pause menu restore the cursor for the active painting
    public static bool IsInspecting => activePainting != null && activePainting.isZoomed;

    bool isZoomed;
    bool isTransitioning;
    Transform originalParent;
    Vector3 originalLocalPosition;
    Quaternion originalLocalRotation;
    float originalFieldOfView;
    CursorLockMode originalCursorLockState;
    bool originalCursorVisible;
    Camera paintingCamera;
    Collider paintingCollider;
    Coroutine runningTransition;
    float lastInteractTime = -99f;

    // store a fixed viewing frame so zoom and dragging never rotate the camera
    Plane paintingPlane;
    Vector3 initialViewPosition;
    Quaternion initialViewRotation;
    float initialViewDistance;
    Vector2 initialMinimumPlaneOffset;
    Vector2 initialMaximumPlaneOffset;
    bool isPanning;
    Vector2 previousPointerPosition;

    void Awake()
    {
        paintingCollider = GetComponent<Collider>();
        if (playerCamera != null) paintingCamera = playerCamera.GetComponent<Camera>();
    }

    void OnEnable()
    {
        if (interactAction != null) interactAction.action.performed += OnInteractPerformed;
    }

    void OnDisable()
    {
        if (interactAction != null) interactAction.action.performed -= OnInteractPerformed;
        if (runningTransition != null) StopCoroutine(runningTransition);
        runningTransition = null;

        // inactive paintings must never unlock another painting's player
        if (activePainting == this) RestorePlayerView();
    }

    bool IsInspectionPaused()
    {
        // the detached camera stays active while the player is disabled by pause
        return (PauseManager.Instance != null && PauseManager.Instance.IsPaused)
            || playerController == null || !playerController.isActiveAndEnabled;
    }

    bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    void Update()
    {
        if (activePainting != this || !isZoomed || isTransitioning) return;

        // forget the drag anchor while paused so menu movement cannot cause a jump
        if (IsInspectionPaused()
            || (PauseManager.Instance != null && PauseManager.Instance.IsInputBlocked())
            || Mouse.current == null || !Application.isFocused || IsPointerOverUI())
        {
            isPanning = false;
            return;
        }

        Mouse mouse = Mouse.current;
        Vector2 pointerPosition = mouse.position.ReadValue();
        if (!paintingCamera.pixelRect.Contains(pointerPosition))
        {
            isPanning = false;
            return;
        }

        // the input system normally supplies one scroll unit per wheel step
        float scrollSteps = mouse.scroll.ReadValue().y;
        if (InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange
            && (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer))
        {
            // legacy windows input uses 120 units per wheel step
            scrollSteps /= 120f;
        }
        if (!Mathf.Approximately(scrollSteps, 0f)) ZoomAtPointer(pointerPosition, scrollSteps);

        // dragging is available only when closer than the initial full view
        if (mouse.middleButton.isPressed && GetViewDistance() < initialViewDistance - 0.0001f)
        {
            if (isPanning) PanBetweenPointers(previousPointerPosition, pointerPosition);
            previousPointerPosition = pointerPosition;
            isPanning = true;
        }
        else
        {
            isPanning = false;
        }
    }

    void OnInteractPerformed(InputAction.CallbackContext context)
    {
        // prevent other paintings from entering inspection on the same click
        if (activePainting != null && activePainting != this) return;
        if (IsInspectionPaused() || isTransitioning) return;
        if (PauseManager.Instance != null && PauseManager.Instance.IsInputBlocked()) return;
        if (IsPointerOverUI()) return;
        if (Time.time - lastInteractTime < interactCooldown) return;

        if (isZoomed || IsPlayerLookingAtPicture())
        {
            lastInteractTime = Time.time;
            ToggleZoom();
        }
    }

    bool IsPlayerLookingAtPicture()
    {
        if (playerCamera == null || viewPoint == null || paintingCamera == null) return false;
        if (Vector3.Distance(playerCamera.position, transform.position) > maxInteractDistance) return false;

        // walking mode still selects paintings through the centre of the view
        Ray lookingRay = new Ray(playerCamera.position, playerCamera.forward);
        if (Physics.Raycast(lookingRay, out RaycastHit hit, 100f, interactLayerMask))
        {
            return hit.collider != null && hit.collider.transform.IsChildOf(transform);
        }
        return false;
    }

    void ToggleZoom()
    {
        if (isTransitioning) return;
        isTransitioning = true;

        if (!isZoomed) runningTransition = StartCoroutine(MoveCameraToViewPoint());
        else runningTransition = StartCoroutine(ReturnCamera());
    }

    void InitializeInspectionView()
    {
        initialViewPosition = viewPoint.position;
        initialViewRotation = viewPoint.rotation;

        // use the front of the collider as the flat surface of the painting
        Vector3 planePosition = paintingCollider.ClosestPoint(initialViewPosition);
        paintingPlane = new Plane(viewPoint.forward, planePosition);
        initialViewDistance = Mathf.Max(0.0001f, -paintingPlane.GetDistanceToPoint(initialViewPosition));

        // measure the actual camera projection instead of assuming a particular screen aspect
        Ray bottomLeftRay = paintingCamera.ViewportPointToRay(Vector3.zero);
        Ray topRightRay = paintingCamera.ViewportPointToRay(Vector3.one);
        paintingPlane.Raycast(bottomLeftRay, out float bottomLeftDistance);
        paintingPlane.Raycast(topRightRay, out float topRightDistance);
        Quaternion inverseViewRotation = Quaternion.Inverse(initialViewRotation);
        initialMinimumPlaneOffset = inverseViewRotation * (bottomLeftRay.GetPoint(bottomLeftDistance) - initialViewPosition);
        initialMaximumPlaneOffset = inverseViewRotation * (topRightRay.GetPoint(topRightDistance) - initialViewPosition);
        isPanning = false;
    }

    float GetViewDistance()
    {
        return -paintingPlane.GetDistanceToPoint(playerCamera.position);
    }

    bool TryGetPaintingPoint(Vector2 pointerPosition, out Vector3 paintingPoint)
    {
        // intersect with the painting plane even if the cursor is over its frame
        Ray pointerRay = paintingCamera.ScreenPointToRay(pointerPosition);
        if (paintingPlane.Raycast(pointerRay, out float rayDistance))
        {
            paintingPoint = pointerRay.GetPoint(rayDistance);
            return true;
        }

        paintingPoint = Vector3.zero;
        return false;
    }

    void ZoomAtPointer(Vector2 pointerPosition, float scrollSteps)
    {
        if (!TryGetPaintingPoint(pointerPosition, out Vector3 paintingPoint)) return;

        float currentDistance = GetViewDistance();
        if (currentDistance <= 0f) return;

        // stop before the near clipping plane can cut through the painting
        float minimumDistance = Mathf.Min(initialViewDistance,
            Mathf.Max(initialViewDistance / Mathf.Max(1f, maxZoomMultiplier), paintingCamera.nearClipPlane + 0.02f));
        float requestedDistance = currentDistance * Mathf.Exp(-scrollSteps * Mathf.Max(0.01f, zoomSpeed));
        float newDistance = Mathf.Clamp(requestedDistance, minimumDistance, initialViewDistance);

        // moving along this ray keeps the same painting detail under the cursor
        Vector3 zoomedPosition = paintingPoint
            + (playerCamera.position - paintingPoint) * (newDistance / currentDistance);
        SetInspectionPosition(zoomedPosition, newDistance);
    }

    void PanBetweenPointers(Vector2 previousPosition, Vector2 currentPosition)
    {
        if (!TryGetPaintingPoint(previousPosition, out Vector3 previousPaintingPoint)
            || !TryGetPaintingPoint(currentPosition, out Vector3 currentPaintingPoint)) return;

        // move opposite to the drag so the painting follows the held cursor
        Vector3 pannedPosition = playerCamera.position + previousPaintingPoint - currentPaintingPoint;
        SetInspectionPosition(pannedPosition, GetViewDistance());
    }

    void SetInspectionPosition(Vector3 requestedPosition, float viewDistance)
    {
        Vector3 viewOffset = Quaternion.Inverse(initialViewRotation) * (requestedPosition - initialViewPosition);

        // keep the visible area inside the original view and recenter when fully zoomed out
        float availablePanFraction = Mathf.Clamp01(1f - viewDistance / initialViewDistance);
        viewOffset.x = Mathf.Clamp(viewOffset.x,
            initialMinimumPlaneOffset.x * availablePanFraction, initialMaximumPlaneOffset.x * availablePanFraction);
        viewOffset.y = Mathf.Clamp(viewOffset.y,
            initialMinimumPlaneOffset.y * availablePanFraction, initialMaximumPlaneOffset.y * availablePanFraction);
        viewOffset.z = initialViewDistance - viewDistance;
        playerCamera.SetPositionAndRotation(initialViewPosition + initialViewRotation * viewOffset, initialViewRotation);
    }

    IEnumerator MoveCameraToViewPoint()
    {
        isZoomed = true;
        activePainting = this;

        // save the walking camera and cursor before taking ownership of them
        originalParent = playerCamera.parent;
        originalLocalPosition = playerCamera.localPosition;
        originalLocalRotation = playerCamera.localRotation;
        originalFieldOfView = paintingCamera.fieldOfView;
        originalCursorLockState = Cursor.lockState;
        originalCursorVisible = Cursor.visible;

        playerController.LockMovement(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        playerCamera.SetParent(null, true);

        Vector3 startPosition = playerCamera.position;
        Quaternion startRotation = playerCamera.rotation;
        float startFieldOfView = paintingCamera.fieldOfView;
        float transitionProgress = 0f;

        while (transitionProgress < 1f)
        {
            // preserve the exact transition position and progress throughout pause
            if (IsInspectionPaused())
            {
                yield return null;
                continue;
            }

            transitionProgress += Time.deltaTime / Mathf.Max(0.0001f, transitionTime);
            float transitionAmount = transitionCurve.Evaluate(Mathf.Clamp01(transitionProgress));
            playerCamera.position = Vector3.Lerp(startPosition, viewPoint.position, transitionAmount);
            playerCamera.rotation = Quaternion.Slerp(startRotation, viewPoint.rotation, transitionAmount);
            paintingCamera.fieldOfView = Mathf.Lerp(startFieldOfView, targetFOV, transitionAmount);
            // finish in this frame so pause cannot interrupt between the last step and cleanup
            if (transitionProgress < 1f) yield return null;
        }

        // snap to the full view before enabling wheel and drag input
        playerCamera.SetPositionAndRotation(viewPoint.position, viewPoint.rotation);
        playerCamera.SetParent(viewPoint, true);
        paintingCamera.fieldOfView = targetFOV;
        InitializeInspectionView();
        isTransitioning = false;
        runningTransition = null;
    }

    IEnumerator ReturnCamera()
    {
        isPanning = false;
        playerCamera.SetParent(null, true);

        // return from the current zoom and pan rather than jumping to the full view
        Vector3 startPosition = playerCamera.position;
        Quaternion startRotation = playerCamera.rotation;
        float startFieldOfView = paintingCamera.fieldOfView;
        Vector3 targetWorldPosition = originalParent != null
            ? originalParent.TransformPoint(originalLocalPosition) : originalLocalPosition;
        Quaternion targetWorldRotation = originalParent != null
            ? originalParent.rotation * originalLocalRotation : originalLocalRotation;

        float transitionProgress = 0f;
        while (transitionProgress < 1f)
        {
            if (IsInspectionPaused())
            {
                yield return null;
                continue;
            }

            transitionProgress += Time.deltaTime / Mathf.Max(0.0001f, transitionTime);
            float transitionAmount = transitionCurve.Evaluate(Mathf.Clamp01(transitionProgress));
            playerCamera.position = Vector3.Lerp(startPosition, targetWorldPosition, transitionAmount);
            playerCamera.rotation = Quaternion.Slerp(startRotation, targetWorldRotation, transitionAmount);
            paintingCamera.fieldOfView = Mathf.Lerp(startFieldOfView, originalFieldOfView, transitionAmount);
            if (transitionProgress < 1f) yield return null;
        }

        RestorePlayerView();
        runningTransition = null;
    }

    void RestorePlayerView()
    {
        // restore the walking view only after the inspection or return transition ends
        if (playerCamera != null)
        {
            playerCamera.SetParent(originalParent, false);
            playerCamera.localPosition = originalLocalPosition;
            playerCamera.localRotation = originalLocalRotation;
        }
        if (paintingCamera != null) paintingCamera.fieldOfView = originalFieldOfView;
        if (playerController != null)
        {
            if (playerCamera != null) playerController.SetCameraPitchFromTransform();
            playerController.LockMovement(false);
        }

        // block the exit click from being reused by another painting or the guestbook
        if (PauseManager.Instance != null) PauseManager.Instance.BlockInputForSeconds(interactCooldown);
        Cursor.lockState = originalCursorLockState;
        Cursor.visible = originalCursorVisible;
        isZoomed = false;
        isTransitioning = false;
        isPanning = false;
        activePainting = null;
    }
}
