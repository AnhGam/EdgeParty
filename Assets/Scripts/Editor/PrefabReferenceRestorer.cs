using UnityEngine;
using UnityEditor;
using EdgeParty.Gameplay.Character;

namespace EdgeParty.Editor
{
    public static class PrefabReferenceRestorer
    {
        [MenuItem("EdgeParty/🌟 ĐẠI TU: Khôi phục khớp nối Prefab Mạng")]
        public static void FormatNetworkPrefab()
        {
            string path = "Assets/Scripts/Gameplay/Character/Network_Player.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Lỗi", "Không tìm thấy Network_Player.prefab", "OK");
                return;
            }

            // Mở Prefab để chỉnh sửa sâu
            using (var editingScope = new PrefabUtility.EditPrefabContentsScope(path))
            {
                GameObject root = editingScope.prefabContentsRoot;

                // Tìm 2 nhánh chính
                Transform physicalRoot = null;
                Transform ghostRoot = null;

                foreach (Transform child in root.transform)
                {
                    if (child.name.EndsWith("_Ghost")) ghostRoot = child;
                    else physicalRoot = child; 
                }

                if (physicalRoot == null || ghostRoot == null)
                {
                    EditorUtility.DisplayDialog("Lỗi", "Không tìm thấy nhánh vật lý và nhánh bóng ma trong Prefab!", "OK");
                    return;
                }

                // --------- FIX PLAYER CONTROLLER ---------
                PlayerController pc = physicalRoot.GetComponent<PlayerController>();
                if (pc != null)
                {
                    pc.ghostRoot = ghostRoot;
                    pc.pelvisRigidbody = FindBoneByName(physicalRoot, "pelvis")?.GetComponent<Rigidbody>();
                    pc.ghostPelvis = FindBoneByName(ghostRoot, "pelvis");
                    pc.ghostAnimator = ghostRoot.GetComponentInChildren<Animator>();

                    if (pc.ghostAnimator == null)
                    {
                        pc.ghostAnimator = ghostRoot.gameObject.AddComponent<Animator>();
                    }

                    // Nếu mất Avatar, nhắc nhở (vì script không biết nguồn avatar gốc ở đâu)
                    if (pc.ghostAnimator.avatar == null)
                    {
                        Debug.LogWarning("[ĐẠI TU PREFAB] Animator của Ghost đang bị thiếu Avatar! Hãy bấm Đúp vào Network_Player.prefab, tìm file Ghost và nhét Avatar vào ô Animator.");
                    }
                    if (pc.ghostAnimator.runtimeAnimatorController == null)
                    {
                        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/MonkeyGameplay.controller");
                        pc.ghostAnimator.runtimeAnimatorController = controller;
                    }
                    pc.ghostAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                // --------- FIX BONE FOLLOWERS ---------
                var followers = physicalRoot.GetComponentsInChildren<RagdollBoneFollower>(true);
                int mapped = 0;
                foreach (var f in followers)
                {
                    string boneName = f.gameObject.name;
                    Transform ghostBoneTarget = FindBoneByName(ghostRoot, boneName);
                    if (ghostBoneTarget != null)
                    {
                        f.targetBone = ghostBoneTarget;
                        mapped++;
                    }
                }

                Debug.Log($"[ĐẠI TU PREFAB] Đã nối thành công {mapped} khớp xương từ Cục Vật Lý sang Bóng Ma!");
            }

            EditorUtility.DisplayDialog("Thành công", "Đã dập lại toàn bộ dây thần kinh điều khiển cho Prefab Network_Player.\n\nLưu ý: Bạn hãy kiểm tra xem Animator của thằng Ghost đã có cục Avatar màu xám xám chưa nhé (như bạn từng gắn tay đó).", "OK");
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
