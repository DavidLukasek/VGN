using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance;

    [Header("UI")]
    public GameObject pauseCanvas;
    public GameObject pausePanel;
    public GameObject settingsPanel;

    [Header("Players")]
    public GameObject pcPlayer;
    public GameObject xrOrigin;

    [Header("Guestbook")]
    public GameObject guestBookCanvas;

    [Header("Input block (to prevent click-through)")]
    public float inputBlockDuration = 0.25f;

    bool isPaused;
    bool vrPauseButtonHeld;
    float inputBlockUntil = 0f;

    readonly string[] guestCloseMethodNames = new string[]
    {
        "SubmitButtonClicked", "OnSubmit", "CloseGuestBook", "Close", "Hide", "OnClose", "Submit"
    };

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Update()
    {
        HandlePCPause();
        HandleVRPause();
    }

    void HandlePCPause()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsGuestBookOpen())
            {
                CloseGuestBook();

                inputBlockUntil = Time.time + inputBlockDuration + 0.08f;

                TogglePause();
                return;
            }

            TogglePause();
        }
    }

    void HandleVRPause()
    {
        if (xrOrigin == null || !xrOrigin.activeSelf)
            return;

        var device = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!device.isValid)
            return;

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out bool pressed))
        {
            if (pressed && !vrPauseButtonHeld)
            {
                if (IsGuestBookOpen())
                {
                    CloseGuestBook();

                    inputBlockUntil = Time.time + inputBlockDuration + 0.08f;

                    TogglePause();
                    vrPauseButtonHeld = true;
                    return;
                }

                TogglePause();
                vrPauseButtonHeld = true;
            }
            else if (!pressed)
            {
                vrPauseButtonHeld = false;
            }
        }
    }

    bool IsGuestBookOpen()
    {
        if (guestBookCanvas == null) return false;

        if (guestBookCanvas.activeInHierarchy) return true;

        var cg = guestBookCanvas.GetComponentInChildren<CanvasGroup>();
        if (cg != null)
        {
            if (cg.alpha > 0.01f && cg.interactable) return true;
        }

        return false;
    }

    void CloseGuestBook()
    {
        if (guestBookCanvas == null)
        {
            return;
        }

        bool invoked = TryInvokeGuestbookCloseMethods(guestBookCanvas);
        if (!invoked)
        {
            guestBookCanvas.SetActive(false);
        }

        inputBlockUntil = Time.time + inputBlockDuration;
    }

    bool TryInvokeGuestbookCloseMethods(GameObject gbCanvas)
    {
        var monos = gbCanvas.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in monos)
        {
            if (mb == null) continue;
            Type t = mb.GetType();

            foreach (var methodName in guestCloseMethodNames)
            {
                // hledáme veřejné i neveřejné metody bez parametrů
                var mi = t.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null && mi.GetParameters().Length == 0)
                {
                    try
                    {
                        mi.Invoke(mb, null);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PauseManager] Chyba při volání {methodName} na {t.Name}: {ex.Message}");
                    }
                }
            }
        }

        try
        {
            gbCanvas.SendMessage("SubmitButtonClicked", SendMessageOptions.DontRequireReceiver);
            return true;
        }
        catch
        {
            //ignor
        }

        return false;
    }

    public void TogglePause()
    {
        isPaused = !isPaused;

        if (pauseCanvas != null)
            pauseCanvas.SetActive(isPaused);

        if (pcPlayer != null)
            pcPlayer.SetActive(!isPaused);

        if (xrOrigin != null)
        {
            #pragma warning disable CS0618
            var locomotion = xrOrigin.GetComponent<LocomotionSystem>();
            if (locomotion != null)
                locomotion.enabled = !isPaused;
            #pragma warning restore CS0618
        }

        Cursor.visible = isPaused;
        Cursor.lockState = isPaused ? CursorLockMode.None : CursorLockMode.Locked;

        if (!isPaused)
        {
            inputBlockUntil = Time.time + inputBlockDuration;
            Debug.Log($"[PauseManager] Resumed -> blok vstupu do {inputBlockUntil} (Time.time={Time.time})");
        }
        else
        {
            Debug.Log("[PauseManager] Paused");
        }
    }

    public void Resume()
    {
        UISoundManager.PlayClick();
        if (!isPaused) return;
        inputBlockUntil = Time.time + inputBlockDuration + 0.12f;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        TogglePause();
    }

    public void ExitToMenu()
    {
        UISoundManager.PlayClick();
        isPaused = false;
        SceneManager.LoadScene("MenuScene");
    }

    public void ShowSettings()
    {
        UISoundManager.PlayClick();
        if (pausePanel != null) pausePanel.SetActive(false);
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        UISoundManager.PlayClick();
        if (pausePanel != null) pausePanel.SetActive(true);
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    public bool IsInputBlocked()
    {
        return Time.time < inputBlockUntil;
    }

    public void BlockInputForSeconds(float seconds)
    {
        inputBlockUntil = Mathf.Max(inputBlockUntil, Time.time + seconds);
    }
}
