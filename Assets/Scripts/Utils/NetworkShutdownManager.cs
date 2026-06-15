using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Utils
{
    /// <summary>
    /// Ensures that the NetworkManager is properly shut down when the game or editor stops.
    /// This prevents "ghost sessions" from running in the background.
    /// </summary>
    public class NetworkShutdownManager : MonoBehaviour
    {
        private void OnApplicationQuit()
        {
            if (NetworkManager.Singleton != null)
            {
                Debug.Log("[NetworkShutdownManager] Shutting down NetworkManager due to Application Quit.");
                NetworkManager.Singleton.Shutdown();
            }
        }

#if UNITY_EDITOR
        private void OnDisable()
        {
            if (!Application.isPlaying && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                Debug.Log("[NetworkShutdownManager] Shutting down NetworkManager due to Play Mode stop.");
                NetworkManager.Singleton.Shutdown();
            }
        }
#endif
    }
}
