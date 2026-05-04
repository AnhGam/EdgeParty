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
        [Range(0f, 1f)] public float velocitySync = 0.5f;

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

        private void Awake()
        {
            _joint = GetComponent<ConfigurableJoint>();
            _rb = GetComponent<Rigidbody>();
            _startingLocalRotation = transform.localRotation;
            _isRootBone = (_joint.connectedBody == null);

            // Cache original joint settings
            _originalXSpring = _joint.angularXDrive.positionSpring;
            _originalXDamper = _joint.angularXDrive.positionDamper;
            _originalYZSpring = _joint.angularYZDrive.positionSpring;
            _originalYZDamper = _joint.angularYZDrive.positionDamper;

            // Joint space calculation
            Vector3 forward = Vector3.Cross(_joint.axis, _joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, _joint.axis).normalized;
            _localToJointSpace = Quaternion.LookRotation(forward, up);
            _jointToLocalSpace = Quaternion.Inverse(_localToJointSpace);
        }

        public void SetSpringMultiplier(float multiplier)
        {
            var xDrive = _joint.angularXDrive;
            xDrive.positionSpring = _originalXSpring * multiplier;
            xDrive.positionDamper = _originalXDamper * multiplier; 
            _joint.angularXDrive = xDrive;

            var yzDrive = _joint.angularYZDrive;
            yzDrive.positionSpring = _originalYZSpring * multiplier;
            yzDrive.positionDamper = _originalYZDamper * multiplier;
            _joint.angularYZDrive = yzDrive;
        }

        public void SetLimp()
        {
            var xDrive = _joint.angularXDrive;
            xDrive.positionSpring = 0.5f;
            xDrive.positionDamper = 0f;
            _joint.angularXDrive = xDrive;

            var yzDrive = _joint.angularYZDrive;
            yzDrive.positionSpring = 0.5f;
            yzDrive.positionDamper = 0f;
            _joint.angularYZDrive = yzDrive;
        }

        private void FixedUpdate()
        {
            if (_isRootBone || targetBone == null) return;

            // 1. Position/Rotation Sync
            Quaternion targetRot = targetBone.localRotation;
            Quaternion resultRotation = _jointToLocalSpace
                                        * Quaternion.Inverse(targetRot)
                                        * _startingLocalRotation
                                        * _localToJointSpace;

            _joint.targetRotation = resultRotation;

            // 2. Gravity Compensation
            if (gravityCompensation > 0 && _rb != null)
            {
                _rb.AddForce(-Physics.gravity * gravityCompensation, ForceMode.Acceleration);
            }

            // 3. Angular Velocity Matching (Combat Power)
            if (velocitySync > 0 && _rb != null)
            {
                Quaternion delta = targetBone.rotation * Quaternion.Inverse(transform.rotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;

                if (Mathf.Abs(angle) > 0.01f)
                {
                    Vector3 worldAngularVel = axis.normalized * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime);
                    _rb.angularVelocity = Vector3.Lerp(_rb.angularVelocity, worldAngularVel, velocitySync);
                }
            }
        }
    }
}
