using UnityEngine;
using UnityEditor;
using Unity.Netcode;

namespace EdgeParty.Editor
{
    public static class PrefabRepairTool
    {
        [MenuItem("EdgeParty/🔥 FIX Repair Network Prefab")]
        public static void Repair()
        {
            string path = "Assets/Scripts/Gameplay/Character/Network_Player.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return;

            bool modified = false;

            // Xóa các rác script bị mất (Unknown script)
            int removedCount = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(prefab);
            if (removedCount > 0)
            {
                modified = true;
                Debug.Log($"[Repair] Đã xóa {removedCount} script bị rác (Unknown/Missing).");
            }

            // Find any child NetworkObjects and destroy them (only root should have one)
            var netObjs = prefab.GetComponentsInChildren<NetworkObject>(true);
            foreach (var no in netObjs)
            {
                if (no.gameObject != prefab)
                {
                    Object.DestroyImmediate(no, true);
                    modified = true;
                    Debug.Log("[Repair] Removed illegal Nested NetworkObject from " + no.gameObject.name);
                }
            }

            if (modified)
            {
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
                EditorUtility.DisplayDialog("Repair", "Đã diệt tận gốc lỗi Nested NetworkObject rác trên Prefab!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Repair", "Prefab đang hoàn hảo, không có lỗi NetworkObject dư thừa.", "OK");
            }
        }
    }
}
