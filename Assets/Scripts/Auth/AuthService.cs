using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

namespace EdgeParty.Auth
{
    public class AuthService : MonoBehaviour
    {
        public static AuthService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<AuthService>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("AuthService");
                        _instance = go.AddComponent<AuthService>();
                    }
                }
                return _instance;
            }
        }
        private static AuthService _instance;

        public event Action OnSignInSuccess;
        public event Action<string> OnSignInFailed;
        public event Action OnSignUpSuccess;
        public event Action<string> OnSignUpFailed;

        // Google OAuth Client ID — public by design, same value as configured on UGS Dashboard.
        // No secret needed: uses PKCE flow (RFC 7636) for desktop apps.
        private const string GoogleClientId = "513426504151-3mgit1uk0b9omj1bm9umvaug1mhnqv4o.apps.googleusercontent.com";
        private const int GoogleRedirectPort = 5000;

        public bool IsInitialized => UnityServices.State == ServicesInitializationState.Initialized;
        public bool IsSignedIn => IsInitialized && AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : null;
        
        private const string LastUsernamePrefKey = "EdgeParty_LastUsername";
        public string CachedUsername
        {
            get => PlayerPrefs.GetString(LastUsernamePrefKey, "Player");
            private set => PlayerPrefs.SetString(LastUsernamePrefKey, value);
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        private async void Start()
        {
            await InitializeServicesAsync();
        }

        public async Task InitializeServicesAsync()
        {
            try
            {
                RunRegistryDiagnostics("Before Init");
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    var options = new InitializationOptions();
                    
                    // Separates Editor vs Standalone Build profiles to support testing on a single machine.
                    // Also supports custom profile command-line argument (e.g. -profile Guest1).
                    string profile = "default";
#if UNITY_EDITOR
                    if (Application.dataPath.Contains("_clone"))
                    {
                        profile = "editor_clone";
                        int idx = Application.dataPath.IndexOf("_clone");
                        if (idx != -1)
                        {
                            int slashIdx = Application.dataPath.IndexOf("/Assets", idx);
                            if (slashIdx == -1) slashIdx = Application.dataPath.IndexOf("\\Assets", idx);
                            if (slashIdx != -1)
                            {
                                profile = "editor_" + Application.dataPath.Substring(idx + 1, slashIdx - idx - 1);
                            }
                        }
                    }
                    else
                    {
                        profile = "editor";
                    }
#else
                    string[] args = System.Environment.GetCommandLineArgs();
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] == "-profile" && i + 1 < args.Length)
                        {
                            profile = args[i + 1];
                            break;
                        }
                    }
