using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Gameplay.Character
{
    /// <summary>
    /// Ensures there is only one active AudioListener in the scene.
    /// Attaches to the Player Prefab root.
    /// - If this is the LOCAL player (IsOwner): disable the AudioListener on the Main Camera,
    ///   because the player IS the camera target and the Camera's AudioListener is fine.
    ///   Actually we just need to disable any AudioListener ON this player prefab hierarchy.
    /// - If NOT owner: disable any AudioListeners found on this player prefab hierarchy.
    /// The single source of truth is the AudioListener on the Main Camera, which follows the local player.
    /// </summary>
    public class AudioListenerManager : NetworkBehaviour
    {
        public override void OnNetworkSpawn()
        {
            // Always disable any AudioListeners on the player prefab hierarchy.
            // The Main Camera already has one and follows the local player — that's enough.
            DisableAudioListenersOnSelf();
        }

        private void Awake()
        {
            // Also disable on Awake for non-networked testing
            DisableAudioListenersOnSelf();
        }

        private void DisableAudioListenersOnSelf()
        {
            var listeners = GetComponentsInChildren<AudioListener>(includeInactive: true);
            foreach (var listener in listeners)
            {
                listener.enabled = false;
                Debug.Log($"[AudioListener] Disabled duplicate AudioListener on '{listener.gameObject.name}'");
            }
        }
    }
}
