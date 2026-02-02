using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // new Input System

/// <summary>
/// Připoj na 3D model knížky (musí mít Collider).
/// Při stisku Interact (LMB) provede raycast a otevře/zavře guestbook canvas.
/// Při otevření dočasně vypne pohybové komponenty (např. tvůj player controller).
/// </summary>
public class BookInteract : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Přiřaď tu Interact action z tvého Player Input (left mouse).")]
    public InputActionReference interactAction;

    [Header("Interaction")]
    [Tooltip("Maximální vzdálenost raycastu od kamery.")]
    public float maxDistance = 2f;
    [Tooltip("Které vrstvy mohou být zasahovány (včetně vrstvy, na které je kniha).")]
    public LayerMask interactLayerMask = ~0;

    public Camera playerCamera;

    [Header("Guestbook UI")]
    [Tooltip("GameObject canvasu/knihy, který se bude zapínat/vypínat.")]
    public GameObject guestbookCanvas;
    [Tooltip("Odkaz na GuestbookManager (použij ho pro refresh nebo jiné volání).")]
    public MonoBehaviour guestbookManager; // může být třída GuestbookManager

    [Header("Disable while open")]
    [Tooltip("Komponenty, které se mají dočasně deaktivovat při otevřené knize (např. PCPlayerController).")]
    public Behaviour[] disableWhileOpen;

    [Tooltip("Volitelně: pokud používáš PlayerInput, můžeš ho sem přiřadit, script ho při otevření deaktivuje.")]
    public UnityEngine.InputSystem.PlayerInput playerInput;

    // interní stavy
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
            guestbookCanvas.SetActive(false); // začneme skrytě
    }

    private void OnInteractPerformed(InputAction.CallbackContext ctx)
    {
        // reagujeme jen na stisk, ne na release / hold
        // (většinou .performed při Click)
        TryToggleIfHit();
    }

    void TryToggleIfHit()
    {
        // Snažíme se získat pozici kurzoru z nového Input Systemu; fallback na Input.mousePosition
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
            // Zkontroluj, zda jsme zasáhli právě tento GameObject (nebo některý z jeho children)
            if (IsHitThisBook(hit.collider))
            {
                ToggleGuestbook();
            }
        }
    }

    bool IsHitThisBook(Collider hitCollider)
    {
        // Pokud máš collider přímo na stejném GameObjectu, stačí porovnat:
        if (hitCollider.gameObject == this.gameObject) return true;

        // Nebo pokud máš kolidéry v children, použij CompareTag nebo IsChildOf:
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
        if (guestbookCanvas == null)
        {
            Debug.LogWarning("[BookInteract] guestbookCanvas není přiřazený.");
            return;
        }

        // uložíme stav kurzoru + komponent
        prevLockState = Cursor.lockState;
        prevCursorVisible = Cursor.visible;

        // odemknout kurzor pro UI interakci
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // deaktivovat movement komponenty, ale zapamatovat jejich původní stav
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

        // volitelně deaktivovat PlayerInput (pokud ho používáš)
        if (playerInput != null) playerInput.enabled = false;

        // aktivovat UI
        guestbookCanvas.SetActive(true);
        isOpen = true;

        // pokud je GuestbookManager, zavoláme refresh metodu (pokud existuje)
        if (guestbookManager != null)
        {
            // pokusíme se volat veřejnou metodu RefreshDoublePageUI() nebo RefreshPageUI() pokud existuje
            var mi = guestbookManager.GetType().GetMethod("RefreshDoublePageUI");
            if (mi == null) mi = guestbookManager.GetType().GetMethod("RefreshPageUI");
            if (mi != null) mi.Invoke(guestbookManager, null);
        }
    }

    void CloseGuestbook()
    {
        if (guestbookCanvas == null) return;

        // schovat UI
        guestbookCanvas.SetActive(false);
        isOpen = false;

        // obnovit kurzor
        Cursor.lockState = prevLockState;
        Cursor.visible = prevCursorVisible;

        // obnovit komponenty
        foreach (var kv in prevEnabled)
        {
            if (kv.Key != null) kv.Key.enabled = kv.Value;
        }

        // obnovit PlayerInput
        if (playerInput != null) playerInput.enabled = true;
    }

    /// <summary>
    /// Volat z UI tlačítka (OnClick) pro zavření knihy.
    /// </summary>
    public void CloseFromUIButton()
    {
        if (isOpen) CloseGuestbook();
    }

    /// <summary>
    /// Volitelně explicitně otevře (např. z jiného UI).
    /// </summary>
    public void OpenFromOther()
    {
        if (!isOpen) OpenGuestbook();
    }
}
