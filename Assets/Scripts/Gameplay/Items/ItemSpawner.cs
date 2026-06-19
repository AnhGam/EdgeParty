using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using EdgeParty.Gameplay.Items;
using EdgeParty.ConnectionManagement;
public class ItemSpawner : MonoBehaviour
{
    public static ItemSpawner Instance { get; private set; }

    [Header("Item Prefabs (NetworkObject)")]
    [Tooltip("Prefab BombItem đã đăng ký trong NetworkManager")]
    public NetworkObject bombPrefab;
    [Tooltip("Prefab StunGun đã đăng ký trong NetworkManager")]
    public NetworkObject stunGunPrefab;

    [Header("Spawn Points")]
    [Tooltip("Tag của các spawn point item trong scene")]
    public string itemSpawnTag = "ItemSpawnPoint";

    [Header("Rarity & Timing")]
    [Tooltip("Giây giữa mỗi lần xem xét spawn item (rolling interval)")]
    public float spawnCheckInterval = 12f;
    [Tooltip("Spawn interval giảm xuống khi match đã qua 60s (phase 2)")]
    public float lateGameInterval = 7f;
    [Tooltip("Số giây vào match thì chuyển sang late-game timing")]
    public float lateGameThreshold = 60f;

    [Header("Spawn Budget")]
    [Tooltip("Số item tối đa được phép tồn tại cùng lúc trên map")]
    public int maxItemsOnMap = 2;
    [Tooltip("Cooldown (giây) của mỗi spawn point sau khi bị dùng")]
    public float spawnPointCooldown = 20f;

    [Header("Rarity Weights")]
    [Tooltip("Weight cho BombItem (số càng cao càng hay spawn)")]
    public int bombWeight = 60;
    [Tooltip("Weight cho StunGun (hiếm hơn Bomb)")]
    public int gunWeight = 40;

    // ─── State ────────────────────────────────────────────────────────────
    private List<Transform> _spawnPoints = new List<Transform>();
    private Dictionary<Transform, float> _pointCooldowns = new Dictionary<Transform, float>();
    private Dictionary<Transform, GameObject> _pointOccupants = new Dictionary<Transform, GameObject>();
    private int _activeItemCount = 0;
    private float _nextSpawnTime = 0f;
    private bool _spawnEnabled = false;

