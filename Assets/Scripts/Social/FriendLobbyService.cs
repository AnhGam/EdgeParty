using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Friends;
using Unity.Services.Friends.Models;
using Unity.Services.Lobbies.Models;
using EdgeParty.Auth;
using EdgeParty.UI;

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

        public List<FriendInfo> Friends { get; private set; } = new List<FriendInfo>();
        public List<FriendRequest> IncomingRequests { get; private set; } = new List<FriendRequest>();
        public List<string> LobbyMembers { get; private set; } = new List<string>();

        public string CurrentLobbyCode { get; private set; } = "";
        public string CurrentLobbyId { get; private set; } = "";
        public bool IsHost { get; private set; } = false;

        public event Action OnFriendsUpdated;
        public event Action OnFriendRequestsUpdated;
        public event Action<List<string>> OnLobbyMembersUpdated;
        public event Action<string> OnLobbyJoined;
        public event Action OnLobbyLeft;

        public LobbyInvite CurrentInvite { get; set; }
        public event Action<LobbyInvite> OnLobbyInviteReceived;
        public event Action OnLobbyInviteCleared;

        private bool _useMockMode = true; // Defaults to mock mode until initialized successfully
        private float _heartbeatTimer = 0f;
        private float _presenceRefreshTimer = 0f;
        private const float PresenceRefreshInterval = 30f;  // Refresh presence mỗi 30s
        private Unity.Services.Lobbies.Models.Lobby _currentLobby;
        private bool _isInitialized = false;
        private Task _initTask;  // Guards against concurrent double-init

        private float _lobbyPollTimer = 0f;
        private const float LobbyPollInterval = 3f;  // Poll lobby mỗi 3s

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
            if (!_useMockMode && IsHost && !string.IsNullOrEmpty(CurrentLobbyId))
            {
                _heartbeatTimer += Time.deltaTime;
                if (_heartbeatTimer >= 15f)
                {
                    _heartbeatTimer = 0f;
                    SendLobbyHeartbeatAsync();
                }
            }

            // Periodic lobby polling to get latest members
            if (!_useMockMode && !string.IsNullOrEmpty(CurrentLobbyId))
            {
                _lobbyPollTimer += Time.deltaTime;
                if (_lobbyPollTimer >= LobbyPollInterval)
                {
                    _lobbyPollTimer = 0f;
                    _ = PollLobbyAsync();
                }
            }

            // Periodic presence refresh để đảm bảo bạn bè thấy trạng thái online
            if (!_useMockMode)
            {
                _presenceRefreshTimer += Time.deltaTime;
                if (_presenceRefreshTimer >= PresenceRefreshInterval)
                {
                    _presenceRefreshTimer = 0f;
                    _ = RefreshFriendsAndRequestsAsync();
                }
            }
        }

        private async Task PollLobbyAsync()
        {
            if (string.IsNullOrEmpty(CurrentLobbyId)) return;
            try
            {
                _currentLobby = await Unity.Services.Lobbies.LobbyService.Instance.GetLobbyAsync(CurrentLobbyId);
                UpdateLobbyMembersList();

                if (!IsHost)
                {
                    CheckMatchmakingStatusFromLobbyData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Failed to poll lobby: {ex.Message}");
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
        }

        public Task InitializeSocialAsync()
        {
            if (_isInitialized)
            {
                _ = RefreshFriendsAndRequestsAsync();
                return Task.CompletedTask;
            }
            // Prevent concurrent double-init (e.g. ShowHome called twice before first init completes)
            if (_initTask == null)
            {
                _initTask = InitializeSocialInternalAsync();
            }
            return _initTask;
        }

        private async Task InitializeSocialInternalAsync()
        {
            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    await EdgeParty.Auth.AuthService.Instance.EnsureInitializedAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    Debug.Log("[FriendLobbyService] UGS not signed in. Defaulting to Mock mode for UI prototyping.");
                    _useMockMode = true;
                    return;
                }

                Friends.Clear();
                IncomingRequests.Clear();
                OnFriendsUpdated?.Invoke();
                OnFriendRequestsUpdated?.Invoke();

                await FriendsService.Instance.InitializeAsync();
                _useMockMode = false;
                _isInitialized = true;
                Debug.Log("[FriendLobbyService] Real UGS Friends initialized. Running in real UGS Mode.");

                // Clean up any stale lobbies the player might still be registered in on UGS side
                // Lobby SDK needs extra time to stabilize its internal HTTP client after auth.
                // Use retry logic since the NullRef comes from inside WrappedLobbyService (timing issue).
                await Task.Delay(2000);
                try
                {
                    List<string> joinedLobbyIds = null;
                    const int maxAttempts = 3;
                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        try
                        {
                            joinedLobbyIds = await Unity.Services.Lobbies.LobbyService.Instance.GetJoinedLobbiesAsync();
                            break; // success
                        }
                        catch (NullReferenceException nre) when (attempt < maxAttempts)
                        {
                            Debug.LogWarning($"[FriendLobbyService] Lobby SDK not ready (attempt {attempt}/{maxAttempts}): {nre.Message}. Retrying in 1s...");
                            await Task.Delay(1000);
                        }
                        catch (NullReferenceException nre)
                        {
                            Debug.LogWarning($"[FriendLobbyService] Lobby SDK not ready after {maxAttempts} attempts: {nre.Message}. Skipping stale cleanup.");
                        }
                    }

                    if (joinedLobbyIds != null && joinedLobbyIds.Count > 0)
                    {
                        Debug.Log($"[FriendLobbyService] Found {joinedLobbyIds.Count} stale lobbies on login. Cleaning them up...");
                        foreach (string lobbyId in joinedLobbyIds)
                        {
                            try
                            {
                                await Unity.Services.Lobbies.LobbyService.Instance.RemovePlayerAsync(lobbyId, AuthenticationService.Instance.PlayerId);
                                Debug.Log($"[FriendLobbyService] Successfully left stale lobby: {lobbyId}");
                            }
                            catch (Exception rmEx)
                            {
                                Debug.LogWarning($"[FriendLobbyService] Failed to leave stale lobby {lobbyId}: {rmEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception lobbyCleanEx)
                {
                    Debug.LogWarning($"[FriendLobbyService] Stale lobby cleanup failed (non-fatal): {lobbyCleanEx.Message}");
                }

                // ✅ Đặt presence = Online để bạn bè thấy mình online
                // SetPresenceAsync<T>(Availability, T activity) — T phải là struct/class có constructor rỗng
                try
                {
                    await FriendsService.Instance.SetPresenceAsync(Availability.Online, new EmptyActivity());
                    Debug.Log("[FriendLobbyService] Presence set to Online.");
                }
                catch (Exception presEx)
                {
                    Debug.LogWarning($"[FriendLobbyService] SetPresence failed (non-fatal): {presEx.Message}");
                }

                FriendsService.Instance.RelationshipAdded += (evt) => _ = RefreshFriendsAndRequestsAsync();
                FriendsService.Instance.RelationshipDeleted += (evt) => _ = RefreshFriendsAndRequestsAsync();
                FriendsService.Instance.PresenceUpdated += (evt) =>
                {
                    // IPresenceUpdatedEvent: evt.ID (string), evt.Presence (Presence model)
                    Debug.Log($"[FriendLobbyService] PresenceUpdated: id={evt.ID}, availability={evt.Presence?.Availability}");
                    _ = RefreshFriendsAndRequestsAsync();
                };

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

        public async Task RefreshFriendsAndRequestsAsync()
        {
            if (_useMockMode)
            {
                OnFriendsUpdated?.Invoke();
                OnFriendRequestsUpdated?.Invoke();
                return;
            }

            if (AuthenticationService.Instance == null || !AuthenticationService.Instance.IsSignedIn)
            {
                // Không tự chuyển sang mock mode khi không thể refresh (ví dụ: đã logout), giữ nguyên trạng thái hiện tại.
                return;
            }

            try
            {
                // ✅ Force fetch từ server thay vì đọc local cache
                await FriendsService.Instance.ForceRelationshipsRefreshAsync();

                var friendsList = FriendsService.Instance.Friends;
                Debug.Log($"[FriendLobbyService] Refreshed {friendsList.Count} friends from server.");

                Friends.Clear();
                foreach (var friend in friendsList)
                {
                    var presence = friend.Member?.Presence;
                    bool isOnline = presence != null && presence.Availability == Availability.Online;
                    string name = friend.Member?.Profile?.Name ?? "Unknown";

                    Debug.Log($"[FriendLobbyService] Friend: {name} | Availability: {presence?.Availability} | IsOnline: {isOnline}");

                    if (presence != null && isOnline)
                    {
                        try
                        {
                            var activity = presence.GetActivity<LobbyActivity>();
                            if (activity != null && !string.IsNullOrEmpty(activity.LobbyCode) && activity.TargetFriendId == AuthenticationService.Instance.PlayerId)
                            {
                                string currentId = friend.Member?.Id ?? friend.Id;
                                if (CurrentInvite == null || CurrentInvite.LobbyCode != activity.LobbyCode || CurrentInvite.InviterId != currentId)
                                {
                                    CurrentInvite = new LobbyInvite
                                    {
                                        InviterName = activity.InvitingName,
                                        LobbyCode = activity.LobbyCode,
                                        InviterId = currentId
                                    };
                                    OnLobbyInviteReceived?.Invoke(CurrentInvite);
                                    Debug.Log($"[FriendLobbyService] Received Lobby Invite from {CurrentInvite.InviterName} ({CurrentInvite.InviterId}) for code {CurrentInvite.LobbyCode}");
                                }
                            }
                        }
                        catch
                        {
                            // Ignore casting errors if they are not playing/using different presence activity
                        }
                    }

                    Friends.Add(new FriendInfo
                    {
                        Id = friend.Member?.Id ?? friend.Id,
                        Username = name,
                        IsOnline = isOnline,
                        InLobby = false
                    });
                }

                var incoming = FriendsService.Instance.IncomingFriendRequests;
                IncomingRequests.Clear();
                foreach (var req in incoming)
                {
                    string name = req.Member?.Profile?.Name ?? "Unknown";
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
                Debug.LogError($"[FriendLobbyService] Error refreshing friends: {ex.Message}\n{ex.StackTrace}");
            }
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
            catch (Unity.Services.Friends.Exceptions.FriendsServiceException fsEx)
            {
                Debug.LogError($"[FriendLobbyService] Error sending request: {fsEx.Message} | StatusCode: {fsEx.StatusCode}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FriendLobbyService] Error sending request: {ex.GetType().Name} — {ex.Message}{(ex.InnerException != null ? " | Inner: " + ex.InnerException.Message : "")}");
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
                StitchUIController.Instance?.ShowErrorPopup("Lỗi Bạn Bè", $"Không thể chấp nhận yêu cầu kết bạn: {ex.Message}");
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
                StitchUIController.Instance?.ShowErrorPopup("Lỗi Bạn Bè", $"Không thể từ chối yêu cầu kết bạn: {ex.Message}");
                return false;
            }
        }

        private async Task ForceRefreshAsync()
        {
            try
            {
                // RefreshFriendsAndRequestsAsync đã gọi ForceRelationshipsRefreshAsync bên trong
                await RefreshFriendsAndRequestsAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Failed to force refresh: {ex.Message}");
            }
        }

        public async Task<bool> CreateLobbyAsync(string lobbyName, int maxPlayers = 4)
        {
            if (_useMockMode)
            {
                CurrentLobbyId = "mock_lobby_" + UnityEngine.Random.Range(1000, 9999);
                CurrentLobbyCode = UnityEngine.Random.Range(100000, 999999).ToString();
                IsHost = true;

                LobbyMembers.Clear();

                Debug.Log($"[Mock Lobby] Created lobby {lobbyName}. Code: {CurrentLobbyCode}");
                OnLobbyJoined?.Invoke(CurrentLobbyCode);
                OnLobbyMembersUpdated?.Invoke(LobbyMembers);
                return true;
            }

            try
            {
                string myName = AuthService.Instance != null ? AuthService.Instance.CachedUsername : "Player";
                var playerData = new Dictionary<string, PlayerDataObject>
                {
                    { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, myName) }
                };
                var options = new Unity.Services.Lobbies.CreateLobbyOptions { Player = new Player(data: playerData) };
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
                Debug.LogWarning($"[FriendLobbyService] Error creating lobby: {ex.Message}");
                StitchUIController.Instance?.ShowErrorPopup("Lỗi Phòng", $"Không thể tạo phòng: {ex.Message}");
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

                Debug.Log($"[Mock Lobby] Joined lobby code: {joinCode}");
                OnLobbyJoined?.Invoke(CurrentLobbyCode);
                OnLobbyMembersUpdated?.Invoke(LobbyMembers);
                return true;
            }

            try
            {
                string myName = AuthService.Instance != null ? AuthService.Instance.CachedUsername : "Player";
                var playerData = new Dictionary<string, PlayerDataObject>
                {
                    { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, myName) }
                };
                var options = new Unity.Services.Lobbies.JoinLobbyByCodeOptions { Player = new Player(data: playerData) };
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
                Debug.LogWarning($"[FriendLobbyService] Error joining lobby: {ex.Message}");
                StitchUIController.Instance?.ShowErrorPopup("Lỗi Phòng", $"Không thể tham gia phòng: {ex.Message}");
                return false;
            }
        }

        public async Task LeaveLobbyAsync()
        {
            if (string.IsNullOrEmpty(CurrentLobbyId)) return;

            if (EdgeParty.ConnectionManagement.MatchmakingManager.Instance != null && EdgeParty.ConnectionManagement.MatchmakingManager.Instance.IsMatchmaking)
            {
                EdgeParty.ConnectionManagement.MatchmakingManager.Instance.StopMatchmaking();
            }

            if (_useMockMode)
            {
                CurrentLobbyId = "";
                CurrentLobbyCode = "";
                IsHost = false;
                LobbyMembers.Clear();

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

                OnLobbyLeft?.Invoke();
                OnLobbyMembersUpdated?.Invoke(LobbyMembers);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Error leaving lobby: {ex.Message}");
                StitchUIController.Instance?.ShowErrorPopup("Lỗi Phòng", $"Không thể rời phòng: {ex.Message}");
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
                // Skip the local player. LobbyMembers will only track other members in the lobby.
                if (player.Id == AuthenticationService.Instance.PlayerId)
                {
                    continue;
                }

                string pName = player.Data != null && player.Data.ContainsKey("PlayerName") ? player.Data["PlayerName"].Value : player.Id;
                LobbyMembers.Add(pName);
            }

            if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
            {
                IsHost = _currentLobby.HostId == AuthenticationService.Instance.PlayerId;
            }

            OnLobbyMembersUpdated?.Invoke(LobbyMembers);
        }

        public async Task<bool> SendLobbyInviteAsync(string friendId, string friendUsername)
        {
            if (_useMockMode)
            {
                Debug.Log($"[Mock Lobby] Sending invite to mock friend {friendUsername}...");
                _ = DelayMockAccept(friendUsername);
                return true;
            }

            try
            {
                if (string.IsNullOrEmpty(CurrentLobbyCode))
                {
                    Debug.Log("[FriendLobbyService] Not in a lobby. Creating a new lobby before inviting...");
                    bool created = await CreateLobbyAsync($"{AuthService.Instance.CachedUsername}'s Room");
                    if (!created) return false;
                }

                string myName = AuthService.Instance != null ? AuthService.Instance.CachedUsername : "Player";
                string myId = AuthenticationService.Instance.PlayerId;

                var activity = new LobbyActivity
                {
                    LobbyCode = CurrentLobbyCode,
                    InvitingName = myName,
                    InvitingId = myId,
                    TargetFriendId = friendId,
                    RequestId = Guid.NewGuid().ToString()
                };

                Debug.Log($"[FriendLobbyService] Setting presence invite for friend {friendUsername} ({friendId}) to lobby {CurrentLobbyCode}");
                await FriendsService.Instance.SetPresenceAsync(Availability.Online, activity);

                _ = ResetPresenceAfterDelay(5000);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Error sending lobby invite: {ex.Message}");
                return false;
            }
        }

        private async Task DelayMockAccept(string friendUsername)
        {
            await Task.Delay(1500);
            SimulateFriendAcceptingInvite(friendUsername);
        }

        private async Task ResetPresenceAfterDelay(int delayMs)
        {
            await Task.Delay(delayMs);
            try
            {
                if (!_useMockMode && AuthenticationService.Instance.IsSignedIn)
                {
                    await FriendsService.Instance.SetPresenceAsync(Availability.Online, new EmptyActivity());
                    Debug.Log("[FriendLobbyService] Reset presence activity back to EmptyActivity.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Failed to reset presence: {ex.Message}");
            }
        }

        public void ClearInvite()
        {
            if (CurrentInvite != null)
            {
                CurrentInvite = null;
                OnLobbyInviteCleared?.Invoke();
            }
        }

        public void TriggerMockInvite(string friendName, string lobbyCode)
        {
            CurrentInvite = new LobbyInvite
            {
                InviterName = friendName,
                LobbyCode = lobbyCode,
                InviterId = "mock_" + friendName
            };
            OnLobbyInviteReceived?.Invoke(CurrentInvite);
            Debug.Log($"[Mock Lobby] Triggered mock lobby invite from {friendName} with code {lobbyCode}");
        }

        [System.Serializable]
        public struct MatchmakingPlayer
        {
            public string id;
            public string username;
        }

        public List<MatchmakingPlayer> GetMatchmakingPlayers()
        {
            List<MatchmakingPlayer> list = new List<MatchmakingPlayer>();
            
            // Add local player
            string myName = AuthService.Instance != null ? AuthService.Instance.CachedUsername : "Player";
            string myId = AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : "local_player";
            list.Add(new MatchmakingPlayer { id = myId, username = myName });

            if (_useMockMode)
            {
                foreach (var memberName in LobbyMembers)
                {
                    if (memberName != "You" && memberName != myName)
                    {
                        list.Add(new MatchmakingPlayer { id = "mock_" + memberName, username = memberName });
                    }
                }
            }
            else if (_currentLobby != null)
            {
                list.Clear(); // Clear and populate from lobby
                foreach (var player in _currentLobby.Players)
                {
                    string pName = player.Data != null && player.Data.ContainsKey("PlayerName") ? player.Data["PlayerName"].Value : player.Id;
                    list.Add(new MatchmakingPlayer { id = player.Id, username = pName });
                }
            }
            
            return list;
        }

        public async Task UpdateLobbyStatusAsync(string status, string matchIP = "", string matchPort = "", string matchId = "")
        {
            if (_useMockMode || string.IsNullOrEmpty(CurrentLobbyId) || !IsHost) return;

            try
            {
                var data = new Dictionary<string, DataObject>
                {
                    { "MatchStatus", new DataObject(DataObject.VisibilityOptions.Member, status) },
                    { "MatchIP", new DataObject(DataObject.VisibilityOptions.Member, matchIP) },
                    { "MatchPort", new DataObject(DataObject.VisibilityOptions.Member, matchPort) },
                    { "MatchId", new DataObject(DataObject.VisibilityOptions.Member, matchId) }
                };

                var options = new Unity.Services.Lobbies.UpdateLobbyOptions { Data = data };
                _currentLobby = await Unity.Services.Lobbies.LobbyService.Instance.UpdateLobbyAsync(CurrentLobbyId, options);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendLobbyService] Failed to update lobby status: {ex.Message}");
            }
        }

        private void CheckMatchmakingStatusFromLobbyData()
        {
            if (_currentLobby == null || _currentLobby.Data == null) return;

            string status = _currentLobby.Data.ContainsKey("MatchStatus") ? _currentLobby.Data["MatchStatus"].Value : "";
            string ip = _currentLobby.Data.ContainsKey("MatchIP") ? _currentLobby.Data["MatchIP"].Value : "";
            string portStr = _currentLobby.Data.ContainsKey("MatchPort") ? _currentLobby.Data["MatchPort"].Value : "";
            string matchId = _currentLobby.Data.ContainsKey("MatchId") ? _currentLobby.Data["MatchId"].Value : "";

            if (status == "Matchmaking")
            {
                if (EdgeParty.ConnectionManagement.MatchmakingManager.Instance != null)
                {
                    EdgeParty.ConnectionManagement.MatchmakingManager.Instance.SetGuestMatchmaking(true);
                }
            }
            else if (status == "Matched" && !string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(portStr))
            {
                if (ushort.TryParse(portStr, out ushort port))
                {
                    var mm = EdgeParty.ConnectionManagement.MatchmakingManager.Instance;
                    // Skip nếu đã connected (NetworkManager đang chạy client/server)
                    bool alreadyConnected = Unity.Netcode.NetworkManager.Singleton != null &&
                                           (Unity.Netcode.NetworkManager.Singleton.IsClient ||
                                            Unity.Netcode.NetworkManager.Singleton.IsServer);
                    if (mm != null && !alreadyConnected)
                    {
                        // Tắt trạng thái guest matchmaking trước khi connect
                        mm.SetGuestMatchmaking(false);
                        if (!string.IsNullOrEmpty(matchId) && EdgeParty.Infrastructure.VoiceChat.VoiceChatManager.Instance != null)
                        {
                            Debug.Log($"[FriendLobbyService] Guest joining Vivox channel: {matchId}");
                            _ = EdgeParty.Infrastructure.VoiceChat.VoiceChatManager.Instance.JoinMatchChannel(matchId);
                        }
                        Debug.Log($"[FriendLobbyService] Guest connecting to matched server: {ip}:{port}");
                        mm.ConnectToServer(ip, port);
                    }
                }
            }
            else
            {
                if (EdgeParty.ConnectionManagement.MatchmakingManager.Instance != null)
                {
                    EdgeParty.ConnectionManagement.MatchmakingManager.Instance.SetGuestMatchmaking(false);
                }
            }
        }

        public void ClearSocialAndLobbyStateLocal()
        {
            CurrentLobbyId = "";
            CurrentLobbyCode = "";
            IsHost = false;
            _currentLobby = null;
            LobbyMembers.Clear();
            Friends.Clear();
            IncomingRequests.Clear();
            CurrentInvite = null;
            _isInitialized = false;
        }

        public void ClearSocialAndLobbyState()
        {
            ClearSocialAndLobbyStateLocal();
            
            OnFriendsUpdated?.Invoke();
            OnFriendRequestsUpdated?.Invoke();
            OnLobbyMembersUpdated?.Invoke(LobbyMembers);
            OnLobbyLeft?.Invoke();
            OnLobbyInviteCleared?.Invoke();
        }
    }

    public class EmptyActivity { }

    [System.Serializable]
    public class LobbyActivity
    {
        public string LobbyCode;
        public string InvitingName;
        public string InvitingId;
        public string TargetFriendId;
        public string RequestId;
    }

    [System.Serializable]
    public class LobbyInvite
    {
        public string InviterName;
        public string LobbyCode;
        public string InviterId;
    }
}
