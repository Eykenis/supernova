using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Rebuilds the ambient environment probe whenever a new scene becomes active.
/// This keeps scenes that rely on ambient lighting visually consistent whether
/// they are the startup scene or loaded from the main menu.
/// </summary>
public static class SceneEnvironmentRefresher
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= RefreshEnvironment;
        SceneManager.sceneLoaded += RefreshEnvironment;
    }

    private static void RefreshEnvironment(Scene scene, LoadSceneMode mode)
    {
        DynamicGI.UpdateEnvironment();
    }
}
