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


        private void Start()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                nm.OnClientConnectedCallback += OnClientConnected;
                nm.OnClientDisconnectCallback += OnClientDisconnect;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
                
                // BIỆN PHÁP AN TOÀN: Nếu tắt UI mà manager vẫn chạy, ép shutdown để nhả Port
                if (NetworkManager.Singleton.IsListening)
                {
                    NetworkManager.Singleton.Shutdown();
                }
            }
        }

        private void OnApplicationQuit()
        {
            // Đảm bảo nhả Port 7777 về cho Windows khi tắt App/Editor
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        private void OnClientConnected(ulong id)
        {
            if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                Debug.Log($"<color=green>[ClientConnectionUI] SUCCESS:</color> Connected to Server as Client ID: {id}");
            }
        }

        private void OnClientDisconnect(ulong id)
        {
            if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                Debug.LogWarning("<color=red>[ClientConnectionUI] DISCONNECTED:</color> Connection lost or closed by server.");
            }
        }


    }
}
