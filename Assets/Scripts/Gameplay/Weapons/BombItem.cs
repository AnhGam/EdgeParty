using UnityEngine;
using Unity.Netcode;
using EdgeParty.Gameplay.Character;

namespace EdgeParty.Gameplay.Items
{
    public class BombItem : NetworkBehaviour
    {
        [Header("Explosion Settings")]
        public float explosionRadius = 5f;
        public float knockbackForce = 100f;
        public float fuseTime = 3.5f;       // Thời gian nổ sau khi ném (default to 3.5s)

        [Header("VFX / SFX")]
        [Tooltip("Kéo Explosion VFX Prefab vào — hoặc để trống để dùng built-in particle")]
        public GameObject explosionVFXPrefab;
        [Tooltip("Kéo Assets/Scripts/Audio/Audios/EXPLOSION_sfx.wav vào đây")]
        public AudioClip explosionSFX;

        public NetworkVariable<bool> isPickup = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<Vector3> _netPosition = new NetworkVariable<Vector3>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Quaternion> _netRotation = new NetworkVariable<Quaternion>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private bool _hasExploded = false;
        private bool _isThrown = false;
        private float _timer = 0f;
        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            // Auto-load SFX nếu không gán trong Inspector
            if (explosionSFX == null)
                explosionSFX = Resources.Load<AudioClip>("Audios/EXPLOSION_sfx");
        }

        public override void OnNetworkSpawn()
        {
            _hasExploded = false;
            _isThrown = false;
            _timer = 0f;
            if (_rb != null) _rb.linearVelocity = Vector3.zero;

            if (IsServer)
            {
                isPickup.Value = true; // Default to pickup when pulled from pool
                _netPosition.Value = transform.position;
                _netRotation.Value = transform.rotation;
            }

            isPickup.OnValueChanged += OnPickupStateChanged;
            if (isPickup.Value) SetupAsPickup();
            else SetupAsProjectile();
        }

        public override void OnNetworkDespawn()
        {
            isPickup.OnValueChanged -= OnPickupStateChanged;
        }

        private void OnPickupStateChanged(bool oldVal, bool newVal)
        {
            if (newVal) SetupAsPickup();
            else SetupAsProjectile();
        }

