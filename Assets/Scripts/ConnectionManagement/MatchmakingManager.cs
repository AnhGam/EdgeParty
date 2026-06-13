using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Edgegap;
using Edgegap.Matchmaking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.CloudCode;
using UnityEngine;
using EdgeParty.Infrastructure.VoiceChat;

namespace EdgeParty.ConnectionManagement
{
    public class MatchmakingManager : MonoBehaviour
    {
        public static MatchmakingManager Instance { get; private set; }

        public event Action OnMatchmakingStarted;
        public event Action OnMatchmakingCancelled;
        public event Action OnMatchmakingSucceeded;
        public event Action<string> OnMatchmakingFailed;

        [Header("EdgeGap Configuration")]
        public string baseUrl = "https://om-ffn6c6ga6e.edgegap.net";
        public string portName = "gameport";
        public float pollingInterval = 3f;

        private Edgegap.Ping pingService;

        private string currentTicketId;
        private bool isPolling = false;
        private string _lastMatchId = "";  // Lưu ticket ID để dùng làm Vivox channel name

        public bool IsMatchmaking => isPolling;
        public float MatchmakingStartTime { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            pingService = new Edgegap.Ping(this);
        }

        public void StartMatchmakingFlow()
        {
            Debug.Log("[MatchmakingManager] Starting matchmaking flow via UGS Cloud Code...");
            MatchmakingStartTime = Time.time;
            OnMatchmakingStarted?.Invoke();
            _ = GetBeaconsAndPingAsync();
        }

        private async Task GetBeaconsAndPingAsync()
        {
            try
            {
                Debug.Log("[MatchmakingManager] Requesting beacons from UGS Cloud Code...");
                var jsonResponse = await CloudCodeService.Instance.CallEndpointAsync(
                    "GetBeacons", new Dictionary<string, object>()
                );

                var beaconsResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<BeaconsResponseDTO>(jsonResponse);

                if (beaconsResponse == null || beaconsResponse.Beacons == null || beaconsResponse.Beacons.Length == 0)
                {
                    Debug.LogWarning("[MatchmakingManager] No beacons received. Starting without beacons...");
                    StartMatchmakingWithPings(new Dictionary<string, float>());
                    return;
                }

                Debug.Log($"[MatchmakingManager] Measuring round trip time for {beaconsResponse.Beacons.Length} beacons...");
                StartCoroutine(MeasureBeaconsRoundTripTimeRoutine(
                    beaconsResponse.Beacons,
                    (Dictionary<string, float> pings) =>
                    {
                        Debug.Log("[MatchmakingManager] Pings measured successfully.");
                        StartMatchmakingWithPings(pings);
                    }
                ));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchmakingManager] Failed to get beacons: {ex.Message}. Attempting empty pings...");
                StartMatchmakingWithPings(new Dictionary<string, float>());
            }
        }

        private IEnumerator MeasureBeaconsRoundTripTimeRoutine(
            BeaconDTO[] beacons,
            Action<Dictionary<string, float>> onCompleteDelegate,
            int requests = 3
        )
        {
            Dictionary<string, float> results = new Dictionary<string, float>();
            int completedCount = 0;

            foreach (var beacon in beacons)
            {
                StartCoroutine(
                    pingService.GetAverageRoundTripTime(
                        beacon.PublicIP,
                        (double ping) =>
                        {
                            string city = beacon.Location?.City ?? "Unknown";
                            float pingVal = (float)ping;

                            if (pingVal <= 0) pingVal = 999f;

                            if (results.ContainsKey(city))
                            {
                                results[city] = Mathf.Min(results[city], pingVal);
                            }
                            else
                            {
                                results[city] = pingVal;
                            }
                            completedCount++;
                        },
                        requests
                    )
                );
            }

            yield return new WaitUntil(() => completedCount == beacons.Length);
            onCompleteDelegate(results);
        }

