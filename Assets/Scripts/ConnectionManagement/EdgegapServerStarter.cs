using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EdgeParty.ConnectionManagement
{
    public class EdgegapServerStarter : MonoBehaviour
    {
        public string portMapName = "gameport";

        private void Start()
        {
            if (Application.isBatchMode)
            {
                var networkManager = NetworkManager.Singleton;
                if (networkManager == null)
                {
                    Debug.LogError("[EdgegapServerStarter] NetworkManager.Singleton is null. Please ensure NetworkManager exists in the scene.");
                    return;
                }

                networkManager.OnServerStarted += () => Debug.Log("[EdgegapServerStarter] OnServerStarted: Server is running & listening!");
                networkManager.OnServerStopped += (obj) => Debug.Log("[EdgegapServerStarter] OnServerStopped: Server shut down.");
                networkManager.OnClientConnectedCallback += (id) => Debug.Log($"[EdgegapServerStarter] OnClientConnected: Client {id} joined!");
                networkManager.OnClientDisconnectCallback += (id) => Debug.Log($"[EdgegapServerStarter] OnClientDisconnect: Client {id} left.");
                
                networkManager.NetworkConfig.ConnectionApproval = false;

                var utp = (UnityTransport)networkManager.NetworkConfig.NetworkTransport;
                utp.SetConnectionData("0.0.0.0", utp.ConnectionData.Port);

                string internalPortAsStr = Environment.GetEnvironmentVariable($"ARBITRIUM_PORT_{portMapName.ToUpper()}_INTERNAL");
                
                Debug.Log($"[EdgegapServerStarter] Detected ARBITRIUM_PORT: {internalPortAsStr}");

                if (!string.IsNullOrEmpty(internalPortAsStr) && ushort.TryParse(internalPortAsStr, out ushort edgegapPort))
                {
                    utp.SetConnectionData("0.0.0.0", edgegapPort);
                }
                else
                {
                    Debug.LogWarning($"[EdgegapServerStarter] Could not find port mapping. Make sure your app version port name matches with \"{portMapName}\".");
                }

                Debug.Log($"[EdgegapServerStarter] Calling StartServer() on Port {utp.ConnectionData.Port} (Listening on 0.0.0.0)...");
                
                if (networkManager.StartServer())
                {
                    NetworkManager.Singleton.SceneManager.LoadScene("DemoScene_Forest", LoadSceneMode.Single);
                }
            }
        }
    }
}
