using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SharedSkySceneLoader
{
    public const string ScenePath = "Assets/Scenes/SharedSky.unity";
    private const string SceneName = "SharedSky";
    private static bool _loading;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureLoaded(SceneManager.GetActiveScene());
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureLoaded(scene);

    private static void EnsureLoaded(Scene scene)
    {
        if (scene.name == SceneName && SceneManager.GetActiveScene().handle == scene.handle)
        {
            if (Camera.main == null)
            {
                var preview = new GameObject("SharedSkyRuntimePreviewCamera");
                preview.tag = "MainCamera";
                preview.AddComponent<Camera>();
            }
            return;
        }
        if (scene.name != "MainWorkspace" && scene.name != "OutdoorsScene" && scene.name != "SampleScene")
            return;
        if (_loading || SceneManager.GetSceneByName(SceneName).isLoaded)
            return;

        var operation = SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Additive);
        if (operation == null)
            throw new InvalidOperationException($"Sky scene could not be loaded: {ScenePath}");
        _loading = true;
        operation.completed += _ => _loading = false;
    }
}
