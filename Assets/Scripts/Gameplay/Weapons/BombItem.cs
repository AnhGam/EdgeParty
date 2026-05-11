using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Gameplay.Items
{
    /// <summary>
    /// Lựu đạn: Đẩy lùi (Knockback) vật lý mạnh + VFX + SFX nổ.
    /// Server-authoritative: Physics và damage tính trên Server, 
    /// VFX/SFX được trigger xuống tất cả Client qua ClientRpc.
    /// </summary>
    public class BombItem : NetworkBehaviour
    {
        [Header("Explosion Settings")]
        public float explosionRadius = 5f;
        public float knockbackForce = 800f;
        public float fuseTime = 2f;       // Thời gian nổ sau khi ném

        [Header("VFX / SFX")]
        public GameObject explosionVFXPrefab;  // Kéo Explosion VFX Prefab vào
        public AudioClip explosionSFX;         // Kéo tiếng nổ vào

        private bool _hasExploded = false;
        private float _timer = 0f;
        private Rigidbody _rb;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }

        /// <summary>Gọi từ PlayerController khi throw bomb (chỉ gọi trên Server)</summary>
        public void ThrowBomb(Vector3 throwDirection, float throwForce)
        {
            if (!IsServer) return;
            if (_rb != null)
                _rb.AddForce(throwDirection * throwForce, ForceMode.Impulse);
        }

        private void Update()
        {
            if (!IsServer || _hasExploded) return;

            _timer += Time.deltaTime;
            if (_timer >= fuseTime)
                ExplodeServerRpc();
        }

        private void OnCollisionEnter(Collision collision)
        {
            // Nổ sớm nếu va chạm mạnh (tuỳ chọn)
            if (!IsServer || _hasExploded) return;
            if (collision.relativeVelocity.magnitude > 8f)
                ExplodeServerRpc();
        }

        [ServerRpc]
        private void ExplodeServerRpc()
        {
            if (_hasExploded) return;
            _hasExploded = true;

            Vector3 explosionPos = transform.position;

            // ── 1. Áp lực vật lý (chỉ tính trên Server) ──
            Collider[] hits = Physics.OverlapSphere(explosionPos, explosionRadius);
            foreach (var hit in hits)
            {
                var rb = hit.GetComponent<Rigidbody>();
                if (rb == null) continue;

                Vector3 dir = (rb.position - explosionPos).normalized;
                float dist = Vector3.Distance(rb.position, explosionPos);
                float falloff = 1f - Mathf.Clamp01(dist / explosionRadius);
                float force = knockbackForce * falloff;

                // Thêm lực lên theo góc 30 độ để bay người đẹp hơn
                Vector3 knockDir = (dir + Vector3.up * 0.5f).normalized;
                rb.AddForce(knockDir * force, ForceMode.Impulse);
            }

            // ── 2. Trigger VFX + SFX trên tất cả Client ──
            TriggerExplosionEffectsClientRpc(explosionPos);

            // ── 3. Despawn object ──
            if (IsSpawned)
                GetComponent<NetworkObject>().Despawn(true);
        }

        [ClientRpc]
        private void TriggerExplosionEffectsClientRpc(Vector3 position)
        {
            // VFX
            if (explosionVFXPrefab != null)
            {
                var vfx = Instantiate(explosionVFXPrefab, position, Quaternion.identity);
                // Tự destroy VFX sau 3 giây
                Destroy(vfx, 3f);
            }

            // SFX
            if (explosionSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(explosionSFX);
        }

        // Debug visualization trong Scene view
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.3f);
            Gizmos.DrawSphere(transform.position, explosionRadius);
        }
    }
}