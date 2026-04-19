using UnityEditor;
using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Editor
{
    [InitializeOnLoad]
    public class AdvancedNetworkSetup
    {
        static AdvancedNetworkSetup()
        {
            EditorApplication.delayCall += ExecuteSetup;
        }

        private static void ExecuteSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            string prefabPath = "Assets/Scripts/Gameplay/Character/Network_Player.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null) return;

            bool changed = false;

            // 1. Setup NetworkObject
            if (prefab.GetComponent<NetworkObject>() == null)
            {
                prefab.AddComponent<NetworkObject>();
                changed = true;
                Debug.Log("[AdvancedNetworkSetup] Added NetworkObject to Player Prefab.");
            }

            // 2. Setup ActiveRagdollSyncer
            if (prefab.GetComponent<EdgeParty.ConnectionManagement.ActiveRagdollSyncer>() == null)
            {
                prefab.AddComponent<EdgeParty.ConnectionManagement.ActiveRagdollSyncer>();
                changed = true;
                Debug.Log("[AdvancedNetworkSetup] Added ActiveRagdollSyncer to Player Prefab.");
            }

            if (changed)
            {
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
            }

            // 3. Register to NetworkManager in Active Scene
            var nm = GameObject.FindFirstObjectByType<NetworkManager>();
            if (nm != null && nm.NetworkConfig != null)
            {
                bool nmChanged = false;
                if (nm.NetworkConfig.PlayerPrefab == null)
                {
                    nm.NetworkConfig.PlayerPrefab = prefab;
                    nmChanged = true;
                }
                
                if (!nm.NetworkConfig.Prefabs.Contains(prefab))
                {
                    nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });
                    nmChanged = true;
                }

                var starter = GameObject.FindFirstObjectByType<EdgeParty.ConnectionManagement.EdgegapServerStarter>();
                if (starter == null)
                {
                    var mgmtObj = new GameObject("NetworkManagers_AutoInjected");
                    mgmtObj.AddComponent<EdgeParty.ConnectionManagement.EdgegapServerStarter>();
                    mgmtObj.AddComponent<EdgeParty.ConnectionManagement.ClientConnectionUI>();
                    nmChanged = true;
                    Debug.Log("[AdvancedNetworkSetup] Auto-injected EdgegapServerStarter and UI into active scene.");
                }

                if (nmChanged)
                {
                    EditorUtility.SetDirty(nm);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(nm.gameObject.scene);
                    Debug.Log("[AdvancedNetworkSetup] Configured NetworkManager with Player Prefab.");
                }
            }
        }
    }
}
