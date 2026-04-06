using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace EdgeParty.ConnectionManagement
{
    /// <summary>
    /// Giao diện HUD cơ bản để nhập IP/Port và kết nối dành riêng cho Client.
    /// Tự động ẩn trên bản build Dedicated Server.
    /// </summary>
    public class ClientConnectionUI : MonoBehaviour
    {
        private string _serverIp = "127.0.0.1";
        private string _serverPort = "7777";
        private string _statusMsg = "";

#if !UNITY_SERVER || UNITY_EDITOR
        private void OnGUI()
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            var utp = (UnityTransport)networkManager.NetworkConfig.NetworkTransport;
            if (utp == null) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 300));

            if (!networkManager.IsClient && !networkManager.IsServer)
            {
                GUILayout.Label("--- EDGE PARTY CLIENT HUD ---");

                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", GUILayout.Width(50));
                _serverIp = GUILayout.TextField(_serverIp);
                GUILayout.EndHorizontal();

                // Lưu ý: Người chơi PHẢI nhập External Port lấy từ Edgegap
                GUILayout.BeginHorizontal();
                GUILayout.Label("Port:", GUILayout.Width(50));
                _serverPort = GUILayout.TextField(_serverPort);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("CONNECT TO SERVER"))
                {
                    if (ushort.TryParse(_serverPort, out ushort port))
                    {
                        utp.SetConnectionData(_serverIp, port);
                        _statusMsg = "Connecting...";
                        Debug.Log($"[ClientConnectionUI] Attempting connection to {_serverIp}:{port}...");
                        
                        if (!networkManager.StartClient())
                        {
                            _statusMsg = "Failed to start client!";
                            Debug.LogError("[ClientConnectionUI] StartClient() returned false.");
                        }
                    }
                    else
                    {
                        _statusMsg = "Invalid Port!";
                        Debug.LogError("[ClientConnectionUI] Invalid Port number!");
                    }
                }

                if (!string.IsNullOrEmpty(_statusMsg))
                {
                    GUILayout.Label($"Status: {_statusMsg}");
                }
            }
            else
            {
                string mode = networkManager.IsHost ? "Host" : (networkManager.IsServer ? "Server" : "Client");
                GUILayout.Label($"Mode: {mode}");
                GUILayout.Label($"Target: {utp.ConnectionData.Address}:{utp.ConnectionData.Port}");
                
                if (networkManager.IsConnectedClient)
                {
                     GUILayout.Label("Status: Connected!");
                }
                else if (networkManager.IsClient)
                {
                     GUILayout.Label("Status: Connecting/Waiting for Approval...");
                }

                if (GUILayout.Button("DISCONNECT"))
                {
                    networkManager.Shutdown();
                    _statusMsg = "Disconnected.";
                }
            }

            GUILayout.EndArea();
        }
#endif
    }
}
