using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace EdgeParty.ConnectionManagement
{
    /// <summary>
    /// Chịu trách nhiệm tự động chạy Server khi ở môi trường Headless (như trên Edgegap)
    /// Học hỏi từ BossRoom: Tách biệt logic khởi chạy Server ra khỏi UI của Client.
    /// Sửa đổi: Lắng nghe thêm các Callback để theo dõi tình trạng Server/Transport trên Unity 6.
    /// </summary>
    public class EdgegapServerStarter : MonoBehaviour
    {
        public string portMapName = "gameport";

        private void Start()
        {
            // Chỉ chạy logic khi được build ở dạng Dedicated Server (BatchMode)
            if (Application.isBatchMode)
            {
                var networkManager = NetworkManager.Singleton;
                if (networkManager == null)
                {
                    Debug.LogError("[EdgegapServerStarter] NetworkManager.Singleton is null. Please ensure NetworkManager exists in the scene.");
                    return;
                }

                // ----------------------------------------------------
                // ĐĂNG KÝ CALLBACK THEO CHUẨN BOSSROOM (Giúp dễ Debug)
                // ----------------------------------------------------
                networkManager.OnServerStarted += () => Debug.Log("[EdgegapServerStarter] OnServerStarted: Server is running & listening!");
                networkManager.OnServerStopped += (obj) => Debug.Log("[EdgegapServerStarter] OnServerStopped: Server shut down.");
                networkManager.OnClientConnectedCallback += (id) => Debug.Log($"[EdgegapServerStarter] OnClientConnected: Client {id} joined!");
                networkManager.OnClientDisconnectCallback += (id) => Debug.Log($"[EdgegapServerStarter] OnClientDisconnect: Client {id} left.");
                
                // (Tùy chọn) Bật chế độ tự động đồng ý kết nối nếu bạn chưa viết logic ApprovalCheck
                networkManager.NetworkConfig.ConnectionApproval = false;

                // ----------------------------------------------------
                // CẤU HÌNH TRANSPORT CHO EDGEGAP
                // ----------------------------------------------------
                var utp = (UnityTransport)networkManager.NetworkConfig.NetworkTransport;

                // 1. Ép Server lắng nghe trên 0.0.0.0 (Bắt buộc cho Cloud)
                utp.SetConnectionData("0.0.0.0", utp.ConnectionData.Port);

                // 2. Đọc Port nội bộ từ hệ thống của Edgegap
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
                
                // 3. Khởi động Server
                if (!networkManager.StartServer())
                {
                    Debug.LogError("[EdgegapServerStarter] Failed to start server. This usually happens if the port is already in use or NetworkManager config is invalid.");
                }
            }
        }
    }
}
