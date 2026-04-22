using UnityEngine;

namespace EdgeParty.Gameplay.Character
{
    public enum BoneCategory { Torso, Head, Arm, Leg, Tail }

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

        // Root bone (pelvis) rotation is handled by PlayerController.MoveRotation,
        // not by spring targetRotation, to avoid the two fighting each other.
        private bool _isRootBone;

        private void Awake()
        {
            _joint = GetComponent<ConfigurableJoint>();
            _startingLocalRotation = transform.localRotation;
            _isRootBone = (_joint.connectedBody == null);

            // Cache the hand-tuned spring and damper values from the Joint Inspector
            _originalXSpring = _joint.angularXDrive.positionSpring;
            _originalXDamper = _joint.angularXDrive.positionDamper;
            _originalYZSpring = _joint.angularYZDrive.positionSpring;
            _originalYZDamper = _joint.angularYZDrive.positionDamper;

            // Joint space calculation (mstevenson/ConfigurableJointExtensions)
            Vector3 forward = Vector3.Cross(_joint.axis, _joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, _joint.axis).normalized;
            _localToJointSpace = Quaternion.LookRotation(forward, up);
            _jointToLocalSpace = Quaternion.Inverse(_localToJointSpace);
        }

        /// <summary>
        /// Scales the original Inspector spring and damper values by the given multiplier.
        /// This preserves the user's hand-tuned balance ratio.
        /// </summary>
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

        /// <summary>
        /// Makes the bone limp (normal ragdoll).
        /// </summary>
        public void SetLimp()
        {
            var xDrive = _joint.angularXDrive;
            xDrive.positionSpring = 0.5f; // Very low to allow physics interaction
            xDrive.positionDamper = 0f;
            _joint.angularXDrive = xDrive;

            var yzDrive = _joint.angularYZDrive;
            yzDrive.positionSpring = 0.5f;
            yzDrive.positionDamper = 0f;
            _joint.angularYZDrive = yzDrive;
        }

        private void FixedUpdate()
        {
            // Root bone (pelvis) rotation is driven by PlayerController.MoveRotation.
            // Setting targetRotation here would fight against it.
            if (_isRootBone || targetBone == null) return;

            Quaternion targetRot = targetBone.localRotation;
            Quaternion resultRotation = _jointToLocalSpace
                                        * Quaternion.Inverse(targetRot)
                                        * _startingLocalRotation
                                        * _localToJointSpace;

            _joint.targetRotation = resultRotation;
        }
    }
}