        private void FilterPingsByRegion(Dictionary<string, float> pings)
        {
            int regionIndex = PlayerPrefs.GetInt("RegionIndex", 0);
            if (regionIndex == 0) return; // Auto (Best Ping)

            List<string> keysToRemove = new List<string>();
            foreach (var kvp in pings)
            {
                string key = kvp.Key.ToLower();
                bool matchesRegion = regionIndex switch
                {
                    1 => key.Contains("us") || key.Contains("na") || key.Contains("america") || key.Contains("virginia") || key.Contains("california") || key.Contains("chicago") || key.Contains("toronto") || key.Contains("montreal") || key.Contains("dallas") || key.Contains("jose") || key.Contains("ashburn") || key.Contains("hillsboro") || key.Contains("seattle") || key.Contains("miami") || key.Contains("atlanta"), // North America
                    2 => key.Contains("eu") || key.Contains("europe") || key.Contains("frankfurt") || key.Contains("london") || key.Contains("paris") || key.Contains("amsterdam") || key.Contains("dublin") || key.Contains("madrid") || key.Contains("stockholm") || key.Contains("warsaw"), // Europe
                    3 => key.Contains("as") || key.Contains("asia") || key.Contains("ap") || key.Contains("sg") || key.Contains("singapore") || key.Contains("tokyo") || key.Contains("japan") || key.Contains("seoul") || key.Contains("mumbai") || key.Contains("hongkong") || key.Contains("sydney") || key.Contains("australia"), // Asia / Oceania
                    _ => true
                };

                if (!matchesRegion)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            // Only apply the filter if it doesn't remove all available beacons
            if (keysToRemove.Count < pings.Count)
            {
                foreach (var key in keysToRemove)
                {
                    pings.Remove(key);
                }
                Debug.Log($"[MatchmakingManager] Matchmaking region filter applied. Remaining beacons: {pings.Count}");
            }
            else
            {
                Debug.LogWarning($"[MatchmakingManager] Region filter would remove all {pings.Count} beacons. Reverting to all available beacons to avoid ticket validation failure.");
            }
        }

        private async void StartMatchmakingWithPings(Dictionary<string, float> pings)
        {
            try
            {
                FilterPingsByRegion(pings);

                var args = new Dictionary<string, object> { { "pings", pings } };
                var jsonResponse = await CloudCodeService.Instance.CallEndpointAsync(
                    "StartMatchmaking", args
                );

                var ticket = Newtonsoft.Json.JsonConvert.DeserializeObject<TicketResponseDTO>(jsonResponse);

                if (ticket == null || string.IsNullOrEmpty(ticket.ID))
                {
                    Debug.LogWarning("[MatchmakingManager] Failed to create matchmaking ticket: invalid response.");
                    OnMatchmakingFailed?.Invoke("Không thể tạo ticket matchmaking, vui lòng thử lại");
                    return;
                }

                currentTicketId = ticket.ID;
                _lastMatchId = ticket.ID;
                Debug.Log($"[MatchmakingManager] Matchmaking ticket created! ID: {currentTicketId}, Status: {ticket.Status}");

                if (ticket.Status == "HOST_ASSIGNED" && ticket.Assignment != null)
                {
                    HandleAssignment(ticket.Assignment);
                    return;
                }

                StartPolling();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchmakingManager] StartMatchmaking failed: {ex.Message}");
                isPolling = false;
                currentTicketId = null;
                OnMatchmakingFailed?.Invoke("Lỗi kết nối mạng, vui lòng kiểm tra lại internet");
            }
        }

        private void StartPolling()
        {
            if (isPolling) return;
            isPolling = true;
            StartCoroutine(PollMatchmakingStatusRoutine());
        }

