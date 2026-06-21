using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace EdgeParty.ConnectionManagement
{
    /// <summary>
    /// A standardized NetworkObjectPool for Unity Netcode for GameObjects.
    /// Handles both server-side retrieval and client-side INetworkPrefabInstanceHandler.
    /// </summary>
    public class NetworkObjectPool : NetworkBehaviour
    {
        public static NetworkObjectPool Singleton { get; private set; }

        [System.Serializable]
        public struct PoolConfig
        {
            public GameObject prefab;
            public int prewarmCount;
        }

        [Header("Prewarm Configuration")]
        [Tooltip("Configure prefabs to instantiate at startup to avoid runtime spikes.")]
        public List<PoolConfig> startupPools = new List<PoolConfig>();

        private Dictionary<GameObject, Queue<NetworkObject>> _pooledObjects = new Dictionary<GameObject, Queue<NetworkObject>>();
        private Dictionary<uint, GameObject> _prefabHashesToPrefabs = new Dictionary<uint, GameObject>();

        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }
            Singleton = this;
            
            // Tự động gán cấu hình mặc định nếu Inspector để trống (4 Bomb, 2 Gun)
            if (startupPools == null || startupPools.Count == 0)
            {
                startupPools = new List<PoolConfig>();
                
                var bombPrefab = Resources.Load<GameObject>("BombBall");
                if (bombPrefab != null) startupPools.Add(new PoolConfig { prefab = bombPrefab, prewarmCount = 4 });
                
                var gunPrefab = Resources.Load<GameObject>("Cosmic_Retro_Blaster_2_5");
                if (gunPrefab != null) startupPools.Add(new PoolConfig { prefab = gunPrefab, prewarmCount = 2 });
            }

            // Prewarm configured objects without spawning them on the network yet
            foreach (var config in startupPools)
            {
                if (config.prefab != null && config.prewarmCount > 0)
                {
                    RegisterPrefab(config.prefab, config.prewarmCount);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            ClearPool();
        }

        /// <summary>
        /// Registers a prefab to be pooled. Must be called before spawning.
        /// </summary>
        public void RegisterPrefab(GameObject prefab, int prewarmCount = 0)
        {
            if (prefab == null) return;
            var netObj = prefab.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"[NetworkObjectPool] Prefab {prefab.name} does not have a NetworkObject component!");
                return;
            }

            if (!_pooledObjects.ContainsKey(prefab))
            {
                _pooledObjects[prefab] = new Queue<NetworkObject>();
                _prefabHashesToPrefabs[netObj.PrefabIdHash] = prefab;

                // Register handler with NGO so clients don't Destroy but instead Return to pool
                NetworkManager.Singleton.PrefabHandler.AddHandler(netObj.PrefabIdHash, new PooledPrefabInstanceHandler(prefab, this));

                // Prewarm
                for (int i = 0; i < prewarmCount; i++)
                {
                    var instance = Instantiate(prefab);
                    instance.SetActive(false);
                    _pooledObjects[prefab].Enqueue(instance.GetComponent<NetworkObject>());
                }
            }
        }

        /// <summary>
        /// Gets a NetworkObject from the pool. Use this INSTEAD of Instantiate() on the Server.
        /// After getting it, you must call .Spawn() on the returned NetworkObject if it's not spawned.
        /// </summary>
        public NetworkObject GetNetworkObject(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!_pooledObjects.ContainsKey(prefab))
            {
                RegisterPrefab(prefab);
            }

            Queue<NetworkObject> queue = _pooledObjects[prefab];
            NetworkObject instance = null;

            while (queue.Count > 0)
            {
                var obj = queue.Dequeue();
                if (obj != null)
                {
                    instance = obj;
                    break;
                }
            }

            if (instance == null)
            {
                var go = Instantiate(prefab);
                instance = go.GetComponent<NetworkObject>();
            }

            instance.transform.position = position;
            instance.transform.rotation = rotation;
            instance.gameObject.SetActive(true);

            return instance;
        }

        /// <summary>
        /// Returns a NetworkObject back to the pool. Use this INSTEAD of Despawn(true) or Destroy().
        /// </summary>
        public void ReturnNetworkObject(NetworkObject networkObject)
        {
            if (networkObject == null) return;

            // If it's spawned, despawn it safely (tell clients to despawn)
            if (networkObject.IsSpawned && IsServer)
            {
                networkObject.Despawn(false); // false = do not destroy gameObject
            }

            networkObject.gameObject.SetActive(false);

            if (_prefabHashesToPrefabs.TryGetValue(networkObject.PrefabIdHash, out GameObject prefab))
            {
                _pooledObjects[prefab].Enqueue(networkObject);
            }
            else
            {
                // Not registered in our pool? Just destroy it.
                Destroy(networkObject.gameObject);
            }
        }

        public void ClearPool()
        {
            foreach (var kvp in _pooledObjects)
            {
                foreach (var obj in kvp.Value)
                {
                    if (obj != null) Destroy(obj.gameObject);
                }
            }
            _pooledObjects.Clear();
            
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.PrefabHandler != null)
            {
                foreach (var hash in _prefabHashesToPrefabs.Keys)
                {
                    NetworkManager.Singleton.PrefabHandler.RemoveHandler(hash);
                }
            }
            _prefabHashesToPrefabs.Clear();
        }

        // --- Inner Handler Class ---
        class PooledPrefabInstanceHandler : INetworkPrefabInstanceHandler
        {
            private GameObject _prefab;
            private NetworkObjectPool _pool;

            public PooledPrefabInstanceHandler(GameObject prefab, NetworkObjectPool pool)
            {
                _prefab = prefab;
                _pool = pool;
            }

            public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
            {
                return _pool.GetNetworkObject(_prefab, position, rotation);
            }

            public void Destroy(NetworkObject networkObject)
            {
                _pool.ReturnNetworkObject(networkObject);
            }
        }
    }
}
