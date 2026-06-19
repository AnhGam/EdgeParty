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
        public static VoiceChatManager Instance { get; private set; }
        public event Action OnVoiceReady;
        [Header("Settings")]
        [SerializeField] private string defaultChannelName = "MainLobby";

        public event Action<string, bool> OnParticipantSpeaking; // (participantId, isSpeaking)
        public event Action<string> OnParticipantJoined;
        public event Action<string> OnParticipantLeft;

        private bool _isInitialized = false;
        private HashSet<string> _activeSpeakers = new HashSet<string>();
        private string _currentGameChannel = "";  // channel đang join trong game

        public string CurrentGameChannel => _currentGameChannel;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private async void Start()
        {
            await InitializeVoiceChat();
        }

        private void OnDestroy()
        {
            if (_isInitialized && VivoxService.Instance != null)
            {
                VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantRemoved;
            }
        }

        private async Task InitializeVoiceChat()
        {
            try
            {
                // 1. Initialize UGS - inject Vivox credentials so the SDK can read them
                //    (SDK reads from InitializationOptions, not from code directly)
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();
                    // These keys match VivoxServiceInternal.k_ServerKey/k_DomainKey/k_IssuerKey/k_TokenKey
                    options.SetOption("com.unity.services.vivox.server", VivoxConfig.Server);
                    options.SetOption("com.unity.services.vivox.domain", VivoxConfig.Domain);
                    options.SetOption("com.unity.services.vivox.issuer", VivoxConfig.TokenIssuer);
                    options.SetOption("com.unity.services.vivox.token",  VivoxConfig.TokenKey);

                    await UnityServices.InitializeAsync(options);
                    Debug.Log("[VoiceChat] UGS Initialized.");
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"[VoiceChat] Signed in as: {AuthenticationService.Instance.PlayerId}");
                }

                string playerId = AuthenticationService.Instance.PlayerId;

                // Register our custom token provider BEFORE initializing Vivox
                VivoxService.Instance.SetTokenProvider(new VivoxManualTokenProvider(playerId));

                await VivoxService.Instance.InitializeAsync();
                Debug.Log("[VoiceChat] Vivox Initialized.");

                VivoxService.Instance.ParticipantAddedToChannel   += OnParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;

                var loginOptions = new LoginOptions
                {
                    DisplayName = playerId,
                    ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.FivePerSecond
                };
                await VivoxService.Instance.LoginAsync(loginOptions);
                Debug.Log("[VoiceChat] Logged into Vivox.");

                _isInitialized = true;
                OnVoiceReady?.Invoke();
                await JoinChannel(defaultChannelName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VoiceChat] Initialization error: {e.Message}");
            }
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
            if (!_isInitialized) return;
            try
            {
                await VivoxService.Instance.JoinPositionalChannelAsync(
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
            if (!_isInitialized) return;
            await VivoxService.Instance.LeaveChannelAsync(channelName);
        }

        public void SetMute(bool mute)
        {
            if (!_isInitialized) return;
            if (mute) VivoxService.Instance.MuteInputDevice();
            else VivoxService.Instance.UnmuteInputDevice();
        }

        public void ToggleMute()
        {
            SetMute(!IsMuted);
        }

        public bool IsMuted => _isInitialized && VivoxService.Instance.IsInputDeviceMuted;

        public bool IsReady => _isInitialized;

        public void UpdateParticipantPosition(GameObject participant, string channelName)
        {
            if (!_isInitialized || !VivoxService.Instance.IsLoggedIn) return;
            VivoxService.Instance.Set3DPosition(participant, channelName);
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
                if (UnityEngine.InputSystem.Keyboard.current != null && 
                    System.Enum.TryParse(mappedPTTKey, true, out UnityEngine.InputSystem.Key pttKey) &&
                    pttKey != UnityEngine.InputSystem.Key.None)
                {
                    bool isPressed = UnityEngine.InputSystem.Keyboard.current[pttKey].isPressed;
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
            }
            else
            {
                if (VivoxService.Instance.IsInputDeviceMuted)
                {
                    VivoxService.Instance.UnmuteInputDevice();
                }
            }
        }

        #endregion
    }
}
