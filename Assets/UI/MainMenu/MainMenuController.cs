using EdgeParty.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace EdgeParty.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        private UIDocument _uiDocument;
        
        // Panels
        private TemplateContainer _settingsPanel;
        private TemplateContainer _creditsPanel;
        private TemplateContainer _joinPanel;
        
        // Buttons
        private Button _btnSettings;
        private Button _btnCredits;
        private Button _btnJoin;
        private Button _btnReturnSettings;
        private Button _btnReturnCredits;
        private Button _btnReturnJoin;

        // Tên âm thanh – khớp với tên khai báo trong AudioManager Inspector
        private const string SoundClick = "Click";
        private const string SoundHover = "Hover";

        private void OnEnable()
        {
            _uiDocument = GetComponent<UIDocument>();
            var root = _uiDocument.rootVisualElement;

            // Lấy các element
            _settingsPanel = root.Q<TemplateContainer>("SettingsPanel");
            _creditsPanel = root.Q<TemplateContainer>("CreditsPanel");
            _joinPanel = root.Q<TemplateContainer>("JoinPanel");

            _btnSettings = root.Q<Button>("SettingsBtn");
            _btnCredits = root.Q<Button>("CreditsBtn");
            _btnJoin = root.Q<Button>("JoinGameBtn"); // Nút ở màn hình chính
            
            _btnReturnSettings = root.Q<Button>("ReturnBtn");
            _btnReturnCredits = root.Q<Button>("ReturnBtnCredits");
            _btnReturnJoin = root.Q<Button>("ReturnBtnJoin"); // Nút Return trong JoinPanel

            // Gắn sự kiện Click
            if (_btnSettings != null) _btnSettings.clicked += OpenSettings;
            if (_btnCredits != null) _btnCredits.clicked += OpenCredits;
            if (_btnJoin != null) _btnJoin.clicked += OpenJoinGame;
                
            if (_btnReturnSettings != null) _btnReturnSettings.clicked += CloseWithSound;
            if (_btnReturnCredits != null) _btnReturnCredits.clicked += CloseWithSound;
            if (_btnReturnJoin != null) _btnReturnJoin.clicked += CloseWithSound;

            // Gắn sự kiện Hover (pointer enter)
            RegisterHover(_btnSettings);
            RegisterHover(_btnCredits);
            RegisterHover(_btnJoin);
            RegisterHover(_btnReturnSettings);
            RegisterHover(_btnReturnCredits);
            RegisterHover(_btnReturnJoin);
                
            // Khởi tạo trạng thái mặc định
            CloseAllPanels();
        }

        private void OnDisable()
        {
            if (_btnSettings != null) _btnSettings.clicked -= OpenSettings;
            if (_btnCredits != null) _btnCredits.clicked -= OpenCredits;
            if (_btnJoin != null) _btnJoin.clicked -= OpenJoinGame;
                
            if (_btnReturnSettings != null) _btnReturnSettings.clicked -= CloseWithSound;
            if (_btnReturnCredits != null) _btnReturnCredits.clicked -= CloseWithSound;
            if (_btnReturnJoin != null) _btnReturnJoin.clicked -= CloseWithSound;
        }

        // ─── Helpers ─────────────────────────────────────────────────────

        /// <summary>Đăng ký âm thanh Hover và Click cho một nút bấm.</summary>
        private void RegisterHover(Button btn)
        {
            if (btn == null) return;
            btn.RegisterCallback<PointerEnterEvent>(_ =>
                EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundHover));
        }

        private void OpenSettings()
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
            CloseAllPanels();
            if (_settingsPanel != null) _settingsPanel.style.display = DisplayStyle.Flex;
        }

        private void OpenCredits()
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
            CloseAllPanels();
            if (_creditsPanel != null) _creditsPanel.style.display = DisplayStyle.Flex;
        }

        private void OpenJoinGame()
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
            CloseAllPanels();
            if (_joinPanel != null) _joinPanel.style.display = DisplayStyle.Flex;
        }

        /// <summary>Nút Return – phát tiếng rồi ẩn panel.</summary>
        private void CloseWithSound()
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
            CloseAllPanels();
        }

        /// <summary>Dùng nội bộ – không phát âm thanh, an toàn khi gọi lúc khởi động.</summary>
        private void CloseAllPanels()
        {
            if (_settingsPanel != null) _settingsPanel.style.display = DisplayStyle.None;
            if (_creditsPanel != null) _creditsPanel.style.display = DisplayStyle.None;
            if (_joinPanel != null) _joinPanel.style.display = DisplayStyle.None;
        }
    }
}
