using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance;

    [Header("UI")]
    public GameObject pauseCanvas;

    [Header("Players")]
    public GameObject pcPlayer;
    public GameObject xrOrigin;

    bool isPaused;
    bool vrPauseButtonHeld;

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
        if (pcPlayer != null && pcPlayer.activeSelf)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
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
                TogglePause();
                vrPauseButtonHeld = true;
            }
            else if (!pressed)
            {
                vrPauseButtonHeld = false;
            }
        }
    }

    public void TogglePause()
    {
        isPaused = !isPaused;

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
        Cursor.lockState = isPaused
                         ? CursorLockMode.None
                         : CursorLockMode.Locked;
    }

    public void Resume()
    {
        if (isPaused) TogglePause();
    }

    public void ExitToMenu()
    {
        isPaused = false;
        SceneManager.LoadScene("MenuScene");
    }
}
