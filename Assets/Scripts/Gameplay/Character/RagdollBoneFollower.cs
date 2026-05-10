using UnityEngine;

namespace EdgeParty.Gameplay.Character
{
    public enum BoneCategory { Torso, Arm, Leg, Head, Tail, Other }

    [RequireComponent(typeof(ConfigurableJoint))]
    public class RagdollBoneFollower : MonoBehaviour
    {
        public BoneCategory category;
        public Transform targetBone;

        [Header("Follow Settings")]
        [Range(0f, 1f)] public float gravityCompensation = 0f;
        [Range(0f, 1f)] public float velocitySync = 0.3f;
        
        [Header("Combat Boost")]
        [Tooltip("Velocity sync used during attack/dash (higher = follows animation more precisely)")]
        [Range(0f, 1f)] public float combatVelocitySync = 0.95f;

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
        private bool _isOneShotMode;

        private void Awake()
        {
            _joint = GetComponent<ConfigurableJoint>();
            _rb = GetComponent<Rigidbody>();
            
            // If this bone is the one with the CharacterMotor/PlayerController, it shouldn't follow itself
            _isRootBone = (GetComponent<PlayerController>() != null || GetComponent<CharacterMotor>() != null);

            if (_isRootBone || targetBone == null) return;

            _startingLocalRotation = transform.localRotation;

            // Setup joint space conversions
            Vector3 forward = Vector3.Cross(_joint.axis, _joint.secondaryAxis).normalized;
            Vector3 up = _joint.secondaryAxis;

            // Safety check for collinear axes
            if (forward.sqrMagnitude < 0.01f)
            {
                Debug.LogError($"[RagdollBoneFollower] Axis and SecondaryAxis are collinear on {name}! Physics will explode. Please make them perpendicular.");
                forward = Vector3.forward;
                up = Vector3.up;
            }

            _localToJointSpace = Quaternion.LookRotation(forward, up);
            _jointToLocalSpace = Quaternion.Inverse(_localToJointSpace);

            // Store original spring values
            _originalXSpring = _joint.angularXDrive.positionSpring;
            _originalXDamper = _joint.angularXDrive.positionDamper;
            _originalYZSpring = _joint.angularYZDrive.positionSpring;
            _originalYZDamper = _joint.angularYZDrive.positionDamper;
        }

        private void OnEnable()
        {
            // Restore springs when enabled
            SetSpringMultiplier(1f);
        }

        private void OnDisable()
        {
            // Go limp when disabled to avoid "infinite spinning" if targetRotation is stale
            if (_joint != null)
            {
                var xd = _joint.angularXDrive; xd.positionSpring = 0.1f; _joint.angularXDrive = xd;
                var yzd = _joint.angularYZDrive; yzd.positionSpring = 0.1f; _joint.angularYZDrive = yzd;
            }
        }

        public void SetSpringMultiplier(float multiplier)
        {
            if (_joint == null) return;

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

        public void SetOneShotMode(bool active)
        {
            _isOneShotMode = active;
        }

        private bool _forceNaturalPose = false;

        public void SetNaturalPose(bool enabled)
        {
            _forceNaturalPose = enabled;
        }

        private void FixedUpdate()
        {
            if (_isRootBone || targetBone == null) return;

            // Nếu đang ép về tư thế tự nhiên (trạng thái None), ta bỏ qua animation của Ghost.
            // Ngược lại, ta lấy rotation từ targetBone (Ghost).
            Quaternion targetRot = _forceNaturalPose ? _startingLocalRotation : targetBone.localRotation;

            _joint.targetRotation = _jointToLocalSpace
                                    * Quaternion.Inverse(targetRot)
                                    * _startingLocalRotation
                                    * _localToJointSpace;

            // 2. Gravity compensation
            if (gravityCompensation > 0.01f)
            {
                _rb.AddForce(-Physics.gravity * (_rb.mass * gravityCompensation), ForceMode.Force);
            }

            // 3. Velocity sync
            // DEPRECATED: Position snapping removed to allow physical movement independent of ghost position.
        }
    }
}