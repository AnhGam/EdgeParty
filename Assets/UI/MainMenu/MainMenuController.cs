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
        private Button _btnHost;
        private Button _btnJoinConfirm;
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
            _btnHost = root.Q<Button>("HostGameBtn"); // Nút Host ở màn hình chính
            
            _btnReturnSettings = root.Q<Button>("ReturnBtn");
            _btnReturnCredits = root.Q<Button>("ReturnBtnCredits");
            _btnReturnJoin = root.Q<Button>("ReturnBtnJoin"); // Nút Return trong JoinPanel
            _btnJoinConfirm = _joinPanel?.Q<Button>("JoinConfirmBtn"); // Nút Join trong JoinPanel

            // Gắn sự kiện Click
            if (_btnSettings != null) _btnSettings.clicked += OpenSettings;
            if (_btnCredits != null) _btnCredits.clicked += OpenCredits;
            if (_btnJoin != null) _btnJoin.clicked += OpenJoinGame;
            if (_btnHost != null) _btnHost.clicked += HostGame;
            if (_btnJoinConfirm != null) _btnJoinConfirm.clicked += JoinGame;
                
            if (_btnReturnSettings != null) _btnReturnSettings.clicked += CloseWithSound;
            if (_btnReturnCredits != null) _btnReturnCredits.clicked += CloseWithSound;
            if (_btnReturnJoin != null) _btnReturnJoin.clicked += CloseWithSound;

            // Gắn sự kiện Hover (pointer enter)
            RegisterHover(_btnSettings);
            RegisterHover(_btnCredits);
            RegisterHover(_btnJoin);
            RegisterHover(_btnHost);
            RegisterHover(_btnJoinConfirm);
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
            if (_btnHost != null) _btnHost.clicked -= HostGame;
            if (_btnJoinConfirm != null) _btnJoinConfirm.clicked -= JoinGame;
                
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

        private void HostGame()
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
            var networkManager = Unity.Netcode.NetworkManager.Singleton;
            if (networkManager != null)
            {
                if (networkManager.IsListening) networkManager.Shutdown();
                var utp = (Unity.Netcode.Transports.UTP.UnityTransport)networkManager.NetworkConfig.NetworkTransport;
                if (utp != null)
                {
                    utp.SetConnectionData("127.0.0.1", 7777);
                }
                if (networkManager.StartHost())
                {
                    Debug.Log("[MainMenuController] Host started successfully. Loading DemoScene_Forest...");
                    networkManager.SceneManager.LoadScene("DemoScene_Forest", UnityEngine.SceneManagement.LoadSceneMode.Single);
                }
                else
                {
                    Debug.LogError("[MainMenuController] Failed to start host!");
                }
            }
        }

        private void JoinGame()
        {
            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
            var networkManager = Unity.Netcode.NetworkManager.Singleton;
            if (networkManager == null) return;

            var utp = (Unity.Netcode.Transports.UTP.UnityTransport)networkManager.NetworkConfig.NetworkTransport;
            if (utp == null) return;

            var ipField = _joinPanel?.Q<TextField>("InputIP");
            var portField = _joinPanel?.Q<TextField>("InputPort");

            string ip = ipField != null ? ipField.value : "127.0.0.1";
            string portStr = portField != null ? portField.value : "7777";

            if (ushort.TryParse(portStr, out ushort port))
            {
                utp.SetConnectionData(ip, port);
                if (networkManager.StartClient())
                {
                    Debug.Log($"[MainMenuController] Connecting to host at {ip}:{port}");
                }
                else
                {
                    Debug.LogError("[MainMenuController] Failed to start client!");
                }
            }
            else
            {
                Debug.LogError("[MainMenuController] Invalid port number entered!");
            }
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
