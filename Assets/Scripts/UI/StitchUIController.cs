using System;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using EdgeParty.Auth;
using EdgeParty.Social;
using EdgeParty.Gameplay.Character;

namespace EdgeParty.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class StitchUIController : MonoBehaviour
    {
        public static StitchUIController Instance { get; private set; }
        public static bool ReturnedFromGame = false;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private UIDocument _uiDocument;
        private VisualElement _root;

        [Header("Menu UXML Templates")]
        [SerializeField] private VisualTreeAsset loginVisualTree;
        [SerializeField] private VisualTreeAsset registerVisualTree;
        [SerializeField] private VisualTreeAsset forgotPasswordVisualTree;
        [SerializeField] private VisualTreeAsset homeVisualTree;
        [SerializeField] private VisualTreeAsset shopVisualTree;
        [SerializeField] private VisualTreeAsset matchmakingVisualTree;
        [SerializeField] private VisualTreeAsset lockerVisualTree;
        [SerializeField] private VisualTreeAsset settingsVisualTree;

        [Header("Customization Data")]
        [SerializeField] private CustomizationData customizationData;

        [Header("Locker (Customization) Prefab")]
        [SerializeField] private GameObject lockerPrefab; // Assign CharacterCustomization.prefab here

        [Header("Coin Textures")]
        [SerializeField] private Texture2D singleCoinTex;
        [SerializeField] private Texture2D pouchTex;
        [SerializeField] private Texture2D smallPileTex;
        [SerializeField] private Texture2D decentPileTex;
        [SerializeField] private Texture2D fullChestTex;
        [SerializeField] private Texture2D massiveChestTex;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (singleCoinTex == null) singleCoinTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Textures/single-coin.png");
            if (pouchTex == null) pouchTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Textures/pouch.png");
            if (smallPileTex == null) smallPileTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Textures/small pile.png");
            if (decentPileTex == null) decentPileTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Textures/decent pile.png");
            if (fullChestTex == null) fullChestTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Textures/full-chest.png");
            if (massiveChestTex == null) massiveChestTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Textures/massive chest.png");

            // Auto-assign visual tree templates if null
            if (loginVisualTree == null) loginVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/StitchUI/UXML/LoginMenu.uxml");
            if (registerVisualTree == null) registerVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/StitchUI/UXML/RegisterMenu.uxml");
            if (forgotPasswordVisualTree == null) forgotPasswordVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/StitchUI/UXML/ForgotPasswordMenu.uxml");
            if (homeVisualTree == null) homeVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/StitchUI/UXML/HomeMenu.uxml");
            if (shopVisualTree == null) shopVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/StitchUI/UXML/ShopMenu.uxml");
            if (matchmakingVisualTree == null) matchmakingVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/StitchUI/UXML/MatchmakingMenu.uxml");
            if (lockerVisualTree == null) lockerVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/StitchUI/UXML/LockerMenu.uxml");
            if (settingsVisualTree == null) settingsVisualTree = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Settings/SettingsMenu.uxml");

            if (lockerPrefab == null) lockerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Scripts/UI/Menus/CharacterCustomization.prefab");
            if (customizationData == null) customizationData = UnityEditor.AssetDatabase.LoadAssetAtPath<CustomizationData>("Assets/UI/Resources/CustomizationData.asset");
        }
