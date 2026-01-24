using System.Collections;
using UnityEngine;
using UnityEngine.XR.Management;

public class GameStarter : MonoBehaviour
{
    void Start()
    {
        string mode = PlayerPrefs.GetString("Mode", "PC");

        if (mode == "VR") {
            StartCoroutine(StartXR());
        } else {
            //PC - nothing needed
        }
    }

    IEnumerator StartXR()
    {
        XRGeneralSettings.Instance.Manager.InitializeLoader();
        yield return null;
        XRGeneralSettings.Instance.Manager.StartSubsystems();
    }
}
