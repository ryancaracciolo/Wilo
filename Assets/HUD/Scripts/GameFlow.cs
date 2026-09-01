using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The porch is the title screen. A session only goes to the water after the
/// player picks a saved lake or finishes a new one.
/// </summary>
public static class GameFlow
{
    public const string IntroScene = "Intro";
    public const string LakeScene = "WiloLake";

    static bool sessionOnLake;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Route()
    {
        if (sessionOnLake)
            return;

        string scene = SceneManager.GetActiveScene().name;
        if (scene == LakeScene)
            SceneManager.LoadScene(IntroScene);
    }

    public static void ContinueToLake()
    {
        sessionOnLake = true;
        SceneManager.LoadScene(LakeScene);
    }
}
