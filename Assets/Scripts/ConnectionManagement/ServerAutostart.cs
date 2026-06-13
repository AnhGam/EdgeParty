using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.ConnectionManagement
{
    public static class ServerAutostart
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBoot()
        {
            if (Application.isBatchMode)
            {
                Debug.Log("[ServerAutostart] Batch mode (headless server) detected. Checking for NetworkManager...");
                
                var networkManager = Object.FindAnyObjectByType<NetworkManager>();
                if (networkManager != null)
                {
                    Debug.Log("[ServerAutostart] NetworkManager already present in the scene.");
                    return;
                }

                Debug.Log("[ServerAutostart] NetworkManager not found in startup scene. Attempting to load from Resources...");
                
                var prefab = Resources.Load<GameObject>("NetworkManager");
                if (prefab != null)
                {
                    var go = Object.Instantiate(prefab);
                    go.name = "NetworkManager (Auto-Bootstrapped)";
                    Debug.Log("[ServerAutostart] NetworkManager prefab loaded and instantiated successfully.");
                }
                else
                {
                    Debug.LogError("[ServerAutostart] ERROR: Could not find 'NetworkManager' prefab inside the Resources folder! " +
                                   "Please create a folder named 'Resources' inside 'Assets' (Assets/Resources/) " +
                                   "and move your 'NetworkManager.prefab' there so the headless server can load it on startup.");
                }
            }
        }
    }
}
