using UnityEngine;
using Unity.Netcode;

public class VoiceChatParticipant : NetworkBehaviour
{
    [SerializeField] private string channelName = "MainLobby";
    private float updateTimer;
    private const float UpdateInterval = 0.3f; // Update 3 times per second (Recommended: 2-4)

    public override void OnNetworkSpawn()
    {
        // Only the local player (owner) should update their own position in Vivox
        // Vivox handles other participants' audio based on the listener's (local player) position
        if (!IsOwner)
        {
            enabled = false;
        }
    }

    private void Update()
    {
        // Safety check: ensure we only run for the local owner
        if (!IsOwner) return;

        updateTimer += Time.deltaTime;
        if (updateTimer >= UpdateInterval)
        {
            updateTimer = 0f;
            if (VoiceChatManager.Instance != null)
            {
                // Update this player's position in the Vivox 3D channel
                VoiceChatManager.Instance.UpdateParticipantPosition(gameObject, channelName);
            }
        }
    }
}
