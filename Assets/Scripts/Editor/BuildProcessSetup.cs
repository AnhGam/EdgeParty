using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace EdgeParty.Editor
{
    public class BuildProcessSetup : IProcessSceneWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            var nm = Object.FindFirstObjectByType<NetworkManager>();
            if (nm != null)
            {
                if (nm.NetworkConfig.PlayerPrefab == null)
                {
                    var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts/Gameplay/Character/Network_Player.prefab");
                    if (prefab != null)
                    {
                        nm.NetworkConfig.PlayerPrefab = prefab;
                        if (!nm.NetworkConfig.Prefabs.Contains(prefab))
                            nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });
                    }
                }

                var starter = Object.FindFirstObjectByType<EdgeParty.ConnectionManagement.EdgegapServerStarter>();
                if (starter == null)
                {
                    var mgmtObj = new GameObject("NetworkManagers_AutoInjected");
                    mgmtObj.AddComponent<EdgeParty.ConnectionManagement.EdgegapServerStarter>();
                    mgmtObj.AddComponent<EdgeParty.ConnectionManagement.ClientConnectionUI>();
                    Debug.Log("[BuildProcessSetup] Auto-injected EdgegapServerStarter and UI prior to build.");
                }
            }
        }
    }
}
