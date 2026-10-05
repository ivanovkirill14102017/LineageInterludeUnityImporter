using L2Viewer.GameServer;
using L2Viewer.GameServer.gameserver;
using L2Viewer.GameServer.gameserver.model;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class L2GameBootstrap : MonoBehaviour
{
    private const string PlayerCharacterPrefabRoot = "Assets/L2Imported/Managed/PlayerCharacterPrefabs";

    private static L2GameBootstrap _instance;
    private static GameObject _cachedDefaultPlayerPrefab;

    public GameObject DefaultPlayerPrefab;
    public Camera TargetCamera;
    public bool ShowRuntimeGui = true;

    private GameServer _server;
    private L2GameCharacterActor _activePlayer;
    private string _status = "Ready";
    private Vector3? _pendingMoveTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrap()
    {
        if (_instance != null || FindFirstObjectByType<L2GameBootstrap>() != null)
        {
            return;
        }

        var root = new GameObject("L2GameServer");
        _instance = root.AddComponent<L2GameBootstrap>();
        DontDestroyOnLoad(root);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        _server = new GameServer(new GameServerConfig());
    }

    private void Update()
    {
        HandleClickToMove();
    }

    private void FixedUpdate()
    {
        if (_server == null)
        {
            return;
        }

        if (_activePlayer != null && _pendingMoveTarget.HasValue)
        {
            _activePlayer.MoveTo(_pendingMoveTarget.Value);
            _pendingMoveTarget = null;
        }

        _server.Tick(Time.fixedDeltaTime);
        _activePlayer?.SyncFromServer(Time.fixedDeltaTime);
    }

    private void OnGUI()
    {
        if (!ShowRuntimeGui || _activePlayer != null)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(16f, 16f, 260f, 112f), GUI.skin.window);
        GUILayout.Label("L2 Single Player");
        if (GUILayout.Button("Create Human Male"))
        {
            CreateHumanMaleUnderCamera();
        }

        GUILayout.Label(_status);
        GUILayout.EndArea();
    }

    public void CreateHumanMaleUnderCamera()
    {
        var camera = ResolveCamera();
        if (camera == null)
        {
            _status = "No camera was found.";
            return;
        }

        var prefab = ResolveDefaultPlayerPrefab();
        if (prefab == null)
        {
            _status = $"Human Male prefab was not found in {PlayerCharacterPrefabRoot}. Import the character archetype first.";
            return;
        }

        var spawnPosition = ResolveSpawnPosition(camera);
        var player = _server.CreateDefaultHumanMale(new Location(spawnPosition.x, spawnPosition.y, spawnPosition.z));
        var instance = Instantiate(prefab, spawnPosition, Quaternion.identity);
        instance.name = "L2Character_Player";

        var actor = instance.GetComponent<L2GameCharacterActor>() ?? instance.AddComponent<L2GameCharacterActor>();
        actor.Bind(player);
        _activePlayer = actor;

        var cameraController = camera.GetComponent<L2ThirdPersonCameraController>() ?? camera.gameObject.AddComponent<L2ThirdPersonCameraController>();
        cameraController.Attach(instance.transform);
        _status = "Player created.";
    }

    private void HandleClickToMove()
    {
        var mouse = Mouse.current;
        if (_activePlayer == null || mouse == null || !mouse.leftButton.wasPressedThisFrame)
        {
            return;
        }

        var camera = ResolveCamera();
        if (camera == null)
        {
            return;
        }

        var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
        {
            _pendingMoveTarget = hit.point;
        }
    }

    private Camera ResolveCamera()
    {
        if (TargetCamera != null)
        {
            return TargetCamera;
        }

        TargetCamera = Camera.main ?? FindFirstObjectByType<Camera>();
        return TargetCamera;
    }

    private Vector3 ResolveSpawnPosition(Camera camera)
    {
        var cameraPosition = camera.transform.position;
        if (Physics.Raycast(cameraPosition, Vector3.down, out var hit, 10000f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * 0.03f;
        }

        return cameraPosition;
    }

    private GameObject ResolveDefaultPlayerPrefab()
    {
        if (DefaultPlayerPrefab != null)
        {
            return DefaultPlayerPrefab;
        }

        if (_cachedDefaultPlayerPrefab != null)
        {
            DefaultPlayerPrefab = _cachedDefaultPlayerPrefab;
            return DefaultPlayerPrefab;
        }

#if UNITY_EDITOR
        if (!AssetDatabase.IsValidFolder(PlayerCharacterPrefabRoot))
        {
            return null;
        }

        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayerCharacterPrefabRoot }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var appearance = prefab != null ? prefab.GetComponent<L2PlayerAppearanceVisual>() : null;
            var archetype = appearance?.Archetype;
            if (archetype == null)
            {
                continue;
            }

            if (archetype.Gender == "Male" &&
                archetype.BaseClass != null &&
                archetype.BaseClass.Contains("Human"))
            {
                DefaultPlayerPrefab = prefab;
                _cachedDefaultPlayerPrefab = prefab;
                return prefab;
            }
        }
#endif

        return null;
    }
}
