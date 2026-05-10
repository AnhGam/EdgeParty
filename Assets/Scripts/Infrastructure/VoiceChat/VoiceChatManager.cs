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

        [Header("Settings")]
        [SerializeField] private string defaultChannelName = "MainLobby";

        public event Action<string, bool> OnParticipantSpeaking; // (participantId, isSpeaking)
        public event Action<string> OnParticipantJoined;
        public event Action<string> OnParticipantLeft;

        private bool _isInitialized = false;
        private HashSet<string> _activeSpeakers = new HashSet<string>();

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

                // 2. Authenticate anonymously if not already signed in
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"[VoiceChat] Signed in as: {AuthenticationService.Instance.PlayerId}");
                }

                string playerId = AuthenticationService.Instance.PlayerId;

                // 3. Register our custom token provider BEFORE initializing Vivox
                VivoxService.Instance.SetTokenProvider(new VivoxManualTokenProvider(playerId));

                // 4. Initialize Vivox (server/domain/issuer already injected in step 1)
                await VivoxService.Instance.InitializeAsync();
                Debug.Log("[VoiceChat] Vivox Initialized.");

                // 5. Subscribe to participant events
                VivoxService.Instance.ParticipantAddedToChannel   += OnParticipantAdded;
                VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantRemoved;

                // 6. Login to Vivox
                var loginOptions = new LoginOptions
                {
                    DisplayName = playerId,
                    ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.FivePerSecond
                };
                await VivoxService.Instance.LoginAsync(loginOptions);
                Debug.Log("[VoiceChat] Logged into Vivox.");

                _isInitialized = true;

                // 7. Join default positional channel
                await JoinChannel(defaultChannelName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VoiceChat] Initialization error: {e.Message}");
            }
        }

        /// <summary>Join a positional (3D audio) Vivox channel.</summary>
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

        /// <summary>Leave a specific channel.</summary>
        public async Task LeaveChannel(string channelName)
        {
            if (!_isInitialized) return;
            await VivoxService.Instance.LeaveChannelAsync(channelName);
        }

        /// <summary>Mute or unmute the local user's microphone.</summary>
        public void SetMute(bool mute)
        {
            if (!_isInitialized) return;
            if (mute) VivoxService.Instance.MuteInputDevice();
            else VivoxService.Instance.UnmuteInputDevice();
        }

        /// <summary>Toggle mute.</summary>
        public void ToggleMute()
        {
            SetMute(!IsMuted);
        }

        /// <summary>Whether the local user's mic is muted.</summary>
        public bool IsMuted => _isInitialized && VivoxService.Instance.IsInputDeviceMuted;

        /// <summary>Whether voice chat is ready.</summary>
        public bool IsReady => _isInitialized;

        /// <summary>Update the player's 3D position in a positional channel.</summary>
        public void UpdateParticipantPosition(GameObject participant, string channelName)
        {
            if (!_isInitialized || !VivoxService.Instance.IsLoggedIn) return;
            VivoxService.Instance.Set3DPosition(participant, channelName);
        }

        /// <summary>Returns the set of player IDs currently speaking.</summary>
        public HashSet<string> GetActiveSpeakers() => _activeSpeakers;

        #region Vivox Event Handlers

        private void OnParticipantAdded(VivoxParticipant participant)
        {
            Debug.Log($"[VoiceChat] Participant joined: {participant.PlayerId}");
            OnParticipantJoined?.Invoke(participant.PlayerId);

            // Subscribe to speech detection using a closure to capture the participant reference
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
            // SpeechDetected toggles true/false as the participant starts/stops speaking
            bool isSpeaking = participant.SpeechDetected;
            if (isSpeaking) _activeSpeakers.Add(participant.PlayerId);
            else _activeSpeakers.Remove(participant.PlayerId);

            OnParticipantSpeaking?.Invoke(participant.PlayerId, isSpeaking);
        }

        #endregion
    }
}
