using UnityEngine;

public class GraphicsMenu : MonoBehaviour
{
    public void SetQuality(int index)
    {
        QualitySettings.SetQualityLevel(index, true);
        PlayerPrefs.SetInt("quality", index);
    }
    
    void Start()
    {
        if (PlayerPrefs.HasKey("quality"))
        {
            int q = PlayerPrefs.GetInt("quality");
            QualitySettings.SetQualityLevel(q, true);
        }
        else
        {
            QualitySettings.SetQualityLevel(0, true);
        }
    }
}
