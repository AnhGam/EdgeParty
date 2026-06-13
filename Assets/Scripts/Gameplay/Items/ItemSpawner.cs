using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using EdgeParty.Gameplay.Items;

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

        RefreshSpawnPoints();
        // Delay trước khi bắt đầu spawn (cho map load xong)
        _nextSpawnTime = Time.time + spawnCheckInterval + 5f;
        _spawnEnabled = true;
        Debug.Log($"[ItemSpawner] Ready with {_spawnPoints.Count} spawn points. First check at t={_nextSpawnTime:F1}s");
    }

    private void Update()
    {
        if (!_spawnEnabled) return;

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

        var spawned = Instantiate(prefab, point.position + Vector3.up * 0.5f, Quaternion.identity);
        spawned.Spawn();
        _activeItemCount++;
        _pointCooldowns[point] = spawnPointCooldown;

        // Tự giảm count khi item despawn
        var tracker = spawned.GetComponent<ItemLifetimeTracker>();
        if (tracker == null) tracker = spawned.gameObject.AddComponent<ItemLifetimeTracker>();
        tracker.OnPickedUp += () => _activeItemCount = Mathf.Max(0, _activeItemCount - 1);

        Debug.Log($"[ItemSpawner] Spawned {prefab.name} at {point.name} | Active: {_activeItemCount}/{maxItemsOnMap}");
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
            if (!_pointCooldowns.ContainsKey(pt) || _pointCooldowns[pt] <= 0f)
                available.Add(pt);
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
        foreach (var go in GameObject.FindGameObjectsWithTag(itemSpawnTag))
            _spawnPoints.Add(go.transform);
        Debug.Log($"[ItemSpawner] Found {_spawnPoints.Count} item spawn points (tag: {itemSpawnTag}).");
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
