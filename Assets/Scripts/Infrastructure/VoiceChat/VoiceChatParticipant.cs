using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Infrastructure.VoiceChat
{
    /// <summary>
    /// Attach to player Prefab. Updates the local player's 3D position in Vivox
    /// so other players hear positional audio relative to their distance.
    /// Only runs on the local owner.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class VoiceChatParticipant : NetworkBehaviour
    {
        [SerializeField] private string channelName = "MainLobby";

        private float _timer;
        private const float UpdateInterval = 0.25f; // 4 times/sec

        public override void OnNetworkSpawn()
        {
            // Only the local owner controls their own 3D position in Vivox
            if (!IsOwner)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;

            _timer += Time.deltaTime;
            if (_timer >= UpdateInterval)
            {
                _timer = 0f;
                if (VoiceChatManager.Instance != null && VoiceChatManager.Instance.IsReady)
                {
                    VoiceChatManager.Instance.UpdateParticipantPosition(gameObject, channelName);
                }
            }
        }
    }
}
