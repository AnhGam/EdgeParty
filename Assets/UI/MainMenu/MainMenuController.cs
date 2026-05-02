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

            // Gắn sự kiện
            if (_btnSettings != null) _btnSettings.clicked += OpenSettings;
            if (_btnCredits != null) _btnCredits.clicked += OpenCredits;
            if (_btnJoin != null) _btnJoin.clicked += OpenJoinGame;
                
            if (_btnReturnSettings != null) _btnReturnSettings.clicked += CloseAllPanels;
            if (_btnReturnCredits != null) _btnReturnCredits.clicked += CloseAllPanels;
            if (_btnReturnJoin != null) _btnReturnJoin.clicked += CloseAllPanels;
                
            // Khởi tạo trạng thái mặc định
            CloseAllPanels();
        }

        private void OnDisable()
        {
            if (_btnSettings != null) _btnSettings.clicked -= OpenSettings;
            if (_btnCredits != null) _btnCredits.clicked -= OpenCredits;
            if (_btnJoin != null) _btnJoin.clicked -= OpenJoinGame;
                
            if (_btnReturnSettings != null) _btnReturnSettings.clicked -= CloseAllPanels;
            if (_btnReturnCredits != null) _btnReturnCredits.clicked -= CloseAllPanels;
            if (_btnReturnJoin != null) _btnReturnJoin.clicked -= CloseAllPanels;
        }

        private void OpenSettings()
        {
            CloseAllPanels();
            if (_settingsPanel != null) _settingsPanel.style.display = DisplayStyle.Flex;
        }

        private void OpenCredits()
        {
            CloseAllPanels();
            if (_creditsPanel != null) _creditsPanel.style.display = DisplayStyle.Flex;
        }

        private void OpenJoinGame()
        {
            CloseAllPanels();
            if (_joinPanel != null) _joinPanel.style.display = DisplayStyle.Flex;
        }

        private void CloseAllPanels()
        {
            if (_settingsPanel != null) _settingsPanel.style.display = DisplayStyle.None;
            if (_creditsPanel != null) _creditsPanel.style.display = DisplayStyle.None;
            if (_joinPanel != null) _joinPanel.style.display = DisplayStyle.None;
        }
    }
}
