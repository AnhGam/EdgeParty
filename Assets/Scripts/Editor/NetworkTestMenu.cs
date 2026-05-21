using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace EdgeParty.Editor
{
    /// <summary>
    /// Quick editor menu shortcuts to start Host/Client without needing the OnGUI HUD.
    /// Access via: EdgeParty menu in the top menu bar.
    /// </summary>
    public static class NetworkTestMenu
    {
        [MenuItem("EdgeParty/Network/Start HOST (localhost:7777)")]
        public static void StartHost()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[NetworkTest] Must be in Play Mode first.");
                return;
            }

            var nm = NetworkManager.Singleton;
            if (nm == null) { Debug.LogError("[NetworkTest] NetworkManager not found!"); return; }

            if (nm.IsListening) { Debug.LogWarning("[NetworkTest] Already running. Stop first."); return; }

            var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
            utp.SetConnectionData("127.0.0.1", 7777);

            if (nm.StartHost())
                Debug.Log("<color=green>[NetworkTest] HOST started on 127.0.0.1:7777</color>");
            else
                Debug.LogError("[NetworkTest] Failed to start Host!");
        }

        [MenuItem("EdgeParty/Network/Connect CLIENT (localhost:7777)")]
        public static void StartClient()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[NetworkTest] Must be in Play Mode first.");
                return;
            }

            var nm = NetworkManager.Singleton;
            if (nm == null) { Debug.LogError("[NetworkTest] NetworkManager not found!"); return; }

            if (nm.IsListening) { Debug.LogWarning("[NetworkTest] Already running. Stop first."); return; }

            var utp = (UnityTransport)nm.NetworkConfig.NetworkTransport;
            utp.SetConnectionData("127.0.0.1", 7777);

            if (nm.StartClient())
                Debug.Log("<color=cyan>[NetworkTest] CLIENT connecting to 127.0.0.1:7777...</color>");
            else
                Debug.LogError("[NetworkTest] Failed to start Client!");
        }

        [MenuItem("EdgeParty/Network/Stop (Disconnect)")]
        public static void StopNetwork()
        {
            if (!Application.isPlaying) return;

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening)
            {
                nm.Shutdown();
                Debug.Log("<color=yellow>[NetworkTest] Network stopped.</color>");
            }
        }

        [MenuItem("EdgeParty/VoiceChat/Check Status")]
        public static void CheckVoiceChatStatus()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[VoiceChat] Must be in Play Mode to check status.");
                return;
            }

            var vm = GameObject.FindAnyObjectByType<EdgeParty.Infrastructure.VoiceChat.VoiceChatManager>();
            if (vm == null)
            {
                Debug.LogError("[VoiceChat] VoiceChatManager not found in scene!");
                return;
            }

            Debug.Log($"[VoiceChat] IsReady: {vm.IsReady} | IsMuted: {vm.IsMuted}");
            Debug.Log($"[VoiceChat] Active Speakers: {string.Join(", ", vm.GetActiveSpeakers())}");
        }

        [MenuItem("EdgeParty/VoiceChat/Toggle Mute")]
        public static void ToggleMute()
        {
            if (!Application.isPlaying) return;

            var vm = GameObject.FindAnyObjectByType<EdgeParty.Infrastructure.VoiceChat.VoiceChatManager>();
            if (vm == null) { Debug.LogError("[VoiceChat] VoiceChatManager not found!"); return; }

            vm.ToggleMute();
            Debug.Log($"[VoiceChat] Muted: {vm.IsMuted}");
        }
    }
}
