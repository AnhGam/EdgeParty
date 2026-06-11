using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using EdgeParty.Gameplay.Character;

/// <summary>
/// Displays a death/spectating overlay with a 10-second countdown when the local player dies.
/// Attach to the same GameObject as HUDController (which has the UIDocument for MainHUD.uxml).
/// </summary>
public class DeathScreenUI : MonoBehaviour
{
    [Header("Settings")]
    public float respawnCountdown = 10f;

    private UIDocument _uiDoc;
    private VisualElement _deathOverlay;
    private Label _spectatingLabel;
    private Label _countdownLabel;

    private PlayerController _localPlayer;
    private Coroutine _countdownCoroutine;

    private void Start()
    {
        _uiDoc = GetComponent<UIDocument>();
        if (_uiDoc == null) _uiDoc = FindFirstObjectByType<UIDocument>();
        if (_uiDoc == null) return;

        var root = _uiDoc.rootVisualElement;
        _deathOverlay   = root.Q<VisualElement>("DeathOverlay");
        _spectatingLabel = root.Q<Label>("SpectatingLabel");
        _countdownLabel  = root.Q<Label>("CountdownLabel");

        // Start hidden
        SetOverlayVisible(false);

        // Subscribe once local player spawns
        StartCoroutine(WaitForLocalPlayer());
    }

    private IEnumerator WaitForLocalPlayer()
    {
        while (_localPlayer == null)
        {
            var allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            foreach (var pc in allPlayers)
            {
                if (pc.IsOwner) { _localPlayer = pc; break; }
            }
            yield return new WaitForSeconds(0.5f);
        }

        if (_localPlayer.stats != null)
        {
            _localPlayer.stats.OnDied     += OnDied;
            _localPlayer.stats.OnRespawned += OnRespawned;
        }
    }

    private void OnDied()
    {
        SetOverlayVisible(true);
        if (_spectatingLabel != null)
            _spectatingLabel.text = "SPECTATING";

        if (_countdownCoroutine != null) StopCoroutine(_countdownCoroutine);
        _countdownCoroutine = StartCoroutine(RunCountdown());
    }

    private void OnRespawned()
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }
        SetOverlayVisible(false);
    }

    private IEnumerator RunCountdown()
    {
        float remaining = respawnCountdown;

        while (remaining > 0f)
        {
            if (_countdownLabel != null)
                _countdownLabel.text = $"Respawning in {Mathf.CeilToInt(remaining)}s";

            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        // Request respawn from server
        if (_localPlayer != null && _localPlayer.stats != null)
        {
            // Use PlayerController to request respawn via server
            RequestRespawnServerRpc_Via(_localPlayer);
        }

        SetOverlayVisible(false);
    }

    private void RequestRespawnServerRpc_Via(PlayerController pc)
    {
        // PlayerController handles SpawnByTeam on the server side.
        // We trigger it by calling the RPC if available, else directly.
        // Since PlayerController is a NetworkBehaviour, send respawn RPC.
        // We'll rely on a simple approach: call stats.Respawn via the server.
        // The cleanest path is asking PlayerController to call a server RPC.
        pc.RequestRespawnRpc();
    }

    private void SetOverlayVisible(bool visible)
    {
        if (_deathOverlay == null) return;
        _deathOverlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnDestroy()
    {
        if (_localPlayer != null && _localPlayer.stats != null)
        {
            _localPlayer.stats.OnDied     -= OnDied;
            _localPlayer.stats.OnRespawned -= OnRespawned;
        }
    }
}