#endif
                    options.SetProfile(profile);
                    options.SetVivoxCredentials(
                        EdgeParty.Infrastructure.VoiceChat.VivoxConfig.Server,
                        EdgeParty.Infrastructure.VoiceChat.VivoxConfig.Domain,
                        EdgeParty.Infrastructure.VoiceChat.VivoxConfig.TokenIssuer,
                        EdgeParty.Infrastructure.VoiceChat.VivoxConfig.TokenKey
                    );

                    await UnityServices.InitializeAsync(options);
                    Debug.Log($"Unity Services Initialized successfully with profile: {profile}");
                    RunRegistryDiagnostics("After Init");
                    
                    // ── DIAGNOSTIC: Xác nhận VivoxService.Instance sau UGS init ──
                    // Nếu log dưới in "null" → VivoxPackageInitializer.Initialize() bị throw
                    // hoặc SDK không được đăng ký đúng cách.
                    Debug.Log($"[AuthService] VivoxService.Instance after UGS init: {(Unity.Services.Vivox.VivoxService.Instance != null ? "NOT NULL ✓" : "NULL ✗")}");
                    
                    AuthenticationService.Instance.SignedIn += () =>
                    {
                        Debug.Log($"UGS Signed In: {AuthenticationService.Instance.PlayerId}");
                    };

                    AuthenticationService.Instance.SignedOut += () =>
                    {
                        Debug.Log("UGS Signed Out.");
                    };

                    AuthenticationService.Instance.SignInFailed += (err) =>
                    {
                        Debug.LogWarning($"UGS Sign In Failed: {err.Message}");
                    };
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize Unity Services: {e.Message}");
            }
        }

        private Task _initTask;

        public Task EnsureInitializedAsync()
        {
            if (IsInitialized) return Task.CompletedTask;
            if (_initTask == null)
            {
                _initTask = InitializeServicesAsync();
            }
            return _initTask;
        }

        public async Task SignUpAsync(string username, string email, string password)
        {
            try
            {
                await EnsureInitializedAsync();
                
                // UGS SignUp requires a username (3-20 characters) and password (8-30 characters)
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                
                try
                {
                    await AuthenticationService.Instance.UpdatePlayerNameAsync(username);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to update player name on signup: {e.Message}");
                }

                CachedUsername = !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName) 
                    ? AuthenticationService.Instance.PlayerName 
                    : username;
                Debug.Log($"Signed up successfully. Player ID: {AuthenticationService.Instance.PlayerId}");
                
                // Sign out immediately so they can log in manually on the Login page
                AuthenticationService.Instance.SignOut(true);
                
                OnSignUpSuccess?.Invoke();
            }
            catch (AuthenticationException ex)
            {
                Debug.LogWarning($"Sign up failed: {ex.Message}");
                OnSignUpFailed?.Invoke(ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Sign up failed with unexpected error: {ex.Message}");
                OnSignUpFailed?.Invoke(ex.Message);
            }
        }

        private async Task EnsurePlayerNameIsSetAsync(string fallbackName)
        {
            try
            {
                if (string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName))
                {
                    Debug.Log($"[AuthService] PlayerName is empty in UGS. Updating to: {fallbackName}");
                    await AuthenticationService.Instance.UpdatePlayerNameAsync(fallbackName);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AuthService] Failed to ensure player name: {ex.Message}");
            }
        }

        public async Task SignInAsync(string usernameOrEmail, string password)
        {
            try
            {
                await EnsureInitializedAsync();
                
                // Note: UGS Auth username/password uses the exact username. 
                // In production, we'd map emails to usernames in a DB, but here we sign in directly.
                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(usernameOrEmail, password);
                
                string displayName = usernameOrEmail;
                if (displayName.Contains("@"))
                {
                    displayName = displayName.Split('@')[0];
                }
                await EnsurePlayerNameIsSetAsync(displayName);

                CachedUsername = !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName)
                    ? AuthenticationService.Instance.PlayerName
                    : usernameOrEmail;

                string fullPlayerName = AuthenticationService.Instance.PlayerName;
                Debug.Log($"Signed in successfully. Player ID: {AuthenticationService.Instance.PlayerId}");
                Debug.Log($"[AuthService] *** Full UGS Player Name (dùng để kết bạn): '{fullPlayerName}' ***");
                await CloudSaveManager.Instance.LoadAllAsync();
                OnSignInSuccess?.Invoke();
            }
            catch (AuthenticationException ex)
            {
                Debug.LogWarning($"Sign in failed: {ex.Message}");
                OnSignInFailed?.Invoke(ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Sign in failed with unexpected error: {ex.Message}");
                OnSignInFailed?.Invoke(ex.Message);
            }
        }

        public void SignInWithGoogle()
        {
            // Uses PKCE flow — opens system browser, exchanges code without client_secret
            GoogleAuthHelper.StartLogin(
                GoogleClientId,
                GoogleRedirectPort,
                async (idToken) =>
                {
                    try
                    {
                        await EnsureInitializedAsync();
                        await AuthenticationService.Instance.SignInWithGoogleAsync(idToken);
                        
                        await EnsurePlayerNameIsSetAsync("GoogleGamer");
                        CachedUsername = !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName)
                            ? AuthenticationService.Instance.PlayerName
                            : "GoogleGamer";

                        Debug.Log($"Google sign in successful. Player ID: {AuthenticationService.Instance.PlayerId}");
                        Debug.Log($"[AuthService] *** Full UGS Player Name (dùng để kết bạn): '{AuthenticationService.Instance.PlayerName}' ***");

                        await CloudSaveManager.Instance.LoadAllAsync();
                        OnSignInSuccess?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        OnSignInFailed?.Invoke(ex.Message);
                    }
                },
                (error) =>
                {
                    OnSignInFailed?.Invoke(error);
                }
            );
        }

        public void SignOut()
        {
            if (IsSignedIn)
            {
                AuthenticationService.Instance.SignOut(true);
            }
        }

        public async Task<bool> TryAutoSignInAsync()
        {
            try
            {
                await EnsureInitializedAsync();

                if (AuthenticationService.Instance.IsSignedIn)
                {
                    await EnsurePlayerNameIsSetAsync("Player");
                    CachedUsername = !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName)
                        ? AuthenticationService.Instance.PlayerName
                        : "Player";

                    Debug.Log($"Already signed in. Player ID: {AuthenticationService.Instance.PlayerId}, Name: {CachedUsername}");
                    await CloudSaveManager.Instance.LoadAllAsync();
                    OnSignInSuccess?.Invoke();
                    return true;
                }

                if (AuthenticationService.Instance.SessionTokenExists)
                {
                    // UGS will automatically reuse session token if we call SignInAnonymouslyAsync()
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    
                    await EnsurePlayerNameIsSetAsync("Player");
                    CachedUsername = !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerName)
                        ? AuthenticationService.Instance.PlayerName
                        : "Player";

                    Debug.Log($"Auto-login successful. Player ID: {AuthenticationService.Instance.PlayerId}, Name: {CachedUsername}");
                    await CloudSaveManager.Instance.LoadAllAsync();
                    OnSignInSuccess?.Invoke();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Auto-login failed or no session found: {ex.Message}");
            }
            return false;
        }

        private string _lastSentOtp = "";
        private string _lastOtpEmail = "";

        public Task<bool> RequestPasswordResetAsync(string email)
        {
            // Since UGS Username/Password has no custom forgot-password recovery endpoint,
            // we simulate a beautiful OTP workflow.
            _lastOtpEmail = email;
            
            System.Random rand = new System.Random();
            _lastSentOtp = rand.Next(1000, 9999).ToString();
            
            Debug.Log($"[MOCK OTP] Sent code {_lastSentOtp} to {email}");
            
            return Task.FromResult(true);
        }

        public Task<bool> VerifyOtpAndResetPasswordAsync(string email, string otp, string newPassword)
        {
            if (email == _lastOtpEmail && (otp == _lastSentOtp || otp == "1234")) // "1234" is fallback override for testing
            {
                Debug.Log($"[MOCK OTP] Password successfully reset for {email} to {newPassword}!");
                return Task.FromResult(true);
            }
            else
            {
                Debug.LogWarning($"[MOCK OTP] OTP Verification failed. Entered: {otp}, Expected: {_lastSentOtp}");
                return Task.FromResult(false);
            }
        }

        private void RunRegistryDiagnostics(string stage)
        {
            try
            {
                Debug.Log($"[RegistryDiagnostics] Stage: {stage}");
                
                // Print active platform / preprocessor settings
                string activePlatform = "";
#if UNITY_EDITOR
                activePlatform += "UNITY_EDITOR ";
#endif
#if UNITY_STANDALONE_WIN
                activePlatform += "UNITY_STANDALONE_WIN ";
#endif
#if UNITY_STANDALONE_LINUX
                activePlatform += "UNITY_STANDALONE_LINUX ";
#endif
#if UNITY_SERVER
                activePlatform += "UNITY_SERVER ";
#endif
#if UNITY_STANDALONE
                activePlatform += "UNITY_STANDALONE ";
#endif
#if !UNITY_STANDALONE_LINUX
                activePlatform += "!UNITY_STANDALONE_LINUX ";
#else
                activePlatform += "UNITY_STANDALONE_LINUX_DEFINED ";
#endif
                Debug.Log($"[RegistryDiagnostics] Compilation Preprocessors: {activePlatform}");
                Debug.Log($"[RegistryDiagnostics] Application Platform: {Application.platform}");

                var coreRegistryType = typeof(Unity.Services.Core.UnityServices).Assembly.GetType("Unity.Services.Core.Internal.CorePackageRegistry");
                if (coreRegistryType == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] Could not find CorePackageRegistry type.");
                    return;
                }
                var instanceProp = coreRegistryType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instanceProp == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] Could not find Instance property on CorePackageRegistry.");
                    return;
                }
                var registryInstance = instanceProp.GetValue(null);
                if (registryInstance == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] CorePackageRegistry.Instance is null.");
                    return;
                }
                var registryProp = coreRegistryType.GetProperty("Registry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (registryProp == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] Could not find Registry property on CorePackageRegistry.");
                    return;
                }
                var registryObj = registryProp.GetValue(registryInstance);
                if (registryObj == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] CorePackageRegistry.Instance.Registry is null.");
                    return;
                }
                Debug.Log($"[RegistryDiagnostics] Registry type: {registryObj.GetType().FullName}");
                var treeProp = registryObj.GetType().GetProperty("Tree", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (treeProp == null)
                {
                    var registryField = registryObj.GetType().GetProperty("Registry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (registryField != null)
                    {
                        var innerRegistry = registryField.GetValue(registryObj);
                        if (innerRegistry != null)
                        {
                            treeProp = innerRegistry.GetType().GetProperty("Tree", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                            registryObj = innerRegistry;
                        }
                    }
                }
                if (treeProp == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] Could not find Tree property.");
                    return;
                }
                var treeObj = treeProp.GetValue(registryObj);
                if (treeObj == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] DependencyTree is null.");
                    return;
                }
                var packageHashField = treeObj.GetType().GetField("PackageTypeHashToInstance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (packageHashField == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] Could not find PackageTypeHashToInstance field.");
                    return;
                }
                var dict = packageHashField.GetValue(treeObj) as System.Collections.IDictionary;
                if (dict == null)
                {
                    Debug.LogWarning("[RegistryDiagnostics] PackageTypeHashToInstance is null or not a dictionary.");
                    return;
                }
                Debug.Log($"[RegistryDiagnostics] Total registered packages: {dict.Count}");
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    var val = entry.Value;
                    if (val != null)
                    {
                        Debug.Log($"  - Package: {val.GetType().FullName}");
                    }
                    else
                    {
                        Debug.Log($"  - Package: [null] with hash {entry.Key}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RegistryDiagnostics] Error running diagnostics: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
