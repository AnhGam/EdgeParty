using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using EdgeParty.Gameplay.Character;

[RequireComponent(typeof(UIDocument))]
public class WinScreenUI : MonoBehaviour
{
    private UIDocument _uiDoc;
    private VisualElement _winOverlay;
    private Label _winTitleLabel;
    private Label _winSubtitleLabel;
    private Label _winDetailsLabel;
    private Label _lobbyCountdownLabel;

    private PlayerController _localPlayer;

    private void Start()
    {
        _uiDoc = GetComponent<UIDocument>();
        if (_uiDoc == null) _uiDoc = FindFirstObjectByType<UIDocument>();
        if (_uiDoc == null) return;

        var root = _uiDoc.rootVisualElement;
        _winOverlay          = root.Q<VisualElement>("WinOverlay");
        _winTitleLabel       = root.Q<Label>("WinTitleLabel");
        _winSubtitleLabel    = root.Q<Label>("WinSubtitleLabel");
        _winDetailsLabel     = root.Q<Label>("WinDetailsLabel");
        _lobbyCountdownLabel = root.Q<Label>("LobbyCountdownLabel");

        if (_winOverlay != null)
            _winOverlay.style.display = DisplayStyle.None;

        StartCoroutine(WaitForGameManagerAndSubscribe());
    }

    private IEnumerator WaitForGameManagerAndSubscribe()
    {
        while (ForestGameManager.Instance == null)
        {
            yield return new WaitForSeconds(0.5f);
        }

        ForestGameManager.Instance.OnMatchEnded += ShowWinScreen;

        while (_localPlayer == null)
        {
            var allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
            foreach (var pc in allPlayers)
            {
                if (pc.IsOwner) { _localPlayer = pc; break; }
            }
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void ShowWinScreen(int winnerTeam)
    {
        if (_winOverlay == null) return;

        int localPlayerTeam = (_localPlayer != null) ? _localPlayer.TeamID.Value : 0;
        
        string titleText = "TRẬN ĐẤU KẾT THÚC!";
        Color titleColor = new Color(1f, 0.85f, 0.24f);
        string subtitleText = "";

        if (winnerTeam == -1)
        {
            titleText = "HÒA NHAU!";
            titleColor = Color.white;
            subtitleText = "Cả hai đội đều ngang tài ngang sức!";
        }
        else if (winnerTeam == localPlayerTeam && localPlayerTeam != 0)
        {
            titleText = "CHIẾN THẮNG!";
            titleColor = new Color(0.3f, 1f, 0.4f);
            subtitleText = $"Đội {(winnerTeam == 1 ? "Đỏ" : "Xanh")} của bạn đã giành thắng lợi!";
        }
        else
        {
            titleText = "THẤT BẠI!";
            titleColor = new Color(1f, 0.3f, 0.3f);
            subtitleText = $"Đội {(winnerTeam == 1 ? "Đỏ" : "Xanh")} đã giành thắng lợi!";
        }

        if (_winTitleLabel != null)
        {
            _winTitleLabel.text = titleText;
            _winTitleLabel.style.color = titleColor;
        }

        if (_winSubtitleLabel != null)
            _winSubtitleLabel.text = subtitleText;

        int scoreT1 = ForestGameManager.Instance.Team1Score.Value;
        int scoreT2 = ForestGameManager.Instance.Team2Score.Value;
        if (_winDetailsLabel != null)
            _winDetailsLabel.text = $"Tỉ số chung cuộc: Đỏ {scoreT1:00} - {scoreT2:00} Xanh";

        _winOverlay.style.display = DisplayStyle.Flex;

        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;

        float delay = ForestGameManager.Instance.delayAfterEnd;
        StartCoroutine(LobbyCountdownRoutine(delay));
    }

    private IEnumerator LobbyCountdownRoutine(float delayRemaining)
    {
        while (delayRemaining > 0f)
        {
            if (_lobbyCountdownLabel != null)
                _lobbyCountdownLabel.text = $"Quay lại phòng chờ sau {Mathf.CeilToInt(delayRemaining)} giây...";
            yield return new WaitForSeconds(1f);
            delayRemaining -= 1f;
        }

        if (_lobbyCountdownLabel != null)
            _lobbyCountdownLabel.text = "Đang tải...";
    }

    private void OnDestroy()
    {
        if (ForestGameManager.Instance != null)
        {
            ForestGameManager.Instance.OnMatchEnded -= ShowWinScreen;
        }
    }
}
