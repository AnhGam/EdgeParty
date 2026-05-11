using UnityEngine;
using Unity.Netcode;
using System.Collections;

namespace EdgeParty.Gameplay.Items
{
    /// <summary>
    /// Súng điện / Gậy giật điện.
    /// Cơ chế: Gây Stun — khóa input của nạn nhân + VFX tia điện bao quanh người.
    /// Server tính trúng đích, ClientRpc trigger VFX/SFX.
    /// </summary>
    public class StunGun : NetworkBehaviour
    {
        [Header("Stun Settings")]
        public float stunDuration = 2.5f;      // Giây bị choáng
        public float stunRange = 3f;           // Tầm bắn / tầm đánh

        [Header("VFX / SFX")]
        public GameObject electricVFXPrefab;   // Particle System tia điện
        public AudioClip stunHitSFX;           // Tiếng "bzzt" khi trúng
        public AudioClip stunLoopSFX;          // Tiếng điện rè trong khi choáng (loop)

        /// <summary>
        /// Gọi khi người chơi dùng item. Chỉ gọi từ Owner → Server.
        /// </summary>
        [ServerRpc]
        public void UseStunGunServerRpc(Vector3 origin, Vector3 direction)
        {
            // Raycast hoặc OverlapSphere tuỳ thiết kế
            if (Physics.Raycast(origin, direction, out RaycastHit hit, stunRange))
            {
                var targetController = hit.collider.GetComponentInParent<EdgeParty.Gameplay.Character.PlayerController>();
                if (targetController != null && targetController != GetComponentInParent<EdgeParty.Gameplay.Character.PlayerController>())
                {
                    ulong targetId = targetController.GetComponent<NetworkObject>().NetworkObjectId;
                    ApplyStunClientRpc(targetId, hit.point);
                }
            }
        }

        [ClientRpc]
        private void ApplyStunClientRpc(ulong targetNetworkObjectId, Vector3 hitPoint)
        {
            // Tìm target
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var targetNetObj))
                return;

            var targetController = targetNetObj.GetComponent<EdgeParty.Gameplay.Character.PlayerController>();
            if (targetController == null) return;

            // Áp dụng stun — chỉ ảnh hưởng đến owner của target hoặc trên server
            targetController.StartCoroutine(StunRoutine(targetController, hitPoint));
        }

        private IEnumerator StunRoutine(EdgeParty.Gameplay.Character.PlayerController target, Vector3 hitPoint)
        {
            // ── VFX: Spawn tia điện gắn vào người ──
            GameObject vfxInstance = null;
            if (electricVFXPrefab != null)
            {
                vfxInstance = Instantiate(electricVFXPrefab, target.transform.position, Quaternion.identity);
                vfxInstance.transform.SetParent(target.transform);
            }

            // ── SFX: Tiếng trúng ──
            if (stunHitSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(stunHitSFX);

            // ── Khóa input: disable PlayerController trên owner ──
            // Chỉ disable trên client là chủ nhân của nhân vật bị stun
            bool isTargetOwner = target.IsOwner;
            if (isTargetOwner) target.enabled = false;

            // ── Chờ hết stun ──
            yield return new WaitForSeconds(stunDuration);

            // ── Phục hồi ──
            if (target != null)
            {
                if (isTargetOwner) target.enabled = true;
            }

            // ── Dọn VFX ──
            if (vfxInstance != null)
                Destroy(vfxInstance);
        }
    }
}