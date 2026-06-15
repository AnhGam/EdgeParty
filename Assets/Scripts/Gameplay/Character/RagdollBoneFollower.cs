using UnityEngine;

namespace EdgeParty.Gameplay.Character
{
    public enum BoneCategory { Torso, Arm, Leg, Head, Tail, Other }

    [RequireComponent(typeof(ConfigurableJoint))]
    public class RagdollBoneFollower : MonoBehaviour
    {
        public BoneCategory category;
        public Transform targetBone;

        private ConfigurableJoint _joint;
        private Quaternion _startingLocalRotation;
        private Quaternion _localToJointSpace;
        private Quaternion _jointToLocalSpace;

        private float _originalXSpring;
        private float _originalXDamper;
        private float _originalYZSpring;
        private float _originalYZDamper;

        private float _originalSlerpSpring;
        private float _originalSlerpDamper;

        private bool _isRootBone;
        private ConfigurableJointMotion _originalAngularXMotion;
        private ConfigurableJointMotion _originalAngularYMotion;
        private ConfigurableJointMotion _originalAngularZMotion;

        private void Awake()
        {
            _joint = GetComponent<ConfigurableJoint>();
            _isRootBone = (_joint.connectedBody == null);
            _startingLocalRotation = transform.localRotation;
            _originalAngularXMotion = _joint.angularXMotion;
            _originalAngularYMotion = _joint.angularYMotion;
            _originalAngularZMotion = _joint.angularZMotion;

            // Cache the hand-tuned spring and damper values from the Joint Inspector
            _originalXSpring = _joint.angularXDrive.positionSpring;
            _originalXDamper = _joint.angularXDrive.positionDamper;
            _originalYZSpring = _joint.angularYZDrive.positionSpring;
            _originalYZDamper = _joint.angularYZDrive.positionDamper;

            _originalSlerpSpring = _joint.slerpDrive.positionSpring;
            _originalSlerpDamper = _joint.slerpDrive.positionDamper;

            // Joint space calculation (mstevenson/ConfigurableJointExtensions)
            Vector3 forward = Vector3.Cross(_joint.axis, _joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, _joint.axis).normalized;

            // Safety check for collinear axes
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
                up = Vector3.up;
            }

            _localToJointSpace = Quaternion.LookRotation(forward, up);
            _jointToLocalSpace = Quaternion.Inverse(_localToJointSpace);
        }

        /// <summary>
        /// Scales the original Inspector spring and damper values by the given multiplier.
        /// This preserves the user's hand-tuned balance ratio.
        /// </summary>
        public void SetSpringMultiplier(float multiplier)
        {
            if (_joint == null) return;

            var xDrive = _joint.angularXDrive;
            xDrive.positionSpring = _originalXSpring * multiplier;
            xDrive.positionDamper = _originalXDamper * multiplier; 
            _joint.angularXDrive = xDrive;

            var yzDrive = _joint.angularYZDrive;
            yzDrive.positionSpring = _originalYZSpring * multiplier;
            yzDrive.positionDamper = _originalYZDamper * multiplier;
            _joint.angularYZDrive = yzDrive;

            var slerpDrive = _joint.slerpDrive;
            slerpDrive.positionSpring = _originalSlerpSpring * multiplier;
            slerpDrive.positionDamper = _originalSlerpDamper * multiplier;
            _joint.slerpDrive = slerpDrive;
        }

        /// <summary>
        /// Makes the bone limp (normal ragdoll).
        /// </summary>
        public void SetLimp()
        {
            if (_joint == null) return;

            var xDrive = _joint.angularXDrive;
            xDrive.positionSpring = 0.5f; // Very low to allow physics interaction
            xDrive.positionDamper = 0f;
            _joint.angularXDrive = xDrive;

            var yzDrive = _joint.angularYZDrive;
            yzDrive.positionSpring = 0.5f;
            yzDrive.positionDamper = 0f;
            _joint.angularYZDrive = yzDrive;

            var slerpDrive = _joint.slerpDrive;
            slerpDrive.positionSpring = 0.5f;
            slerpDrive.positionDamper = 0f;
            _joint.slerpDrive = slerpDrive;
        }

        /// <summary>
        /// Mở khoá toàn bộ giới hạn góc quay để ragdoll gục tự do theo mọi hướng.
        /// </summary>
        public void UnlockAllLimits()
        {
            if (_joint == null) return;
            _joint.angularXMotion = ConfigurableJointMotion.Free;
            _joint.angularYMotion = ConfigurableJointMotion.Free;
            _joint.angularZMotion = ConfigurableJointMotion.Free;
        }

        /// <summary>
        /// Khôi phục các giới hạn góc quay ban đầu được thiết lập trong Inspector.
        /// </summary>
        public void RestoreLimits()
        {
            if (_joint == null) return;
            _joint.angularXMotion = _originalAngularXMotion;
            _joint.angularYMotion = _originalAngularYMotion;
            _joint.angularZMotion = _originalAngularZMotion;
        }

        private CharacterAnimationController _animController;

        private void Start()
        {
            _animController = transform.root.GetComponentInChildren<CharacterAnimationController>();
        }

        private void FixedUpdate()
        {
            // Root bone (pelvis) rotation is driven by CharacterMotor/Movement,
            // setting targetRotation here would fight against it.
            if (_isRootBone || targetBone == null) return;

            Quaternion targetRot = targetBone.localRotation;

            if (category == BoneCategory.Leg && _animController != null)
            {
                bool isDashing = false;
                if (_animController.IsPlayingOneShot && _animController.ghostAnimator != null)
                {
                    var info = _animController.ghostAnimator.GetCurrentAnimatorStateInfo(0);
                    if (info.IsName(_animController.dashState))
                    {
                        isDashing = true;
                    }
                }

                if (isDashing)
                {
                    if (_joint.angularXMotion != ConfigurableJointMotion.Locked)
                    {
                        _joint.angularXMotion = ConfigurableJointMotion.Locked;
                    }
                    targetRot = Quaternion.identity;
                }
                else
                {
                    if (_joint.angularXMotion != _originalAngularXMotion)
                    {
                        _joint.angularXMotion = _originalAngularXMotion;
                    }
                }
            }

            Quaternion resultRotation = _jointToLocalSpace
                                        * Quaternion.Inverse(targetRot)
                                        * _startingLocalRotation
                                        * _localToJointSpace;

            _joint.targetRotation = resultRotation;
        }
    }
}
