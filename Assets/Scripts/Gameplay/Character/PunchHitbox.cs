using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Attach to the PHYSICS hand/fist Rigidbody object (e.g. hand_r on the ragdoll).
    ///
    /// How it works:
    ///   • Normally inactive – does no damage.
    ///   • CharacterAnimationController calls Activate() at the start of attack swing
    ///     and Deactivate() when the swing window ends.
    ///   • While active, any Rigidbody it touches (that has a PlayerStats) takes damage.
    ///   • One hit per activation window to avoid multi-hits.
    ///
    /// Inspector setup:
    ///   1. Add a Collider to this GameObject (sphere ~0.12 radius is good), set Is Trigger = true.
    ///   2. The collider can be on the same GO as the ragdoll hand Rigidbody – that's fine
    ///      because PlayerController.IgnoreInternalCollisions() already ignores it against
    ///      own body; here we only process OTHER characters.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PunchHitbox : MonoBehaviour
    {
        [Header("Damage")]
        [Tooltip("Base damage dealt per punch")]
        public float damage = 34f;
        [Tooltip("Radius for hit detection")]
        public float hitRadius = 0.5f;
        [Tooltip("Layer mask for valid hit targets (should include the Player layer)")]
        public LayerMask targetLayers = ~0;

        [Header("Visual Feedback")]
        [Tooltip("Optional particle played at hit point")]
        public GameObject hitVFXPrefab;

        // Runtime
        private bool _isActive;
        private Rigidbody _ownerPelvis;         // set by CharacterAnimationController
        private PlayerStats _ownerStats;        // to avoid self-hit
        private System.Collections.Generic.HashSet<PlayerStats> _hitThisSwing = new();

        private Collider _col;

        private void Awake()
        {
            _col = GetComponent<Collider>();
            if (_col != null)
            {
                _col.isTrigger = true;
                _col.enabled = false;
            }
            _isActive = false;
        }

        /// <summary>Called by CharacterAnimationController when punch swing begins.</summary>
        public void Activate(Rigidbody ownerPelvis, PlayerStats ownerStats)
        {
            _ownerPelvis = ownerPelvis;
            _ownerStats  = ownerStats;
            _hitThisSwing.Clear();
            _isActive    = true;
        }

        /// <summary>Called by CharacterAnimationController when punch swing ends.</summary>
        public void Deactivate()
        {
            _isActive    = false;
        }

        private void FixedUpdate()
        {
            if (!_isActive) return;

            Collider[] hits = Physics.OverlapSphere(transform.position, hitRadius, targetLayers);
            foreach (var other in hits)
            {
                // Find PlayerStats on target hierarchy
                var targetStats = other.GetComponentInParent<PlayerStats>();
                if (targetStats == null)
                    targetStats = other.GetComponentInChildren<PlayerStats>();
                if (targetStats == null) continue;

                // Don't hit self
                if (targetStats == _ownerStats) continue;

                if (_hitThisSwing.Contains(targetStats)) continue;

                // Skip own body
                var otherController = other.GetComponentInParent<PlayerController>();
                var myController = GetComponentInParent<PlayerController>();
                if (myController != null && otherController != null && myController == otherController) continue;

                _hitThisSwing.Add(targetStats);

                // Direction from attacker pelvis toward target
                Vector3 hitDir = Vector3.forward;
                if (_ownerPelvis != null)
                    hitDir = (other.transform.position - _ownerPelvis.position).normalized;

                // Find target pelvis Rigidbody for knockback
                Rigidbody targetPelvis = null;
                var allRbs = targetStats.GetComponentsInChildren<Rigidbody>();
                foreach (var rb in allRbs)
                    if (rb.name.ToLower().Contains("pelvis")) { targetPelvis = rb; break; }

                // Damage is server-side
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                {
                    // Offline / solo test
                    targetStats.TakeDamage(damage, hitDir, targetPelvis);
                }
                else
                {
                    // Already running on server because PunchHitbox lives on the physics ragdoll
                    // which is server-simulated. Safe to call directly.
                    if (NetworkManager.Singleton.IsServer)
                        targetStats.TakeDamage(damage, hitDir, targetPelvis);
                }

                // Spawn hit VFX at contact point (local, cosmetic)
                if (hitVFXPrefab != null)
                {
                    var closest = other.ClosestPoint(transform.position);
                    Instantiate(hitVFXPrefab, closest, Quaternion.LookRotation(-hitDir));
                }
            }
        }
    }
}
