using UnityEngine;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Robust Grab logic using OverlapSphere with Offset.
    /// Handles physical connection and tug-of-war stabilization.
    /// 
    /// Yêu cầu: GrabHandler phải được attach trên một GameObject có Rigidbody
    /// (ví dụ: xương tay ragdoll). Nếu không có Rigidbody, FixedJoint sẽ không hoạt động.
    /// </summary>
    public class GrabHandler : MonoBehaviour
    {
        [Header("Settings")]
        public float grabRadius = 0.6f;
        public Vector3 grabOffset = new Vector3(0, 0, 0.3f); // Reach beyond the hand
        public LayerMask grabLayer = ~0;
        public float breakForce = 2000f;

        public bool IsActive { get; private set; }
        public bool IsConnected => _grabJoint != null;

        private FixedJoint _grabJoint;
        private Rigidbody _myRigidbody;

        private void Awake()
        {
            _myRigidbody = GetComponent<Rigidbody>();
            if (_myRigidbody == null)
            {
                Debug.LogWarning($"[GrabHandler] '{gameObject.name}' không có Rigidbody! " +
                                 "FixedJoint cần Rigidbody trên cùng GameObject để hoạt động. " +
                                 "GrabHandler sẽ bị vô hiệu hóa.");
                enabled = false;
            }
        }

        public void SetActive(bool active)
        {
            IsActive = active;
            if (!active) Release();
        }

        private void FixedUpdate()
        {
            if (!IsActive || IsConnected) return;

            // Tìm target trong phạm vi phía trước bàn tay
            Vector3 worldOffset = transform.TransformDirection(grabOffset);
            Vector3 sphereCenter = transform.position + worldOffset;
            Collider[] hits = Physics.OverlapSphere(sphereCenter, grabRadius, grabLayer,
                                                    QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
            {
                // Bỏ qua trigger
                if (hit.isTrigger) continue;

                // Bỏ qua bản thân (theo root hierarchy)
                if (hit.transform.IsChildOf(transform.root)) continue;

                // Lấy Rigidbody của target — duyệt lên cây nếu cần
                Rigidbody targetRb = hit.attachedRigidbody;
                if (targetRb == null) targetRb = hit.GetComponentInParent<Rigidbody>();

                if (targetRb == null)
                {
                    Debug.Log($"[GrabHandler] Bỏ qua '{hit.name}': không có Rigidbody.");
                    continue;
                }

                if (targetRb.isKinematic)
                {
                    Debug.Log($"[GrabHandler] Bỏ qua '{hit.name}': Rigidbody là kinematic.");
                    continue;
                }

                Connect(targetRb, hit);
                break;
            }
        }

        private void Connect(Rigidbody target, Collider targetCollider)
        {
            Release();
            _grabJoint = gameObject.AddComponent<FixedJoint>();
            _grabJoint.connectedBody = target;
            _grabJoint.breakForce = breakForce;
            _grabJoint.breakTorque = breakForce;
            _grabJoint.enableCollision = false; // Tắt collision giữa hand và grabbed object

            Debug.Log($"[GrabHandler] '{gameObject.name}' đã kết nối với '{target.name}'");
        }

        public void Release()
        {
            if (_grabJoint != null)
            {
                Destroy(_grabJoint);
                _grabJoint = null;
                Debug.Log($"[GrabHandler] '{gameObject.name}' đã giải phóng grab.");
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsActive ? (IsConnected ? Color.green : Color.yellow) : Color.red;
            Vector3 worldOffset = transform.TransformDirection(grabOffset);
            Gizmos.DrawWireSphere(transform.position + worldOffset, grabRadius);
            Gizmos.DrawLine(transform.position, transform.position + worldOffset);
        }

        private void OnJointBreak(float breakForce)
        {
            _grabJoint = null;
            Debug.Log($"[GrabHandler] '{gameObject.name}' Joint bị break (force={breakForce:F1}).");
        }
    }
}
