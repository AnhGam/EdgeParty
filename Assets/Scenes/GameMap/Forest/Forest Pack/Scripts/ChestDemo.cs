using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Replaces ChestDemo. 
/// The chest opens every 30 seconds, spawns a crown, and closes 3s after the crown is taken.
/// Attach to the ForestChestBottom GameObject which has an Animator with "open"/"close" triggers.
/// 
/// SETUP:
///   1. Attach this script to ForestChestBottom (replaces ChestDemo).
///   2. Assign crownPrefab (a NetworkObject prefab with ForestCrownPickup + SphereCollider trigger).
///   3. Assign crownSpawnPoint (an empty child Transform above the chest opening).
/// </summary>
public class ForestChestController : NetworkBehaviour
{
    [Header("References")]
    public Animator chestAnim;
    [Tooltip("Prefab with NetworkObject + ForestCrownPickup + SphereCollider (trigger)")]
    public GameObject crownPrefab;
    [Tooltip("Spawn point above chest opening")]
    public Transform crownSpawnPoint;

    [Header("Timing")]
    public float respawnInterval = 30f;   // seconds between each chest open
    public float closeDelay     = 3f;     // seconds after crown taken before closing

    private bool _isOpen = false;
    private NetworkObject _spawnedCrown;
    private Coroutine _cycleCoroutine;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────

    private void Awake()
    {
        if (chestAnim == null)
            chestAnim = GetComponent<Animator>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        _cycleCoroutine = StartCoroutine(ChestCycle());
    }

    public override void OnNetworkDespawn()
    {
        if (_cycleCoroutine != null)
            StopCoroutine(_cycleCoroutine);
    }

    // ─── Server Logic ─────────────────────────────────────────────────────

    private IEnumerator ChestCycle()
    {
        while (true)
        {
            // Wait before opening
            yield return new WaitForSeconds(respawnInterval);
            OpenChest();
        }
    }

    private void OpenChest()
    {
        if (_isOpen) return;
        _isOpen = true;

        // Trigger animator on all clients
        OpenChestClientRpc();

        // Spawn crown
        if (crownPrefab != null)
        {
            Vector3 pos = crownSpawnPoint != null
                ? crownSpawnPoint.position
                : transform.position + Vector3.up * 1.2f;

            var go = Instantiate(crownPrefab, pos, Quaternion.identity);
            _spawnedCrown = go.GetComponent<NetworkObject>();
            if (_spawnedCrown != null)
            {
                _spawnedCrown.Spawn(true);
                var crown = go.GetComponent<ForestCrownPickup>();
                crown?.SetChest(this);
            }
        }
    }

    /// <summary>Called by ForestCrownPickup when the crown is collected.</summary>
    public void OnCrownCollected()
    {
        if (!IsServer) return;
        _spawnedCrown = null;
        StartCoroutine(DelayedClose());
    }

    private IEnumerator DelayedClose()
    {
        yield return new WaitForSeconds(closeDelay);
        CloseChest();
    }

    private void CloseChest()
    {
        _isOpen = false;
        CloseChestClientRpc();
    }

    // ─── Client RPCs ──────────────────────────────────────────────────────

    [Rpc(SendTo.ClientsAndHost)]
    private void OpenChestClientRpc()
    {
        if (chestAnim != null)
            chestAnim.SetTrigger("open");
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void CloseChestClientRpc()
    {
        if (chestAnim != null)
            chestAnim.SetTrigger("close");
    }
}
