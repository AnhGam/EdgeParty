using UnityEngine;

namespace EdgeParty.Gameplay.Character
{
    public enum BoneCategory { Torso, Head, Arm, Leg, Tail }

    [RequireComponent(typeof(ConfigurableJoint))]
    public class RagdollBoneFollower : MonoBehaviour
    {
        public BoneCategory category;
        public Transform targetBone;

        [Header("Enhanced Power")]
        [Range(0f, 1f)] public float gravityCompensation = 0f;
        [Range(0f, 1f)] public float velocitySync = 0.3f;

        [Header("Combat Override")]
        [Tooltip("Velocity sync used during punch swing (higher = snappier hit)")]
        [Range(0f, 1f)] public float combatVelocitySync = 0.92f;

        // Runtime
        private ConfigurableJoint _joint;
        private Rigidbody _rb;
        private Quaternion _startingLocalRotation;
        private Quaternion _localToJointSpace;
        private Quaternion _jointToLocalSpace;

        private float _originalXSpring;
        private float _originalXDamper;
        private float _originalYZSpring;
        private float _originalYZDamper;

        private bool _isRootBone;
        private bool _isCombatActive;

        private void Awake()
        {
            _joint = GetComponent<ConfigurableJoint>();
            _rb = GetComponent<Rigidbody>();
            _startingLocalRotation = transform.localRotation;
            _isRootBone = (_joint.connectedBody == null);

            _originalXSpring = _joint.angularXDrive.positionSpring;
            _originalXDamper = _joint.angularXDrive.positionDamper;
            _originalYZSpring = _joint.angularYZDrive.positionSpring;
            _originalYZDamper = _joint.angularYZDrive.positionDamper;

            Vector3 forward = Vector3.Cross(_joint.axis, _joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, _joint.axis).normalized;
            _localToJointSpace = Quaternion.LookRotation(forward, up);
            _jointToLocalSpace = Quaternion.Inverse(_localToJointSpace);
        }

        public void SetSpringMultiplier(float multiplier)
        {
            var xd = _joint.angularXDrive;
            xd.positionSpring = _originalXSpring * multiplier;
            xd.positionDamper = _originalXDamper * multiplier;
            _joint.angularXDrive = xd;

            var yzd = _joint.angularYZDrive;
            yzd.positionSpring = _originalYZSpring * multiplier;
            yzd.positionDamper = _originalYZDamper * multiplier;
            _joint.angularYZDrive = yzd;
        }

        public void SetCombatMode(bool active)
        {
            _isCombatActive = active;
        }

        public void SetLimp()
        {
            var xd = _joint.angularXDrive; xd.positionSpring = 0.5f; xd.positionDamper = 0f; _joint.angularXDrive = xd;
            var yd = _joint.angularYZDrive; yd.positionSpring = 0.5f; yd.positionDamper = 0f; _joint.angularYZDrive = yd;
        }

        private void FixedUpdate()
        {
            if (_isRootBone || targetBone == null) return;

            // 1. Rotation targeting
            Quaternion targetRot = targetBone.localRotation;
            _joint.targetRotation = _jointToLocalSpace
                                    * Quaternion.Inverse(targetRot)
                                    * _startingLocalRotation
                                    * _localToJointSpace;

            // 2. Gravity compensation
            if (gravityCompensation > 0f && _rb != null)
                _rb.AddForce(-Physics.gravity * gravityCompensation, ForceMode.Acceleration);

            // 3. Velocity sync (higher during combat for snappy punch)
            if (_rb != null)
            {
                Quaternion delta = targetBone.rotation * Quaternion.Inverse(transform.rotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;

                if (Mathf.Abs(angle) > 0.01f)
                {
                    float sync = _isCombatActive ? combatVelocitySync : velocitySync;
                    Vector3 worldAngVel = axis.normalized * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);
                    _rb.angularVelocity = Vector3.Lerp(_rb.angularVelocity, worldAngVel, sync);
                }
            }
        }
    }
}