        private void SetupAsPickup()
        {
            var pickup = GetComponent<WeaponPickup>();
            if (pickup != null)
            {
                pickup.enabled = true;
            }
            enabled = false;

            transform.localScale = Vector3.one;

            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.enabled = true;
            }

            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.useGravity = false;
            }
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void SetupAsProjectile()
        {
            var pickup = GetComponent<WeaponPickup>();
            if (pickup != null)
            {
                pickup.enabled = false;
            }
            enabled = true;

            transform.localScale = Vector3.one;

            // Keep the networked bomb visible
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.enabled = true;
            }

            if (_rb == null) _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.isKinematic = !IsServer;
                _rb.useGravity = IsServer;
            }
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = !IsServer;
        }

        public void ThrowBomb(Vector3 throwDirection, float throwForce, PlayerController thrower = null)
        {
            if (!IsServer) return;
            isPickup.Value = false;
            SetupAsProjectile(); // Explicitly call to set physics parameters immediately on server
            _isThrown = true;
            _netPosition.Value = transform.position;
            _netRotation.Value = transform.rotation;

            // Ignore collision with the thrower to prevent immediate explosion in hand
            if (thrower != null)
            {
                var bombCollider = GetComponent<Collider>();
                if (bombCollider != null)
                {
                    var throwerColliders = thrower.GetComponentsInChildren<Collider>();
                    foreach (var c in throwerColliders)
                    {
                        Physics.IgnoreCollision(bombCollider, c, true);
                    }
                }
            }

            if (_rb != null)
            {
                _rb.isKinematic = false;
                _rb.AddForce(throwDirection * throwForce, ForceMode.Impulse);
            }
        }

        private void FixedUpdate()
        {
            if (IsServer && _isThrown && !_hasExploded)
            {
                _netPosition.Value = transform.position;
                _netRotation.Value = transform.rotation;
            }
        }

        private void Update()
        {
            if (IsServer)
            {
                if (_hasExploded || !_isThrown) return;

                _timer += Time.deltaTime;
                if (_timer >= fuseTime)
                    Explode();
            }
            else
            {
                // Smooth interpolation for clients when thrown
                if (!isPickup.Value && !_hasExploded)
                {
                    transform.position = Vector3.Lerp(transform.position, _netPosition.Value, Time.deltaTime * 25f);
                    transform.rotation = Quaternion.Slerp(transform.rotation, _netRotation.Value, Time.deltaTime * 25f);
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Do not explode on impact. Only explode when fuseTime timer expires.
        }

        private void Explode()
        {
            if (_hasExploded) return;
            _hasExploded = true;

            Vector3 explosionPos = transform.position;
            Debug.Log($"[BombItem] Explode() triggered at {explosionPos}. Radius: {explosionRadius}");

            // Physics knockback — tính trên server
            Collider[] hits = Physics.OverlapSphere(explosionPos, explosionRadius);
            Debug.Log($"[BombItem] OverlapSphere found {hits.Length} colliders.");

            foreach (var hit in hits)
            {
                var rb = hit.GetComponent<Rigidbody>();
                if (rb == null) continue;

                // Nếu là Player thì bỏ qua, TakeDamage sẽ lo việc AddForce một lần duy nhất vào pelvis
                if (hit.GetComponentInParent<PlayerStats>() != null || hit.GetComponentInChildren<PlayerStats>() != null)
                {
                    Debug.Log($"[BombItem] Skipping physics force loop for player bone: {hit.name}");
                    continue;
                }

                Vector3 dir = (rb.position - explosionPos).normalized;
                float dist = Vector3.Distance(rb.position, explosionPos);
                float falloff = 1f - Mathf.Clamp01(dist / explosionRadius);
                float force = knockbackForce * falloff * 0.2f; // Giảm mạnh lực đối với vật thể thường

                Vector3 knockDir = (dir + Vector3.up * 0.1f).normalized;
                rb.AddForce(knockDir * force, ForceMode.Impulse);
                Debug.Log($"[BombItem] Applied {force} knockback force to regular object: {hit.name}");
            }

            // Damage calculation — only on server
            var processedPlayers = new System.Collections.Generic.HashSet<PlayerStats>();
            foreach (var hit in hits)
            {
                var targetStats = hit.GetComponentInParent<PlayerStats>();
                if (targetStats == null)
                    targetStats = hit.GetComponentInChildren<PlayerStats>();

                if (targetStats == null)
                {
                    Debug.Log($"[BombItem] Collider {hit.name} has no PlayerStats in parent/children.");
                    continue;
                }

                if (processedPlayers.Contains(targetStats))
                {
                    Debug.Log($"[BombItem] Player {targetStats.name} already processed.");
                    continue;
                }
                
                processedPlayers.Add(targetStats);

                // Find the pelvis or transform to measure distance, resolving nested structure correctly
                var targetController = targetStats.GetComponentInChildren<PlayerController>();
                if (targetController == null)
                    targetController = targetStats.GetComponentInParent<PlayerController>();

                Rigidbody targetPelvis = null;
                if (targetController != null)
                {
                    targetPelvis = targetController.pelvisRigidbody;
                }
                if (targetPelvis == null)
                {
                    foreach (var rb in targetStats.GetComponentsInChildren<Rigidbody>())
                    {
                        if (rb.name.ToLower().Contains("pelvis"))
                        {
                            targetPelvis = rb;
                            break;
                        }
                    }
                }

                Transform targetCenter = (targetPelvis != null) ? targetPelvis.transform : targetStats.transform;
                if (targetPelvis != null)
                {
                    Debug.Log($"[BombItem] Found player pelvis at {targetCenter.position}");
                }
                else
                {
                    Debug.Log($"[BombItem] Target has no pelvis, using root/fallback transform at {targetCenter.position}");
                }

                float dist = Vector3.Distance(targetCenter.position, explosionPos);
                float damage = 0f;

                if (dist <= 1.0f)
                {
                    damage = targetStats.maxHealth; // Death
                    Debug.Log($"[BombItem] Distance to {targetStats.name} is {dist}m (<= 1.0m) -> Fatal Damage: {damage}");
                }
                else if (dist <= explosionRadius)
                {
                    // Scale damage from 80 down to 25 across the range 1.0m to explosionRadius
                    float t = (dist - 1.0f) / (explosionRadius - 1.0f);
                    damage = Mathf.Lerp(80f, 25f, t);
                    Debug.Log($"[BombItem] Distance to {targetStats.name} is {dist}m -> Scaled Damage: {damage}");
                }
                else
                {
                    Debug.Log($"[BombItem] Distance to {targetStats.name} is {dist}m (> {explosionRadius}m) -> No Damage");
                }

                if (damage > 0f)
                {
                    Vector3 hitDir = (targetCenter.position - explosionPos).normalized;
                    
                    // Scale bomb knockback force by distance falloff (increased by 1.3x per user request)
                    float falloff = 1f - Mathf.Clamp01(dist / explosionRadius);
                    float bombForce = knockbackForce * falloff * 1.3f;
                    
                    Debug.Log($"[BombItem] Calling TakeDamage on {targetStats.name} for {damage} HP. Force: {bombForce}");
                    targetStats.TakeDamage(damage, hitDir, targetPelvis, bombForce);
                }
            }

            // Trigger VFX + SFX trên tất cả clients
            TriggerExplosionEffectsClientRpc(explosionPos);

            if (IsSpawned)
            {
                if (EdgeParty.ConnectionManagement.NetworkObjectPool.Singleton != null)
                {
                    EdgeParty.ConnectionManagement.NetworkObjectPool.Singleton.ReturnNetworkObject(GetComponent<NetworkObject>());
                }
                else
                {
                    GetComponent<NetworkObject>().Despawn(true);
                }
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TriggerExplosionEffectsClientRpc(Vector3 position)
        {
            _hasExploded = true;
            
            // VFX
            if (explosionVFXPrefab != null)
            {
                var vfx = Instantiate(explosionVFXPrefab, position, Quaternion.identity);
                Destroy(vfx, 3f);
            }

            // SFX
            if (explosionSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(explosionSFX);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.3f);
            Gizmos.DrawSphere(transform.position, explosionRadius);
        }
    }
}