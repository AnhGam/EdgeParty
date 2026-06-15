using UnityEngine;
using Unity.Netcode;

namespace EdgeParty.Infrastructure.VoiceChat
{
    [RequireComponent(typeof(NetworkObject))]
    public class VoiceChatParticipant : NetworkBehaviour
    {
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
            if (VoiceChatManager.Instance == null || !VoiceChatManager.Instance.IsReady) return;

            string channel = VoiceChatManager.Instance.CurrentGameChannel;
            if (string.IsNullOrEmpty(channel)) return;

            _timer += Time.deltaTime;
            if (_timer >= UpdateInterval)
            {
                _timer = 0f;
                VoiceChatManager.Instance.UpdateParticipantPosition(gameObject, channel);
            }
        }
    }
}
