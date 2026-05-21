using UnityEngine;
using Unity.Netcode;
using System.Collections;

namespace EdgeParty.Gameplay.Items
{
    /// <summary>
    /// Bẫy Chuối: Khi dẫm lên → trượt dài, mất điều khiển + VFX vệt trượt + SFX hài hước.
    /// - Bẫy được đặt trên Server, tồn tại cho đến khi hết thời gian hoặc bị kích hoạt.
    /// - Hiệu ứng trượt: Giảm ma sát Rigidbody + vô hiệu hoá input tạm thời.
    /// </summary>
    public class BananaPeel : NetworkBehaviour
    {
        [Header("Trap Settings")]
        public float slideDuration = 2f;         // Giây bị trượt
        public float slideForceMultiplier = 3f;  // Nhân thêm lực hiện tại để trượt
        public float trapLifetime = 30f;          // Tự biến mất sau N giây

        [Header("VFX / SFX")]
        public GameObject slideTrailVFXPrefab;   // VFX vệt trượt (Trail / Particle)
        public AudioClip slipSFX;                // Âm thanh "uợt!" hài hước
        public AudioClip trapPlaceSFX;           // Âm thanh đặt bẫy

        private bool _isTriggered = false;
        private float _lifetimeTimer = 0f;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                // SFX đặt bẫy cho tất cả client
                PlayPlaceSFXClientRpc(transform.position);
            }
        }

        private void Update()
        {
            if (!IsServer || _isTriggered) return;
            _lifetimeTimer += Time.deltaTime;
            if (_lifetimeTimer >= trapLifetime)
            {
                if (IsSpawned) GetComponent<NetworkObject>().Despawn(true);
            }
        }

        // Dùng trigger collider (Is Trigger = true trên Collider của object này)
        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || _isTriggered) return;

            var controller = other.GetComponentInParent<EdgeParty.Gameplay.Character.PlayerController>();
            if (controller == null) return;

            // Không trap chính mình (tuỳ chọn — có thể bỏ comment để trap cả người đặt)
            // if (controller.OwnerClientId == OwnerClientId) return;

            _isTriggered = true;

            // Lấy Rigidbody của nạn nhân (pelvis)
            var victimRb = controller.pelvisRigidbody;
            ulong victimId = controller.GetComponent<NetworkObject>().NetworkObjectId;

            // Trigger slide effect trên tất cả client
            TriggerSlideClientRpc(victimId, transform.position);

            // Despawn bẫy
            if (IsSpawned) GetComponent<NetworkObject>().Despawn(true);
        }

        [ClientRpc]
        private void TriggerSlideClientRpc(ulong victimNetworkObjectId, Vector3 trapPosition)
        {
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(victimNetworkObjectId, out var targetNetObj))
                return;

            var controller = targetNetObj.GetComponent<EdgeParty.Gameplay.Character.PlayerController>();
            if (controller == null) return;

            // ── SFX ──
            if (slipSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(slipSFX);

            // ── VFX vệt trượt gắn vào nạn nhân ──
            GameObject trailVFX = null;
            if (slideTrailVFXPrefab != null)
            {
                trailVFX = Instantiate(slideTrailVFXPrefab, controller.transform.position, Quaternion.identity);
                trailVFX.transform.SetParent(controller.transform);
            }

            controller.StartCoroutine(SlideRoutine(controller, trailVFX));
        }

        private IEnumerator SlideRoutine(EdgeParty.Gameplay.Character.PlayerController target, GameObject trailVFX)
        {
            var rb = target.pelvisRigidbody;

            // ── Vô hiệu hoá input ──
            bool isOwner = target.IsOwner;
            if (isOwner) target.enabled = false;

            // ── Áp lực trượt: phóng theo hướng đang đi với lực nhân thêm ──
            if (rb != null)
            {
                Vector3 slideDir = rb.linearVelocity.sqrMagnitude > 0.1f
                    ? rb.linearVelocity.normalized
                    : target.transform.forward;

                rb.AddForce(slideDir * slideForceMultiplier, ForceMode.VelocityChange);

                // Giảm ma sát trong khi trượt
                var colliders = target.GetComponentsInChildren<Collider>();
                PhysicsMaterial slippyMat = new PhysicsMaterial { dynamicFriction = 0f, staticFriction = 0f };
                foreach (var col in colliders) col.material = slippyMat;

                yield return new WaitForSeconds(slideDuration);

                // Phục hồi ma sát mặc định
                PhysicsMaterial defaultMat = new PhysicsMaterial { dynamicFriction = 0.6f, staticFriction = 0.6f };
                foreach (var col in colliders) col.material = defaultMat;
            }
            else
            {
                yield return new WaitForSeconds(slideDuration);
            }

            // ── Phục hồi input ──
            if (target != null && isOwner) target.enabled = true;

            // ── Dọn VFX ──
            if (trailVFX != null) Destroy(trailVFX);
        }

        [ClientRpc]
        private void PlayPlaceSFXClientRpc(Vector3 position)
        {
            if (trapPlaceSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(trapPlaceSFX);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawSphere(transform.position, 0.5f);
        }
    }
}