    // ─── Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Auto-load prefabs if not assigned in Inspector
        if (bombPrefab == null)
        {
            var go = Resources.Load<GameObject>("BombBall");
            if (go != null) bombPrefab = go.GetComponent<NetworkObject>();
        }
        if (stunGunPrefab == null)
        {
            var go = Resources.Load<GameObject>("Cosmic_Retro_Blaster_2_5");
            if (go != null) stunGunPrefab = go.GetComponent<NetworkObject>();
        }
    }

    private void Start()
    {
        // Delay check until NetworkManager is active and we are confirmed as Server
        StartCoroutine(WaitForServerAndStart());
    }

    private IEnumerator WaitForServerAndStart()
    {
        while (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            yield return new WaitForSeconds(0.5f);
        }

        // Delay trước khi bắt đầu spawn (cho map load xong)
        _nextSpawnTime = Time.time + spawnCheckInterval + 5f;
        _spawnEnabled = true;
        Debug.Log($"[ItemSpawner] Server ready. First spawn check at t={_nextSpawnTime:F1}s");
    }

    private void Update()
    {
        if (!_spawnEnabled) return;

        // Chỉ server mới spawn item
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        // Cập nhật cooldowns
        UpdateCooldowns();

        if (Time.time < _nextSpawnTime) return;

        // Tính interval theo pha match
        float matchTime = ForestGameManager.Instance != null
            ? Time.time - ForestGameManager.Instance.MatchStartTime
            : 0f;

        float interval = matchTime >= lateGameThreshold ? lateGameInterval : spawnCheckInterval;
        _nextSpawnTime = Time.time + interval;

        TrySpawnItem();
    }

    // ─── Spawn Logic ──────────────────────────────────────────────────────

    private void TrySpawnItem()
    {
        if (_spawnPoints == null || _spawnPoints.Count == 0)
        {
            RefreshSpawnPoints();
            if (_spawnPoints == null || _spawnPoints.Count == 0)
            {
                Debug.LogWarning("[ItemSpawner] No ItemSpawnPoint tags found in scene! Generating fallback spawn points...");
                GenerateFallbackSpawnPoints();
            }
        }

        if (_activeItemCount >= maxItemsOnMap)
        {
            Debug.Log($"[ItemSpawner] Skipped spawn — max items on map ({maxItemsOnMap}) reached.");
            return;
        }

        Transform point = GetAvailableSpawnPoint();
        if (point == null)
        {
            Debug.Log("[ItemSpawner] No available spawn points (all on cooldown).");
            return;
        }

        // Weighted random chọn item type
        NetworkObject prefab = PickItemByWeight();
        if (prefab == null) return;

        if (NetworkObjectPool.Singleton != null)
        {
            var spawned = NetworkObjectPool.Singleton.GetNetworkObject(prefab.gameObject, point.position + Vector3.up * 0.5f, Quaternion.identity);
            if (!spawned.IsSpawned) spawned.Spawn();
            _activeItemCount++;
            _pointCooldowns[point] = spawnPointCooldown;
            _pointOccupants[point] = spawned.gameObject;

            // Tự giảm count khi item despawn
            var tracker = spawned.GetComponent<ItemLifetimeTracker>();
            if (tracker == null) tracker = spawned.gameObject.AddComponent<ItemLifetimeTracker>();
            
            // Clean up old listener to avoid double-decrement if pulled from pool
            tracker.OnPickedUp = null; 
            tracker.OnPickedUp += () => _activeItemCount = Mathf.Max(0, _activeItemCount - 1);

            Debug.Log($"[ItemSpawner] Pooled Spawn {prefab.name} at {point.name} | Active: {_activeItemCount}/{maxItemsOnMap}");
        }
        else
        {
            // Fallback nếu chưa khởi tạo Pool
            var spawned = Instantiate(prefab, point.position + Vector3.up * 0.5f, Quaternion.identity);
            spawned.Spawn();
            _activeItemCount++;
            _pointCooldowns[point] = spawnPointCooldown;
            _pointOccupants[point] = spawned.gameObject;

            var tracker = spawned.GetComponent<ItemLifetimeTracker>();
            if (tracker == null) tracker = spawned.gameObject.AddComponent<ItemLifetimeTracker>();
            tracker.OnPickedUp += () => _activeItemCount = Mathf.Max(0, _activeItemCount - 1);

            Debug.Log($"[ItemSpawner] Instantiate Spawn {prefab.name} at {point.name} | Active: {_activeItemCount}/{maxItemsOnMap}");
        }
    }

    private NetworkObject PickItemByWeight()
    {
        int total = bombWeight + gunWeight;
        int roll = Random.Range(0, total);
        if (roll < bombWeight) return bombPrefab;
        return stunGunPrefab;
    }

    private Transform GetAvailableSpawnPoint()
    {
        List<Transform> available = new List<Transform>();
        foreach (var pt in _spawnPoints)
        {
            bool isOccupied = _pointOccupants.ContainsKey(pt) && _pointOccupants[pt] != null;
            bool isCoolingDown = _pointCooldowns.ContainsKey(pt) && _pointCooldowns[pt] > 0f;

            if (!isOccupied && !isCoolingDown)
            {
                available.Add(pt);
            }
        }
        if (available.Count == 0) return null;
        return available[Random.Range(0, available.Count)];
    }

    private void UpdateCooldowns()
    {
        var keys = new List<Transform>(_pointCooldowns.Keys);
        foreach (var k in keys)
        {
            _pointCooldowns[k] -= Time.deltaTime;
            if (_pointCooldowns[k] < 0f) _pointCooldowns[k] = 0f;
        }
    }

    private void RefreshSpawnPoints()
    {
        _spawnPoints.Clear();
        
        try {
            foreach (var go in GameObject.FindGameObjectsWithTag("ItemSpawnPoint"))
                _spawnPoints.Add(go.transform);
        } catch (System.Exception e) {
            Debug.LogError($"[ItemSpawner] Tag ItemSpawnPoint error: {e.Message}");
        }

        if (_spawnPoints.Count == 0)
        {
            try {
                foreach (var go in GameObject.FindGameObjectsWithTag("ItemSpawn"))
                    _spawnPoints.Add(go.transform);
            } catch {}
        }
        
        if (_spawnPoints.Count == 0)
        {
            // Fallback: Find by object name just in case the tag was not defined but the object was named correctly
            foreach (var obj in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (obj.name.Contains("ItemSpawnPoint") || obj.name.Contains("ItemSpawn"))
                {
                    _spawnPoints.Add(obj.transform);
                }
            }
        }
        
        Debug.Log($"[ItemSpawner] Found {_spawnPoints.Count} item spawn points.");
    }

    /// <summary>
    /// Tự generate các spawn point trên map nếu không tìm thấy tagged objects.
    /// Tạo grid 3x3 quanh tâm map (y=1) để item có chỗ xuất hiện.
    /// </summary>
    private void GenerateFallbackSpawnPoints()
    {
        // Tạo 9 spawn points theo grid 3x3, cách nhau 10 units
        int[] gridX = { -10, 0, 10 };
        int[] gridZ = { -10, 0, 10 };
        float groundY = 1f;

        for (int xi = 0; xi < gridX.Length; xi++)
        {
            for (int zi = 0; zi < gridZ.Length; zi++)
            {
                var go = new GameObject($"ItemSpawnPoint_Fallback_{xi}_{zi}");
                go.transform.position = new Vector3(gridX[xi], groundY, gridZ[zi]);
                _spawnPoints.Add(go.transform);
            }
        }
        Debug.Log($"[ItemSpawner] Generated {_spawnPoints.Count} fallback spawn points.");
    }
}

public class ItemLifetimeTracker : MonoBehaviour
{
    public System.Action OnPickedUp;

    private void OnDisable()
    {
        OnPickedUp?.Invoke();
    }
}
