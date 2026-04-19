using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Linq;

namespace EdgeParty.Editor
{
    public static class CreateGameplayAnimator
    {
        [MenuItem("EdgeParty/Create Gameplay Animator")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Animations"))
                AssetDatabase.CreateFolder("Assets", "Animations");

            string path = "Assets/Animations/MonkeyGameplay.controller";
            
            // Delete old one to be sure
            AssetDatabase.DeleteAsset(path);
            
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var rootSM = controller.layers[0].stateMachine;

            // 1. Create States
            var noneState = rootSM.AddState("None");
            var idleState = rootSM.AddState("IdleA");
            var walkState = rootSM.AddState("Walk");
            var runState = rootSM.AddState("Run");
            var jumpState = rootSM.AddState("Jump");
            var dashState = rootSM.AddState("Dash");

            rootSM.defaultState = noneState;

            // 2. Assign clips from your specific FBX files
            AssignClipFromFBX(idleState, "Anim_Chibi@IdleA");
            AssignClipFromFBX(walkState, "Anim_Chibi@Walk", true);
            AssignClipFromFBX(runState, "Anim_Chibi@Run", true);
            AssignClipFromFBX(jumpState, "Anim_Chibi@Jump");
            AssignClipFromFBX(dashState, "Anim_Chibi@Dash");

            AssetDatabase.SaveAssets();
            Selection.activeObject = controller;
            EditorGUIUtility.PingObject(controller);

            EditorUtility.DisplayDialog("Xong!", "Đã tạo Animator và TỰ ĐỘNG gán clip.\nNếu 'Clip Count' vẫn là 0, hãy kiểm tra Console để xem tên file nào bị sai.", "OK");
        }

        private static void AssignClipFromFBX(AnimatorState state, string fbxName, bool loop = false)
        {
            // Find the FBX asset
            string[] guids = AssetDatabase.FindAssets(fbxName + " t:Model");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[EdgeParty] Không tìm thấy file FBX: " + fbxName);
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            
            // Load ALL assets at this path (FBX contains mesh, materials, and ANIMATION CLIPS)
            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            
            // Find the first animation clip that isn't the "__preview__" one
            AnimationClip clip = allAssets.OfType<AnimationClip>().FirstOrDefault(c => !c.name.Contains("__preview__"));

            if (clip != null)
            {
                state.motion = clip;
                if (loop)
                {
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = true;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                }
                Debug.Log($"[EdgeParty] Đã gán {clip.name} vào State {state.name}");
            }
            else
            {
                Debug.LogWarning("[EdgeParty] Không tìm thấy Clip bên trong FBX: " + fbxName);
            }
        }
    }
}
