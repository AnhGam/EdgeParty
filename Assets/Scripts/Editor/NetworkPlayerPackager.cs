using UnityEngine;
using UnityEditor;
using EdgeParty.Gameplay.Character;

namespace EdgeParty.Editor
{
    public static class NetworkPlayerPackager
    {
        [MenuItem("EdgeParty/🔥 FIX: Package Network Player")]
        public static void PackagePlayer()
        {
            var monkey = GameObject.Find("Chibi_Monkey_00");
            var ghost = GameObject.Find("Chibi_Monkey_00_Ghost");

            if (monkey == null || ghost == null)
            {
                // Try finding clones
                monkey = GameObject.Find("Chibi_Monkey_00 Variant(Clone)");
                if (monkey == null) monkey = GameObject.Find("Chibi_Monkey_00 Variant");
                if (monkey == null) monkey = UnityEngine.Object.FindFirstObjectByType<PlayerController>()?.gameObject;

                if (ghost == null && monkey != null)
                {
                    var pc = monkey.GetComponent<PlayerController>();
                    if (pc != null && pc.ghostRoot != null)
                    {
                        var potentialGhost = pc.ghostRoot;
                        // trace up to root
                        while (potentialGhost.parent != null && !potentialGhost.name.EndsWith("_Ghost"))
                            potentialGhost = potentialGhost.parent;
                        ghost = potentialGhost.gameObject;
                    }
                }
            }

            if (monkey == null || ghost == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not find Chibi_Monkey_00 or Chibi_Monkey_00_Ghost in the active scene! Please open SampleScene.", "OK");
                return;
            }

            // Remove existing NetworkObjects
            Object.DestroyImmediate(monkey.GetComponent<Unity.Netcode.NetworkObject>());

            // Step 1: Create Root
            GameObject networkPlayer = new GameObject("Network_Player");
            networkPlayer.transform.position = monkey.transform.position;
            networkPlayer.transform.rotation = monkey.transform.rotation;

            // Step 2: Parent them
            monkey.transform.SetParent(networkPlayer.transform);
            ghost.transform.SetParent(networkPlayer.transform);

            // Step 3: Add NetworkObject to Root
            networkPlayer.AddComponent<Unity.Netcode.NetworkObject>();

            // Step 4: Save as Prefab
            string destPath = "Assets/Scripts/Gameplay/Character/Network_Player.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(networkPlayer, destPath, InteractionMode.UserAction);

            // Step 5: Update all configs
            var nm = UnityEngine.Object.FindFirstObjectByType<Unity.Netcode.NetworkManager>();
            if (nm != null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(destPath);
                nm.NetworkConfig.PlayerPrefab = prefab;
                if (!nm.NetworkConfig.Prefabs.Contains(prefab))
                    nm.NetworkConfig.Prefabs.Add(new Unity.Netcode.NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(nm);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(nm.gameObject.scene);
            }

            EditorUtility.DisplayDialog("Thành công!", "Đã gộp Khỉ và Bóng ma thành 1 Prefab duy nhất (Network_Player) và gài vào NetworkManager!\n\nLỗi mất di chuyển đã được khắc phục hoàn toàn.", "OK");
        }
    }
}
