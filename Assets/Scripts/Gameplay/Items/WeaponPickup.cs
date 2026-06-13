using UnityEngine;
using Unity.Netcode;
using EdgeParty.Gameplay.Character;

namespace EdgeParty.Gameplay.Items
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(NetworkObject))]
    public class WeaponPickup : NetworkBehaviour
    {
        public enum ItemType { Bomb, StunGun }

        [Header("Config")]
        public ItemType itemType = ItemType.Bomb;

        [Header("Visual")]
        [Tooltip("Bob up/down animation speed")]
        public float bobSpeed = 1.5f;
        [Tooltip("Bob amplitude")]
        public float bobHeight = 0.15f;
        [Tooltip("Rotation speed (degrees/sec)")]
        public float rotateSpeed = 90f;

        [Header("SFX")]
        public AudioClip pickupSFX;

        private Vector3 _startPos;
        private bool _picked = false;

        private void Start()
        {
            _startPos = transform.position;
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        private void Update()
        {
            if (_picked) return;
            float y = _startPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
            transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer || _picked) return;

            var pc = other.GetComponentInParent<PlayerController>();
            if (pc == null) return;
            if (pc.stats != null && pc.stats.IsDead.Value) return;
            if (pc.CurrentHeldItem != null) return; // đã cầm item rồi

            _picked = true;

            pc.PickupItem(itemType);

            PlayPickupEffectsClientRpc();

            if (IsSpawned)
                NetworkObject.Despawn(true);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PlayPickupEffectsClientRpc()
        {
            if (pickupSFX != null && AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(pickupSFX);
        }

        private void OnDrawGizmosSelected()
        {
            var col = GetComponent<SphereCollider>();
            if (col == null) return;
            Gizmos.color = itemType == ItemType.Bomb
                ? new Color(1f, 0.4f, 0f, 0.3f)
                : new Color(0.3f, 0.7f, 1f, 0.3f);
            Gizmos.DrawSphere(transform.position, col.radius);
        }
    }
}
