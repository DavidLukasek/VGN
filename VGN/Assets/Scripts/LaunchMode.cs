using UnityEngine;
using UnityEngine.SceneManagement;

public class LaunchMode : MonoBehaviour
{
    public void StartPCMode() {
        PlayerPrefs.SetString("Mode", "PC");
        SceneManager.LoadScene("MainScene");
    }

    public void StartVRMode() {
        PlayerPrefs.SetString("Mode", "VR");
        SceneManager.LoadScene("MainScene");
    }
}
