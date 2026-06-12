using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
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

        [Header("Google API Config")]
        [SerializeField] private string googleClientId = "";
        [SerializeField] private string googleClientSecret = "";
        [SerializeField] private int googleRedirectPort = 5000;

        public bool IsInitialized => UnityServices.State == ServicesInitializationState.Initialized;
        public bool IsSignedIn => IsInitialized && AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : null;
        
        // Simple cached session key for UI username binding
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
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    await UnityServices.InitializeAsync();
                    Debug.Log("Unity Services Initialized successfully.");
                    
                    // Setup UGS authentication events
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
                        Debug.LogError($"UGS Sign In Failed: {err.Message}");
                    };
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize Unity Services: {e.Message}");
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (!IsInitialized)
            {
                await InitializeServicesAsync();
            }
        }

        public async Task SignUpAsync(string username, string email, string password)
        {
            try
            {
                await EnsureInitializedAsync();
                
                // UGS SignUp requires a username (3-20 characters) and password (8-30 characters)
                await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
                
                // Optional: Update Player Name on profile
                try
                {
                    await AuthenticationService.Instance.UpdatePlayerNameAsync(username);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to update player name on signup: {e.Message}");
                }

                CachedUsername = username;
                Debug.Log($"Signed up successfully. Player ID: {AuthenticationService.Instance.PlayerId}");
                
                // Sign out immediately so they can log in manually on the Login page
                AuthenticationService.Instance.SignOut(true);
                
                OnSignUpSuccess?.Invoke();
            }
            catch (AuthenticationException ex)
            {
                Debug.LogError($"Sign up failed: {ex.Message}");
                OnSignUpFailed?.Invoke(ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Sign up failed with unexpected error: {ex.Message}");
                OnSignUpFailed?.Invoke(ex.Message);
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
                
                CachedUsername = usernameOrEmail;
                Debug.Log($"Signed in successfully. Player ID: {AuthenticationService.Instance.PlayerId}");
                await CloudSaveManager.Instance.LoadAllAsync();
                OnSignInSuccess?.Invoke();
            }
            catch (AuthenticationException ex)
            {
                Debug.LogError($"Sign in failed: {ex.Message}");
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
            if (string.IsNullOrEmpty(googleClientId) || string.IsNullOrEmpty(googleClientSecret))
            {
                Debug.LogWarning("Google Sign-In parameters are not configured in the Unity Inspector.");
                OnSignInFailed?.Invoke("Google Sign-In has not been configured in the Unity Inspector yet. Please fill Client ID and Client Secret on the AuthService component.");
                return;
            }

            GoogleAuthHelper.StartLogin(
                googleClientId,
                googleClientSecret,
                googleRedirectPort,
                async (idToken) =>
                {
                    try
                    {
                        await EnsureInitializedAsync();
                        await AuthenticationService.Instance.SignInWithGoogleAsync(idToken);
                        CachedUsername = "GoogleGamer";
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
                AuthenticationService.Instance.SignOut();
            }
        }

        public async Task<bool> TryAutoSignInAsync()
        {
            try
            {
                await EnsureInitializedAsync();

                if (AuthenticationService.Instance.IsSignedIn)
                {
                    Debug.Log($"Already signed in. Player ID: {AuthenticationService.Instance.PlayerId}");
                    await CloudSaveManager.Instance.LoadAllAsync();
                    return true;
                }

                if (AuthenticationService.Instance.SessionTokenExists)
                {
                    // UGS will automatically reuse session token if we call SignInAnonymouslyAsync()
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"Auto-login successful. Player ID: {AuthenticationService.Instance.PlayerId}");
                    await CloudSaveManager.Instance.LoadAllAsync();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Auto-login failed or no session found: {ex.Message}");
            }
            return false;
        }

        // ─── OTP Password Reset Simulators (Mock) ──────────────────────

        private string _lastSentOtp = "";
        private string _lastOtpEmail = "";

        public Task<bool> RequestPasswordResetAsync(string email)
        {
            // Since UGS Username/Password has no custom forgot-password recovery endpoint,
            // we simulate a beautiful OTP workflow.
            _lastOtpEmail = email;
            
            // Generate simple random 4-digit code
            System.Random rand = new System.Random();
            _lastSentOtp = rand.Next(1000, 9999).ToString();
            
            Debug.Log($"[MOCK OTP] Sent code {_lastSentOtp} to {email}");
            
            // Simulate networking delay
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
    }
}
