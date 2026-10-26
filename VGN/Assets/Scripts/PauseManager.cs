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
    // expose the pause state to camera transitions and painting controls
    public bool IsPaused => isPaused;

    bool vrPauseButtonHeld;
    float inputBlockUntil = 0f;
    bool savedCursorStateStored = false;
    bool savedCursorVisible = false;
    CursorLockMode savedCursorLockState = CursorLockMode.Locked;

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

        if (XRSettings.loadedDeviceName == null || XRSettings.loadedDeviceName == "")
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

        if (isPaused)
        {
            if (!savedCursorStateStored)
            {
                savedCursorVisible = Cursor.visible;
                savedCursorLockState = Cursor.lockState;
                savedCursorStateStored = true;
            }
        }

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

        if (isPaused)
        {
            // unlock the cursor before showing it in the pause menu
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // the player enables before this branch and hides the cursor again
            if (IsGuestBookOpen() || PaintingInteractable.IsInspecting)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (savedCursorStateStored)
            {
                Cursor.lockState = savedCursorLockState;
                Cursor.visible = savedCursorVisible;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // consume the cursor snapshot after returning to the game
            savedCursorStateStored = false;
            inputBlockUntil = Time.time + inputBlockDuration;

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void Resume()
    {
        UISoundManager.PlayClick();
        if (isPaused)
        {
            inputBlockUntil = Time.time + inputBlockDuration + 0.12f;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            TogglePause();
        }
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
        // block scene interactions for the entire pause and shortly after resuming
        return isPaused || Time.time < inputBlockUntil;
    }

    public void BlockInputForSeconds(float seconds)
    {
        inputBlockUntil = Mathf.Max(inputBlockUntil, Time.time + seconds);
    }
}
