using UnityEngine;
using UnityEngine.UI;
using System.Text;

namespace EdgeParty.UI
{
    using EdgeParty.Infrastructure.VoiceChat;

    /// <summary>
    /// Simple voice chat HUD - shows mute toggle and active speakers.
    /// Assign Button and Text references in the Inspector.
    /// </summary>
    public class VoiceChatUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private Button muteButton;
        [SerializeField] private Text muteButtonText;
        [SerializeField] private Text speakersListText;

        private void Start()
        {
            if (muteButton != null)
                muteButton.onClick.AddListener(OnMuteButtonClicked);

            RefreshMuteLabel();
        }

        private void Update()
        {
            RefreshSpeakersList();
        }

        private void OnMuteButtonClicked()
        {
            VoiceChatManager.Instance?.ToggleMute();
            RefreshMuteLabel();
        }

        private void RefreshMuteLabel()
        {
            if (muteButtonText == null) return;
            bool muted = VoiceChatManager.Instance != null && VoiceChatManager.Instance.IsMuted;
            muteButtonText.text = muted ? "🎤 OFF" : "🎤 ON";
        }

        private void RefreshSpeakersList()
        {
            if (speakersListText == null) return;
            if (VoiceChatManager.Instance == null)
            {
                speakersListText.text = "";
                return;
            }

            var speakers = VoiceChatManager.Instance.GetActiveSpeakers();
            if (speakers.Count == 0)
            {
                speakersListText.text = "";
                return;
            }

            var sb = new StringBuilder("🔊 Đang nói:\n");
            foreach (var id in speakers)
                sb.AppendLine($"  • {id}");

            speakersListText.text = sb.ToString();
        }
    }
}
