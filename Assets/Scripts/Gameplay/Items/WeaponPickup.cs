using UnityEngine;
using Unity.Netcode;
using EdgeParty.Gameplay.Character;

namespace EdgeParty.Gameplay.Items
{
    public class WeaponPickup : MonoBehaviour
    {
        public enum ItemType { Bomb, StunGun }

        [Header("Config")]
        public ItemType itemType = ItemType.Bomb;

        [Header("Visual")]
        [Tooltip("Bob up/down animation speed")]
        public float bobSpeed = 1f;
        [Tooltip("Bob amplitude")]
        public float bobHeight = 0.03f;
        [Tooltip("Rotation speed (degrees/sec)")]
        public float rotateSpeed = 30f;

        [Header("SFX")]
        public AudioClip pickupSFX;

        private Vector3 _startPos;
        private bool _picked = false;

        private void Start()
        {
            _startPos = transform.position;
            
            var col = GetComponent<SphereCollider>();
            if (col == null)
            {
                col = gameObject.AddComponent<SphereCollider>();
            }

            // Calculate combined bounds from all renderers in children to fit the collider perfectly
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            var renderers = GetComponentsInChildren<Renderer>();
            bool hasBounds = false;
            foreach (var r in renderers)
            {
                if (!hasBounds)
                {
                    bounds = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (hasBounds)
            {
                col.center = transform.InverseTransformPoint(bounds.center);
                col.radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) * 1.5f;
            }
            else
            {
                col.center = Vector3.zero;
                col.radius = 1.0f;
            }
            col.isTrigger = true;

            var otherCols = GetComponentsInChildren<Collider>();
            foreach (var c in otherCols)
            {
                c.isTrigger = true;
            }

            // Ensure Rigidbody is kinematic for pickups
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
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
            if (!enabled || _picked) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

            var pc = other.GetComponentInParent<PlayerController>();
            if (pc == null) return;
            if (pc.stats != null && pc.stats.IsDead.Value) return;
            if (pc.CurrentHeldItem != null) return; // already holding an item

            _picked = true;

            pc.PickupItem(itemType);

            CollectClientRpc();

            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned)
            {
                netObj.Despawn(true);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CollectClientRpc()
        {
            gameObject.SetActive(false);
            // Play SFX
            AudioManager.Instance?.PlaySFX(pickupSFX);
        }
    }
}
