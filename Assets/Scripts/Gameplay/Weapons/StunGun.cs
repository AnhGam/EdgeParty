using UnityEngine;
using Unity.Netcode;
using System.Collections;

namespace EdgeParty.Gameplay.Items
{
    public class StunGun : NetworkBehaviour
    {
        [Header("Stun Settings")]
        public float stunDuration = 2.5f;      // Giây bị choáng
        public float stunRange = 3f;           // Tầm bắn / tầm đánh

        [Header("VFX / SFX")]
        [Tooltip("Particle System tia điện — để trống để dùng built-in")]
        public GameObject electricVFXPrefab;
        [Tooltip("Kéo electricShock_sfx.wav vào đây")]
        public AudioClip stunHitSFX;
        [Tooltip("(Optional) Tiếng điện rè loop trong khi choáng")]
        public AudioClip stunLoopSFX;

        private void Awake()
        {
            // Auto-load SFX nếu không gán trong Inspector
            if (stunHitSFX == null)
                stunHitSFX = Resources.Load<AudioClip>("Audios/electricShock_sfx");
        }

        [Rpc(SendTo.Server)]
        public void UseStunGunServerRpc(Vector3 origin, Vector3 direction)
        {
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

        [Rpc(SendTo.ClientsAndHost)]
        private void ApplyStunClientRpc(ulong targetNetworkObjectId, Vector3 hitPoint)
        {
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var targetNetObj))
                return;

            var targetController = targetNetObj.GetComponent<EdgeParty.Gameplay.Character.PlayerController>();
            if (targetController == null) return;

            // Áp dụng stun — chạy trên mọi client (ragdoll + VFX local)
            targetController.StartCoroutine(StunRoutine(targetController, hitPoint));
        }

        private IEnumerator StunRoutine(EdgeParty.Gameplay.Character.PlayerController target, Vector3 hitPoint)
        {
            // VFX — electric arc bao quanh người bị choáng
            GameObject vfxInstance = null;
            if (electricVFXPrefab != null)
            {
                vfxInstance = Instantiate(electricVFXPrefab, target.transform.position, Quaternion.identity);
                vfxInstance.transform.SetParent(target.transform);
            }
            else
            {
                vfxInstance = SpawnBuiltinElectricVFX(target.transform);
            }

            // SFX hit
            if (stunHitSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(stunHitSFX);

            // Lock input của owner
            bool isTargetOwner = target.IsOwner;
            if (isTargetOwner) target.enabled = false;

            yield return new WaitForSeconds(stunDuration);

            if (target != null && isTargetOwner)
                target.enabled = true;

            if (vfxInstance != null)
                Destroy(vfxInstance);
        }

        private GameObject SpawnBuiltinElectricVFX(Transform parent)
        {
            var root = new GameObject("ElectricVFX_Auto");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.up * 0.8f; // ngang người

            var sparkSys = root.AddComponent<ParticleSystem>();
            var main = sparkSys.main;
            main.duration = stunDuration + 0.5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.3f, 0.9f, 1f),   // cyan
                new Color(1f, 1f, 0.3f));     // vàng
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = sparkSys.emission;
            emission.rateOverTime = 80f;

            var shape = sparkSys.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;

            // Renderer — unlit additive để trông như điện
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4f;

            sparkSys.Play();
            return root;
        }
    }
}