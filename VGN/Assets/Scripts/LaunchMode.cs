using UnityEngine;
using UnityEngine.SceneManagement;

public class LaunchMode : MonoBehaviour
{
    void Start()
    {
        // restore the cursor after gameplay objects finish unloading
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

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
