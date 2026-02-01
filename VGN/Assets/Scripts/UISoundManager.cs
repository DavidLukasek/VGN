using UnityEngine;

public class UISoundManager : MonoBehaviour
{
    public static UISoundManager Instance;

    [Header("Audio")]
    public AudioSource source;

    public AudioClip hoverSound;
    public AudioClip clickSound;
    public AudioClip helpSound;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static void PlayHover()
    {
        if (Instance == null) return;
        Instance.source.PlayOneShot(Instance.hoverSound);
    }

    public static void PlayClick()
    {
        if (Instance == null) return;
        Instance.source.PlayOneShot(Instance.clickSound);
    }

    public static void PlayHelp()
    {
        if (Instance == null) return;
        Instance.source.PlayOneShot(Instance.helpSound);
    }
}
