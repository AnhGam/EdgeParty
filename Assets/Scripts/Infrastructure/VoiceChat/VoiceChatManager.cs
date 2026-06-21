using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace EdgeParty.Infrastructure.VoiceChat
{
    public class VoiceChatManager : MonoBehaviour
    {
        private static VoiceChatManager _instance;
        public static VoiceChatManager Instance
        {
            get
            {
                if (_instance == null)
                {
#if UNITY_2023_1_OR_NEWER
                    _instance = FindFirstObjectByType<VoiceChatManager>();
#else
                    _instance = FindObjectOfType<VoiceChatManager>();
#endif
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("VoiceChatManager");
                        _instance = go.AddComponent<VoiceChatManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
            private set => _instance = value;
        }

        public event Action OnVoiceReady;
        [Header("Settings")]
        [SerializeField] private string defaultChannelName = "MainLobby";

        public event Action<string, bool> OnParticipantSpeaking; // (participantId, isSpeaking)
        public event Action<string> OnParticipantJoined;
        public event Action<string> OnParticipantLeft;

        private bool _isInitialized = false;
        private IVivoxService _vivoxService; // cached from GetService or VivoxService.Instance
        private HashSet<string> _activeSpeakers = new HashSet<string>();
        private string _currentGameChannel = "";  // channel đang join trong game
        private float _nextDebugLogTime = 0f;

        public string CurrentGameChannel => _currentGameChannel;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // AfterSceneLoad: scene đã load, AuthService đã Awake, UGS có thể init
        // (BeforeSceneLoad cũ chạy TRƯỚC khi UGS init → VivoxService.Instance luôn null)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstanceExists()
        {
            // Chỉ tạo GameObject nếu chưa có — không gọi Vivox ở đây
            var _ = Instance;
        }

        private void Start()
        {
            if (EdgeParty.Auth.AuthService.Instance == null)
            {
                Debug.LogWarning("[VoiceChat] AuthService not found. VoiceChat will not initialize.");
                return;
            }

            // Nếu đã đăng nhập rồi thì khởi tạo luôn
            if (EdgeParty.Auth.AuthService.Instance.IsSignedIn)
            {
                _ = InitializeVoiceChat();
            }
            else
            {
                // Nếu chưa, thì "link" thẳng vào event của AuthService, khi nào đăng nhập xong nó sẽ tự gọi
                EdgeParty.Auth.AuthService.Instance.OnSignInSuccess += OnAuthSuccess;
            }
        }

        private void OnAuthSuccess()
        {
            EdgeParty.Auth.AuthService.Instance.OnSignInSuccess -= OnAuthSuccess;
            _ = InitializeVoiceChat();
        }

        private void OnDestroy()
        {
            if (_isInitialized && _vivoxService != null)
            {
                _vivoxService.ParticipantAddedToChannel -= OnParticipantAdded;
                _vivoxService.ParticipantRemovedFromChannel -= OnParticipantRemoved;
            }
        }

        private async Task InitializeVoiceChat()
        {
            try
            {
                // Đảm bảo UGS đã sẵn sàng (AuthService đã lo phần này)
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    Debug.LogWarning($"[VoiceChat] UGS not Initialized yet (State={UnityServices.State}). Aborting.");
                    return;
                }

                // Ưu tiên lấy IVivoxService từ UGS registry (hoạt động với cả Default và Instance path)
                IVivoxService vivox = VivoxService.Instance;

                if (vivox == null)
                {
                    // Fallback: lấy trực tiếp từ UnityServices registry
                    try { vivox = UnityServices.Instance.GetService<IVivoxService>(); } catch { }
                    if (vivox != null)
                    {
                        Debug.Log("[VoiceChat] Got IVivoxService via UnityServices.Instance.GetService<> (VivoxService.Instance was null).");
                    }
                }

                if (vivox == null)
                {
                    Debug.LogWarning("[VoiceChat] VivoxService.Instance is null right after OnSignInSuccess — polling up to 3s...");
                    vivox = await WaitForVivoxServiceAsync(maxWaitMs: 3000);
                    if (vivox != null)
                        Debug.Log("[VoiceChat] VivoxService.Instance became available after polling.");
                }

                if (vivox == null)
                {
                    Debug.LogError("[VoiceChat] Cannot obtain IVivoxService after all attempts.\n" +
                                   "  → VivoxService.Instance: NULL\n" +
                                   "  → UnityServices.GetService<IVivoxService>: NULL\n" +
                                   "  → Poll 3s: NULL\n" +
                                   "Possible causes:\n" +
                                   "  1. VivoxPackageInitializer.Register() chạy SAU khi CorePackageRegistry bị Lock()\n" +
                                   "     → Kiểm tra xem có 'Package registration has been locked' trong log không.\n" +
                                   "  2. Package com.unity.services.vivox không được cài đúng.\n" +
                                   "  3. VivoxPackageInitializer constructor throw silently.\n" +
                                   $"  UGS State: {UnityServices.State}");
                    return;
                }

                string playerId = AuthenticationService.Instance.PlayerId;

                // Since we call options.SetVivoxCredentials in AuthService before UGS InitializeAsync,
                // Vivox will automatically generate tokens client-side using the Token Key.
                // No manual token provider is needed for local development.
                // vivox.SetTokenProvider(new VivoxManualTokenProvider(playerId));

                await vivox.InitializeAsync();
                Debug.Log("[VoiceChat] Vivox Initialized.");

                vivox.ParticipantAddedToChannel   += OnParticipantAdded;
                vivox.ParticipantRemovedFromChannel += OnParticipantRemoved;

                var loginOptions = new LoginOptions
                {
                    DisplayName = playerId,
                    ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.FivePerSecond
                };
                await vivox.LoginAsync(loginOptions);
                Debug.Log("[VoiceChat] Logged into Vivox.");

                _isInitialized = true;
                _vivoxService = vivox; // cache for OnDestroy/JoinChannel
                OnVoiceReady?.Invoke();
                await JoinChannel(defaultChannelName);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Polls VivoxService.Instance until non-null or timeout (handles async propagation delay).</summary>
        private async Task<IVivoxService> WaitForVivoxServiceAsync(int maxWaitMs = 3000)
        {
            const int pollIntervalMs = 100;
            int elapsed = 0;
            while (elapsed < maxWaitMs)
            {
                if (VivoxService.Instance != null)
                    return VivoxService.Instance;
                await Task.Delay(pollIntervalMs);
                elapsed += pollIntervalMs;
            }
            return VivoxService.Instance; // null if never became available
        }

        public async Task JoinMatchChannel(string matchId)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[VoiceChat] JoinMatchChannel called before initialized.");
                return;
            }

            // Sanitize channel name: chỉ giữ [a-zA-Z0-9-_]
            string sanitized = System.Text.RegularExpressions.Regex.Replace(matchId, @"[^a-zA-Z0-9\-_]", "-");
            if (string.IsNullOrEmpty(sanitized)) sanitized = defaultChannelName;
            // Giới hạn 200 ký tự (Vivox limit)
            if (sanitized.Length > 200) sanitized = sanitized.Substring(0, 200);

            // Rời channel cũ nếu đang join channel khác
            if (!string.IsNullOrEmpty(_currentGameChannel) && _currentGameChannel != sanitized)
            {
                await LeaveCurrentGameChannel();
            }

            _currentGameChannel = sanitized;
            await JoinChannel(sanitized);
            Debug.Log($"[VoiceChat] Joined match channel: {sanitized} (from matchId: {matchId})");
        }

        public async Task LeaveCurrentGameChannel()
        {
            if (string.IsNullOrEmpty(_currentGameChannel)) return;
            await LeaveChannel(_currentGameChannel);
            _currentGameChannel = "";
        }

        public async Task JoinChannel(string channelName)
        {
            if (!_isInitialized || _vivoxService == null) return;
            try
            {
                await _vivoxService.JoinPositionalChannelAsync(
                    channelName,
                    ChatCapability.AudioOnly,
                    new Channel3DProperties());
                Debug.Log($"[VoiceChat] Joined channel: {channelName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VoiceChat] JoinChannel error: {e.Message}");
            }
        }

        public async Task LeaveChannel(string channelName)
        {
            if (!_isInitialized || _vivoxService == null) return;
            await _vivoxService.LeaveChannelAsync(channelName);
        }

        public void SetMute(bool mute)
        {
            if (!_isInitialized || _vivoxService == null) return;
            if (mute) _vivoxService.MuteInputDevice();
            else _vivoxService.UnmuteInputDevice();
        }

        public void ToggleMute()
        {
            SetMute(!IsMuted);
        }

        public bool IsMuted => _isInitialized && _vivoxService != null && _vivoxService.IsInputDeviceMuted;

        public bool IsReady => _isInitialized;

        public void UpdateParticipantPosition(GameObject participant, string channelName)
        {
            if (!_isInitialized || _vivoxService == null || !_vivoxService.IsLoggedIn) return;
            _vivoxService.Set3DPosition(participant, channelName);
        }

        public HashSet<string> GetActiveSpeakers() => _activeSpeakers;

        #region Vivox Event Handlers

        private void OnParticipantAdded(VivoxParticipant participant)
        {
            Debug.Log($"[VoiceChat] Participant joined: {participant.PlayerId}");
            OnParticipantJoined?.Invoke(participant.PlayerId);

            participant.ParticipantSpeechDetected += () => OnVivoxSpeechDetected(participant);
        }

        private void OnParticipantRemoved(VivoxParticipant participant)
        {
            Debug.Log($"[VoiceChat] Participant left: {participant.PlayerId}");
            _activeSpeakers.Remove(participant.PlayerId);
            OnParticipantLeft?.Invoke(participant.PlayerId);
        }

        private void OnVivoxSpeechDetected(VivoxParticipant participant)
        {
            bool isSpeaking = participant.SpeechDetected;
            if (isSpeaking) _activeSpeakers.Add(participant.PlayerId);
            else _activeSpeakers.Remove(participant.PlayerId);

            OnParticipantSpeaking?.Invoke(participant.PlayerId, isSpeaking);
        }

        private string MapKeyCodeToKeyName(string keyName)
        {
            string mappedName = keyName.Trim();
            if (mappedName.StartsWith("Alpha", System.StringComparison.OrdinalIgnoreCase) && mappedName.Length == 6 && char.IsDigit(mappedName[5]))
            {
                return "Digit" + mappedName[5];
            }
            if (mappedName.Equals("LeftControl", System.StringComparison.OrdinalIgnoreCase)) return "LeftCtrl";
            if (mappedName.Equals("RightControl", System.StringComparison.OrdinalIgnoreCase)) return "RightCtrl";
            if (mappedName.Equals("Space", System.StringComparison.OrdinalIgnoreCase)) return "Space";
            return mappedName;
        }

        private void Update()
        {
            if (!_isInitialized || VivoxService.Instance == null) return;

            if (Time.time >= _nextDebugLogTime)
            {
                _nextDebugLogTime = Time.time + 2f;
                LogVoiceChatDebugStatus();
            }

            bool voiceEnabled = PlayerPrefs.GetInt("VoiceChatEnabled", 1) == 1;
            if (!voiceEnabled)
            {
                if (!VivoxService.Instance.IsInputDeviceMuted)
                {
                    VivoxService.Instance.MuteInputDevice();
                }
                return;
            }

            bool usePTT = PlayerPrefs.GetInt("TransmissionModePTT", 1) == 1;
            if (usePTT)
            {
                string pttKeyName = PlayerPrefs.GetString("KeybindPTT", "V");
                string mappedPTTKey = MapKeyCodeToKeyName(pttKeyName);

                bool isPressed = false;

                // Primary: New Input System
                if (UnityEngine.InputSystem.Keyboard.current != null)
                {
                    if (System.Enum.TryParse(mappedPTTKey, true, out UnityEngine.InputSystem.Key pttKey) &&
                        pttKey != UnityEngine.InputSystem.Key.None)
                    {
                        isPressed = UnityEngine.InputSystem.Keyboard.current[pttKey].isPressed;
                    }
                    else
                    {
                        Debug.LogWarning($"[VoiceChat] PTT key '{pttKeyName}' (mapped: '{mappedPTTKey}') " +
                                         "could not be parsed as InputSystem.Key. Check keybind setting.");
                    }
                }
                else
                {
                    // Fallback: Legacy Input System
                    if (System.Enum.TryParse(pttKeyName, true, out KeyCode legacyKey))
                    {
                        isPressed = Input.GetKey(legacyKey);
                    }
                }

                if (isPressed)
                {
                    if (VivoxService.Instance.IsInputDeviceMuted)
                        VivoxService.Instance.UnmuteInputDevice();
                }
                else
                {
                    if (!VivoxService.Instance.IsInputDeviceMuted)
                        VivoxService.Instance.MuteInputDevice();
                }
            }
            else
            {
                // Open Mic mode: always unmuted
                if (VivoxService.Instance.IsInputDeviceMuted)
                {
                    VivoxService.Instance.UnmuteInputDevice();
                }
            }
        }

        private void LogVoiceChatDebugStatus()
        {
            if (!_isInitialized || _vivoxService == null)
            {
                Debug.Log("[VoiceChatDebug] Not initialized.");
                return;
            }

            bool voiceEnabled = PlayerPrefs.GetInt("VoiceChatEnabled", 1) == 1;
            bool usePTT = PlayerPrefs.GetInt("TransmissionModePTT", 1) == 1;
            string pttKeyName = PlayerPrefs.GetString("KeybindPTT", "V");
            bool isMuted = _vivoxService.IsInputDeviceMuted;
            string activeDevice = _vivoxService.ActiveInputDevice != null ? _vivoxService.ActiveInputDevice.DeviceName : "None";

            double localEnergy = 0;
            bool localSpeaking = false;
            bool foundSelf = false;

            foreach (var channelPair in _vivoxService.ActiveChannels)
            {
                foreach (var participant in channelPair.Value)
                {
                    if (participant.IsSelf)
                    {
                        localEnergy = participant.AudioEnergy;
                        localSpeaking = participant.SpeechDetected;
                        foundSelf = true;
                    }
                }
            }

            Debug.Log($"[VoiceChatDebug] Enabled: {voiceEnabled} | Mode: {(usePTT ? $"PTT ({pttKeyName})" : "Open Mic")} | " +
                      $"Device: {activeDevice} | Muted: {isMuted} | InChannel: {foundSelf} | " +
                      $"Energy: {localEnergy:F5} | Speaking: {localSpeaking}");
        }

        #endregion
    }
}
