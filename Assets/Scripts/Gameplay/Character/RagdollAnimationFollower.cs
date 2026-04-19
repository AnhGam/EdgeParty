using UnityEngine;
using System;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Copies bone rotations from an animated reference skeleton onto the
    /// ConfigurableJoints of a ragdoll, making the ragdoll "follow" the animation
    /// through physics spring forces.
    /// 
    /// Based on the Active Ragdoll technique used in Gang Beasts / Very Very Valet.
    /// Uses the correct mstevenson/ConfigurableJointExtensions joint-space formula.
    /// </summary>
    public class RagdollAnimationFollower : MonoBehaviour
    {
        [Serializable]
        public struct BonePair
        {
            [Tooltip("The ConfigurableJoint on the ragdoll bone.")]
            public ConfigurableJoint ragdollJoint;

            [Tooltip("The corresponding bone Transform on the animated reference.")]
            public Transform referenceBone;

            [HideInInspector]
            public Quaternion initialLocalRotation;

            [HideInInspector]
            public Quaternion localToJointSpace;

            [HideInInspector]
            public Quaternion jointToLocalSpace;
        }

        [Header("Bone Mapping")]
        [Tooltip("Pairs of ragdoll joints and their corresponding animated reference bones.")]
        public BonePair[] bonePairs;

        [Header("Strength")]
        [Tooltip("Global multiplier for joint spring forces. Higher = stiffer follow.")]
        [Range(0f, 10f)]
        public float strengthMultiplier = 1f;

        [Tooltip("Base spring value applied to AngularXDrive and AngularYZDrive.")]
        public float baseSpring = 300f;

        [Tooltip("Damping applied to reduce oscillation.")]
        public float baseDamper = 10f;

        private void Start()
        {
            if (bonePairs == null || bonePairs.Length == 0)
            {
                Debug.LogWarning("[RagdollAnimationFollower] No bone pairs assigned. Script will do nothing.");
                return;
            }

            // Cache each joint's initial local rotation (the bind pose) and joint space
            for (int i = 0; i < bonePairs.Length; i++)
            {
                if (bonePairs[i].ragdollJoint != null)
                {
                    bonePairs[i].initialLocalRotation = bonePairs[i].ragdollJoint.transform.localRotation;

                    // Compute joint coordinate system (mstevenson formula)
                    var joint = bonePairs[i].ragdollJoint;
                    Vector3 jointX = joint.axis;
                    Vector3 jointZ = Vector3.Cross(jointX, joint.secondaryAxis).normalized;
                    Vector3 jointY = Vector3.Cross(jointZ, jointX).normalized;

                    bonePairs[i].localToJointSpace = Quaternion.LookRotation(jointZ, jointY);
                    bonePairs[i].jointToLocalSpace = Quaternion.Inverse(bonePairs[i].localToJointSpace);
                }
            }
        }

        private void FixedUpdate()
        {
            if (bonePairs == null) return;

            for (int i = 0; i < bonePairs.Length; i++)
            {
                var pair = bonePairs[i];
                if (pair.ragdollJoint == null || pair.referenceBone == null) continue;

                // CORRECT targetRotation formula (mstevenson/ConfigurableJointExtensions)
                // 1. Compute delta from current animation pose to bind pose
                Quaternion targetLocalRotation = pair.referenceBone.localRotation;
                Quaternion deltaRotation = Quaternion.Inverse(targetLocalRotation) * pair.initialLocalRotation;

                // 2. Transform delta into joint-space coordinates
                pair.ragdollJoint.targetRotation = pair.jointToLocalSpace * deltaRotation * pair.localToJointSpace;

                // Apply dynamic spring strength
                float spring = baseSpring * strengthMultiplier;
                float damper = baseDamper * strengthMultiplier;

                var xDrive = pair.ragdollJoint.angularXDrive;
                xDrive.positionSpring = spring;
                xDrive.positionDamper = damper;
                pair.ragdollJoint.angularXDrive = xDrive;

                var yzDrive = pair.ragdollJoint.angularYZDrive;
                yzDrive.positionSpring = spring;
                yzDrive.positionDamper = damper;
                pair.ragdollJoint.angularYZDrive = yzDrive;
            }
        }

        /// <summary>
        /// Temporarily reduce strength (e.g., when the character is "stunned" or ragdolled).
        /// </summary>
        public void SetStrength(float multiplier)
        {
            strengthMultiplier = Mathf.Clamp(multiplier, 0f, 10f);
        }
    }
}
