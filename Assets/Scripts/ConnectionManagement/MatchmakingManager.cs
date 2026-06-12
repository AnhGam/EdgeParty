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

namespace EdgeParty.ConnectionManagement
{
    public class MatchmakingManager : MonoBehaviour
    {
        public static MatchmakingManager Instance { get; private set; }

        public event Action OnMatchmakingStarted;
        public event Action OnMatchmakingCancelled;
        public event Action OnMatchmakingSucceeded;

        [Header("EdgeGap Configuration")]
        public string baseUrl = "https://om-ffn6c6ga6e.edgegap.net";
        public string portName = "gameport";
        public float pollingInterval = 1.5f;

        // We use GroupClient strictly as a helper for pinging beacons (client-side latency check)
        private GroupClient<SimpleGroupUpRequestDTO, LatenciesAttributesDTO> pingHelperClient;
        
        private string currentGroupId;
        private string currentMemberId;
        private bool isPolling = false;

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
            
            // Initialize the helper client with dummy parameters (safe since it only does client-side pinging)
            pingHelperClient = new GroupClient<SimpleGroupUpRequestDTO, LatenciesAttributesDTO>(
                this, "http://dummy", "dummy"
            );
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
                // 1. Get Beacons list from UGS Cloud Code
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

                // 2. Measure ping latencies locally
                Debug.Log($"[MatchmakingManager] Measuring round trip time for {beaconsResponse.Beacons.Length} beacons...");
                pingHelperClient.MeasureBeaconsRoundTripTime(
                    beaconsResponse.Beacons,
                    (Dictionary<string, float> pings) =>
                    {
                        Debug.Log("[MatchmakingManager] Pings measured successfully.");
                        StartMatchmakingWithPings(pings);
                    }
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MatchmakingManager] Failed to get beacons: {ex.Message}. Attempting empty pings...");
                StartMatchmakingWithPings(new Dictionary<string, float>());
            }
        }

        private async void StartMatchmakingWithPings(Dictionary<string, float> pings)
        {
            try
            {
                Debug.Log("[MatchmakingManager] Sending pings to UGS Cloud Code to start matchmaking...");
                var args = new Dictionary<string, object> { { "pings", pings } };
                var jsonResponse = await CloudCodeService.Instance.CallEndpointAsync(
                    "StartMatchmaking", args
                );
                
                var response = Newtonsoft.Json.JsonConvert.DeserializeObject<GroupUpResponseDTO>(jsonResponse);

                if (response == null || string.IsNullOrEmpty(response.GroupID))
                {
                    Debug.LogError("[MatchmakingManager] Failed to create matchmaking group.");
                    return;
                }

                currentGroupId = response.GroupID;
                currentMemberId = response.MemberID;
                
                Debug.Log($"[MatchmakingManager] Matchmaking ticket created! Group ID: {currentGroupId}, Member ID: {currentMemberId}");
                
                // 3. Start polling matchmaking status
                StartPolling();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MatchmakingManager] StartMatchmaking failed: {ex.Message}");
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
            var args = new Dictionary<string, object>
            {
                { "groupId", currentGroupId },
                { "memberId", currentMemberId },
                { "cancel", false }
            };

            while (isPolling)
            {
                yield return new WaitForSeconds(pollingInterval);

                var task = CloudCodeService.Instance.CallEndpointAsync(
                    "StartMatchmaking", args
                );

                yield return new WaitUntil(() => task.IsCompleted);

                if (task.Exception != null)
                {
                    Debug.LogWarning($"[MatchmakingManager] Polling error: {task.Exception.InnerException?.Message ?? task.Exception.Message}");
                    continue;
                }

                var response = Newtonsoft.Json.JsonConvert.DeserializeObject<GroupUpResponseDTO>(task.Result);
                if (response == null) continue;

                Debug.Log($"[MatchmakingManager] Polling group status: {response.Status}");

                if (response.Status == "HOST_ASSIGNED" || response.Status == "ASSIGNED")
                {
                    if (response.Assignment != null && response.Assignment.Ports.ContainsKey(portName))
                    {
                        var portInfo = response.Assignment.Ports[portName];
                        string ip = response.Assignment.PublicIP;
                        ushort.TryParse(portInfo.External, out ushort port);

                        Debug.Log($"[MatchmakingManager] Match Found! Connecting to {ip}:{port}");
                        OnMatchmakingSucceeded?.Invoke();
                        ConnectToServer(ip, port);
                        
                        isPolling = false;
                    }
                }
                else if (response.Status == "CANCELLED")
                {
                    Debug.LogWarning("[MatchmakingManager] Matchmaking ticket was cancelled.");
                    isPolling = false;
                    OnMatchmakingCancelled?.Invoke();
                }
            }
        }

        private void ConnectToServer(string ip, ushort port)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("[MatchmakingManager] NetworkManager not found!");
                return;
            }

            var utp = (UnityTransport)networkManager.NetworkConfig.NetworkTransport;
            utp.SetConnectionData(ip, port);
            
            Debug.Log($"[MatchmakingManager] Netcode connecting to {ip}:{port}...");
            networkManager.StartClient();
        }

        public async void StopMatchmaking()
        {
            Debug.Log("[MatchmakingManager] Stopping Matchmaking.");
            isPolling = false;
            OnMatchmakingCancelled?.Invoke();

            if (string.IsNullOrEmpty(currentGroupId) || string.IsNullOrEmpty(currentMemberId)) return;

            try
            {
                var args = new Dictionary<string, object>
                {
                    { "groupId", currentGroupId },
                    { "memberId", currentMemberId },
                    { "cancel", true }
                };

                await CloudCodeService.Instance.CallEndpointAsync(
                    "StartMatchmaking", args
                );
                
                Debug.Log("[MatchmakingManager] Matchmaking successfully cancelled.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MatchmakingManager] Error stopping matchmaking: {ex.Message}");
            }
            finally
            {
                currentGroupId = null;
                currentMemberId = null;
            }
        }

        private void OnApplicationQuit()
        {
            StopMatchmaking();
        }
    }
}
