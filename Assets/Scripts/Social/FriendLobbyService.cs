using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Friends;
using Unity.Services.Friends.Models;

namespace EdgeParty.Social
{
    public class FriendLobbyService : MonoBehaviour
    {
        public static FriendLobbyService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<FriendLobbyService>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("FriendLobbyService");
                        _instance = go.AddComponent<FriendLobbyService>();
                    }
                }
                return _instance;
            }
        }
        private static FriendLobbyService _instance;

        [System.Serializable]
        public struct FriendInfo
        {
            public string Id;
            public string Username;
            public bool IsOnline;
            public bool InLobby;
        }

        [System.Serializable]
        public struct FriendRequest
        {
            public string Id;
            public string Username;
        }

        // Public lists for UI binding
        public List<FriendInfo> Friends { get; private set; } = new List<FriendInfo>();
        public List<FriendRequest> IncomingRequests { get; private set; } = new List<FriendRequest>();
        public List<string> LobbyMembers { get; private set; } = new List<string>();

        public string CurrentLobbyCode { get; private set; } = "";
        public string CurrentLobbyId { get; private set; } = "";
        public bool IsHost { get; private set; } = false;

        // Events
        public event Action OnFriendsUpdated;
        public event Action OnFriendRequestsUpdated;
        public event Action<List<string>> OnLobbyMembersUpdated;
        public event Action<string> OnLobbyJoined;
        public event Action OnLobbyLeft;

        private bool _useMockMode = true; // Defaults to mock mode until initialized successfully
        private float _heartbeatTimer = 0f;
        private Unity.Services.Lobbies.Models.Lobby _currentLobby;

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
                return;
            }

            InitializeMockData();
        }

        private void Update()
        {
            // Send heartbeat to UGS Lobby if host
            if (!_useMockMode && IsHost && !string.IsNullOrEmpty(CurrentLobbyId))
            {
                _heartbeatTimer += Time.deltaTime;
                if (_heartbeatTimer >= 15f)
                {
                    _heartbeatTimer = 0f;
                    SendLobbyHeartbeatAsync();
                }
            }
        }

        private async void SendLobbyHeartbeatAsync()
        {
            try
            {
                await Unity.Services.Lobbies.LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobbyId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Failed to send lobby heartbeat: {ex.Message}");
            }
        }

        private void InitializeMockData()
        {
            Friends.Clear();
            Friends.Add(new FriendInfo { Id = "mock_f1", Username = "CoolGuy99", IsOnline = true, InLobby = false });
            Friends.Add(new FriendInfo { Id = "mock_f2", Username = "SleepyBear", IsOnline = false, InLobby = false });
            Friends.Add(new FriendInfo { Id = "mock_f3", Username = "RetroRunner", IsOnline = true, InLobby = false });
            Friends.Add(new FriendInfo { Id = "mock_f4", Username = "NeonPanda", IsOnline = true, InLobby = false });
            Friends.Add(new FriendInfo { Id = "mock_f5", Username = "BubblyBot", IsOnline = false, InLobby = false });

            IncomingRequests.Clear();
            IncomingRequests.Add(new FriendRequest { Id = "req_1", Username = "PixelArtist" });
            IncomingRequests.Add(new FriendRequest { Id = "req_2", Username = "SpeedyGamer" });

            LobbyMembers.Clear();
            LobbyMembers.Add("You");
        }

        // Try initializing actual UGS services. If it fails, fall back gracefully to Mock mode.
        public async Task InitializeSocialAsync()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Debug.Log("[FriendLobbyService] UGS not signed in. Defaulting to Mock mode for UI prototyping.");
                    _useMockMode = true;
                    return;
                }

                // Clear mock data immediately while initializing real services
                Friends.Clear();
                IncomingRequests.Clear();
                OnFriendsUpdated?.Invoke();
                OnFriendRequestsUpdated?.Invoke();

                // Try initializing Friends service
                await FriendsService.Instance.InitializeAsync();
                _useMockMode = false;
                Debug.Log("[FriendLobbyService] Real UGS Friends initialized successfully. Running in real UGS Mode.");

                // Subscribe to real-time events to update the UI instantly
                FriendsService.Instance.RelationshipAdded += (evt) => _ = RefreshFriendsAndRequestsAsync();
                FriendsService.Instance.RelationshipDeleted += (evt) => _ = RefreshFriendsAndRequestsAsync();
                FriendsService.Instance.PresenceUpdated += (evt) => _ = RefreshFriendsAndRequestsAsync();

                await RefreshFriendsAndRequestsAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Failed to initialize real UGS Friends/Lobby. Running in MOCK Mode. Error: {ex.Message}");
                _useMockMode = true;
                InitializeMockData();
                OnFriendsUpdated?.Invoke();
                OnFriendRequestsUpdated?.Invoke();
            }
        }

        public Task RefreshFriendsAndRequestsAsync()
        {
            if (_useMockMode)
            {
                OnFriendsUpdated?.Invoke();
                OnFriendRequestsUpdated?.Invoke();
                return Task.CompletedTask;
            }

            try
            {
                // Retrieve friends list from local cache
                var friendsList = FriendsService.Instance.Friends;
                Friends.Clear();
                foreach (var friend in friendsList)
                {
                    bool isOnline = friend.Member != null && friend.Member.Presence != null && friend.Member.Presence.Availability == Availability.Online;
                    string name = friend.Member != null && friend.Member.Profile != null ? friend.Member.Profile.Name : "Unknown";

                    Friends.Add(new FriendInfo
                    {
                        Id = friend.Member?.Id ?? friend.Id,
                        Username = name,
                        IsOnline = isOnline,
                        InLobby = false
                    });
                }

                // Retrieve incoming friend requests from local cache
                var incoming = FriendsService.Instance.IncomingFriendRequests;
                IncomingRequests.Clear();
                foreach (var req in incoming)
                {
                    string name = req.Member != null && req.Member.Profile != null ? req.Member.Profile.Name : "Unknown";
                    IncomingRequests.Add(new FriendRequest
                    {
                        Id = req.Member?.Id ?? req.Id,
                        Username = name
                    });
                }

                OnFriendsUpdated?.Invoke();
                OnFriendRequestsUpdated?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FriendLobbyService] Error refreshing friends: {ex.Message}");
            }
            return Task.CompletedTask;
        }

        public async Task<bool> SendFriendRequestAsync(string username)
        {
            if (string.IsNullOrEmpty(username)) return false;

            if (_useMockMode)
            {
                Debug.Log($"[Mock Social] Sent friend request to username: {username}");
                return true;
            }

            try
            {
                await FriendsService.Instance.AddFriendByNameAsync(username);
                await ForceRefreshAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FriendLobbyService] Error sending request: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AcceptFriendRequestAsync(string requestId)
        {
            if (_useMockMode)
            {
                var req = IncomingRequests.Find(r => r.Id == requestId);
                if (!string.IsNullOrEmpty(req.Id))
                {
                    IncomingRequests.Remove(req);
                    Friends.Add(new FriendInfo { Id = "mock_" + req.Username, Username = req.Username, IsOnline = true, InLobby = false });
                    OnFriendsUpdated?.Invoke();
                    OnFriendRequestsUpdated?.Invoke();
                    Debug.Log($"[Mock Social] Accepted friend request from: {req.Username}");
                    return true;
                }
                return false;
            }

            try
            {
                // In UGS Friends, calling AddFriendAsync with the member ID of an incoming request accepts the request.
                await FriendsService.Instance.AddFriendAsync(requestId);
                await ForceRefreshAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FriendLobbyService] Error accepting request: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeclineFriendRequestAsync(string requestId)
        {
            if (_useMockMode)
            {
                var req = IncomingRequests.Find(r => r.Id == requestId);
                if (!string.IsNullOrEmpty(req.Id))
                {
                    IncomingRequests.Remove(req);
                    OnFriendRequestsUpdated?.Invoke();
                    Debug.Log($"[Mock Social] Declined friend request from: {req.Username}");
                    return true;
                }
                return false;
            }

            try
            {
                await FriendsService.Instance.DeleteIncomingFriendRequestAsync(requestId);
                await ForceRefreshAsync();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FriendLobbyService] Error declining request: {ex.Message}");
                return false;
            }
        }

        private async Task ForceRefreshAsync()
        {
            try
            {
                await FriendsService.Instance.ForceRelationshipsRefreshAsync();
                await RefreshFriendsAndRequestsAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Failed to force refresh: {ex.Message}");
            }
        }

        // ─── Lobby Services ──────────────────────────────────────────────

        public async Task<bool> CreateLobbyAsync(string lobbyName, int maxPlayers = 4)
        {
            if (_useMockMode)
            {
                CurrentLobbyId = "mock_lobby_" + UnityEngine.Random.Range(1000, 9999);
                CurrentLobbyCode = UnityEngine.Random.Range(100000, 999999).ToString();
                IsHost = true;

                LobbyMembers.Clear();
                LobbyMembers.Add("You");

                Debug.Log($"[Mock Lobby] Created lobby {lobbyName}. Code: {CurrentLobbyCode}");
                OnLobbyJoined?.Invoke(CurrentLobbyCode);
                OnLobbyMembersUpdated?.Invoke(LobbyMembers);
                return true;
            }

            try
            {
                var options = new Unity.Services.Lobbies.CreateLobbyOptions();
                _currentLobby = await Unity.Services.Lobbies.LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
                CurrentLobbyId = _currentLobby.Id;
                CurrentLobbyCode = _currentLobby.LobbyCode;
                IsHost = true;

                UpdateLobbyMembersList();
                OnLobbyJoined?.Invoke(CurrentLobbyCode);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FriendLobbyService] Error creating lobby: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> JoinLobbyByCodeAsync(string joinCode)
        {
            if (string.IsNullOrEmpty(joinCode)) return false;

            if (_useMockMode)
            {
                CurrentLobbyId = "mock_lobby_joined";
                CurrentLobbyCode = joinCode;
                IsHost = false;

                LobbyMembers.Clear();
                LobbyMembers.Add("LobbyHost");
                LobbyMembers.Add("You");

                Debug.Log($"[Mock Lobby] Joined lobby code: {joinCode}");
                OnLobbyJoined?.Invoke(CurrentLobbyCode);
                OnLobbyMembersUpdated?.Invoke(LobbyMembers);
                return true;
            }

            try
            {
                var options = new Unity.Services.Lobbies.JoinLobbyByCodeOptions();
                _currentLobby = await Unity.Services.Lobbies.LobbyService.Instance.JoinLobbyByCodeAsync(joinCode, options);
                CurrentLobbyId = _currentLobby.Id;
                CurrentLobbyCode = _currentLobby.LobbyCode;
                IsHost = _currentLobby.HostId == AuthenticationService.Instance.PlayerId;

                UpdateLobbyMembersList();
                OnLobbyJoined?.Invoke(CurrentLobbyCode);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FriendLobbyService] Error joining lobby: {ex.Message}");
                return false;
            }
        }

        public async Task LeaveLobbyAsync()
        {
            if (string.IsNullOrEmpty(CurrentLobbyId)) return;

            if (_useMockMode)
            {
                CurrentLobbyId = "";
                CurrentLobbyCode = "";
                IsHost = false;
                LobbyMembers.Clear();
                LobbyMembers.Add("You");

                Debug.Log("[Mock Lobby] Left lobby.");
                OnLobbyLeft?.Invoke();
                OnLobbyMembersUpdated?.Invoke(LobbyMembers);
                return;
            }

            try
            {
                string playerId = AuthenticationService.Instance.PlayerId;
                await Unity.Services.Lobbies.LobbyService.Instance.RemovePlayerAsync(CurrentLobbyId, playerId);
                
                CurrentLobbyId = "";
                CurrentLobbyCode = "";
                IsHost = false;
                LobbyMembers.Clear();
                LobbyMembers.Add("You");

                OnLobbyLeft?.Invoke();
                OnLobbyMembersUpdated?.Invoke(LobbyMembers);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FriendLobbyService] Error leaving lobby: {ex.Message}");
            }
        }

        public void SimulateFriendAcceptingInvite(string friendUsername)
        {
            if (!LobbyMembers.Contains(friendUsername))
            {
                LobbyMembers.Add(friendUsername);
                OnLobbyMembersUpdated?.Invoke(LobbyMembers);
                Debug.Log($"[Mock Lobby] {friendUsername} accepted the invite and joined your lobby!");
            }
        }

        private void UpdateLobbyMembersList()
        {
            if (_currentLobby == null) return;

            LobbyMembers.Clear();
            foreach (var player in _currentLobby.Players)
            {
                string pName = player.Data != null && player.Data.ContainsKey("PlayerName") ? player.Data["PlayerName"].Value : player.Id;
                if (player.Id == AuthenticationService.Instance.PlayerId)
                {
                    LobbyMembers.Add("You");
                }
                else
                {
                    LobbyMembers.Add(pName);
                }
            }
            OnLobbyMembersUpdated?.Invoke(LobbyMembers);
        }
    }
}