#endif

        [Header("SFX Sound Names")]
        private const string SoundClick = "Click";
        private const string SoundHover = "Hover";

        private float _lastClickTime = -1f;
        private float _lastHoverTime = -1f;

        // State variables for binding demonstration
        private int _currentCoins = 1240;
        private int _onlineFriendsCount = 5;
        private string _username = "PlayerOne";
        private string _currentFriendsDrawerTab = "friends";

        // Locker overlay instance
        private GameObject _lockerInstance;

        // Shop preview state
        private string _currentlyPreviewedCategory = null;
        private int _currentlyPreviewedIndex = -1;

        // UGS Auth integration states
        private string _pendingEmail = "";
        private Button _loadingButton;
        private string _originalButtonText;

        private async void OnEnable()
        {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) return;

            AuthService.Instance.OnSignInSuccess += HandleSignInSuccess;
            AuthService.Instance.OnSignInFailed += HandleSignInFailed;
            AuthService.Instance.OnSignUpSuccess += HandleSignUpSuccess;
            AuthService.Instance.OnSignUpFailed += HandleSignUpFailed;

            if (FriendLobbyService.Instance != null)
            {
                FriendLobbyService.Instance.OnFriendsUpdated += HandleSocialUpdated;
                FriendLobbyService.Instance.OnFriendRequestsUpdated += HandleSocialUpdated;
                FriendLobbyService.Instance.OnLobbyMembersUpdated += UpdateLobbyPodiums;
                FriendLobbyService.Instance.OnLobbyJoined += HandleLobbyJoined;
                FriendLobbyService.Instance.OnLobbyLeft += HandleLobbyLeft;
                FriendLobbyService.Instance.OnLobbyInviteReceived += HandleLobbyInviteReceived;
                FriendLobbyService.Instance.OnLobbyInviteCleared += HandleLobbyInviteCleared;
            }

            var matchmakingMgr = FindFirstObjectByType<EdgeParty.ConnectionManagement.MatchmakingManager>();
            if (matchmakingMgr == null)
            {
                var go = new GameObject("MatchmakingManager");
                matchmakingMgr = go.AddComponent<EdgeParty.ConnectionManagement.MatchmakingManager>();
            }

            if (EdgeParty.ConnectionManagement.MatchmakingManager.Instance != null)
            {
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.OnMatchmakingStarted += HandleMatchmakingStarted;
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.OnMatchmakingCancelled += HandleMatchmakingCancelled;
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.OnMatchmakingSucceeded += HandleMatchmakingSucceeded;
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.OnMatchmakingFailed += HandleMatchmakingFailed;
            }

            if (ReturnedFromGame || AuthService.Instance.IsSignedIn)
            {
                ReturnedFromGame = false;
                _username = AuthService.Instance.CachedUsername;
                ShowHome();
            }
            else
            {
                // Start by showing the Login Menu by default
                ShowLogin();

                // Try Auto Sign-In (check UGS session token)
                bool autoSignedIn = await AuthService.Instance.TryAutoSignInAsync();
                if (autoSignedIn)
                {
                    _username = AuthService.Instance.CachedUsername;
                    ShowHome();
                }
            }
        }

        private void OnDisable()
        {
            if (AuthService.Instance != null)
            {
                AuthService.Instance.OnSignInSuccess -= HandleSignInSuccess;
                AuthService.Instance.OnSignInFailed -= HandleSignInFailed;
                AuthService.Instance.OnSignUpSuccess -= HandleSignUpSuccess;
                AuthService.Instance.OnSignUpFailed -= HandleSignUpFailed;
            }

            if (FriendLobbyService.Instance != null)
            {
                FriendLobbyService.Instance.OnFriendsUpdated -= HandleSocialUpdated;
                FriendLobbyService.Instance.OnFriendRequestsUpdated -= HandleSocialUpdated;
                FriendLobbyService.Instance.OnLobbyMembersUpdated -= UpdateLobbyPodiums;
                FriendLobbyService.Instance.OnLobbyJoined -= HandleLobbyJoined;
                FriendLobbyService.Instance.OnLobbyLeft -= HandleLobbyLeft;
                FriendLobbyService.Instance.OnLobbyInviteReceived -= HandleLobbyInviteReceived;
                FriendLobbyService.Instance.OnLobbyInviteCleared -= HandleLobbyInviteCleared;
            }

            if (EdgeParty.ConnectionManagement.MatchmakingManager.Instance != null)
            {
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.OnMatchmakingStarted -= HandleMatchmakingStarted;
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.OnMatchmakingCancelled -= HandleMatchmakingCancelled;
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.OnMatchmakingSucceeded -= HandleMatchmakingSucceeded;
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.OnMatchmakingFailed -= HandleMatchmakingFailed;
            }
        }

        // ─── UGS Auth Callbacks ──────────────────────────────────────────

        private void HandleSignInSuccess()
        {
            HideErrorBanner();
            ResetButtonLoading();
            _username = AuthService.Instance.CachedUsername;
            ShowHome();
        }

        private void HandleSignInFailed(string errorMessage)
        {
            ResetButtonLoading();
            ShowErrorBanner(errorMessage);
        }

        private void HandleSignUpSuccess()
        {
            HideErrorBanner();
            ResetButtonLoading();
            ShowLogin();
            
            // Show registration success banner in green
            var banner = _root.Q<Label>("error-banner");
            if (banner != null)
            {
                banner.text = "Account created successfully! Please log in.";
                banner.style.backgroundColor = new StyleColor(new Color(0.9f, 0.99f, 0.9f));
                SetBorderColor(banner, new Color(0.7f, 0.9f, 0.7f));
                banner.style.color = new StyleColor(new Color(0.1f, 0.5f, 0.1f));
                banner.style.display = DisplayStyle.Flex;
            }
        }

        private void HandleSignUpFailed(string errorMessage)
        {
            ResetButtonLoading();
            ShowErrorBanner(errorMessage);
        }

        private void EnsureLockerInstance()
        {
            if (_lockerInstance == null)
            {
                if (lockerPrefab == null)
                {
                    Debug.LogWarning("[StitchUIController] lockerPrefab is not assigned! Please assign CharacterCustomization.prefab in the Inspector.");
                    return;
                }
                _lockerInstance = Instantiate(lockerPrefab);
                _lockerInstance.name = "LockerOverlay (runtime)";
            }
        }

        public void ShowLogin()
        {
            if (_lockerInstance != null) _lockerInstance.SetActive(false);
            if (loginVisualTree == null) return;
            SetNewScreen(loginVisualTree.CloneTree());
            BindLoginEvents();
        }

        public void ShowRegister()
        {
            if (_lockerInstance != null) _lockerInstance.SetActive(false);
            if (registerVisualTree == null) return;
            SetNewScreen(registerVisualTree.CloneTree());
            BindRegisterEvents();
        }

        public void ShowForgotPassword()
        {
            if (_lockerInstance != null) _lockerInstance.SetActive(false);
            if (forgotPasswordVisualTree == null) return;
            SetNewScreen(forgotPasswordVisualTree.CloneTree());
            BindForgotPasswordEvents();
        }

        public void ShowHome()
        {
            if (_lockerInstance != null) _lockerInstance.SetActive(false);
            if (homeVisualTree == null) return;
            SetNewScreen(homeVisualTree.CloneTree());
            BindSharedSidebarEvents("tab-home");
            BindSharedHeaderEvents("tab-home");
            BindHomeEvents();
            ApplyDataBindings();

            _ = FriendLobbyService.Instance.InitializeSocialAsync();

            // Setup character preview for home
            EnsureLockerInstance();
            if (_lockerInstance != null)
            {
                _lockerInstance.SetActive(true);
                ApplyCloudOutfitToPreview();

                // Hide its default UI Document
                var lockerDoc = _lockerInstance.GetComponent<UIDocument>();
                if (lockerDoc != null) lockerDoc.enabled = false;

                // Bind preview image to RenderTexture
                var previewCamLink = _lockerInstance.GetComponentInChildren<PreviewCameraLink>(true);
                if (previewCamLink != null)
                {
                    previewCamLink.InitializeIfNeeded();
                    previewCamLink.ConfigureCamera(60f, new Vector3(0f, 0.75f, 2.4f));
                    if (previewCamLink.previewTexture != null)
                    {
                        var characterAvatar = _root.Q<VisualElement>("character-avatar");
                        if (characterAvatar != null)
                        {
                            characterAvatar.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(previewCamLink.previewTexture));
                        }
                    }
                }
            }
        }

        public void ShowShop()
        {
            _currentlyPreviewedCategory = null;
            _currentlyPreviewedIndex = -1;

            if (shopVisualTree == null) return;
            SetNewScreen(shopVisualTree.CloneTree());
            BindSharedSidebarEvents("tab-shop");
            BindSharedHeaderEvents("tab-shop");
            BindShopEvents();
            ApplyDataBindings();

            // Setup character preview for shop
            EnsureLockerInstance();
            if (_lockerInstance != null)
            {
                _lockerInstance.SetActive(true);
                ApplyCloudOutfitToPreview();

                // Hide its default UI Document
                var lockerDoc = _lockerInstance.GetComponent<UIDocument>();
                if (lockerDoc != null) lockerDoc.enabled = false;

                // Bind preview image to RenderTexture
                var previewCamLink = _lockerInstance.GetComponentInChildren<PreviewCameraLink>(true);
                if (previewCamLink != null)
                {
                    previewCamLink.InitializeIfNeeded();
                    previewCamLink.ConfigureCamera(60f, new Vector3(0f, 0.75f, 2.4f));
                    if (previewCamLink.previewTexture != null)
                    {
                        var previewAvatar = _root.Q<Image>("shop-preview-avatar");
                        if (previewAvatar != null)
                        {
                            previewAvatar.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(previewCamLink.previewTexture));
                        }
                    }
                }
            }
        }

        public void ShowMatchmaking()
        {
            if (_lockerInstance != null) _lockerInstance.SetActive(false);
            if (matchmakingVisualTree == null) return;
            SetNewScreen(matchmakingVisualTree.CloneTree());
            // Matchmaking has stretched header, no sidebar, and active header tab
            BindSharedHeaderEvents("tab-matchmaking");
            BindMatchmakingEvents();
            ApplyDataBindings();
        }

        public void ShowLocker()
        {
            if (lockerVisualTree == null) return;
            SetNewScreen(lockerVisualTree.CloneTree());
            BindSharedSidebarEvents("tab-locker");
            BindSharedHeaderEvents("tab-locker");
            ApplyDataBindings();

            // Setup character preview
            EnsureLockerInstance();
            if (_lockerInstance != null)
            {
                _lockerInstance.SetActive(true);
                ApplyCloudOutfitToPreview();
                
                // Hide its default UI Document because we are rendering inside the LockerMenu page UI
                var lockerDoc = _lockerInstance.GetComponent<UIDocument>();
                if (lockerDoc != null) lockerDoc.enabled = false;

                // Bind preview image to RenderTexture
                var previewCamLink = _lockerInstance.GetComponentInChildren<PreviewCameraLink>(true);
                if (previewCamLink != null)
                {
                    previewCamLink.InitializeIfNeeded();
                    previewCamLink.ConfigureCamera(60f, new Vector3(0f, 0.75f, 2.4f));
                    if (previewCamLink.previewTexture != null)
                    {
                        var previewAvatar = _root.Q<Image>("locker-preview-avatar");
                        if (previewAvatar != null)
                        {
                            previewAvatar.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(previewCamLink.previewTexture));
                        }
                    }
                }

                // Bind customization controller logic to the main UI root
                var customCtrl = _lockerInstance.GetComponent<CustomizationController>();
                if (customCtrl != null)
                {
                    customCtrl.InitializeWithRoot(_root);
                }
            }
        }

        public void HideLocker()
        {
            if (_lockerInstance != null)
                _lockerInstance.SetActive(false);
        }

        public void ShowSettings()
        {
            if (_lockerInstance != null) _lockerInstance.SetActive(false);
            if (settingsVisualTree == null) return;
            SetNewScreen(settingsVisualTree.CloneTree());

            // Get or Add SettingsMenu component
            var settingsMenu = GetComponent<SettingsMenu>();
            if (settingsMenu == null)
            {
                settingsMenu = gameObject.AddComponent<SettingsMenu>();
            }

            settingsMenu.OnCloseSettingsEvent = () =>
            {
                // Return to home screen when Settings Back button is pressed
                ShowHome();
            };

            settingsMenu.InitializeWithRoot(_root);
            BindSharedSidebarEvents("tab-settings");
            BindSharedHeaderEvents("tab-settings");
            ApplyDataBindings();
        }

        // Helper to replace root visual content
        private void SetNewScreen(VisualElement newScreenContent)
        {
            _root = _uiDocument.rootVisualElement;
            if (_root != null)
            {
                _root.style.display = DisplayStyle.Flex;
            }
            _root.Clear();
            newScreenContent.style.flexGrow = 1;
            newScreenContent.style.width = Length.Percent(100);
            newScreenContent.style.height = Length.Percent(100);
            _root.Add(newScreenContent);

            RefreshLobbyInviteUI();
        }

        // ─── Navigation & Button Event Bindings ─────────────────────────

        private void BindLoginEvents()
        {
            var btnLogin = _root.Q<Button>("btn-login");
            var btnForgot = _root.Q<Button>("btn-forgot-password-link");
            var btnGotoRegister = _root.Q<Button>("btn-goto-register-link");
            var btnGoogle = _root.Q<Button>("btn-google-login");
            var errorBanner = _root.Q<Label>("error-banner");
            if (errorBanner != null) errorBanner.style.display = DisplayStyle.None;

            if (btnLogin != null)
            {
                RegisterHoverAndClick(btnLogin, async () => {
                    var usernameInput = _root.Q<TextField>("username-field");
                    var passwordInput = _root.Q<TextField>("password-field");

                    if (usernameInput == null || string.IsNullOrEmpty(usernameInput.value))
                    {
                        ShowErrorBanner("Username or Email cannot be empty.");
                        HighlightInputError(usernameInput, true);
                        return;
                    }
                    HighlightInputError(usernameInput, false);

                    if (passwordInput == null || string.IsNullOrEmpty(passwordInput.value))
                    {
                        ShowErrorBanner("Password cannot be empty.");
                        HighlightInputError(passwordInput, true);
                        return;
                    }
                    HighlightInputError(passwordInput, false);

                    SetButtonLoading(btnLogin, true, "Logging in...");
                    await AuthService.Instance.SignInAsync(usernameInput.value, passwordInput.value);
                });
            }

            if (btnForgot != null)
            {
                RegisterHoverAndClick(btnForgot, ShowForgotPassword);
            }

            if (btnGotoRegister != null)
            {
                RegisterHoverAndClick(btnGotoRegister, ShowRegister);
            }

            if (btnGoogle != null)
            {
                RegisterHoverAndClick(btnGoogle, () => {
                    SetButtonLoading(btnGoogle, true, "Connecting to Google...");
                    AuthService.Instance.SignInWithGoogle();
                });
            }
        }

        private void BindRegisterEvents()
        {
            var btnBackToLogin = _root.Q<Button>("btn-back-to-login-link");
            var btnRegisterSubmit = _root.Q<Button>("btn-register-submit");
            var errorBanner = _root.Q<Label>("error-banner");
            if (errorBanner != null) errorBanner.style.display = DisplayStyle.None;

            if (btnBackToLogin != null)
            {
                RegisterHoverAndClick(btnBackToLogin, ShowLogin);
            }

            if (btnRegisterSubmit != null)
            {
                RegisterHoverAndClick(btnRegisterSubmit, async () => {
                    var usernameInput = _root.Q<TextField>("register-username");
                    var emailInput = _root.Q<TextField>("register-email");
                    var passwordInput = _root.Q<TextField>("register-password");
                    var confirmInput = _root.Q<TextField>("register-confirm-password");

                    if (usernameInput == null || string.IsNullOrEmpty(usernameInput.value) || usernameInput.value.Length < 3)
                    {
                        ShowErrorBanner("Username must be at least 3 characters.");
                        HighlightInputError(usernameInput, true);
                        return;
                    }
                    HighlightInputError(usernameInput, false);

                    if (emailInput == null || string.IsNullOrEmpty(emailInput.value) || !emailInput.value.Contains("@"))
                    {
                        ShowErrorBanner("Please enter a valid email address.");
                        HighlightInputError(emailInput, true);
                        return;
                    }
                    HighlightInputError(emailInput, false);

                    if (passwordInput == null || string.IsNullOrEmpty(passwordInput.value) || passwordInput.value.Length < 8)
                    {
                        ShowErrorBanner("Password must be at least 8 characters.");
                        HighlightInputError(passwordInput, true);
                        return;
                    }
                    HighlightInputError(passwordInput, false);

                    if (confirmInput == null || confirmInput.value != passwordInput.value)
                    {
                        ShowErrorBanner("Passwords do not match.");
                        HighlightInputError(confirmInput, true);
                        return;
                    }
                    HighlightInputError(confirmInput, false);

                    SetButtonLoading(btnRegisterSubmit, true, "Registering...");
                    await AuthService.Instance.SignUpAsync(usernameInput.value, emailInput.value, passwordInput.value);
                });
            }
        }

        private void BindForgotPasswordEvents()
        {
            var forgotEmailCard = _root.Q<VisualElement>("forgot-email-card");
            var otpInnerCard = _root.Q<VisualElement>("otp-inner-card");

            var errorBanner = _root.Q<Label>("error-banner");
            var errorBannerOtp = _root.Q<Label>("error-banner-otp");
            if (errorBanner != null) errorBanner.style.display = DisplayStyle.None;
            if (errorBannerOtp != null) errorBannerOtp.style.display = DisplayStyle.None;

            if (forgotEmailCard != null) forgotEmailCard.style.display = DisplayStyle.Flex;
            if (otpInnerCard != null) otpInnerCard.style.display = DisplayStyle.None;

            var btnForgotSend = _root.Q<Button>("btn-forgot-send");
            var btnForgotBackToLogin = _root.Q<Button>("btn-forgot-back-to-login");
            var emailField = _root.Q<TextField>("forgot-email-field");
            var otpSubtitle = _root.Q<Label>("otp-subtitle");

            if (btnForgotSend != null)
            {
                RegisterHoverAndClick(btnForgotSend, async () => {
                    if (emailField == null || string.IsNullOrEmpty(emailField.value))
                    {
                        ShowErrorBanner("Please enter your email or username.");
                        HighlightInputError(emailField, true);
                        return;
                    }
                    HighlightInputError(emailField, false);

                    _pendingEmail = emailField.value;
                    SetButtonLoading(btnForgotSend, true, "Sending...");

                    bool success = await AuthService.Instance.RequestPasswordResetAsync(_pendingEmail);
                    ResetButtonLoading();

                    if (success)
                    {
                        if (otpSubtitle != null)
                        {
                            otpSubtitle.text = $"We've sent a bubbly 4-digit code to {_pendingEmail}";
                        }

                        if (forgotEmailCard != null) forgotEmailCard.style.display = DisplayStyle.None;
                        if (otpInnerCard != null) otpInnerCard.style.display = DisplayStyle.Flex;
                        if (errorBannerOtp != null) errorBannerOtp.style.display = DisplayStyle.None;
                    }
                    else
                    {
                        ShowErrorBanner("Failed to send code. Please try again.");
                    }
                });
            }

            if (btnForgotBackToLogin != null)
            {
                RegisterHoverAndClick(btnForgotBackToLogin, ShowLogin);
            }

            var btnChangeEmail = _root.Q<Button>("btn-otp-change-email");
            var btnVerify = _root.Q<Button>("btn-otp-verify");
            var btnResend = _root.Q<Button>("btn-otp-resend");

            if (btnChangeEmail != null)
            {
                RegisterHoverAndClick(btnChangeEmail, () => {
                    if (forgotEmailCard != null) forgotEmailCard.style.display = DisplayStyle.Flex;
                    if (otpInnerCard != null) otpInnerCard.style.display = DisplayStyle.None;
                    if (errorBanner != null) errorBanner.style.display = DisplayStyle.None;
                });
            }

            if (btnVerify != null)
            {
                RegisterHoverAndClick(btnVerify, async () => {
                    var otp1 = _root.Q<TextField>("otp-1");
                    var otp2 = _root.Q<TextField>("otp-2");
                    var otp3 = _root.Q<TextField>("otp-3");
                    var otp4 = _root.Q<TextField>("otp-4");

                    string otpCode = $"{(otp1?.value ?? "")}{(otp2?.value ?? "")}{(otp3?.value ?? "")}{(otp4?.value ?? "")}";
                    if (otpCode.Length != 4)
                    {
                        ShowErrorBannerOtp("Please enter all 4 digits of the OTP.");
                        return;
                    }

                    SetButtonLoading(btnVerify, true, "Verifying...");
                    bool success = await AuthService.Instance.VerifyOtpAndResetPasswordAsync(_pendingEmail, otpCode, "NewPassword123");
                    ResetButtonLoading();

                    if (success)
                    {
                        ShowLogin();
                        var banner = _root.Q<Label>("error-banner");
                        if (banner != null)
                        {
                            banner.text = "Password reset successful! Please log in.";
                            banner.style.backgroundColor = new StyleColor(new Color(0.9f, 0.99f, 0.9f));
                            SetBorderColor(banner, new Color(0.7f, 0.9f, 0.7f));
                            banner.style.color = new StyleColor(new Color(0.1f, 0.5f, 0.1f));
                            banner.style.display = DisplayStyle.Flex;
                        }
                    }
                    else
                    {
                        ShowErrorBannerOtp("Invalid verification code. Use 1234 to bypass.");
                    }
                });
            }

            if (btnResend != null)
            {
                RegisterHoverAndClick(btnResend, async () => {
                    SetButtonLoading(btnResend, true, "Resending...");
                    await AuthService.Instance.RequestPasswordResetAsync(_pendingEmail);
                    ResetButtonLoading();
                    Debug.Log("OTP code resent successfully!");
                });
            }
        }

        private void BindHomeEvents()
        {
            var btnPlay = _root.Q<Button>("btn-home-play");
            var btnCreateRoom = _root.Q<Button>("btn-home-create-room");
            var btnTestHost = _root.Q<Button>("btn-home-test-host");

            if (btnPlay != null)
            {
                RegisterHoverAndClick(btnPlay, ShowMatchmaking);
            }

            if (btnCreateRoom != null)
            {
                RegisterHoverAndClick(btnCreateRoom, ShowMatchmaking);
            }

            if (btnTestHost != null)
            {
                RegisterHoverAndClick(btnTestHost, () => {
                    Debug.Log("[StitchUIController] Starting Local Host Test Mode...");
                    var networkManager = Unity.Netcode.NetworkManager.Singleton;
#if UNITY_EDITOR
                    if (networkManager == null)
                    {
                        Debug.Log("[StitchUIController] NetworkManager.Singleton is null. Attempting to auto-load Assets/Resources/NetworkManager.prefab in Editor...");
                        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/NetworkManager.prefab");
                        if (prefab != null)
                        {
                            var go = Instantiate(prefab);
                            go.name = "NetworkManager (Auto-Injected)";
                            networkManager = Unity.Netcode.NetworkManager.Singleton;
                        }
                    }
#endif
                    if (networkManager != null)
                    {
                        if (networkManager.IsListening) networkManager.Shutdown();
                        var utp = (Unity.Netcode.Transports.UTP.UnityTransport)networkManager.NetworkConfig.NetworkTransport;
                        if (utp != null)
                        {
                            utp.SetConnectionData("127.0.0.1", 7777);
                        }
                        global::ForestGameManager.SpawnTestDummiesOnStart = true;
                        if (networkManager.StartHost())
                        {
                            Debug.Log("[StitchUIController] Host started successfully. Loading Forest scene...");
                            networkManager.SceneManager.LoadScene("DemoScene_Forest", UnityEngine.SceneManagement.LoadSceneMode.Single);
                        }
                        else
                        {
                            Debug.LogError("[StitchUIController] Failed to start host!");
                        }
                    }
                    else
                    {
                        Debug.LogError("[StitchUIController] NetworkManager.Singleton is null!");
                    }
                });
            }

            var btnTestInvite = _root.Q<Button>("btn-home-test-invite");
            if (btnTestInvite != null)
            {
                RegisterHoverAndClick(btnTestInvite, () => {
                    Debug.Log("[StitchUIController] Triggering mock lobby invite...");
                    if (FriendLobbyService.Instance != null)
                    {
                        FriendLobbyService.Instance.TriggerMockInvite("CoolGuy99", "123456");
                    }
                });
            }
        }

        private void BindShopEvents()
        {
            var btnBuyFeatured = _root.Q<Button>("btn-buy-featured");
            var btnBuy1 = _root.Q<Button>("btn-buy-item-1");
            var btnBuy2 = _root.Q<Button>("btn-buy-item-2");
            var btnBuy3 = _root.Q<Button>("btn-buy-item-3");
            var btnBuy4 = _root.Q<Button>("btn-buy-item-4");

            // Bind coin icons for prices
            if (singleCoinTex != null)
            {
                _root.Query<VisualElement>(className: "shop-coin-icon").ForEach(icon => {
                    icon.style.backgroundImage = new StyleBackground(singleCoinTex);
                });
            }

            if (btnBuyFeatured != null)
            {
                RegisterHoverAndClick(btnBuyFeatured, () => BuyItem("hat_0", "Blue Cap (Cap A)", 150));
            }
            if (btnBuy1 != null)
            {
                RegisterHoverAndClick(btnBuy1, () => BuyItem("glasses_0", "Pixel Glasses", 400));
            }
            if (btnBuy2 != null)
            {
                RegisterHoverAndClick(btnBuy2, () => BuyItem("hat_1", "Yellow Cap (Cap B)", 250));
            }
            if (btnBuy3 != null)
            {
                RegisterHoverAndClick(btnBuy3, () => BuyItem("neck_0", "Red Scarf", 300));
            }
            if (btnBuy4 != null)
            {
                RegisterHoverAndClick(btnBuy4, () => BuyItem("neck_1", "Gold Necklace", 500));
            }

            var btnPreviewFeatured = _root.Q<Button>("btn-preview-featured");
            var btnPreview1 = _root.Q<Button>("btn-preview-item-1");
            var btnPreview2 = _root.Q<Button>("btn-preview-item-2");
            var btnPreview3 = _root.Q<Button>("btn-preview-item-3");
            var btnPreview4 = _root.Q<Button>("btn-preview-item-4");

            if (btnPreviewFeatured != null) RegisterHoverAndClick(btnPreviewFeatured, () => PreviewItemInShop("hat", 0));
            if (btnPreview1 != null) RegisterHoverAndClick(btnPreview1, () => PreviewItemInShop("glasses", 0));
            if (btnPreview2 != null) RegisterHoverAndClick(btnPreview2, () => PreviewItemInShop("hat", 1));
            if (btnPreview3 != null) RegisterHoverAndClick(btnPreview3, () => PreviewItemInShop("necklace", 0));
            if (btnPreview4 != null) RegisterHoverAndClick(btnPreview4, () => PreviewItemInShop("necklace", 1));

            // Set shop item textures from CustomizationData if assigned
            if (customizationData != null)
            {
                if (customizationData.hats != null && customizationData.hats.Count > 0)
                    SetShopItemTexture("featured-item-thumbnail", customizationData.hats[0].icon);
                if (customizationData.glasses != null && customizationData.glasses.Count > 0)
                    SetShopItemTexture("item-1-thumb", customizationData.glasses[0].icon);
                if (customizationData.hats != null && customizationData.hats.Count > 1)
                    SetShopItemTexture("item-2-thumb", customizationData.hats[1].icon);
                if (customizationData.necklaces != null && customizationData.necklaces.Count > 0)
                    SetShopItemTexture("item-3-thumb", customizationData.necklaces[0].icon);
                if (customizationData.necklaces != null && customizationData.necklaces.Count > 1)
                    SetShopItemTexture("item-4-thumb", customizationData.necklaces[1].icon);
            }

            var btnAll = _root.Q<Button>("btn-filter-all");
            var btnHats = _root.Q<Button>("btn-filter-hats");
            var btnGlasses = _root.Q<Button>("btn-filter-glasses");

            if (btnAll != null) RegisterHoverAndClick(btnAll, () => FilterShop("All", btnAll, btnHats, btnGlasses));
            if (btnHats != null) RegisterHoverAndClick(btnHats, () => FilterShop("Hats", btnAll, btnHats, btnGlasses));
            if (btnGlasses != null) RegisterHoverAndClick(btnGlasses, () => FilterShop("Glasses", btnAll, btnHats, btnGlasses));

            RefreshShopButtons();
        }

        private void FilterShop(string category, Button btnAll, Button btnHats, Button btnGlasses)
        {
            var activeClass = "btn-primary-3d";
            var inactiveClass = "btn-surface-3d";

            if (btnAll != null)
            {
                btnAll.RemoveFromClassList(activeClass);
                btnAll.RemoveFromClassList(inactiveClass);
                btnAll.AddToClassList(category == "All" ? activeClass : inactiveClass);
            }
            if (btnHats != null)
            {
                btnHats.RemoveFromClassList(activeClass);
                btnHats.RemoveFromClassList(inactiveClass);
                btnHats.AddToClassList(category == "Hats" ? activeClass : inactiveClass);
            }
            if (btnGlasses != null)
            {
                btnGlasses.RemoveFromClassList(activeClass);
                btnGlasses.RemoveFromClassList(inactiveClass);
                btnGlasses.AddToClassList(category == "Glasses" ? activeClass : inactiveClass);
            }

            var cardFeatured = _root.Q<VisualElement>("shop-card-featured");
            var card1 = _root.Q<VisualElement>("shop-card-item-1");
            var card2 = _root.Q<VisualElement>("shop-card-item-2");
            var card3 = _root.Q<VisualElement>("shop-card-item-3");
            var card4 = _root.Q<VisualElement>("shop-card-item-4");

            var row1 = _root.Q<VisualElement>("shop-row-1");
            var row2 = _root.Q<VisualElement>("shop-row-2");

            if (cardFeatured != null) cardFeatured.style.display = (category == "All" || category == "Hats") ? DisplayStyle.Flex : DisplayStyle.None;
            if (card1 != null) card1.style.display = (category == "All" || category == "Glasses") ? DisplayStyle.Flex : DisplayStyle.None;
            if (card2 != null) card2.style.display = (category == "All" || category == "Hats") ? DisplayStyle.Flex : DisplayStyle.None;
            if (card3 != null) card3.style.display = (category == "All") ? DisplayStyle.Flex : DisplayStyle.None;
            if (card4 != null) card4.style.display = (category == "All") ? DisplayStyle.Flex : DisplayStyle.None;

            if (row1 != null)
            {
                row1.style.display = (category == "All" || category == "Hats" || category == "Glasses") ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (row2 != null)
            {
                row2.style.display = (category == "All") ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void SetShopItemTexture(string elementName, Texture2D tex)
        {
            var img = _root.Q<Image>(elementName);
            if (img != null)
            {
                img.Clear(); // Clear any sub-elements
                if (tex != null)
                {
                    img.style.backgroundImage = new StyleBackground(tex);
                }
                else
                {
                    img.style.backgroundImage = null;
                }
            }
        }

        private Coroutine _matchmakingTimerCoroutine;
        private float _matchmakingStartTime;

        private void BindMatchmakingEvents()
        {
            var btnCancel = _root.Q<Button>("btn-matchmaking-cancel");
            var btnPlay = _root.Q<Button>("btn-matchmaking-play");
            var statusBanner = _root.Q<VisualElement>("status-banner");
            var matchmakingFooter = _root.Q<VisualElement>("matchmaking-footer");

            // Initialize UI state based on active matchmaking
            var mgr = EdgeParty.ConnectionManagement.MatchmakingManager.Instance;
            if (mgr != null && mgr.IsMatchmaking)
            {
                if (statusBanner != null) statusBanner.style.display = DisplayStyle.Flex;
                if (matchmakingFooter != null) matchmakingFooter.style.display = DisplayStyle.None;
                
                var titleLabel = _root.Q<Label>("matchmaking-title");
                if (titleLabel != null) titleLabel.text = "Matchmaking...";
                
                var btnCancelBanner = _root.Q<Button>("btn-matchmaking-cancel");
                if (btnCancelBanner != null) btnCancelBanner.style.display = DisplayStyle.Flex;

                _matchmakingStartTime = mgr.MatchmakingStartTime;
                if (_matchmakingTimerCoroutine != null) StopCoroutine(_matchmakingTimerCoroutine);
                _matchmakingTimerCoroutine = StartCoroutine(UpdateMatchmakingTimerRoutine());
            }
            else
            {
                if (statusBanner != null) statusBanner.style.display = DisplayStyle.None;
                if (matchmakingFooter != null) matchmakingFooter.style.display = DisplayStyle.Flex;
                if (_matchmakingTimerCoroutine != null)
                {
                    StopCoroutine(_matchmakingTimerCoroutine);
                    _matchmakingTimerCoroutine = null;
                }
            }

            if (btnCancel != null)
            {
                RegisterHoverAndClick(btnCancel, () => {
                    if (EdgeParty.ConnectionManagement.MatchmakingManager.Instance != null)
                    {
                        EdgeParty.ConnectionManagement.MatchmakingManager.Instance.StopMatchmaking();
                    }
                });
            }

            if (btnPlay != null)
            {
                RegisterHoverAndClick(btnPlay, () => {
                    Debug.Log("[StitchUIController] Starting matchmaking via Edgegap...");
                    if (EdgeParty.ConnectionManagement.MatchmakingManager.Instance != null)
                    {
                        EdgeParty.ConnectionManagement.MatchmakingManager.Instance.StartMatchmakingFlow();
                    }
                    else
                    {
                        Debug.LogWarning("[StitchUIController] MatchmakingManager.Instance is null!");
                    }
                });
            }

            var btnLeave = _root.Q<Button>("btn-matchmaking-leave");
            if (btnLeave != null)
            {
                RegisterHoverAndClick(btnLeave, async () => {
                    Debug.Log("[StitchUIController] Leaving lobby...");
                    if (FriendLobbyService.Instance != null)
                    {
                        SetButtonLoading(btnLeave, true, "Leaving...");
                        await FriendLobbyService.Instance.LeaveLobbyAsync();
                        ResetButtonLoading();
                    }
                    ShowHome();
                });
            }

            var matchmakingAddInput = _root.Q<TextField>("add-friend-input");
            if (matchmakingAddInput != null)
            {
                matchmakingAddInput.style.borderTopLeftRadius = 18;
                matchmakingAddInput.style.borderTopRightRadius = 18;
                matchmakingAddInput.style.borderBottomLeftRadius = 18;
                matchmakingAddInput.style.borderBottomRightRadius = 18;
                var inputEl = matchmakingAddInput.Q(className: "unity-text-field__input");
                if (inputEl != null)
                {
                    inputEl.style.borderTopLeftRadius = 18;
                    inputEl.style.borderTopRightRadius = 18;
                    inputEl.style.borderBottomLeftRadius = 18;
                    inputEl.style.borderBottomRightRadius = 18;
                }
            }

            // Platforms Drawer Toggle Logic (Click empty platform to slide cabinet right-drawer in/out)
            var btnInvite2 = _root.Q<Button>("btn-invite-slot-2");
            var btnInvite3 = _root.Q<Button>("btn-invite-slot-3");
            var btnInvite4 = _root.Q<Button>("btn-invite-slot-4");

            if (btnInvite2 != null) RegisterHoverAndClick(btnInvite2, OpenFriendsDrawer);
            if (btnInvite3 != null) RegisterHoverAndClick(btnInvite3, OpenFriendsDrawer);
            if (btnInvite4 != null) RegisterHoverAndClick(btnInvite4, OpenFriendsDrawer);

            // Close drawer if clicking anywhere on matchmaking layout that is not the drawer itself
            var layoutContainer = _root.Q<VisualElement>("matchmaking-layout");
            if (layoutContainer != null)
            {
                layoutContainer.RegisterCallback<PointerDownEvent>(evt => {
                    var drawer = _root.Q<VisualElement>("friends-drawer");
                    if (drawer != null && drawer.ClassListContains("friends-drawer-open"))
                    {
                        // Check if click target is outside the drawer or invite buttons
                        VisualElement target = evt.target as VisualElement;
                        bool insideDrawer = false;
                        while (target != null)
                        {
                            if (target.name == "friends-drawer" || 
                                target.name == "btn-invite-slot-2" || 
                                target.name == "btn-invite-slot-3" || 
                                target.name == "btn-invite-slot-4" ||
                                target.name == "tab-friends-list" ||
                                target.name == "tab-friends-requests" ||
                                target.name == "tab-friends-add" ||
                                target.name == "add-friend-container" ||
                                target.name == "btn-add-friend-submit" ||
                                target.name == "add-friend-input" ||
                                target.name == "drawer-friends-scroll")
                            {
                                insideDrawer = true;
                                break;
                            }
                            target = target.parent;
                        }

                        if (!insideDrawer)
                        {
                            _lastClickTime = Time.unscaledTime;
                            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                            drawer.RemoveFromClassList("friends-drawer-open");
                            var area = _root.Q<VisualElement>("platforms-area");
                            if (area != null) area.RemoveFromClassList("platforms-area-drawer-open");
                        }
                    }
                });
            }

            // Friends Drawer Tabs Setup
            _currentFriendsDrawerTab = "friends";
            var tabFriends = _root.Q<Button>("tab-friends-list");
            var tabRequests = _root.Q<Button>("tab-friends-requests");
            var tabAdd = _root.Q<Button>("tab-friends-add");

            if (tabFriends != null)
            {
                RegisterHoverAndClick(tabFriends, () => {
                    _currentFriendsDrawerTab = "friends";
                    UpdateFriendsDrawerTabsUI();
                    PopulateFriendsDrawer();
                });
            }

            if (tabRequests != null)
            {
                RegisterHoverAndClick(tabRequests, () => {
                    _currentFriendsDrawerTab = "requests";
                    UpdateFriendsDrawerTabsUI();
                    PopulateFriendsDrawer();
                });
            }

            if (tabAdd != null)
            {
                RegisterHoverAndClick(tabAdd, () => {
                    _currentFriendsDrawerTab = "add";
                    UpdateFriendsDrawerTabsUI();
                    PopulateFriendsDrawer();
                });
            }

            // Add Friend Submit Button Setup
            var btnAddSubmit = _root.Q<Button>("btn-add-friend-submit");
            if (btnAddSubmit != null)
            {
                RegisterHoverAndClick(btnAddSubmit, async () => {
                    var inputField = _root.Q<TextField>("add-friend-input");
                    var statusLabel = _root.Q<Label>("add-friend-status");
                    if (inputField != null && !string.IsNullOrEmpty(inputField.value))
                    {
                        if (statusLabel != null)
                        {
                            statusLabel.text = "Sending request...";
                            statusLabel.style.color = new StyleColor(new Color(0.3f, 0.2f, 0f));
                            statusLabel.style.display = DisplayStyle.Flex;
                        }
                        
                        bool ok = await FriendLobbyService.Instance.SendFriendRequestAsync(inputField.value);
                        if (statusLabel != null)
                        {
                            if (ok)
                            {
                                statusLabel.text = "Request sent successfully!";
                                statusLabel.style.color = new StyleColor(new Color(0.1f, 0.5f, 0.1f));
                                inputField.value = "";
                            }
                            else
                            {
                                statusLabel.text = "Failed to send request.";
                                statusLabel.style.color = new StyleColor(new Color(0.7f, 0.1f, 0.1f));
                            }
                        }
                    }
                });
            }

            UpdateFriendsDrawerTabsUI();
            PopulateFriendsDrawer();
            UpdateLobbyPodiums(FriendLobbyService.Instance.LobbyMembers);
        }

        private void HandleMatchmakingStarted()
        {
            var statusBanner = _root.Q<VisualElement>("status-banner");
            if (statusBanner != null)
            {
                statusBanner.style.display = DisplayStyle.Flex;
            }

            var matchmakingFooter = _root.Q<VisualElement>("matchmaking-footer");
            if (matchmakingFooter != null)
            {
                matchmakingFooter.style.display = DisplayStyle.None;
            }

            var btnCancel = _root.Q<Button>("btn-matchmaking-cancel");
            if (btnCancel != null)
            {
                bool inLobby = FriendLobbyService.Instance != null && !string.IsNullOrEmpty(FriendLobbyService.Instance.CurrentLobbyId);
                bool isHost = !inLobby || (FriendLobbyService.Instance != null && FriendLobbyService.Instance.IsHost);
                btnCancel.style.display = isHost ? DisplayStyle.Flex : DisplayStyle.None;
            }

            var titleLabel = _root.Q<Label>("matchmaking-title");
            if (titleLabel != null)
            {
                titleLabel.text = "Matchmaking...";
            }

            var mgr = EdgeParty.ConnectionManagement.MatchmakingManager.Instance;
            _matchmakingStartTime = (mgr != null) ? mgr.MatchmakingStartTime : Time.time;

            if (_matchmakingTimerCoroutine != null) StopCoroutine(_matchmakingTimerCoroutine);
            _matchmakingTimerCoroutine = StartCoroutine(UpdateMatchmakingTimerRoutine());
        }

        private void HandleMatchmakingCancelled()
        {
            var statusBanner = _root.Q<VisualElement>("status-banner");
            if (statusBanner != null)
            {
                statusBanner.style.display = DisplayStyle.None;
            }

            var matchmakingFooter = _root.Q<VisualElement>("matchmaking-footer");
            if (matchmakingFooter != null)
            {
                matchmakingFooter.style.display = DisplayStyle.Flex;
            }

            if (_matchmakingTimerCoroutine != null)
            {
                StopCoroutine(_matchmakingTimerCoroutine);
                _matchmakingTimerCoroutine = null;
            }
        }

        private void HandleMatchmakingSucceeded()
        {
            var titleLabel = _root.Q<Label>("matchmaking-title");
            if (titleLabel != null)
            {
                titleLabel.text = "Match Found!";
            }

            var elapsedLabel = _root.Q<Label>("matchmaking-elapsed");
            if (elapsedLabel != null)
            {
                elapsedLabel.text = "Connecting to server...";
            }

            var btnCancel = _root.Q<Button>("btn-matchmaking-cancel");
            if (btnCancel != null)
            {
                btnCancel.style.display = DisplayStyle.None;
            }

            if (_matchmakingTimerCoroutine != null)
            {
                StopCoroutine(_matchmakingTimerCoroutine);
                _matchmakingTimerCoroutine = null;
            }
        }

        private void HandleMatchmakingFailed(string message)
        {
            var statusBanner = _root.Q<VisualElement>("status-banner");
            if (statusBanner != null)
            {
                statusBanner.style.display = DisplayStyle.None;
            }

            var matchmakingFooter = _root.Q<VisualElement>("matchmaking-footer");
            if (matchmakingFooter != null)
            {
                matchmakingFooter.style.display = DisplayStyle.Flex;
            }

            if (_matchmakingTimerCoroutine != null)
            {
                StopCoroutine(_matchmakingTimerCoroutine);
                _matchmakingTimerCoroutine = null;
            }

            ShowMatchmakingErrorPopup(message);
        }

        public void ShowErrorPopup(string title, string message)
        {
            if (_root == null) return;

            var existing = _root.Q<VisualElement>("modal-overlay");
            if (existing != null)
            {
                _root.Remove(existing);
            }

            var card = CreateModalOverlay(title, null);

            Label infoText = new Label(message);
            infoText.AddToClassList("font-body");
            infoText.style.fontSize = 20;
            infoText.style.color = new StyleColor(new Color(0.61f, 0.11f, 0.11f));
            infoText.style.whiteSpace = WhiteSpace.Normal;
            infoText.style.marginBottom = 24;
            infoText.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(infoText);

            Button okBtn = new Button();
            okBtn.text = "Đóng";
            okBtn.AddToClassList("bouncy-btn");
            okBtn.AddToClassList("btn-primary-3d");
            okBtn.style.height = 56;
            okBtn.style.fontSize = 20;
            okBtn.style.paddingTop = 0;
            okBtn.style.paddingBottom = 0;
            okBtn.style.borderTopLeftRadius = 20;
            okBtn.style.borderTopRightRadius = 20;
            okBtn.style.borderBottomLeftRadius = 20;
            okBtn.style.borderBottomRightRadius = 20;

            RegisterHoverAndClick(okBtn, () => {
                var overlay = _root.Q<VisualElement>("modal-overlay");
                if (overlay != null)
                {
                    _root.Remove(overlay);
                }
            });

            card.Add(okBtn);
        }

        private void ShowMatchmakingErrorPopup(string message)
        {
            ShowErrorPopup("Lỗi Kết Nối", message);
        }

        private System.Collections.IEnumerator UpdateMatchmakingTimerRoutine()
        {
            var elapsedLabel = _root.Q<Label>("matchmaking-elapsed");
            while (true)
            {
                float elapsed = Time.time - _matchmakingStartTime;
                int minutes = (int)(elapsed / 60f);
                int seconds = (int)(elapsed % 60f);
                if (elapsedLabel != null)
                {
                    elapsedLabel.text = string.Format("Time elapsed: {0:D2}:{1:D2}", minutes, seconds);
                }
                yield return new WaitForSeconds(1f);
            }
        }

        // Shared sidebar navigation bindings
        private void BindSharedSidebarEvents(string activeTabName)
        {
            var btnHome = _root.Q<Button>("tab-home");
            var btnLocker = _root.Q<Button>("tab-locker");
            var btnShop = _root.Q<Button>("tab-shop");
            var btnSettings = _root.Q<Button>("tab-settings");
            var btnAddFriend = _root.Q<Button>("btn-add-friend");
            var btnFriendRequests = _root.Q<Button>("btn-friend-requests");
            var btnLogout = _root.Q<Button>("btn-logout");

            // Setup active visual state
            if (btnHome != null) SetupSidebarTabState(btnHome, activeTabName == "tab-home", ShowHome);
            if (btnLocker != null) SetupSidebarTabState(btnLocker, activeTabName == "tab-locker", ShowLocker);
            if (btnShop != null) SetupSidebarTabState(btnShop, activeTabName == "tab-shop", ShowShop);
            if (btnSettings != null) SetupSidebarTabState(btnSettings, activeTabName == "tab-settings", ShowSettings);

            if (btnAddFriend != null)
            {
                RegisterHoverAndClick(btnAddFriend, ShowAddFriendPopup);
            }
            if (btnFriendRequests != null)
            {
                RegisterHoverAndClick(btnFriendRequests, ShowFriendRequestsPopup);
            }
            if (btnLogout != null)
            {
                RegisterHoverAndClick(btnLogout, async () => {
                    if (FriendLobbyService.Instance != null)
                    {
                        await FriendLobbyService.Instance.LeaveLobbyAsync();
                        FriendLobbyService.Instance.ClearSocialAndLobbyState();
                    }
                    AuthService.Instance.SignOut();
                    ShowLogin();
                });
            }
        }

        private void SetupSidebarTabState(Button tabButton, bool isActive, System.Action onClickAction)
        {
            if (isActive)
            {
                tabButton.AddToClassList("active-tab");
            }
            else
            {
                tabButton.RemoveFromClassList("active-tab");
            }

            // Update icon tint color dynamically to match code.html
            var icon = tabButton.Q<VisualElement>(className: "sidebar-tab-icon");
            if (icon != null)
            {
                icon.style.unityBackgroundImageTintColor = isActive
                    ? new StyleColor(new Color(0.31f, 0.21f, 0f)) // #4e3500 (active)
                    : new StyleColor(new Color(0.56f, 0.30f, 0f, 0.7f)); // rgba(144, 77, 0, 0.7) (inactive)
            }

            if (!isActive)
            {
                RegisterHoverAndClick(tabButton, onClickAction);
            }
        }

        // Shared header navigation tab bindings
        private void BindSharedHeaderEvents(string activeHeaderTab)
        {
            var tabHomeHeader = _root.Q<Button>("tab-home-header");
            var tabMatchmaking = _root.Q<Button>("tab-matchmaking");
            var tabEvents = _root.Q<Button>("tab-events");
            var btnToll = _root.Q<Button>("btn-toll");
            var avatar = _root.Q<Image>("header-avatar");

            if (tabHomeHeader != null)
            {
                bool isActive = (activeHeaderTab == "tab-home");
                if (isActive) tabHomeHeader.AddToClassList("active-header-tab");
                else tabHomeHeader.RemoveFromClassList("active-header-tab");

                if (!isActive)
                {
                    RegisterHoverAndClick(tabHomeHeader, ShowHome);
                }
            }

            if (tabMatchmaking != null)
            {
                bool isActive = (activeHeaderTab == "tab-matchmaking");
                if (isActive) tabMatchmaking.AddToClassList("active-header-tab");
                else tabMatchmaking.RemoveFromClassList("active-header-tab");

                if (!isActive)
                {
                    RegisterHoverAndClick(tabMatchmaking, ShowMatchmaking);
                }
            }

            if (tabEvents != null)
            {
                bool isActive = (activeHeaderTab == "tab-events");
                if (isActive) tabEvents.AddToClassList("active-header-tab");
                else tabEvents.RemoveFromClassList("active-header-tab");

                if (!isActive)
                {
                    RegisterHoverAndClick(tabEvents, null);
                }
            }

            if (btnToll != null)
            {
                RegisterHoverAndClick(btnToll, () => Debug.Log("Toll Store open requested"));
            }

            var coinCounter = _root.Q<VisualElement>("coin-counter");
            if (coinCounter != null)
            {
                coinCounter.RegisterCallback<PointerDownEvent>(_ => {
                    EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                    ShowRechargePopup();
                });

                var coinIcon = coinCounter.Q<VisualElement>("coin-icon");
                if (coinIcon != null && singleCoinTex != null)
                {
                    coinIcon.style.backgroundImage = new StyleBackground(singleCoinTex);
                }
            }

            if (avatar != null)
            {
                avatar.RegisterCallback<PointerDownEvent>(_ => {
                    EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                    Debug.Log("Clicked on Player Profile Avatar!");
                });
            }
        }

        // Helper to register standard hover and click sound trigger hooks
        private void RegisterHoverAndClick(Button btn, System.Action onClickAction)
        {
            if (btn == null) return;
            
            // PointerEnter (Hover) Trigger
            btn.RegisterCallback<PointerEnterEvent>(_ => {
                if (Time.unscaledTime - _lastClickTime < 0.2f) return;
                if (Time.unscaledTime - _lastHoverTime < 0.15f) return;
                _lastHoverTime = Time.unscaledTime;
                EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundHover);
            });

            // Click Trigger
            btn.clicked += () => {
                _lastClickTime = Time.unscaledTime;
                EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                onClickAction?.Invoke();
            };
        }

        // ─── State Bindings & Data Layer ───────────────────────────────

        private void ApplyDataBindings()
        {
            // ── Pull live data from CloudSaveManager (single source of truth) ──
            if (CloudSaveManager.Instance != null && CloudSaveManager.Instance.IsLoaded)
                _currentCoins = CloudSaveManager.Instance.CachedCoins;

            if (FriendLobbyService.Instance != null)
            {
                _onlineFriendsCount = FriendLobbyService.Instance.Friends.FindAll(f => f.IsOnline).Count;
            }

            // Binds dynamic stats to standard UI elements
            var coinsLabel = _root.Q<Label>("coin-value");
            if (coinsLabel != null) coinsLabel.text = _currentCoins.ToString("N0");

            var friendsOnlineText = _root.Q<Label>("friends-online-text");
            if (friendsOnlineText != null) friendsOnlineText.text = $"{_onlineFriendsCount} Online";

            // Profile Title on Sidebar showing current username
            var profileTitle = _root.Q<Label>("profile-title");
            var profileDisc = _root.Q<Label>("profile-discriminator");
            if (profileTitle != null)
            {
                if (!string.IsNullOrEmpty(_username) && _username.Contains("#"))
                {
                    var parts = _username.Split('#');
                    profileTitle.text = parts[0];
                    if (profileDisc != null)
                    {
                        profileDisc.text = "#" + parts[1];
                        profileDisc.style.display = DisplayStyle.Flex;
                    }
                }
                else
                {
                    profileTitle.text = _username;
                    if (profileDisc != null)
                    {
                        profileDisc.style.display = DisplayStyle.None;
                    }
                }
            }

            // Update friend requests badge if present on sidebar
            var badge = _root.Q<VisualElement>("request-badge");
            var badgeCount = _root.Q<Label>("request-badge-count");
            if (badge != null && badgeCount != null)
            {
                int count = FriendLobbyService.Instance != null ? FriendLobbyService.Instance.IncomingRequests.Count : 0;
                if (count > 0)
                {
                    badgeCount.text = count.ToString();
                    badge.style.display = DisplayStyle.Flex;
                }
                else
                {
                    badge.style.display = DisplayStyle.None;
                }
            }

            // If shop page is active, refresh shop buttons state
            if (_root != null && _root.Q<Button>("btn-buy-featured") != null)
            {
                RefreshShopButtons();
            }

            Debug.Log($"Dynamic data applied successfully. Coins: {_currentCoins}, User: {_username}");
        }

        public void SetCoins(int coinAmount)
        {
            _currentCoins = coinAmount;
            ApplyDataBindings();
        }

        public void SetFriendsOnline(int onlineCount)
        {
            _onlineFriendsCount = onlineCount;
            ApplyDataBindings();
        }

        // ─── Interaction Handlers ──────────────────────────────────────

        private void ToggleFriendsDrawer()
        {
            var drawer = _root.Q<VisualElement>("friends-drawer");
            var area = _root.Q<VisualElement>("platforms-area");
            if (drawer != null)
            {
                drawer.ToggleInClassList("friends-drawer-open");
                if (area != null) area.ToggleInClassList("platforms-area-drawer-open");
            }
        }

        private void OpenFriendsDrawer()
        {
            var drawer = _root.Q<VisualElement>("friends-drawer");
            var area = _root.Q<VisualElement>("platforms-area");
            if (drawer != null && !drawer.ClassListContains("friends-drawer-open"))
            {
                drawer.AddToClassList("friends-drawer-open");
                if (area != null) area.AddToClassList("platforms-area-drawer-open");
            }
        }

        private void BuyItem(string itemId, string itemName, int price)
        {
            var csm = CloudSaveManager.Instance;
            int coins = csm != null && csm.IsLoaded ? csm.CachedCoins : _currentCoins;

            if (csm != null && csm.OwnsItem(itemId))
            {
                Debug.Log($"Already own {itemName}!");
                return;
            }

            if (coins >= price)
            {
                coins -= price;
                _currentCoins = coins;

                List<string> ownedItems = csm != null ? new List<string>(csm.CachedOwnedItems) : new List<string>();
                if (!ownedItems.Contains(itemId)) ownedItems.Add(itemId);

                // Persist to cloud
                if (csm != null)
                    _ = csm.SaveCoinsAndItemsAsync(coins, ownedItems);

                ApplyDataBindings();
                RefreshShopButtons();
                Debug.Log($"Successfully bought {itemName}! Remaining coins: {coins}");
            }
            else
            {
                Debug.LogWarning($"Insufficient coins to buy {itemName}! Have: {coins}, Need: {price}");
                ShowErrorPopup("Không Đủ Xu", $"Bạn không có đủ xu để mua {itemName}.\nBạn cần thêm {price - coins} xu.");
            }
        }

        private void RefreshShopButtons()
        {
            if (_root == null) return;
            var btnBuyFeatured = _root.Q<Button>("btn-buy-featured");
            var btnBuy1 = _root.Q<Button>("btn-buy-item-1");
            var btnBuy2 = _root.Q<Button>("btn-buy-item-2");
            var btnBuy3 = _root.Q<Button>("btn-buy-item-3");
            var btnBuy4 = _root.Q<Button>("btn-buy-item-4");

            UpdateSingleShopButton(btnBuyFeatured, "featured-price-indicator", "hat_0", "Buy Now", true);
            UpdateSingleShopButton(btnBuy1, "price-item-1", "glasses_0", "🛒", false);
            UpdateSingleShopButton(btnBuy2, "price-item-2", "hat_1", "🛒", false);
            UpdateSingleShopButton(btnBuy3, "price-item-3", "neck_0", "🛒", false);
            UpdateSingleShopButton(btnBuy4, "price-item-4", "neck_1", "🛒", false);
        }

        private void UpdateSingleShopButton(Button button, string priceContainerName, string itemId, string defaultText, bool isFeatured)
        {
            if (button == null) return;

            var csm = CloudSaveManager.Instance;
            bool isOwned = csm != null && csm.IsLoaded && csm.OwnsItem(itemId);

            var priceIndicator = _root.Q<VisualElement>(priceContainerName);

            if (isOwned)
            {
                button.text = "Owned";
                button.SetEnabled(false);
                button.RemoveFromClassList("btn-primary-3d");
                button.RemoveFromClassList("btn-surface-3d");
                button.AddToClassList("btn-surface-3d");
                button.style.opacity = 0.6f;

                if (!isFeatured)
                {
                    button.style.width = StyleKeyword.Auto;
                    button.style.height = 40;
                    button.style.paddingLeft = 12;
                    button.style.paddingRight = 12;
                    button.style.fontSize = 14;
                    button.style.borderTopLeftRadius = 12;
                    button.style.borderTopRightRadius = 12;
                    button.style.borderBottomLeftRadius = 12;
                    button.style.borderBottomRightRadius = 12;
                }

                if (priceIndicator != null)
                {
                    priceIndicator.style.display = DisplayStyle.None;
                }
            }
            else
            {
                button.text = defaultText;
                button.SetEnabled(true);
                button.style.opacity = 1f;

                if (isFeatured)
                {
                    button.RemoveFromClassList("btn-surface-3d");
                    button.AddToClassList("btn-primary-3d");
                }
                else
                {
                    button.RemoveFromClassList("btn-primary-3d");
                    button.AddToClassList("btn-surface-3d");
                    button.style.width = 60;
                    button.style.height = 60;
                    button.style.paddingLeft = StyleKeyword.Null;
                    button.style.paddingRight = StyleKeyword.Null;
                    button.style.fontSize = 24;
                    button.style.borderTopLeftRadius = 30;
                    button.style.borderTopRightRadius = 30;
                    button.style.borderBottomLeftRadius = 30;
                    button.style.borderBottomRightRadius = 30;
                }

                if (priceIndicator != null)
                {
                    priceIndicator.style.display = DisplayStyle.Flex;
                }
            }
        }

        private void PreviewItemInShop(string category, int index)
        {
            EnsureLockerInstance();
            if (_lockerInstance == null) return;
            var appearance = _lockerInstance.GetComponentInChildren<NetworkPlayerAppearance>(true);
            if (appearance == null)
            {
                var childTransform = _lockerInstance.transform.Find("Chibi_Monkey_00 Variant");
                if (childTransform != null)
                {
                    appearance = childTransform.gameObject.AddComponent<NetworkPlayerAppearance>();
                    appearance.data = customizationData;
                }
            }

            if (appearance != null)
            {
                if (_currentlyPreviewedCategory == category && _currentlyPreviewedIndex == index)
                {
                    // Clicked the active preview again -> Toggle it OFF (reset to cloud outfit)
                    _currentlyPreviewedCategory = null;
                    _currentlyPreviewedIndex = -1;
                    ApplyCloudOutfitToPreview();
                    Debug.Log($"[StitchUIController] Cleared shop preview (toggled OFF)");
                }
                else
                {
                    // Clicked a different preview -> Reset other previews first, then preview this single item
                    _currentlyPreviewedCategory = category;
                    _currentlyPreviewedIndex = index;
                    
                    ApplyCloudOutfitToPreview(); // Reset to actual equipped outfit
                    appearance.PreviewItem(category, index); // Apply the single item preview
                    Debug.Log($"[StitchUIController] Previewed single shop item: Category={category}, Index={index}");
                }
            }
        }

        private void ApplyCloudOutfitToPreview()
        {
            EnsureLockerInstance();
            if (_lockerInstance == null) return;

            var appearance = _lockerInstance.GetComponentInChildren<NetworkPlayerAppearance>(true);
            if (appearance == null)
            {
                var childTransform = _lockerInstance.transform.Find("Chibi_Monkey_00 Variant");
                if (childTransform != null)
                {
                    appearance = childTransform.gameObject.AddComponent<NetworkPlayerAppearance>();
                    appearance.data = customizationData;
                }
            }

            if (appearance != null)
            {
                var csm = CloudSaveManager.Instance;
                if (csm != null && csm.IsLoaded)
                {
                    var outfit = csm.CachedEquipped;
                    appearance.PreviewItem("hat", outfit.hat);
                    appearance.PreviewItem("glasses", outfit.glasses);
                    appearance.PreviewItem("necklace", outfit.necklace);
                    appearance.PreviewItem("emotion", outfit.emotion);
                    appearance.PreviewItem("color", outfit.color);
                }
            }
        }

        private void SendChatMessage()
        {
            var chatInput = _root.Q<TextField>("chat-input");
            var scroll = _root.Q<ScrollView>();
            if (chatInput != null && !string.IsNullOrEmpty(chatInput.value) && scroll != null)
            {
                var newMsg = new Label($"{_username}: {chatInput.value}");
                newMsg.AddToClassList("font-body");
                newMsg.style.fontSize = 12;
                newMsg.style.color = new Color(0.12f, 0.11f, 0.06f);
                newMsg.style.marginBottom = 4;
                scroll.Add(newMsg);
                
                chatInput.value = "";
            }
        }

        // ─── UI Helper Methods for UGS ────────────────────────────────

        private void SetButtonLoading(Button button, bool isLoading, string loadingText = "Processing...")
        {
            ResetButtonLoading();

            if (isLoading && button != null)
            {
                _loadingButton = button;
                _originalButtonText = button.text;
                button.text = loadingText;
                button.AddToClassList("btn-loading");
                button.SetEnabled(false);

                var label = button.Q<Label>(className: "social-btn-text");
                if (label != null)
                {
                    _originalButtonText = label.text;
                    label.text = loadingText;
                }
            }
        }

        private void ResetButtonLoading()
        {
            if (_loadingButton != null)
            {
                _loadingButton.RemoveFromClassList("btn-loading");
                _loadingButton.SetEnabled(true);
                
                var label = _loadingButton.Q<Label>(className: "social-btn-text");
                if (label != null)
                {
                    label.text = _originalButtonText;
                }
                else
                {
                    _loadingButton.text = _originalButtonText;
                }
                
                _loadingButton = null;
            }
        }

        private void ShowErrorBanner(string message)
        {
            var banner = _root.Q<Label>("error-banner");
            if (banner != null)
            {
                banner.text = message;
                banner.style.backgroundColor = new StyleColor(new Color(0.99f, 0.91f, 0.91f));
                SetBorderColor(banner, new Color(0.97f, 0.7f, 0.7f));
                banner.style.color = new StyleColor(new Color(0.61f, 0.11f, 0.11f));
                banner.style.display = DisplayStyle.Flex;
            }
        }

        private void HideErrorBanner()
        {
            var banner = _root.Q<Label>("error-banner");
            if (banner != null)
            {
                banner.style.display = DisplayStyle.None;
            }
        }

        private void ShowErrorBannerOtp(string message)
        {
            var banner = _root.Q<Label>("error-banner-otp");
            if (banner != null)
            {
                banner.text = message;
                banner.style.display = DisplayStyle.Flex;
            }
        }

        private void HighlightInputError(TextField field, bool isError)
        {
            if (field == null) return;
            if (isError)
            {
                field.AddToClassList("input-error");
            }
            else
            {
                field.RemoveFromClassList("input-error");
            }
        }

        private void SetBorderColor(VisualElement element, Color color)
        {
            if (element == null) return;
            element.style.borderTopColor = new StyleColor(color);
            element.style.borderRightColor = new StyleColor(color);
            element.style.borderBottomColor = new StyleColor(color);
            element.style.borderLeftColor = new StyleColor(color);
        }



        private void UpdateFriendsDrawerTabsUI()
        {
            var tabFriends = _root.Q<Button>("tab-friends-list");
            var tabRequests = _root.Q<Button>("tab-friends-requests");
            var tabAdd = _root.Q<Button>("tab-friends-add");
            var addContainer = _root.Q<VisualElement>("add-friend-container");

            if (tabFriends != null) SetupDrawerTabState(tabFriends, _currentFriendsDrawerTab == "friends");
            if (tabRequests != null) SetupDrawerTabState(tabRequests, _currentFriendsDrawerTab == "requests");
            if (tabAdd != null) SetupDrawerTabState(tabAdd, _currentFriendsDrawerTab == "add");

            if (addContainer != null)
            {
                addContainer.style.display = (_currentFriendsDrawerTab == "add") ? DisplayStyle.Flex : DisplayStyle.None;
                var statusLabel = addContainer.Q<Label>("add-friend-status");
                if (statusLabel != null) statusLabel.style.display = DisplayStyle.None;
            }
        }

        private void SetupDrawerTabState(Button tabButton, bool isActive)
        {
            if (isActive)
            {
                tabButton.AddToClassList("active-tab");
            }
            else
            {
                tabButton.RemoveFromClassList("active-tab");
            }
        }

        private void PopulateFriendsDrawer()
        {
            var scroll = _root.Q<ScrollView>("drawer-friends-scroll");
            if (scroll == null) return;

            scroll.Clear();

            // Dynamic drawer subtitle status
            var drawerSub = _root.Q<Label>("friends-online-text-drawer");
            int onlineCount = FriendLobbyService.Instance != null ? FriendLobbyService.Instance.Friends.FindAll(f => f.IsOnline).Count : 0;
            if (drawerSub != null)
            {
                drawerSub.text = $"{onlineCount} Online";
            }

            if (_currentFriendsDrawerTab == "friends")
            {
                var friends = FriendLobbyService.Instance != null ? FriendLobbyService.Instance.Friends : new List<FriendLobbyService.FriendInfo>();
                foreach (var friend in friends)
                {
                    VisualElement row = new VisualElement();
                    row.AddToClassList("friend-row-item");
                    row.AddToClassList("row-between");
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.justifyContent = Justify.SpaceBetween;

                    VisualElement info = new VisualElement();
                    info.AddToClassList("friend-info-block");
                    info.style.flexDirection = FlexDirection.Row;
                    info.style.alignItems = Align.Center;

                    VisualElement avatar = new VisualElement();
                    avatar.AddToClassList("friend-avatar-circle");
                    if (!friend.IsOnline)
                    {
                        avatar.AddToClassList("offline-avatar");
                    }
                    else
                    {
                        VisualElement dot = new VisualElement();
                        dot.AddToClassList("online-indicator");
                        avatar.Add(dot);
                    }

                    // Assign unique preset based on friend username hash
                    int avatarIdx = Mathf.Abs(friend.Username.GetHashCode() % 3) + 1;
                    avatar.AddToClassList($"avatar-preset-{avatarIdx}");

                    info.Add(avatar);

                    Label name = new Label(friend.Username);
                    name.AddToClassList("font-headline");
                    name.AddToClassList("friend-name");
                    if (!friend.IsOnline)
                    {
                        name.AddToClassList("offline-name");
                    }
                    info.Add(name);
                    row.Add(info);

                    if (friend.IsOnline)
                    {
                        Button inviteBtn = new Button();
                        inviteBtn.AddToClassList("bouncy-btn");
                        inviteBtn.style.height = 32;

                        var activeInvite = FriendLobbyService.Instance != null ? FriendLobbyService.Instance.CurrentInvite : null;
                        bool hasInviteFromThisFriend = activeInvite != null && 
                            (activeInvite.InviterId == friend.Id || activeInvite.InviterName == friend.Username);

                        if (hasInviteFromThisFriend)
                        {
                            inviteBtn.text = "Join";
                            inviteBtn.AddToClassList("btn-primary-3d");
                            inviteBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.62f, 0.18f)); // green
                            inviteBtn.style.width = 48;
                            inviteBtn.style.fontSize = 11;
                            
                            RegisterHoverAndClick(inviteBtn, async () =>
                            {
                                if (FriendLobbyService.Instance != null)
                                {
                                    bool ok = await FriendLobbyService.Instance.JoinLobbyByCodeAsync(activeInvite.LobbyCode);
                                    if (ok)
                                    {
                                        FriendLobbyService.Instance.ClearInvite();
                                        ShowMatchmaking();
                                    }
                                }
                            });
                        }
                        else
                        {
                            inviteBtn.text = "+";
                            inviteBtn.AddToClassList("btn-primary-3d");
                            inviteBtn.AddToClassList("friend-add-btn");
                            inviteBtn.style.width = 32;
                            
                            RegisterHoverAndClick(inviteBtn, async () =>
                            {
                                if (FriendLobbyService.Instance != null)
                                {
                                    await FriendLobbyService.Instance.SendLobbyInviteAsync(friend.Id, friend.Username);
                                }
                            });
                        }
                        row.Add(inviteBtn);
                    }
                    scroll.Add(row);
                }
            }
            else if (_currentFriendsDrawerTab == "requests")
            {
                var requests = FriendLobbyService.Instance != null ? FriendLobbyService.Instance.IncomingRequests : new List<FriendLobbyService.FriendRequest>();
                if (requests.Count == 0)
                {
                    Label emptyLabel = new Label("No pending requests");
                    emptyLabel.AddToClassList("font-body");
                    emptyLabel.style.fontSize = 14;
                    emptyLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
                    emptyLabel.style.marginTop = 16;
                    emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    scroll.Add(emptyLabel);
                }
                else
                {
                    foreach (var req in requests)
                    {
                        VisualElement row = new VisualElement();
                        row.AddToClassList("friend-row-item");
                        row.AddToClassList("row-between");
                        row.style.flexDirection = FlexDirection.Row;
                        row.style.alignItems = Align.Center;
                        row.style.justifyContent = Justify.SpaceBetween;

                        VisualElement info = new VisualElement();
                        info.AddToClassList("friend-info-block");
                        info.style.flexDirection = FlexDirection.Row;
                        info.style.alignItems = Align.Center;

                        VisualElement avatar = new VisualElement();
                        avatar.AddToClassList("friend-avatar-circle");
                        avatar.AddToClassList("offline-avatar"); // requests are offline look by default

                        // Assign unique preset based on request username hash
                        int avatarIdx = Mathf.Abs(req.Username.GetHashCode() % 3) + 1;
                        avatar.AddToClassList($"avatar-preset-{avatarIdx}");

                        info.Add(avatar);

                        Label name = new Label(req.Username);
                        name.AddToClassList("font-headline");
                        name.AddToClassList("friend-name");
                        info.Add(name);
                        row.Add(info);

                        VisualElement actions = new VisualElement();
                        actions.style.flexDirection = FlexDirection.Row;

                        Button acceptBtn = new Button();
                        acceptBtn.text = "✓";
                        acceptBtn.AddToClassList("bouncy-btn");
                        acceptBtn.AddToClassList("btn-primary-3d");
                        acceptBtn.style.width = 32;
                        acceptBtn.style.height = 32;
                        RegisterHoverAndClick(acceptBtn, async () =>
                        {
                            await FriendLobbyService.Instance.AcceptFriendRequestAsync(req.Id);
                        });
                        actions.Add(acceptBtn);

                        Button declineBtn = new Button();
                        declineBtn.text = "✗";
                        declineBtn.AddToClassList("bouncy-btn");
                        declineBtn.AddToClassList("btn-surface-3d");
                        declineBtn.style.width = 32;
                        declineBtn.style.height = 32;
                        declineBtn.style.marginLeft = 4;
                        RegisterHoverAndClick(declineBtn, async () =>
                        {
                            await FriendLobbyService.Instance.DeclineFriendRequestAsync(req.Id);
                        });
                        actions.Add(declineBtn);

                        row.Add(actions);
                        scroll.Add(row);
                    }
                }
            }
        }

        private void UpdateLobbyPodiums(List<string> members)
        {
            // ── Slot 1 = always the local player ──────────────────────────────
            var slot1Btn = _root.Q<VisualElement>("podium-player-1");
            if (slot1Btn != null)
            {
                var tagText1 = slot1Btn.Q<Label>(className: "podium-tag-text");
                if (tagText1 != null) tagText1.text = _username;
            }

            // ── Slots 2-4 = other lobby members ──────────────────────────────
            for (int i = 2; i <= 4; i++)
            {
                var slotBtn = _root.Q<Button>($"btn-invite-slot-{i}");
                if (slotBtn == null) continue;

                int memberIndex = i - 2; // OTHER members start at index 0 (slot 2) since local player is skipped
                if (memberIndex < members.Count)
                {
                    string memberName = members[memberIndex] ?? "Unknown";
                    
                    // Occupied State
                    slotBtn.RemoveFromClassList("empty-podium");
                    slotBtn.AddToClassList("occupied-podium");
                    
                    var plusLabel = slotBtn.Q<Label>(className: "empty-podium-plus");
                    if (plusLabel != null) plusLabel.style.display = DisplayStyle.None;
                    
                    var tagText = slotBtn.Q<Label>(className: "podium-tag-text");
                    if (tagText != null) tagText.text = memberName;
                    
                    // Add visual avatar container inside the button if not already present
                    var avatarImg = slotBtn.Q<VisualElement>(className: "podium-avatar-img");
                    if (avatarImg == null)
                    {
                        avatarImg = new VisualElement();
                        avatarImg.AddToClassList("podium-avatar-img");
                        slotBtn.Insert(0, avatarImg);
                    }
                    avatarImg.style.display = DisplayStyle.Flex;

                    // Clear previous presets
                    avatarImg.RemoveFromClassList("avatar-preset-1");
                    avatarImg.RemoveFromClassList("avatar-preset-2");
                    avatarImg.RemoveFromClassList("avatar-preset-3");

                    // Assign unique preset based on player name hash
                    int avatarIdx = Mathf.Abs(memberName.GetHashCode() % 3) + 1;
                    avatarImg.AddToClassList($"avatar-preset-{avatarIdx}");
                }
                else
                {
                    // Empty State
                    slotBtn.AddToClassList("empty-podium");
                    slotBtn.RemoveFromClassList("occupied-podium");
                    
                    var plusLabel = slotBtn.Q<Label>(className: "empty-podium-plus");
                    if (plusLabel != null) plusLabel.style.display = DisplayStyle.Flex;
                    
                    var tagText = slotBtn.Q<Label>(className: "podium-tag-text");
                    if (tagText != null) tagText.text = "Empty";
                    
                    var avatarImg = slotBtn.Q<VisualElement>(className: "podium-avatar-img");
                    if (avatarImg != null) avatarImg.style.display = DisplayStyle.None;
                }
            }

            // ── Matchmaking Play Button State based on Host Authority & Lobby Size ──
            var btnPlay = _root.Q<Button>("btn-matchmaking-play");
            var btnLeave = _root.Q<Button>("btn-matchmaking-leave");
            if (FriendLobbyService.Instance != null)
            {
                bool inLobby = !string.IsNullOrEmpty(FriendLobbyService.Instance.CurrentLobbyId);
                bool isHost = !inLobby || FriendLobbyService.Instance.IsHost;
                int playerCount = members.Count + 1; // local player + other members
                
                if (btnLeave != null)
                {
                    btnLeave.style.display = inLobby ? DisplayStyle.Flex : DisplayStyle.None;
                }

                if (btnPlay != null)
                {
                    if (!isHost)
                    {
                        btnPlay.text = "WAITING FOR HOST";
                        btnPlay.SetEnabled(false);
                    }
                    else
                    {
                        if (playerCount == 1 || playerCount == 2)
                        {
                            btnPlay.text = "PLAY";
                            btnPlay.SetEnabled(true);
                        }
                        else if (playerCount == 3)
                        {
                            btnPlay.text = "3 PLAYERS NOT SUPPORTED";
                            btnPlay.SetEnabled(false);
                        }
                        else // 4 players
                        {
                            btnPlay.text = "ROOM FULL";
                            btnPlay.SetEnabled(false);
                        }
                    }
                }
            }
        }

        private void HandleSocialUpdated()
        {
            PopulateFriendsDrawer();
            ApplyDataBindings();
        }

        private VisualElement CreateModalOverlay(string title, System.Action onClose)
        {
            // Create full screen overlay background
            VisualElement overlay = new VisualElement();
            overlay.name = "modal-overlay";
            overlay.style.position = Position.Absolute;
            overlay.style.top = 0;
            overlay.style.left = 0;
            overlay.style.width = Length.Percent(100);
            overlay.style.height = Length.Percent(100);
            overlay.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.6f));
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;

            // Prevent clicks from going through to elements behind the overlay
            overlay.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());

            // Modal Card Container
            VisualElement card = new VisualElement();
            card.AddToClassList("tactile-panel");
            card.style.width = 560; // 1.4x wider
            card.style.maxHeight = Length.Percent(80);
            card.style.backgroundColor = new StyleColor(new Color(1f, 0.98f, 0.9f)); // Warm yellow-white
            card.style.borderTopWidth = 4;
            card.style.borderBottomWidth = 4;
            card.style.borderLeftWidth = 4;
            card.style.borderRightWidth = 4;
            card.style.borderTopLeftRadius = 32;
            card.style.borderTopRightRadius = 32;
            card.style.borderBottomLeftRadius = 32;
            card.style.borderBottomRightRadius = 32;
            card.style.paddingTop = 32;
            card.style.paddingRight = 32;
            card.style.paddingBottom = 32;
            card.style.paddingLeft = 32;
            
            // Header Row
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 24;

            Label titleLabel = new Label(title);
            titleLabel.AddToClassList("font-headline");
            titleLabel.style.fontSize = 32;
            titleLabel.style.color = new StyleColor(new Color(0.2f, 0.15f, 0.05f));
            header.Add(titleLabel);

            Button closeBtn = new Button();
            closeBtn.text = "✖";
            // Clean style for borderless, chubby Close X button
            closeBtn.style.backgroundColor = new StyleColor(Color.clear);
            closeBtn.style.borderTopWidth = 0;
            closeBtn.style.borderBottomWidth = 0;
            closeBtn.style.borderLeftWidth = 0;
            closeBtn.style.borderRightWidth = 0;
            closeBtn.style.width = 44;
            closeBtn.style.height = 44;
            closeBtn.style.fontSize = 28;
            closeBtn.style.color = new StyleColor(new Color(0.43f, 0.35f, 0.23f)); // Warm dark brown
            closeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeBtn.style.paddingTop = 0;
            closeBtn.style.paddingBottom = 0;
            closeBtn.style.paddingLeft = 0;
            closeBtn.style.paddingRight = 0;
            
            RegisterHoverAndClick(closeBtn, () => {
                _root.Remove(overlay);
                onClose?.Invoke();
            });
            header.Add(closeBtn);
            card.Add(header);

            overlay.Add(card);
            _root.Add(overlay);

            return card; // Return card to add content
        }

        private void ShowRechargePopup()
        {
            var card = CreateModalOverlay("Coin Recharge", null);
            card.style.width = 1400; // 2.5x wider (was 560)

            Label infoText = new Label("Recharge simulation. Click a tier to instantly add coins to your account.");
            infoText.AddToClassList("font-body");
            infoText.style.fontSize = 20;
            infoText.style.color = new StyleColor(new Color(0.35f, 0.3f, 0.2f));
            infoText.style.whiteSpace = WhiteSpace.Normal;
            infoText.style.marginBottom = 24;
            infoText.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(infoText);

            VisualElement grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.Center;
            grid.style.alignItems = Align.Center;
            grid.style.width = Length.Percent(100);
            card.Add(grid);

            var packages = new[]
            {
                new { name = "Pocket", coins = 100, price = "$0.99", texture = singleCoinTex },
                new { name = "Purse", coins = 500, price = "$3.99", texture = pouchTex },
                new { name = "Bag", coins = 1200, price = "$7.99", texture = smallPileTex },
                new { name = "Chest", coins = 3000, price = "$14.99", texture = decentPileTex },
                new { name = "Vault", coins = 7500, price = "$29.99", texture = fullChestTex },
                new { name = "Mountain", coins = 20000, price = "$69.99", texture = massiveChestTex }
            };

            foreach (var pkg in packages)
            {
                VisualElement pkgCard = new VisualElement();
                pkgCard.style.width = 350;   // 2.5x wider (was 140)
                pkgCard.style.height = 270;  // 1.5x higher (was 180)
                pkgCard.style.marginTop = 15;
                pkgCard.style.marginBottom = 15;
                pkgCard.style.marginLeft = 15;
                pkgCard.style.marginRight = 15;
                pkgCard.style.paddingTop = 20;
                pkgCard.style.paddingBottom = 20;
                pkgCard.style.paddingLeft = 20;
                pkgCard.style.paddingRight = 20;
                pkgCard.style.alignItems = Align.Center;
                pkgCard.style.justifyContent = Justify.SpaceBetween;
                pkgCard.style.borderTopWidth = 3;
                pkgCard.style.borderBottomWidth = 3;
                pkgCard.style.borderLeftWidth = 3;
                pkgCard.style.borderRightWidth = 3;
                pkgCard.style.borderTopLeftRadius = 24;
                pkgCard.style.borderTopRightRadius = 24;
                pkgCard.style.borderBottomLeftRadius = 24;
                pkgCard.style.borderBottomRightRadius = 24;
                pkgCard.style.borderTopColor = new StyleColor(new Color(0.85f, 0.75f, 0.65f));
                pkgCard.style.borderBottomColor = new StyleColor(new Color(0.85f, 0.75f, 0.65f));
                pkgCard.style.borderLeftColor = new StyleColor(new Color(0.85f, 0.75f, 0.65f));
                pkgCard.style.borderRightColor = new StyleColor(new Color(0.85f, 0.75f, 0.65f));
                pkgCard.style.backgroundColor = new StyleColor(new Color(1f, 0.95f, 0.88f));

                VisualElement pkgImage = new VisualElement();
                pkgImage.style.width = 150;  // 2.5x larger image (was 60)
                pkgImage.style.height = 150; // 2.5x larger image (was 60)
                if (pkg.texture != null)
                {
                    pkgImage.style.backgroundImage = new StyleBackground(pkg.texture);
                    pkgImage.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                    pkgImage.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
                    pkgImage.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
                    pkgImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                }
                pkgCard.Add(pkgImage);

                Label coinLabel = new Label($"+{pkg.coins:N0}");
                coinLabel.AddToClassList("font-headline");
                coinLabel.style.fontSize = 28; // Larger coin text (was 20)
                coinLabel.style.color = new StyleColor(new Color(0.2f, 0.15f, 0.05f));
                pkgCard.Add(coinLabel);

                Label nameLabel = new Label(pkg.name);
                nameLabel.AddToClassList("font-body");
                nameLabel.style.fontSize = 16; // Larger name text (was 12)
                nameLabel.style.color = new StyleColor(new Color(0.5f, 0.45f, 0.35f));
                pkgCard.Add(nameLabel);

                Button buyBtn = new Button();
                buyBtn.text = pkg.price;
                buyBtn.AddToClassList("bouncy-btn");
                buyBtn.AddToClassList("btn-primary-3d");
                buyBtn.style.width = 250;  // Wider buy button (was 110)
                buyBtn.style.height = 54;  // Higher buy button (was 36)
                buyBtn.style.fontSize = 20; // Larger button text (was 14)
                buyBtn.style.paddingTop = 0;
                buyBtn.style.paddingBottom = 0;
                
                RegisterHoverAndClick(buyBtn, async () => {
                    var csm = CloudSaveManager.Instance;
                    if (csm != null)
                    {
                        int targetCoins = (csm.IsLoaded ? csm.CachedCoins : _currentCoins) + pkg.coins;
                        _currentCoins = targetCoins;
                        await csm.SaveCoinsAndItemsAsync(targetCoins, csm.IsLoaded ? csm.CachedOwnedItems : new List<string>());
                        ApplyDataBindings();
                    }
                    else
                    {
                        _currentCoins += pkg.coins;
                        ApplyDataBindings();
                    }
                    
                    Debug.Log($"Simulated recharge: added {pkg.coins} coins. New total: {_currentCoins}");
                });
                
                pkgCard.Add(buyBtn);
                grid.Add(pkgCard);
            }
        }

        private void ShowAddFriendPopup()
        {
            var card = CreateModalOverlay("Add Friend", null);

            Label infoText = new Label("Enter a friend's exact username to send a request.");
            infoText.AddToClassList("font-body");
            infoText.style.fontSize = 18;
            infoText.style.color = new StyleColor(new Color(0.3f, 0.25f, 0.15f));
            infoText.style.whiteSpace = WhiteSpace.Normal;
            infoText.style.marginBottom = 24;
            card.Add(infoText);

            TextField inputField = new TextField();
            inputField.name = "popup-add-friend-input";
            inputField.style.height = 56;
            inputField.style.fontSize = 22;
            inputField.style.marginBottom = 20;
            
            // Round parent and inner input element
            inputField.style.borderTopLeftRadius = 24;
            inputField.style.borderTopRightRadius = 24;
            inputField.style.borderBottomLeftRadius = 24;
            inputField.style.borderBottomRightRadius = 24;
            
            // Add a style class for custom border styling
            inputField.AddToClassList("tactile-input");
            card.Add(inputField);

            var inputEl = inputField.Q(className: "unity-text-field__input");
            if (inputEl != null)
            {
                inputEl.style.borderTopLeftRadius = 24;
                inputEl.style.borderTopRightRadius = 24;
                inputEl.style.borderBottomLeftRadius = 24;
                inputEl.style.borderBottomRightRadius = 24;
            }

            Label statusLabel = new Label("");
            statusLabel.AddToClassList("font-body");
            statusLabel.style.fontSize = 18;
            statusLabel.style.color = new StyleColor(new Color(0.6f, 0.1f, 0.1f));
            statusLabel.style.display = DisplayStyle.None;
            statusLabel.style.marginBottom = 20;
            card.Add(statusLabel);

            Button submitBtn = new Button();
            submitBtn.text = "Send Request";
            submitBtn.AddToClassList("bouncy-btn");
            submitBtn.AddToClassList("btn-primary-3d");
            submitBtn.style.height = 64;
            submitBtn.style.fontSize = 22;
            submitBtn.style.paddingTop = 0;
            submitBtn.style.paddingBottom = 0;
            submitBtn.style.borderTopLeftRadius = 24;
            submitBtn.style.borderTopRightRadius = 24;
            submitBtn.style.borderBottomLeftRadius = 24;
            submitBtn.style.borderBottomRightRadius = 24;

            RegisterHoverAndClick(submitBtn, async () => {
                string username = inputField.value;
                if (string.IsNullOrEmpty(username))
                {
                    statusLabel.text = "Username cannot be empty!";
                    statusLabel.style.color = new StyleColor(new Color(0.7f, 0.1f, 0.1f));
                    statusLabel.style.display = DisplayStyle.Flex;
                    return;
                }

                statusLabel.text = "Sending request...";
                statusLabel.style.color = new StyleColor(new Color(0.3f, 0.2f, 0f));
                statusLabel.style.display = DisplayStyle.Flex;
                submitBtn.SetEnabled(false);

                bool success = await FriendLobbyService.Instance.SendFriendRequestAsync(username);
                submitBtn.SetEnabled(true);

                if (success)
                {
                    statusLabel.text = "Friend request sent successfully!";
                    statusLabel.style.color = new StyleColor(new Color(0.1f, 0.5f, 0.1f));
                    inputField.value = "";
                    ApplyDataBindings(); // Refresh badges/etc.
                }
                else
                {
                    statusLabel.text = "Failed to send request.";
                    statusLabel.style.color = new StyleColor(new Color(0.7f, 0.1f, 0.1f));
                }
            });

            card.Add(submitBtn);
        }

        private void ShowFriendRequestsPopup()
        {
            var card = CreateModalOverlay("Friend Requests", null);

            ScrollView scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.height = 240;
            scroll.style.marginBottom = 16;
            card.Add(scroll);

            System.Action populateList = null;
            populateList = () => {
                scroll.Clear();
                var requests = FriendLobbyService.Instance != null ? FriendLobbyService.Instance.IncomingRequests : new List<FriendLobbyService.FriendRequest>();
                if (requests.Count == 0)
                {
                    Label emptyLabel = new Label("No pending requests");
                    emptyLabel.AddToClassList("font-body");
                    emptyLabel.style.fontSize = 14;
                    emptyLabel.style.color = new StyleColor(new Color(0.5f, 0.5f, 0.5f));
                    emptyLabel.style.marginTop = 24;
                    emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    scroll.Add(emptyLabel);
                    ApplyDataBindings(); // update badge immediately when count reaches 0
                    return;
                }

                foreach (var req in requests)
                {
                    VisualElement row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.justifyContent = Justify.SpaceBetween;
                    row.style.paddingTop = 8;
                    row.style.paddingBottom = 8;
                    row.style.borderBottomWidth = 1;
                    row.style.borderBottomColor = new StyleColor(new Color(0.9f, 0.85f, 0.7f));

                    VisualElement info = new VisualElement();
                    info.style.flexDirection = FlexDirection.Row;
                    info.style.alignItems = Align.Center;

                    VisualElement avatar = new VisualElement();
                    avatar.AddToClassList("friend-avatar-circle");
                    avatar.AddToClassList("offline-avatar");
                    avatar.style.width = 36;
                    avatar.style.height = 36;
                    avatar.style.borderTopLeftRadius = 18;
                    avatar.style.borderTopRightRadius = 18;
                    avatar.style.borderBottomLeftRadius = 18;
                    avatar.style.borderBottomRightRadius = 18;
                    avatar.style.marginRight = 10;
                    
                    int avatarIdx = Mathf.Abs(req.Username.GetHashCode() % 3) + 1;
                    avatar.AddToClassList($"avatar-preset-{avatarIdx}");

                    info.Add(avatar);

                    Label name = new Label(req.Username);
                    name.AddToClassList("font-headline");
                    name.style.fontSize = 16;
                    name.style.color = new StyleColor(new Color(0.2f, 0.15f, 0.05f));
                    info.Add(name);
                    row.Add(info);

                    VisualElement actions = new VisualElement();
                    actions.style.flexDirection = FlexDirection.Row;

                    Button acceptBtn = new Button();
                    acceptBtn.text = "✓";
                    acceptBtn.AddToClassList("bouncy-btn");
                    acceptBtn.AddToClassList("btn-primary-3d");
                    acceptBtn.style.width = 36;
                    acceptBtn.style.height = 36;
                    acceptBtn.style.fontSize = 14;
                    acceptBtn.style.paddingTop = 0;
                    acceptBtn.style.paddingBottom = 0;
                    acceptBtn.style.paddingLeft = 0;
                    acceptBtn.style.paddingRight = 0;
                    RegisterHoverAndClick(acceptBtn, async () => {
                        bool ok = await FriendLobbyService.Instance.AcceptFriendRequestAsync(req.Id);
                        if (ok)
                        {
                            populateList();
                            ApplyDataBindings();
                        }
                    });
                    actions.Add(acceptBtn);

                    Button declineBtn = new Button();
                    declineBtn.text = "✗";
                    declineBtn.AddToClassList("bouncy-btn");
                    declineBtn.AddToClassList("btn-surface-3d");
                    declineBtn.style.width = 36;
                    declineBtn.style.height = 36;
                    declineBtn.style.fontSize = 14;
                    declineBtn.style.marginLeft = 6;
                    declineBtn.style.paddingTop = 0;
                    declineBtn.style.paddingBottom = 0;
                    declineBtn.style.paddingLeft = 0;
                    declineBtn.style.paddingRight = 0;
                    RegisterHoverAndClick(declineBtn, async () => {
                        bool ok = await FriendLobbyService.Instance.DeclineFriendRequestAsync(req.Id);
                        if (ok)
                        {
                            populateList();
                            ApplyDataBindings();
                        }
                    });
                    actions.Add(declineBtn);

                    row.Add(actions);
                    scroll.Add(row);
                }
            };

            populateList();
        }

        private void HandleLobbyJoined(string lobbyCode)
        {
            Debug.Log($"Lobby joined/created successfully: {lobbyCode}");
        }

        private void HandleLobbyLeft()
        {
            Debug.Log("Lobby left.");
        }

        private void HandleLobbyInviteReceived(LobbyInvite invite)
        {
            RefreshLobbyInviteUI();
        }

        private void HandleLobbyInviteCleared()
        {
            RefreshLobbyInviteUI();
        }

        private void RefreshLobbyInviteUI()
        {
            if (_root == null) return;

            var activeInvite = FriendLobbyService.Instance != null ? FriendLobbyService.Instance.CurrentInvite : null;
            bool isInLobby = _root != null && _root.Q("matchmaking-layout") != null;

            // 1. Refresh Sidebar Invitation Box
            var sidebarInvite = _root.Q<VisualElement>("sidebar-lobby-invite");
            if (sidebarInvite != null)
            {
                if (activeInvite != null && !isInLobby)
                {
                    var inviteText = sidebarInvite.Q<Label>("sidebar-invite-text");
                    if (inviteText != null)
                    {
                        inviteText.text = $"Invite from {activeInvite.InviterName}";
                    }

                    var buttonsRow = sidebarInvite.Q<VisualElement>("sidebar-invite-buttons-row");
                    if (buttonsRow != null)
                    {
                        buttonsRow.Clear();

                        var acceptBtn = new Button();
                        acceptBtn.name = "btn-sidebar-invite-accept";
                        acceptBtn.text = "Accept";
                        acceptBtn.AddToClassList("bouncy-btn");
                        acceptBtn.AddToClassList("btn-primary-3d");
                        acceptBtn.style.flexGrow = 1;
                         bool isJoining = false;
                        RegisterHoverAndClick(acceptBtn, async () => {
                            if (isJoining) return;
                            isJoining = true;
                            acceptBtn.SetEnabled(false);
                            try
                            {
                                if (FriendLobbyService.Instance != null)
                                {
                                    bool ok = await FriendLobbyService.Instance.JoinLobbyByCodeAsync(activeInvite.LobbyCode);
                                    if (ok)
                                    {
                                        FriendLobbyService.Instance.ClearInvite();
                                        ShowMatchmaking();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[StitchUIController] Error accepting sidebar invite: {ex.Message}");
                            }
                            finally
                            {
                                isJoining = false;
                                acceptBtn.SetEnabled(true);
                            }
                        });
                        buttonsRow.Add(acceptBtn);
 
                        var declineBtn = new Button();
                        declineBtn.name = "btn-sidebar-invite-decline";
                        declineBtn.text = "Decline";
                        declineBtn.AddToClassList("bouncy-btn");
                        declineBtn.AddToClassList("btn-surface-3d");
                        declineBtn.style.flexGrow = 1;
                        declineBtn.style.height = 32;
                        declineBtn.style.fontSize = 13;
                        declineBtn.style.paddingLeft = 0;
                        declineBtn.style.paddingRight = 0;
                        declineBtn.style.borderTopLeftRadius = 10;
                        declineBtn.style.borderTopRightRadius = 10;
                        declineBtn.style.borderBottomLeftRadius = 10;
                        declineBtn.style.borderBottomRightRadius = 10;
                        declineBtn.style.marginLeft = 4;
                        declineBtn.style.borderTopColor = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                        declineBtn.style.borderBottomColor = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                        declineBtn.style.borderLeftColor = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                        declineBtn.style.borderRightColor = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                        declineBtn.style.color = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                        RegisterHoverAndClick(declineBtn, () => {
                            try
                            {
                                if (FriendLobbyService.Instance != null)
                                {
                                    FriendLobbyService.Instance.ClearInvite();
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[StitchUIController] Error declining sidebar invite: {ex.Message}");
                            }
                        });
                        buttonsRow.Add(declineBtn);
                    }

                    sidebarInvite.style.display = DisplayStyle.Flex;
                }
                else
                {
                    sidebarInvite.style.display = DisplayStyle.None;
                }
            }

            // 2. Refresh Bottom-Right Floating Popup
            var popupInvite = _root.Q<VisualElement>("bottom-right-invite-popup");
            if (activeInvite != null && !isInLobby)
            {
                if (popupInvite == null)
                {
                    popupInvite = new VisualElement();
                    popupInvite.name = "bottom-right-invite-popup";
                    popupInvite.AddToClassList("tactile-panel");
                    popupInvite.style.position = Position.Absolute;
                    popupInvite.style.bottom = 40;
                    popupInvite.style.right = 40;
                    popupInvite.style.width = 300;
                    popupInvite.style.paddingTop = 16;
                    popupInvite.style.paddingBottom = 16;
                    popupInvite.style.paddingLeft = 20;
                    popupInvite.style.paddingRight = 20;
                    popupInvite.style.backgroundColor = new StyleColor(new Color(1f, 0.98f, 0.9f));
                    popupInvite.style.borderTopLeftRadius = 24;
                    popupInvite.style.borderTopRightRadius = 24;
                    popupInvite.style.borderBottomLeftRadius = 24;
                    popupInvite.style.borderBottomRightRadius = 24;
                    popupInvite.style.borderTopWidth = 3;
                    popupInvite.style.borderBottomWidth = 3;
                    popupInvite.style.borderLeftWidth = 3;
                    popupInvite.style.borderRightWidth = 3;
                    popupInvite.style.borderTopColor = new StyleColor(new Color(1f, 0.84f, 0f)); // Gold
                    popupInvite.style.borderBottomColor = new StyleColor(new Color(1f, 0.84f, 0f));
                    popupInvite.style.borderLeftColor = new StyleColor(new Color(1f, 0.84f, 0f));
                    popupInvite.style.borderRightColor = new StyleColor(new Color(1f, 0.84f, 0f));
                    
                    popupInvite.style.transitionProperty = new List<StylePropertyName> { "opacity", "scale" };
                    popupInvite.style.transitionDuration = new List<TimeValue> { 0.15f };
                    
                    _root.Add(popupInvite);
                }

                popupInvite.Clear();

                var titleLabel = new Label("LOBBY INVITATION");
                titleLabel.AddToClassList("font-headline");
                titleLabel.style.fontSize = 16;
                titleLabel.style.color = new StyleColor(new Color(0.31f, 0.21f, 0f));
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.marginBottom = 6;
                titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                popupInvite.Add(titleLabel);

                var descLabel = new Label($"{activeInvite.InviterName} has invited you to join their lobby!");
                descLabel.AddToClassList("font-body");
                descLabel.style.fontSize = 14;
                descLabel.style.color = new StyleColor(new Color(0.2f, 0.15f, 0.05f));
                descLabel.style.marginBottom = 14;
                descLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                popupInvite.Add(descLabel);

                var pButtonsRow = new VisualElement();
                pButtonsRow.style.flexDirection = FlexDirection.Row;
                pButtonsRow.style.justifyContent = Justify.SpaceBetween;
                pButtonsRow.style.alignItems = Align.Center;

                var acceptBtn = new Button();
                acceptBtn.text = "Accept";
                acceptBtn.AddToClassList("bouncy-btn");
                acceptBtn.AddToClassList("btn-primary-3d");
                acceptBtn.style.flexGrow = 1;
                acceptBtn.style.height = 36;
                acceptBtn.style.fontSize = 14;
                acceptBtn.style.borderTopLeftRadius = 12;
                acceptBtn.style.borderTopRightRadius = 12;
                acceptBtn.style.borderBottomLeftRadius = 12;
                acceptBtn.style.borderBottomRightRadius = 12;
                acceptBtn.style.marginRight = 6;
                bool isJoiningPopup = false;
                RegisterHoverAndClick(acceptBtn, async () => {
                    if (isJoiningPopup) return;
                    isJoiningPopup = true;
                    acceptBtn.SetEnabled(false);
                    try
                    {
                        if (FriendLobbyService.Instance != null)
                        {
                            bool ok = await FriendLobbyService.Instance.JoinLobbyByCodeAsync(activeInvite.LobbyCode);
                            if (ok)
                            {
                                FriendLobbyService.Instance.ClearInvite();
                                ShowMatchmaking();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[StitchUIController] Error accepting bottom invite: {ex.Message}");
                    }
                    finally
                    {
                        isJoiningPopup = false;
                        acceptBtn.SetEnabled(true);
                    }
                });
                pButtonsRow.Add(acceptBtn);
 
                var declineBtn = new Button();
                declineBtn.text = "Decline";
                declineBtn.AddToClassList("bouncy-btn");
                declineBtn.AddToClassList("btn-surface-3d");
                declineBtn.style.flexGrow = 1;
                declineBtn.style.height = 36;
                declineBtn.style.fontSize = 14;
                declineBtn.style.borderTopLeftRadius = 12;
                declineBtn.style.borderTopRightRadius = 12;
                declineBtn.style.borderBottomLeftRadius = 12;
                declineBtn.style.borderBottomRightRadius = 12;
                declineBtn.style.marginLeft = 6;
                declineBtn.style.borderTopColor = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                declineBtn.style.borderBottomColor = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                declineBtn.style.borderLeftColor = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                declineBtn.style.borderRightColor = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                declineBtn.style.color = new StyleColor(new Color(0.73f, 0.1f, 0.1f));
                RegisterHoverAndClick(declineBtn, () => {
                    try
                    {
                        if (FriendLobbyService.Instance != null)
                        {
                            FriendLobbyService.Instance.ClearInvite();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[StitchUIController] Error declining bottom invite: {ex.Message}");
                    }
                });
                pButtonsRow.Add(declineBtn);

                popupInvite.Add(pButtonsRow);
            }
            else
            {
                if (popupInvite != null)
                {
                    _root.Remove(popupInvite);
                }
            }
        }
    }
}
