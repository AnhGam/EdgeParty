using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using EdgeParty.Gameplay.Character;

public class DeathScreenUI : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Phải khớp với PlayerController.autoRespawnDelay để hiển thị đúng")]
    public float respawnCountdown = 5f;

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

        SetOverlayVisible(false);

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
            _spectatingLabel.text = "ĐANG NẰM...";

        if (_countdownCoroutine != null) StopCoroutine(_countdownCoroutine);
        _countdownCoroutine = StartCoroutine(RunDisplayCountdown());
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

    private IEnumerator RunDisplayCountdown()
    {
        float remaining = respawnCountdown;

        while (remaining > 0f)
        {
            if (_countdownLabel != null)
                _countdownLabel.text = $"Hồi lại sau {Mathf.CeilToInt(remaining)}s";

            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        if (_countdownLabel != null)
            _countdownLabel.text = "Đang hồi...";
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
