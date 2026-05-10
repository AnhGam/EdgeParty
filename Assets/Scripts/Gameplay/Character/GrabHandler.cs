using UnityEngine;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Robust Grab logic using OverlapSphere with Offset.
    /// Handles physical connection and tug-of-war stabilization.
    /// </summary>
    public class GrabHandler : MonoBehaviour
    {
        [Header("Settings")]
        public float grabRadius = 0.3f; 
        public Vector3 grabOffset = new Vector3(0, 0, 0.2f); // Reach beyond the player's body
        public LayerMask grabLayer = ~0; 
        public float breakForce = 2000f;

        public bool IsActive { get; private set; }
        public bool IsConnected => _grabJoint != null;

        private FixedJoint _grabJoint;

        public void SetActive(bool active)
        {
            IsActive = active;
            if (!active) Release();
        }

        private void FixedUpdate()
        {
            if (!IsActive || IsConnected) return;

            // Search for targets in front of the hand
            Vector3 worldOffset = transform.TransformDirection(grabOffset);
            Collider[] hits = Physics.OverlapSphere(transform.position + worldOffset, grabRadius, grabLayer);
            
            foreach (var hit in hits)
            {
                if (hit.isTrigger) continue;
                if (hit.attachedRigidbody == null || hit.attachedRigidbody.isKinematic) continue;
                if (hit.transform.root == transform.root) continue;

                Connect(hit.attachedRigidbody);
                break;
            }
        }

        private void Connect(Rigidbody target)
        {
            Release();
            _grabJoint = gameObject.AddComponent<FixedJoint>();
            _grabJoint.connectedBody = target;
            _grabJoint.breakForce = breakForce;
            _grabJoint.breakTorque = breakForce;

            // Ignore collision with the grabbed object to prevent physics jitter/resistance
            Collider myCol = GetComponent<Collider>();
            Collider targetCol = target.GetComponent<Collider>();
            if (myCol != null && targetCol != null)
                Physics.IgnoreCollision(myCol, targetCol, true);
            
            Debug.Log($"[GrabHandler] {gameObject.name} Connected to {target.name}");
        }

        public void Release()
        {
            if (_grabJoint != null)
            {
                // Re-enable collision before destroying the joint
                if (_grabJoint.connectedBody != null)
                {
                    Collider myCol = GetComponent<Collider>();
                    Collider targetCol = _grabJoint.connectedBody.GetComponent<Collider>();
                    if (myCol != null && targetCol != null)
                        Physics.IgnoreCollision(myCol, targetCol, false);
                }
                Destroy(_grabJoint);
                _grabJoint = null;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsActive ? Color.green : Color.red;
            Vector3 worldOffset = transform.TransformDirection(grabOffset);
            Gizmos.DrawWireSphere(transform.position + worldOffset, grabRadius);
        }

        private void OnJointBreak(float breakForce)
        {
            _grabJoint = null;
        }
    }
}
