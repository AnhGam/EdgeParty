using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using EdgeParty.Auth;
using EdgeParty.Social;

namespace EdgeParty.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class StitchUIController : MonoBehaviour
    {
        private UIDocument _uiDocument;
        private VisualElement _root;

        [Header("Menu UXML Templates")]
        [SerializeField] private VisualTreeAsset loginVisualTree;
        [SerializeField] private VisualTreeAsset registerVisualTree;
        [SerializeField] private VisualTreeAsset forgotPasswordVisualTree;
        [SerializeField] private VisualTreeAsset homeVisualTree;
        [SerializeField] private VisualTreeAsset shopVisualTree;
        [SerializeField] private VisualTreeAsset matchmakingVisualTree;

        [Header("SFX Sound Names")]
        private const string SoundClick = "Click";
        private const string SoundHover = "Hover";

        // State variables for binding demonstration
        private int _currentCoins = 1240;
        private int _onlineFriendsCount = 5;
        private string _username = "PlayerOne";
        private string _currentFriendsDrawerTab = "friends";

        // UGS Auth integration states
        private string _pendingEmail = "";
        private Button _loadingButton;
        private string _originalButtonText;

        private async void OnEnable()
        {
            _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) return;

            // Register AuthService events
            AuthService.Instance.OnSignInSuccess += HandleSignInSuccess;
            AuthService.Instance.OnSignInFailed += HandleSignInFailed;
            AuthService.Instance.OnSignUpSuccess += HandleSignUpSuccess;
            AuthService.Instance.OnSignUpFailed += HandleSignUpFailed;

            // Register Social Events
            if (FriendLobbyService.Instance != null)
            {
                FriendLobbyService.Instance.OnFriendsUpdated += HandleSocialUpdated;
                FriendLobbyService.Instance.OnFriendRequestsUpdated += HandleSocialUpdated;
                FriendLobbyService.Instance.OnLobbyMembersUpdated += UpdateLobbyPodiums;
                FriendLobbyService.Instance.OnLobbyJoined += HandleLobbyJoined;
                FriendLobbyService.Instance.OnLobbyLeft += HandleLobbyLeft;
            }

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

        // ─── Screen Transition Methods ───────────────────────────────────

        public void ShowLogin()
        {
            if (loginVisualTree == null) return;
            SetNewScreen(loginVisualTree.CloneTree());
            BindLoginEvents();
        }

        public void ShowRegister()
        {
            if (registerVisualTree == null) return;
            SetNewScreen(registerVisualTree.CloneTree());
            BindRegisterEvents();
        }

        public void ShowForgotPassword()
        {
            if (forgotPasswordVisualTree == null) return;
            SetNewScreen(forgotPasswordVisualTree.CloneTree());
            BindForgotPasswordEvents();
        }

        public void ShowHome()
        {
            if (homeVisualTree == null) return;
            SetNewScreen(homeVisualTree.CloneTree());
            BindSharedSidebarEvents("tab-home");
            BindSharedHeaderEvents("tab-home");
            BindHomeEvents();
            ApplyDataBindings();

            // Auto initialize Social once signed in
            _ = FriendLobbyService.Instance.InitializeSocialAsync();
        }

        public void ShowShop()
        {
            if (shopVisualTree == null) return;
            SetNewScreen(shopVisualTree.CloneTree());
            BindSharedSidebarEvents("tab-shop");
            BindSharedHeaderEvents("tab-shop");
            BindShopEvents();
            ApplyDataBindings();
        }

        public void ShowMatchmaking()
        {
            if (matchmakingVisualTree == null) return;
            SetNewScreen(matchmakingVisualTree.CloneTree());
            // Matchmaking has stretched header, no sidebar, and active header tab
            BindSharedHeaderEvents("tab-matchmaking");
            BindMatchmakingEvents();
            ApplyDataBindings();
        }

        // Helper to replace root visual content
        private void SetNewScreen(VisualElement newScreenContent)
        {
            _root = _uiDocument.rootVisualElement;
            _root.Clear();
            newScreenContent.style.flexGrow = 1;
            newScreenContent.style.width = Length.Percent(100);
            newScreenContent.style.height = Length.Percent(100);
            _root.Add(newScreenContent);
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

            // Ensure correct default visibility on load
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
                        Debug.Log("[StitchUIController] NetworkManager.Singleton is null. Attempting to auto-load Assets/NetworkManager.prefab in Editor...");
                        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/NetworkManager.prefab");
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
        }

        private void BindShopEvents()
        {
            var btnBuyCap = _root.Q<Button>("btn-buy-banana-cap");
            var btnBuy1 = _root.Q<Button>("btn-buy-item-1");
            var btnBuy2 = _root.Q<Button>("btn-buy-item-2");

            if (btnBuyCap != null)
            {
                RegisterHoverAndClick(btnBuyCap, () => BuyItem("banana-cap", "Banana Split Cap", 850));
            }
            if (btnBuy1 != null)
            {
                RegisterHoverAndClick(btnBuy1, () => BuyItem("retro-shaders", "Retro Shaders", 320));
            }
            if (btnBuy2 != null)
            {
                RegisterHoverAndClick(btnBuy2, () => BuyItem("fuzzy-bucket", "Fuzzy Bucket", 450));
            }

            // Shop Filters
            var btnAll = _root.Q<Button>("btn-filter-all");
            var btnHats = _root.Q<Button>("btn-filter-hats");
            var btnGlasses = _root.Q<Button>("btn-filter-glasses");

            if (btnAll != null) RegisterHoverAndClick(btnAll, null);
            if (btnHats != null) RegisterHoverAndClick(btnHats, null);
            if (btnGlasses != null) RegisterHoverAndClick(btnGlasses, null);
        }

        private void BindMatchmakingEvents()
        {
            var btnCancel = _root.Q<Button>("btn-matchmaking-cancel");
            var btnPlay = _root.Q<Button>("btn-matchmaking-play");

            if (btnCancel != null)
            {
                RegisterHoverAndClick(btnCancel, ShowHome);
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

            // Round matchmaking drawer add friend text field
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
            if (btnLocker != null) SetupSidebarTabState(btnLocker, activeTabName == "tab-locker", null);
            if (btnShop != null) SetupSidebarTabState(btnShop, activeTabName == "tab-shop", ShowShop);
            if (btnSettings != null) SetupSidebarTabState(btnSettings, activeTabName == "tab-settings", null);

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
                RegisterHoverAndClick(btnLogout, () => {
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

            RegisterHoverAndClick(tabButton, onClickAction);
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
                if (activeHeaderTab == "tab-home") tabHomeHeader.AddToClassList("active-header-tab");
                else tabHomeHeader.RemoveFromClassList("active-header-tab");

                RegisterHoverAndClick(tabHomeHeader, ShowHome);
            }

            if (tabMatchmaking != null)
            {
                if (activeHeaderTab == "tab-matchmaking") tabMatchmaking.AddToClassList("active-header-tab");
                else tabMatchmaking.RemoveFromClassList("active-header-tab");

                RegisterHoverAndClick(tabMatchmaking, ShowMatchmaking);
            }

            if (tabEvents != null)
            {
                if (activeHeaderTab == "tab-events") tabEvents.AddToClassList("active-header-tab");
                else tabEvents.RemoveFromClassList("active-header-tab");

                RegisterHoverAndClick(tabEvents, null);
            }

            if (btnToll != null)
            {
                RegisterHoverAndClick(btnToll, () => Debug.Log("Toll Store open requested"));
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
            btn.RegisterCallback<PointerEnterEvent>(_ =>
                EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundHover));

            // Click Trigger
            btn.clicked += () => {
                EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                onClickAction?.Invoke();
            };
        }

        // ─── State Bindings & Data Layer ───────────────────────────────

        private void ApplyDataBindings()
        {
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
            if (profileTitle != null) profileTitle.text = _username;

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
            if (_currentCoins >= price)
            {
                _currentCoins -= price;
                ApplyDataBindings();
                Debug.Log($"Successfully bought {itemName}!");
            }
            else
            {
                Debug.LogWarning($"Insufficient coins to buy {itemName}!");
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
                        inviteBtn.text = "+";
                        inviteBtn.AddToClassList("bouncy-btn");
                        inviteBtn.AddToClassList("btn-primary-3d");
                        inviteBtn.AddToClassList("friend-add-btn");
                        inviteBtn.style.width = 32;
                        inviteBtn.style.height = 32;
                        
                        inviteBtn.clicked += () =>
                        {
                            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                            // Invite friend
                            FriendLobbyService.Instance.SimulateFriendAcceptingInvite(friend.Username);
                        };
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
                        acceptBtn.clicked += async () =>
                        {
                            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                            await FriendLobbyService.Instance.AcceptFriendRequestAsync(req.Id);
                        };
                        actions.Add(acceptBtn);

                        Button declineBtn = new Button();
                        declineBtn.text = "✗";
                        declineBtn.AddToClassList("bouncy-btn");
                        declineBtn.AddToClassList("btn-surface-3d");
                        declineBtn.style.width = 32;
                        declineBtn.style.height = 32;
                        declineBtn.style.marginLeft = 4;
                        declineBtn.clicked += async () =>
                        {
                            EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                            await FriendLobbyService.Instance.DeclineFriendRequestAsync(req.Id);
                        };
                        actions.Add(declineBtn);

                        row.Add(actions);
                        scroll.Add(row);
                    }
                }
            }
        }

        private void UpdateLobbyPodiums(List<string> members)
        {
            for (int i = 2; i <= 4; i++)
            {
                var slotBtn = _root.Q<Button>($"btn-invite-slot-{i}");
                if (slotBtn == null) continue;

                int memberIndex = i - 1; // You is index 0
                if (memberIndex < members.Count)
                {
                    string memberName = members[memberIndex];
                    
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
            
            closeBtn.clicked += () => {
                EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                _root.Remove(overlay);
                onClose?.Invoke();
            };
            header.Add(closeBtn);
            card.Add(header);

            overlay.Add(card);
            _root.Add(overlay);

            return card; // Return card to add content
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

            submitBtn.clicked += async () => {
                EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
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
            };

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
                    acceptBtn.clicked += async () => {
                        EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                        bool ok = await FriendLobbyService.Instance.AcceptFriendRequestAsync(req.Id);
                        if (ok)
                        {
                            populateList();
                            ApplyDataBindings();
                        }
                    };
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
                    declineBtn.clicked += async () => {
                        EdgeParty.Core.AudioManager.Instance?.PlaySFX(SoundClick);
                        bool ok = await FriendLobbyService.Instance.DeclineFriendRequestAsync(req.Id);
                        if (ok)
                        {
                            populateList();
                            ApplyDataBindings();
                        }
                    };
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
    }
}
