#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Discord;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class UERP
{
    private const string applicationId = "1474131169242124318";
    private static Discord.Discord discord;

    private static long editorStartTimestamp;
    private static long playStartTimestamp;
    private static bool playMode = false;
    private static bool initialized = false;

    static UERP()
    {
        _ = DelayStart();
    }

    public static async Task DelayStart(int delay = 1000)
    {
        await Task.Delay(delay);
        if (DiscordRunning())
            Init();
    }

    public static void Init()
    {
        if (initialized) return;
        initialized = true;

        try
        {
            discord = new Discord.Discord(long.Parse(applicationId), (long)CreateFlags.Default);
        }
        catch (Exception e)
        {
            Debug.LogError(e.ToString());
            initialized = false;
            return;
        }

        var elapsed = TimeSpan.FromSeconds(EditorAnalyticsSessionInfo.elapsedTime);

        editorStartTimestamp = DateTimeOffset.Now.Subtract(elapsed).ToUnixTimeSeconds();
        playStartTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

        playMode = EditorApplication.isPlaying;

        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += PlayModeChanged;

        EditorSceneManager.activeSceneChangedInEditMode += (_, __) => UpdateActivity();
        EditorSceneManager.sceneOpened += (_, __) => UpdateActivity();

        UpdateActivity();
    }

    private static void Update()
    {
        if (discord == null)
        {
            return;
        }

        try
        {
            discord.RunCallbacks();
        }
        catch (Exception e)
        {
            Debug.LogError(e);
        }
    }

    private static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            playStartTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            UpdateActivity();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            UpdateActivity();
        }
    }

    public static void UpdateActivity()
    {
        if (discord == null)
        {
            initialized = false;
            if (DiscordRunning()) Init();
            return;
        }

        bool isPlaying = EditorApplication.isPlaying;
        string sceneName = EditorSceneManager.GetActiveScene().name;

        string details = $"Workspace {Application.productName}";
        string state   = $"Editing {sceneName}";   

        string largeText = "Unity " + Application.unityVersion;
        string smallText = isPlaying ? "Play mode" : "Edit mode";

        string largeImageKey = "unity-icon";
        string smallImageKey = isPlaying ? "play-mode" : "edit-mode";

        var activity = new Activity
        {
            Details = details,
            State = state,
            Assets =
            {
                LargeImage = largeImageKey,
                LargeText  = largeText,
                SmallImage = smallImageKey,
                SmallText  = smallText,
            },
        };
        
        if (isPlaying)
        {
            activity.Timestamps = new ActivityTimestamps { Start = playStartTimestamp };
        }

        discord.GetActivityManager().UpdateActivity(activity, result =>
        {
            if (result != Result.Ok)
                Debug.LogError(result.ToString());
        });
    }

    private static bool DiscordRunning()
    {
        Process[] processes = Process.GetProcessesByName("Discord");
        if (processes.Length == 0) processes = Process.GetProcessesByName("DiscordPTB");
        if (processes.Length == 0) processes = Process.GetProcessesByName("DiscordCanary");
        return processes.Length != 0;
    }
}
#endif
