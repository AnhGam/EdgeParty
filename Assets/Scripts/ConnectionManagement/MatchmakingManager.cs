using System.Collections;
using System.Collections.Generic;
using Edgegap.Matchmaking;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.Networking;
using MyTicketsAttributes = Edgegap.Matchmaking.LatenciesAttributesDTO;
using MyTicketsRequestDTO = Edgegap.Matchmaking.SimpleTicketsRequestDTO;

namespace EdgeParty.ConnectionManagement
{
    public class MatchmakingManager : MonoBehaviour
    {
        public static MatchmakingManager Instance { get; private set; }

        [Header("EdgeGap Configuration")]
        public string baseUrl = "https://om-ffn6c6ga6e.edgegap.net";
        public string authToken = "06e1183c-e42f-4cb6-9824-d85a8c6b575a";
        public string clientVersion = "1.0.0";
        public string profileName = "simple-example";
        public string portName = "gameport";

        [Header("Matchmaking Settings")]
        public bool autoStartOnPlay = false;
        public float pollingInterval = 1f;

        public Client<MyTicketsRequestDTO, MyTicketsAttributes> MatchmakingClient;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            InitializeClient();
            
            if (autoStartOnPlay)
            {
                StartMatchmakingFlow();
            }
        }

        private void InitializeClient()
        {
            MatchmakingClient = new Client<MyTicketsRequestDTO, MyTicketsAttributes>(
                this,
                baseUrl,
                authToken,
                clientVersion,
                false, // SaveStateInPlayerPrefs (Đổi thành false để tránh tự kết nối lại)
                "EdgegapMatchmakingVersion",
                "EdgegapMatchmakingTicket",
                "EdgegapMatchmakingAssignment",
                3,    // Timeout
                pollingInterval,
                10,   // Max errors
                30f,  // Remove assignment seconds
                true, // Log tickets
                true, // Log assignments
                false // Log polling
            );

            MatchmakingClient.Initialize(OnMonitorUpdate, OnAssignmentUpdate);
            Debug.Log("[MatchmakingManager] EdgeGap Client Initialized.");
        }

        private void OnMonitorUpdate(Observable<MonitorResponseDTO> monitor, ObservableActionType action, string message)
        {
            if (action == ObservableActionType.Update)
            {
                if (message == "healthy")
                {
                    Debug.Log("[MatchmakingManager] Matchmaker service is healthy.");
                    MatchmakingClient.ResumeMatchmaking();
                }
                else
                {
                    Debug.LogWarning($"[MatchmakingManager] Matchmaker service status: {message}");
                }
            }
        }

        private void OnAssignmentUpdate(Observable<TicketResponseDTO> assignment, ObservableActionType action, string message)
        {
            if (assignment.Current == null) return;

            Debug.Log($"[MatchmakingManager] Assignment Update: {assignment.Current.Status} - {message}");

            if (assignment.Current.Status == "HOST_ASSIGNED" || assignment.Current.Status == "ASSIGNED")
            {
                if (assignment.Current.Assignment != null && assignment.Current.Assignment.Ports.ContainsKey(portName))
                {
                    var portInfo = assignment.Current.Assignment.Ports[portName];
                    string ip = assignment.Current.Assignment.PublicIP;
                    ushort.TryParse(portInfo.External, out ushort port);

                    Debug.Log($"[MatchmakingManager] Match Found! Connecting to {ip}:{port}");
                    ConnectToServer(ip, port);
                    
                    // Stop polling once we have our server
                    MatchmakingClient.StopMatchmaking();
                }
            }
        }

        public void StartMatchmakingFlow()
        {
            Debug.Log("[MatchmakingManager] Starting Matchmaking Flow (Beacons -> Ticket)...");
            
            MatchmakingClient.Beacons(
                (BeaconsResponseDTO beacons) =>
                {
                    Debug.Log($"[MatchmakingManager] Beacons received. Measuring pings to {beacons.Beacons.Length} locations...");
                    MatchmakingClient.MeasureBeaconsRoundTripTime(
                        beacons.Beacons,
                        (Dictionary<string, float> pings) =>
                        {
                            Debug.Log("[MatchmakingManager] Pings measured. Creating ticket...");
                            var request = new MyTicketsRequestDTO(pings) { Profile = profileName };
                            MatchmakingClient.StartMatchmaking(request);
                        }
                    );
                },
                (string error, UnityWebRequest request) =>
                {
                    Debug.LogError($"[MatchmakingManager] Beacon error: {error}. Trying without beacons...");
                    var requestWithoutBeacons = new MyTicketsRequestDTO(new Dictionary<string, float>()) { Profile = profileName };
                    MatchmakingClient.StartMatchmaking(requestWithoutBeacons);
                }
            );
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

        public void StopMatchmaking()
        {
            Debug.Log("[MatchmakingManager] Stopping Matchmaking.");
            if (MatchmakingClient != null)
            {
                MatchmakingClient.StopMatchmaking();
            }
        }

        private void OnApplicationQuit()
        {
            if (MatchmakingClient != null)
            {
                MatchmakingClient.StopMatchmaking();
            }
        }
    }
}
