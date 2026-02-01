using UnityEngine;
using UnityEngine.SceneManagement;

public class LaunchMode : MonoBehaviour
{
    public void StartPCMode() 
    {
        StartMode("PC");
    }

    public void StartVRMode()
    {
        StartMode("VR");
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    private void StartMode(string mode)
    {
        UISoundManager.PlayClick();
        PlayerPrefs.SetString("Mode", mode);
        SceneManager.LoadScene("MainScene");
    }
}
