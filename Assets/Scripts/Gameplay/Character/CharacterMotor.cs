using UnityEngine;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Handles server-side physics, movement forces, and ground detection.
    /// Part of the character refactor.
    /// </summary>
    public class CharacterMotor : MonoBehaviour
    {
        [Header("References")]
        public Rigidbody pelvisRigidbody;
        public Transform ghostRoot;
        public Transform ghostPelvis;

        [Header("Movement Settings")]
        public float walkForce = 75f;
        public float runForce = 125f;
        public float rotationSpeed = 5f;
        public float jumpImpulse = 50f;
        public float dashImpulse = 100f;
        [Range(0f, 1f)] public float airControlFactor = 0.15f;

        [Header("Ground Detection")]
        public LayerMask groundLayer = ~0;
        public float groundCheckDistance = 0.5f;
        public float groundCheckRadius = 0.2f;
        public float slopeLimit = 45f;

        public bool IsGrounded { get; private set; }
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        private Vector3 _moveDir;
        private bool _isRunning;
        private bool _isOneShotActive;
        private Vector3 _facingDir = Vector3.forward;

        private void FixedUpdate()
        {
            if (pelvisRigidbody == null)
            {
                // Deep search in the entire player hierarchy
                var allRbs = transform.root.GetComponentsInChildren<Rigidbody>();
                foreach (var rb in allRbs)
                {
                    if (rb.name.ToLower().Contains("pelvis"))
                    {
                        pelvisRigidbody = rb;
                        break;
                    }
                }
                
                if (pelvisRigidbody == null) return;
            }

            UpdateGroundCheck();
            ApplyMovementForces();
            ApplyRotation();
        }

        private void UpdateGroundCheck()
        {
            if (pelvisRigidbody == null) return;
            
            // SphereCast downwards from the pelvis
            Ray ray = new Ray(pelvisRigidbody.position, Vector3.down);
            if (Physics.SphereCast(ray, groundCheckRadius, out RaycastHit hit, groundCheckDistance, groundLayer))
            {
                IsGrounded = true;
                GroundNormal = hit.normal;
            }
            else
            {
                IsGrounded = false;
                GroundNormal = Vector3.up;
            }
        }

        public void SetMovementInput(Vector3 moveDir, bool isRunning)
        {
            _moveDir = moveDir;
            _isRunning = isRunning;
            
            if (moveDir.sqrMagnitude > 0.01f)
            {
                _facingDir = moveDir;
            }
        }

        public void SetOneShotActive(bool active)
        {
            _isOneShotActive = active;
        }

        public void ApplyJump(Vector3 moveDir)
        {
            if (pelvisRigidbody == null) return;

            var vel = pelvisRigidbody.linearVelocity;
            pelvisRigidbody.linearVelocity = new Vector3(vel.x * 0.3f, vel.y, vel.z * 0.3f);
            pelvisRigidbody.AddForce(Vector3.up * jumpImpulse, ForceMode.Impulse);
            
            if (moveDir.sqrMagnitude > 0.01f)
                pelvisRigidbody.AddForce(moveDir * (jumpImpulse * 0.2f), ForceMode.Impulse);
        }

        public void ApplyDash()
        {
            if (pelvisRigidbody == null || ghostRoot == null) return;
            pelvisRigidbody.AddForce(ghostRoot.forward * dashImpulse, ForceMode.Impulse);
        }

        private void ApplyMovementForces()
        {
            if (pelvisRigidbody == null || _moveDir.sqrMagnitude < 0.01f) return;

            float force = _isRunning ? runForce : walkForce;
            if (_isOneShotActive) force *= airControlFactor;

            Vector3 finalMoveDir = _moveDir;
            
            // Project movement onto slope if grounded
            if (IsGrounded)
            {
                finalMoveDir = Vector3.ProjectOnPlane(_moveDir, GroundNormal).normalized;
                
                // Extra downward force to keep the character glued to the slope
                pelvisRigidbody.AddForce(-GroundNormal * 10f, ForceMode.Acceleration);
            }

            pelvisRigidbody.AddForce(finalMoveDir * force, ForceMode.Acceleration);
        }

        private void ApplyRotation()
        {
            if (pelvisRigidbody == null || ghostPelvis == null || ghostRoot == null) return;

            // Rotate visual root to face direction
            if (_facingDir.sqrMagnitude > 0.001f)
            {
                ghostRoot.rotation = Quaternion.Slerp(ghostRoot.rotation, Quaternion.LookRotation(_facingDir, Vector3.up), Time.fixedDeltaTime * 10f);
            }

            // Sync physics pelvis rotation to ghost animator's pelvis (Active Ragdoll magic)
            Quaternion targetRot = ghostPelvis.rotation;
            Quaternion deltaRot = targetRot * Quaternion.Inverse(pelvisRigidbody.rotation);

            deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;

            if (Mathf.Abs(angle) > 0.01f)
            {
                Vector3 torque = axis.normalized * (angle * rotationSpeed);
                pelvisRigidbody.AddTorque(torque, ForceMode.Acceleration);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (pelvisRigidbody == null) return;
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(pelvisRigidbody.position + Vector3.down * groundCheckDistance, groundCheckRadius);
        }
    }
}