        private IEnumerator PollMatchmakingStatusRoutine()
        {
            int consecutiveErrors = 0;
            while (isPolling)
            {
                yield return new WaitForSeconds(pollingInterval);

                var args = new Dictionary<string, object>
                {
                    { "ticketId", currentTicketId },
                    { "cancel", false }
                };

                var task = CloudCodeService.Instance.CallEndpointAsync("StartMatchmaking", args);

                yield return new WaitUntil(() => task.IsCompleted);

                if (task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion)
                {
                    consecutiveErrors++;
                    string errMsg = task.Exception?.InnerException?.Message ?? task.Exception?.Message ?? "Task cancelled or failed";
                    Debug.LogWarning($"[MatchmakingManager] Polling error ({consecutiveErrors}/3): {errMsg}");
                    if (consecutiveErrors >= 3)
                    {
                        isPolling = false;
                        OnMatchmakingFailed?.Invoke("Lỗi kết nối mạng, vui lòng kiểm tra lại internet");
                        yield break;
                    }
                    continue;
                }
                consecutiveErrors = 0;

                TicketResponseDTO ticket = null;
                try
                {
                    ticket = Newtonsoft.Json.JsonConvert.DeserializeObject<TicketResponseDTO>(task.Result);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MatchmakingManager] Failed to deserialize ticket response: {ex.Message}");
                    isPolling = false;
                    OnMatchmakingFailed?.Invoke("Lỗi dữ liệu phản hồi từ máy chủ");
                    yield break;
                }
                if (ticket == null) continue;

                Debug.Log($"[MatchmakingManager] Polling ticket status: {ticket.Status}");

                if (ticket.Status == "HOST_ASSIGNED" && ticket.Assignment != null)
                {
                    isPolling = false;
                    HandleAssignment(ticket.Assignment);
                }
                else if (ticket.Status == "CANCELLED")
                {
                    Debug.LogWarning("[MatchmakingManager] Matchmaking ticket was cancelled.");
                    isPolling = false;
                    OnMatchmakingCancelled?.Invoke();
                }
            }
        }

        private void HandleAssignment(DeploymentDTO assignment)
        {
            if (assignment == null)
            {
                Debug.LogError("[MatchmakingManager] HandleAssignment called with null assignment!");
                return;
            }

            if (!assignment.Ports.ContainsKey(portName))
            {
                Debug.LogError($"[MatchmakingManager] Port '{portName}' not found in assignment. Available ports: {string.Join(", ", assignment.Ports.Keys)}");
                OnMatchmakingFailed?.Invoke("Lỗi cấu hình server port");
                return;
            }

            var portInfo = assignment.Ports[portName];

            // Prefer Public IP to avoid DNS propagation delays on newly deployed containers, fall back to FQDN
            string host = !string.IsNullOrEmpty(assignment.PublicIP) ? assignment.PublicIP : assignment.Fqdn;

            // External port is an integer in Ticket API
            if (!ushort.TryParse(portInfo.External, out ushort port))
            {
                Debug.LogError($"[MatchmakingManager] Could not parse external port: {portInfo.External}");
                OnMatchmakingFailed?.Invoke("Lỗi cấu hình server port");
                return;
            }

            Debug.Log($"[MatchmakingManager] Match Found! Connecting to {host}:{port}");
            OnMatchmakingSucceeded?.Invoke();

            // Tham gia Vivox channel cho match (dùng ticket ID là channel name chung)
            if (!string.IsNullOrEmpty(_lastMatchId) && VoiceChatManager.Instance != null)
            {
                _ = VoiceChatManager.Instance.JoinMatchChannel(_lastMatchId);
                Debug.Log($"[MatchmakingManager] Joining Vivox channel for match: {_lastMatchId}");
            }

            ConnectToServer(host, port);
        }

        private void ConnectToServer(string host, ushort port)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                networkManager = FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
            }

#if UNITY_EDITOR
            if (networkManager == null)
            {
                Debug.Log("[MatchmakingManager] NetworkManager.Singleton is null. Attempting to auto-load Assets/Resources/NetworkManager.prefab in Editor...");
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/NetworkManager.prefab");
                if (prefab != null)
                {
                    var go = Instantiate(prefab);
                    go.name = "NetworkManager (Auto-Injected)";
                    networkManager = NetworkManager.Singleton;
                }
            }
#endif

            if (networkManager == null)
            {
                Debug.LogError("[MatchmakingManager] NetworkManager not found! Make sure NetworkManager exists in the scene or is configured as a prefab.");
                OnMatchmakingFailed?.Invoke("Không tìm thấy NetworkManager trong game");
                return;
            }


            var utp = (UnityTransport)networkManager.NetworkConfig.NetworkTransport;
            utp.SetConnectionData(host, port);

            // Allow up to 10 minutes for on-demand container initialization (e.g. cold start/image pull)
            utp.MaxConnectAttempts = 600;
            utp.ConnectTimeoutMS = 1000;

            Debug.Log($"[MatchmakingManager] Netcode connecting to {host}:{port}... (Will retry up to 600 seconds for container startup)");
            try
            {
                if (!networkManager.StartClient())
                {
                    Debug.LogError("[MatchmakingManager] StartClient returned false!");
                    OnMatchmakingFailed?.Invoke("Không thể khởi động kết nối client");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MatchmakingManager] Exception during StartClient: {ex.Message}");
                OnMatchmakingFailed?.Invoke($"Lỗi khởi động kết nối: {ex.Message}");
            }
        }

        public async void StopMatchmaking()
        {
            Debug.Log("[MatchmakingManager] Stopping Matchmaking.");
            isPolling = false;
            OnMatchmakingCancelled?.Invoke();

            if (string.IsNullOrEmpty(currentTicketId)) return;

            try
            {
                var args = new Dictionary<string, object>
                {
                    { "ticketId", currentTicketId },
                    { "cancel", true }
                };

                await CloudCodeService.Instance.CallEndpointAsync("StartMatchmaking", args);
                Debug.Log("[MatchmakingManager] Matchmaking ticket successfully cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchmakingManager] Error stopping matchmaking: {ex.Message}");
            }
            finally
            {
                currentTicketId = null;
            }
        }

        private void OnApplicationQuit()
        {
            StopMatchmaking();
        }
    }
}
