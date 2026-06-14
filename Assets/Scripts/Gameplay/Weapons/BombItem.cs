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
        public float fuseTime = 3f;       // Thời gian nổ sau khi ném (default to 3s)

        [Header("VFX / SFX")]
        [Tooltip("Kéo Explosion VFX Prefab vào — hoặc để trống để dùng built-in particle")]
        public GameObject explosionVFXPrefab;
        [Tooltip("Kéo Assets/Scripts/Audio/Audios/EXPLOSION_sfx.wav vào đây")]
        public AudioClip explosionSFX;

        public NetworkVariable<bool> isPickup = new NetworkVariable<bool>(true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

            // Hide the networked bomb completely! 
            // The visual representation is handled by PlayerController.SpawnVisualBombClientRpc
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.enabled = false;
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
            _isThrown = true;

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

        private void Update()
        {
            if (!IsServer || _hasExploded || !_isThrown) return;

            _timer += Time.deltaTime;
            if (_timer >= fuseTime)
                Explode();
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

            // Physics knockback — tính trên server
            Collider[] hits = Physics.OverlapSphere(explosionPos, explosionRadius);
            foreach (var hit in hits)
            {
                var rb = hit.GetComponent<Rigidbody>();
                if (rb == null) continue;

                Vector3 dir = (rb.position - explosionPos).normalized;
                float dist = Vector3.Distance(rb.position, explosionPos);
                float falloff = 1f - Mathf.Clamp01(dist / explosionRadius);
                float force = knockbackForce * falloff;

                // Thêm lực lên theo góc nhỏ để bay người đẹp hơn
                Vector3 knockDir = (dir + Vector3.up * 0.1f).normalized;
                rb.AddForce(knockDir * force, ForceMode.Impulse);
            }

            // Damage calculation — only on server
            var processedPlayers = new System.Collections.Generic.HashSet<PlayerStats>();
            foreach (var hit in hits)
            {
                var targetStats = hit.GetComponentInParent<PlayerStats>();
                if (targetStats == null)
                    targetStats = hit.GetComponentInChildren<PlayerStats>();

                if (targetStats == null || processedPlayers.Contains(targetStats)) continue;
                processedPlayers.Add(targetStats);

                // Find the pelvis or transform to measure distance
                Transform targetCenter = targetStats.transform;
                var targetController = targetStats.GetComponent<PlayerController>();
                if (targetController != null && targetController.pelvisRigidbody != null)
                {
                    targetCenter = targetController.pelvisRigidbody.transform;
                }

                float dist = Vector3.Distance(targetCenter.position, explosionPos);
                float damage = 0f;

                if (dist > 3f)
                {
                    damage = 0f;
                }
                else if (dist <= 0.5f)
                {
                    damage = targetStats.maxHealth; // Death
                }
                else if (dist >= 2f && dist <= 3f)
                {
                    damage = 34f; // 1 punch hit
                }
                else // between 0.5m and 2m
                {
                    float t = (2f - dist) / 1.5f;
                    damage = 34f + t * (75f - 34f);
                }

                if (damage > 0f)
                {
                    Rigidbody targetPelvis = targetController != null ? targetController.pelvisRigidbody : null;
                    Vector3 hitDir = (targetCenter.position - explosionPos).normalized;
                    targetStats.TakeDamage(damage, hitDir, targetPelvis);
                }
            }

            // Trigger VFX + SFX trên tất cả clients
            TriggerExplosionEffectsClientRpc(explosionPos);

            if (IsSpawned)
                GetComponent<NetworkObject>().Despawn(true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void TriggerExplosionEffectsClientRpc(Vector3 position)
        {
            // VFX
            if (explosionVFXPrefab != null)
            {
                var vfx = Instantiate(explosionVFXPrefab, position, Quaternion.identity);
                Destroy(vfx, 3f);
            }
            else
            {
                // Built-in fallback: tạo particle system đơn giản
                SpawnBuiltinExplosionVFX(position);
            }

            // SFX
            if (explosionSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(explosionSFX);
        }

        private void SpawnBuiltinExplosionVFX(Vector3 position)
        {
            var root = new GameObject("ExplosionVFX_Auto");
            root.transform.position = position;

            // 1. Lửa — burst cam-đỏ
            var fireGO = new GameObject("Fire");
            fireGO.transform.SetParent(root.transform, false);
            var fireSys = fireGO.AddComponent<ParticleSystem>();
            fireSys.Stop(); // Ngừng chạy trước khi setup để tránh lỗi

            var fireMain = fireSys.main;
            fireMain.duration = 0.4f;
            fireMain.loop = false;
            fireMain.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            fireMain.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f);
            fireMain.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            fireMain.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.5f, 0f), new Color(1f, 0.2f, 0f));
            fireMain.gravityModifier = -0.3f;
            
            var fireEmission = fireSys.emission;
            fireEmission.rateOverTime = 0;
            fireEmission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 40) });
            
            var fireShape = fireSys.shape;
            fireShape.shapeType = ParticleSystemShapeType.Sphere;
            fireShape.radius = 0.3f;

            var fireRenderer = fireGO.GetComponent<ParticleSystemRenderer>();

            fireSys.Play();

            // 2. Khói — burst xám
            var smokeGO = new GameObject("Smoke");
            smokeGO.transform.SetParent(root.transform, false);
            var smokeSys = smokeGO.AddComponent<ParticleSystem>();
            smokeSys.Stop(); // Ngừng chạy trước khi setup để tránh lỗi

            var smokeMain = smokeSys.main;
            smokeMain.duration = 0.5f;
            smokeMain.loop = false;
            smokeMain.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
            smokeMain.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            smokeMain.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
            smokeMain.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.3f, 0.3f, 0.3f, 0.8f), new Color(0.6f, 0.6f, 0.6f, 0.4f));
            smokeMain.gravityModifier = -0.15f;
            
            var smokeEmission = smokeSys.emission;
            smokeEmission.rateOverTime = 0;
            smokeEmission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 20) });

            var smokeRenderer = smokeGO.GetComponent<ParticleSystemRenderer>();
            
            smokeSys.Play();

            Destroy(root, 3f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.3f);
            Gizmos.DrawSphere(transform.position, explosionRadius);
        }
    }
}