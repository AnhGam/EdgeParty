using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using EdgeParty.Gameplay.Character;

namespace EdgeParty.Editor
{
    public static class ActiveRagdollCreator
    {
        [MenuItem("EdgeParty/Setup Active Ragdoll (Gang Beasts Style)")]
        public static void Create()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Error", "Please select your Chibi_Monkey_00 in the Hierarchy first.", "OK");
                return;
            }

            Transform ragdollPelvis = FindBoneByName(selected.transform, "pelvis");
            if (ragdollPelvis == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not find 'pelvis' bone in the selected object.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Setup Active Ragdoll");
            int group = Undo.GetCurrentGroup();

            GameObject ghost = GameObject.Instantiate(selected);
            ghost.name = selected.name + "_Ghost";
            Undo.RegisterCreatedObjectUndo(ghost, "Create Ghost");

            // 1. Remove networking components FIRST before they initialize/register
            var netObjects = ghost.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true);
            foreach (var netObj in netObjects) Object.DestroyImmediate(netObj);
            
            var netBehaviors = ghost.GetComponentsInChildren<Unity.Netcode.NetworkBehaviour>(true);
            foreach (var netBeh in netBehaviors) Object.DestroyImmediate(netBeh);

            // 2. Remove scripts next (because they might depend on joints)
            foreach (var comp in ghost.GetComponentsInChildren<PlayerController>(true)) Object.DestroyImmediate(comp);
            foreach (var comp in ghost.GetComponentsInChildren<RagdollBoneFollower>(true)) Object.DestroyImmediate(comp);
            
            // 3. Remove joints
            foreach (var joint in ghost.GetComponentsInChildren<Joint>(true)) Object.DestroyImmediate(joint);
            
            // 4. Remove rigidbodies and colliders
            foreach (var rb in ghost.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            foreach (var col in ghost.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

            // Make the Ghost invisible but allow Animator to run
            foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;

            // Save Avatar from the source model's Animator BEFORE we destroy it.
            // The Avatar is the skeleton mapping — without it, animations play but
            // no bone transforms are driven. This was the "no animation" root cause.
            Avatar sourceAvatar = null;
            RuntimeAnimatorController sourceController = null;
            foreach (var anim in selected.GetComponentsInChildren<Animator>(true))
            {
                if (anim.avatar != null) sourceAvatar = anim.avatar;
                if (anim.runtimeAnimatorController != null) sourceController = anim.runtimeAnimatorController;
            }

            Animator ghostAnim = ghost.GetComponentInChildren<Animator>(true);
            if (ghostAnim == null) ghostAnim = Undo.AddComponent<Animator>(ghost);
            
            // CRITICAL: Assign the Avatar so the Animator knows which transforms to drive
            ghostAnim.avatar = sourceAvatar;

            // IMPORTANT: Ghost must always animate even when invisible
            ghostAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ghostAnim.applyRootMotion = false;
            
            string controllerPath = "Assets/Animations/MonkeyGameplay.controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            ghostAnim.runtimeAnimatorController = controller != null ? controller : sourceController;

            if (ghostAnim.avatar == null)
            {
                Debug.LogWarning("[ActiveRagdollCreator] Could not find Avatar on source model! " +
                    "You need to manually assign the Avatar in the Ghost's Animator Inspector.");
            }

            // Remove Animator from the physical model (it's now on the ghost)
            foreach (var anim in selected.GetComponentsInChildren<Animator>(true))
                Undo.DestroyObjectImmediate(anim);


            PlayerController pc = selected.GetComponent<PlayerController>();
            if (pc == null) pc = Undo.AddComponent<PlayerController>(selected);
            
            pc.pelvisRigidbody = ragdollPelvis.GetComponent<Rigidbody>();
            pc.ghostRoot = ghost.transform;
            pc.ghostPelvis = FindBoneByName(ghost.transform, "pelvis");
            pc.ghostAnimator = ghostAnim;

            // Remove any conflicting scripts
            foreach (var comp in selected.GetComponentsInChildren<RagdollAnimationFollower>(true)) Object.DestroyImmediate(comp);

            int mappedBones = 0;
            int skippedBones = 0;
            var ragdollJoints = selected.GetComponentsInChildren<ConfigurableJoint>(true);
            foreach (var joint in ragdollJoints)
            {
                string boneName = joint.gameObject.name.ToLower();
                
                // --- ARCHITECTURAL FIX: ONLY IGNORE TRUE END-EFFECTORS ---
                // Physics needs authority over feet and hands for balance and grounding.
                // Thighs, CALVES, arms, and LOWERARMS MUST be driven to maintain posture and prevent buckling.
                bool isEndEffector = boneName.Contains("foot")     || 
                                     boneName.Contains("hand")     ||
                                     boneName.Contains("toe")      ||
                                     boneName.Contains("finger");

                if (isEndEffector)
                {
                    skippedBones++;
                    continue; 
                }

                Transform ghostBone = FindBoneByName(ghost.transform, joint.gameObject.name);
                if (ghostBone != null)
                {
                    RagdollBoneFollower follower = joint.gameObject.GetComponent<RagdollBoneFollower>();
                    if (follower == null) follower = Undo.AddComponent<RagdollBoneFollower>(joint.gameObject);
                    follower.targetBone = ghostBone;
                    follower.category = GuessCategory(joint.gameObject.name);
                    mappedBones++;
                }
            }

            ghost.transform.position = selected.transform.position;
            ghost.transform.rotation = selected.transform.rotation;

            Undo.CollapseUndoOperations(group);

            EditorUtility.DisplayDialog("Thành công!", 
                $"Đã kết nối Active Ragdoll cho {mappedBones} xương!\n\n" +
                $"- Đã bỏ qua {skippedBones} xương đầu mút (End-effector) để Physics tự xử lý.\n" +
                $"- Các thông số Joint (Spring/Damper) được GIỮ NGUYÊN theo ý bạn.\n" +
                $"- Đã tự động map Ghost bones vào RagdollBoneFollower.\n\n" +
                $"Bấm Play để kiểm tra.", "OK");


        }

        private static BoneCategory GuessCategory(string name)
        {
            string lowerName = name.ToLower();
            if (lowerName.Contains("leg") || lowerName.Contains("foot") || lowerName.Contains("toe")) return BoneCategory.Leg;
            if (lowerName.Contains("arm") || lowerName.Contains("hand") || lowerName.Contains("finger") || lowerName.Contains("shoulder")) return BoneCategory.Arm;
            if (lowerName.Contains("head") || lowerName.Contains("neck")) return BoneCategory.Head;
            return BoneCategory.Torso;
        }

        private static Transform FindBoneByName(Transform root, string boneName)
        {
            if (root.name == boneName) return root;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == boneName) return child;
            }
            return null;
        }
    }
}
