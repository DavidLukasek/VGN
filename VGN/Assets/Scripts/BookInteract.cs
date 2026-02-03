using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class BookInteract : MonoBehaviour
{
    [Header("Input")]
    public InputActionReference interactAction;
    [Header("Interaction")]
    public float maxDistance = 2f;
    public LayerMask interactLayerMask = ~0;
    public Camera playerCamera;
    [Header("Guestbook UI")]
    public GameObject guestbookCanvas;
    public MonoBehaviour guestbookManager;
    [Header("Disable while open")]
    public Behaviour[] disableWhileOpen;
    public UnityEngine.InputSystem.PlayerInput playerInput;

    bool isOpen = false;
    Dictionary<Behaviour, bool> prevEnabled = new Dictionary<Behaviour, bool>();
    CursorLockMode prevLockState;
    bool prevCursorVisible;

    void OnEnable()
    {
        if (interactAction != null && interactAction.action != null)
            interactAction.action.performed += OnInteractPerformed;
    }

    void OnDisable()
    {
        if (interactAction != null && interactAction.action != null)
            interactAction.action.performed -= OnInteractPerformed;
    }

    void Start()
    {
        if (guestbookCanvas != null)
            guestbookCanvas.SetActive(false);
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        if (PauseManager.Instance != null && PauseManager.Instance.IsInputBlocked()) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        TryToggleIfHit();
    }

    void TryToggleIfHit()
    {
        if (PauseManager.Instance != null && PauseManager.Instance.IsInputBlocked()) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Vector2 screenPos;
        try
        {
            if (Mouse.current != null)
                screenPos = Mouse.current.position.ReadValue();
            else
                screenPos = Input.mousePosition;
        }
        catch
        {
            screenPos = Input.mousePosition;
        }

        Ray ray = playerCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, interactLayerMask))
        {
            if (IsHitThisBook(hit.collider))
            {
                ToggleGuestbook();
            }
        }
    }

    bool IsHitThisBook(Collider hitCollider)
    {
        if (hitCollider.gameObject == this.gameObject) return true;

        if (hitCollider.transform.IsChildOf(this.transform)) return true;

        return false;
    }

    void ToggleGuestbook()
    {
        if (!isOpen) OpenGuestbook();
        else CloseGuestbook();
    }

    void OpenGuestbook()
    {
        prevLockState = Cursor.lockState;
        prevCursorVisible = Cursor.visible;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        prevEnabled.Clear();
        if (disableWhileOpen != null)
        {
            foreach (var comp in disableWhileOpen)
            {
                if (comp == null) continue;
                prevEnabled[comp] = comp.enabled;
                comp.enabled = false;
            }
        }

        if (playerInput != null) playerInput.enabled = false;

        guestbookCanvas.SetActive(true);
        isOpen = true;

        if (guestbookManager != null)
        {
            var mi = guestbookManager.GetType().GetMethod("RefreshDoublePageUI");
            if (mi == null) mi = guestbookManager.GetType().GetMethod("RefreshPageUI");
            if (mi != null) mi.Invoke(guestbookManager, null);
        }
    }

    void CloseGuestbook()
    {
        if (guestbookCanvas == null) return;

        guestbookCanvas.SetActive(false);
        isOpen = false;

        Cursor.lockState = prevLockState;
        Cursor.visible = prevCursorVisible;

        foreach (var kv in prevEnabled)
        {
            if (kv.Key != null) kv.Key.enabled = kv.Value;
        }

        if (playerInput != null) playerInput.enabled = true;
    }

    public void CloseFromUIButton()
    {
        if (isOpen) CloseGuestbook();
    }

    public void OpenFromOther()
    {
        if (!isOpen) OpenGuestbook();
    }
}
