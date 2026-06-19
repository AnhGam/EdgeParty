using UnityEngine;
using Unity.Netcode;
using EdgeParty.Gameplay.Character;

/// <summary>
/// Coin collectible on the Forest map.
/// Attach to each ForestCoin GameObject alongside a trigger MeshCollider.
/// When a player touches it, they earn 1 point for their team.
/// </summary>
public class ForestCoin : NetworkBehaviour
{
    [Header("Audio")]
    [Tooltip("SFX to play when collected")]
    public AudioClip collectSound;

    // Prevent multi-collect in same frame
    private bool _collected = false;

    private void OnTriggerEnter(Collider other)
    {
        // Try to get player
        var pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;

        // On client, if it is the local player, we can play the sound instantly for responsive feedback!
        if (pc.IsLocalPlayer)
        {
            if (!_collected)
            {
                if (!IsServer)
                {
                    _collected = true;
                    AudioManager.Instance?.PlaySFX(collectSound);
                }
            }
        }

        if (!IsServer) return;
        if (_collected) return;

        int teamID = pc.TeamID.Value;
        if (teamID <= 0) return; // not yet assigned

        _collected = true;

        // Award 1 point
        if (ForestGameManager.Instance != null)
            ForestGameManager.Instance.AddScoreServerRpc(teamID, 1);

        // Notify all clients for SFX/VFX/Deactivation
        CollectClientRpc(pc.OwnerClientId);

        // Despawn coin on server without destroying (since it is an in-scene object)
        if (NetworkObject != null)
            NetworkObject.Despawn(false);

        gameObject.SetActive(false);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void CollectClientRpc(ulong collectorClientId)
    {
        gameObject.SetActive(false);

        bool isCollector = (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == collectorClientId);

        // Play SFX if:
        // - We are not the collector (we need to hear other players collect it)
        // - Or we are the collector AND we are the Host/Server (since host bypasses early client play)
        if (!isCollector || IsServer)
        {
            _collected = true;
            AudioManager.Instance?.PlaySFX(collectSound);
        }
    }
}
