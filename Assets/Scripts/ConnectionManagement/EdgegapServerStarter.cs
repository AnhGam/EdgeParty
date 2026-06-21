using System;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EdgeParty.ConnectionManagement
{
    public class EdgegapServerStarter : MonoBehaviour
    {
        public string portMapName = "gameport";

        private void Awake()
        {
            var networkManager = GetComponent<NetworkManager>();
            if (networkManager != null && networkManager.NetworkConfig != null)
            {
                networkManager.NetworkConfig.ConnectionApproval = false;
                networkManager.NetworkConfig.ForceSamePrefabs = true;
            }
        }

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

                // Guard: nếu server đã running (do Start() bị gọi lại sau scene reload) thì bỏ qua
                if (networkManager.IsServer || networkManager.IsListening)
                {
                    Debug.Log("[EdgegapServerStarter] Server already running (scene reloaded). Skipping re-initialization.");
                    return;
                }

                // Log all Arbitrium env variables to help troubleshoot port configurations
                try
                {
                    foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
                    {
                        string key = de.Key.ToString();
                        if (key.StartsWith("ARBITRIUM_"))
                        {
                            Debug.Log($"[EdgegapServerStarter] Env Var: {key} = {de.Value}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[EdgegapServerStarter] Failed to log environment variables: {ex.Message}");
                }


                networkManager.OnServerStarted += () => Debug.Log("[EdgegapServerStarter] OnServerStarted: Server is running & listening!");
                networkManager.OnServerStopped += (obj) => Debug.Log("[EdgegapServerStarter] OnServerStopped: Server shut down.");
                networkManager.OnClientConnectedCallback += (id) => Debug.Log($"[EdgegapServerStarter] OnClientConnected: Client {id} joined!");
                networkManager.OnClientDisconnectCallback += (id) => Debug.Log($"[EdgegapServerStarter] OnClientDisconnect: Client {id} left.");
                
                networkManager.NetworkConfig.ConnectionApproval = false;
                networkManager.NetworkConfig.ForceSamePrefabs = true;

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

                LogNetworkConfig("Server Startup");

                Debug.Log($"[EdgegapServerStarter] Calling StartServer() on Port {utp.ConnectionData.Port} (Listening on 0.0.0.0)...");
                
                if (networkManager.StartServer())
                {
                    Debug.Log("[EdgegapServerStarter] Server started successfully.");
                    NetworkManager.Singleton.SceneManager.LoadScene("DemoScene_Forest", LoadSceneMode.Single);
                }
                else
                {
                    Debug.LogError("[EdgegapServerStarter] Failed to start server.");
                }
            }
        }

        public static void LogNetworkConfig(string context)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.NetworkConfig == null)
            {
                Debug.LogWarning($"[{context}] NetworkManager or NetworkConfig is null!");
                return;
            }

            var config = nm.NetworkConfig;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{context}] --- NetworkConfig Debug Information ---");
            sb.AppendLine($"  Computed Hash: {config.GetConfig()}");
            sb.AppendLine($"  ProtocolVersion (User-defined): {config.ProtocolVersion}");

            // Read internal PROTOCOL_VERSION via reflection
            try
            {
                var netConstantsType = typeof(NetworkManager).Assembly.GetType("Unity.Netcode.NetworkConstants");
                var protocolField = netConstantsType?.GetField("PROTOCOL_VERSION", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                string internalProtocol = protocolField?.GetValue(null) as string ?? "unknown";
                sb.AppendLine($"  NetworkConstants.PROTOCOL_VERSION (Internal): {internalProtocol}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  NetworkConstants.PROTOCOL_VERSION (Internal): Error reading ({ex.Message})");
            }

            sb.AppendLine($"  TickRate: {config.TickRate}");
            sb.AppendLine($"  ConnectionApproval: {config.ConnectionApproval}");
            sb.AppendLine($"  ForceSamePrefabs: {config.ForceSamePrefabs}");
            sb.AppendLine($"  EnableSceneManagement: {config.EnableSceneManagement}");
            sb.AppendLine($"  EnsureNetworkVariableLengthSafety: {config.EnsureNetworkVariableLengthSafety}");
            sb.AppendLine($"  RpcHashSize: {config.RpcHashSize}");

            if (config.Prefabs != null)
            {
                sb.AppendLine($"  Prefabs.NetworkPrefabOverrideLinks Count: {config.Prefabs.NetworkPrefabOverrideLinks.Count}");
                // In ra các key (GlobalObjectIdHash) để xem có sự lệch danh sách prefab hay thứ tự không
                var sortedLinks = config.Prefabs.NetworkPrefabOverrideLinks.OrderBy(x => x.Key).ToList();
                for (int i = 0; i < sortedLinks.Count; i++)
                {
                    var link = sortedLinks[i].Value;
                    string srcName = link.SourcePrefabToOverride != null ? link.SourcePrefabToOverride.name : (link.Prefab != null ? link.Prefab.name : "null");
                    string tgtName = link.OverridingTargetPrefab != null ? link.OverridingTargetPrefab.name : "None";
                    sb.AppendLine($"    [{i}] Key: {sortedLinks[i].Key} (SourcePrefab: {srcName} -> TargetPrefab: {tgtName}, OverrideType: {link.Override})");
                }
            }
            else
            {
                sb.AppendLine("  Prefabs list is null");
            }

            Debug.Log(sb.ToString());
        }
    }
}
