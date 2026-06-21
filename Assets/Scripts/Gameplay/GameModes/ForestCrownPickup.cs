using UnityEngine;
using Unity.Netcode;
using EdgeParty.Gameplay.Character;

/// <summary>
/// Crown that spawns above an open chest.
/// Slowly rotates and bobs up/down.
/// When a player grabs/touches it, they earn 10 points for their team.
/// Server despawns the crown after collection; notifies chest to close.
/// </summary>
public class ForestCrownPickup : NetworkBehaviour
{
    [Header("Animation")]
    public float rotateSpeed = 45f;       // degrees/sec
    public float bobAmplitude = 0.15f;    // metres
    public float bobFrequency = 1.2f;     // cycles/sec

    [Header("Audio")]
    public AudioClip collectSound;

    private Vector3 _originLocalPos;
    private bool _collected = false;
    private ForestChestController _chest;

    public override void OnNetworkSpawn()
    {
        _originLocalPos = transform.localPosition;

        // Notify all clients that the crown has appeared (server fires the RPC)
        if (IsServer && ForestGameManager.Instance != null)
        {
            ForestGameManager.Instance.NotifyCrownSpawnedClientRpc();
        }
    }

    private void Update()
    {
        // Visual only (runs on all clients)
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
        float bob = Mathf.Sin(Time.time * bobFrequency * Mathf.PI * 2f) * bobAmplitude;
        transform.localPosition = _originLocalPos + Vector3.up * bob;
    }

    /// <summary>Called by ForestChestController to link back to the chest.</summary>
    public void SetChest(ForestChestController chest) => _chest = chest;

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (_collected) return;

        var pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;

        int teamID = pc.TeamID.Value;
        if (teamID <= 0) return;

        _collected = true;

        // Award 10 points
        if (ForestGameManager.Instance != null)
            ForestGameManager.Instance.AddScoreServerRpc(teamID, 10);

        // Notify clients
        CollectClientRpc();

        // Tell chest to close after delay
        _chest?.OnCrownCollected();

        // Despawn crown
        if (NetworkObject != null)
            NetworkObject.Despawn(true);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void CollectClientRpc()
    {
        AudioManager.Instance?.PlaySFX(collectSound);
    }
}
