using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

public class GameStarter : MonoBehaviour
{
    public GameObject playerPC;
    public GameObject xrOrigin;

    IEnumerator Start()
    {
        string mode = PlayerPrefs.GetString("Mode");

        if (playerPC) playerPC.SetActive(false);
        if (xrOrigin) xrOrigin.SetActive(false);

        if (mode == "VR")
        {
            yield return InitializeXR();

            if (xrOrigin) xrOrigin.SetActive(true);
        }
        else
        {
            if (playerPC) playerPC.SetActive(true);
        }
    }

    IEnumerator InitializeXR()
    {
        var xrManager = XRGeneralSettings.Instance.Manager;
        if (xrManager.activeLoader == null)
        {
            yield return xrManager.InitializeLoader();
        }
        if (xrManager.activeLoader != null)
        {
            xrManager.StartSubsystems();
        }
        else
        {
            Debug.LogError("Failed to initialize XR Loader.");
        }
    }

    void OnDisable()
    {
        var xrManager = XRGeneralSettings.Instance.Manager;
        if (xrManager.activeLoader != null)
        {
            xrManager.StopSubsystems();
            xrManager.DeinitializeLoader();
        }
    }
}
