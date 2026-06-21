using Unity.Netcode;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using EdgeParty.Gameplay.Character;

public class ForestGameManager : NetworkBehaviour
{
    public static ForestGameManager Instance;

    // ─── Network Variables ────────────────────────────────────────────────
    public NetworkVariable<int> Team1Score = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> Team2Score = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> MatchTimeRemaining = new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> WinnerTeam = new NetworkVariable<int>(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server); // 0=none, -1=draw
    public NetworkVariable<bool> MatchEnded = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<bool> MatchStarted = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ─── Config ───────────────────────────────────────────────────────────
    [Header("Match Settings")]
    [Tooltip("Thời gian mỗi trận (giây)")]
    public float matchDuration = 300f;
    [Tooltip("Điểm để thắng sớm (0 = không giới hạn điểm, chỉ tính giờ)")]
    public int scoreToWin = 0;
    [Tooltip("Đếm ngược trước khi bắt đầu (giây)")]
    public float countdownDuration = 15f;
    [Tooltip("Giây chờ sau khi match kết thúc trước khi reset")]
    public float delayAfterEnd = 8f;

    [Header("Player Ready Gate")]
    [Tooltip("Số người chơi phải vào trước khi bắt đầu đếm ngược")]
    public int requiredPlayers = 2;
    [Tooltip("Giây tối đa chờ đủ người trước khi tự bắt đầu")]
    public float maxWaitForPlayersSeconds = 30f;

    // ─── Events (Client-side) ────────────────────────────────────────────
    public event Action<int> OnCountdown;              // mỗi giây countdown
    public event Action OnMatchStarted;
    public event Action<int> OnMatchEnded;             // winnerTeam (1/2/-1=draw)
    public event Action OnScoreChanged;
    public event Action OnCrownSpawned;                // Crown xuất hiện
    public event Action<int, int> OnScoreAdded;        // teamID, points
    public event Action<int> OnWaitingForPlayers;      // số player hiện tại đang chờ

    // ─── State ───────────────────────────────────────────────────────────
    public static bool SpawnTestDummiesOnStart = false;
    private bool _matchRunning = false;
    public float MatchStartTime { get; private set; }

    // Countdown beep clip loaded at runtime
    private AudioClip _countdownBeepClip;

    // URL of a short free countdown beep (CC0 license from freesound.org via direct CDN)
    private const string CountdownBeepUrl =
        "https://assets.mixkit.co/active_storage/sfx/2568/2568-preview.mp3";

    // ─── Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        countdownDuration = 15f; // Force 15 seconds countdown preparation time
        maxWaitForPlayersSeconds = 300f; // Force 5 minutes timeout to prevent starting with 0 players during cold start

        // Initialize with generated beep immediately so there is always a sound
        _countdownBeepClip = GenerateBeepClip(880f, 0.15f);

        if (GetComponent<ItemSpawner>() == null)
        {
            gameObject.AddComponent<ItemSpawner>();
            Debug.Log("[ForestGameManager] Dynamically attached ItemSpawner component!");
        }
    }

    public override void OnNetworkSpawn()
    {
        // Subscribe local events để HUD tự cập nhật
        Team1Score.OnValueChanged += (_, __) => OnScoreChanged?.Invoke();
        Team2Score.OnValueChanged += (_, __) => OnScoreChanged?.Invoke();
        WinnerTeam.OnValueChanged += (_, winner) => { if (MatchEnded.Value) OnMatchEnded?.Invoke(winner); };
        MatchEnded.OnValueChanged += (_, ended) => { if (ended) OnMatchEnded?.Invoke(WinnerTeam.Value); };
        MatchStarted.OnValueChanged += (_, started) => { if (started) OnMatchStarted?.Invoke(); };

        // Pre-load countdown beep on all clients
        StartCoroutine(LoadCountdownBeepCoroutine());

        if (IsServer)
        {
            StartCoroutine(MatchFlowCoroutine());
            if (SpawnTestDummiesOnStart)
            {
                SpawnTestDummies();
            }
        }
        else
        {
            // Client: khi bị disconnect (server tắt sau match) → về MainMenu
            NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnectedFromServer;
        }
    }

    private IEnumerator LoadCountdownBeepCoroutine()
    {
        using (var uwr = UnityWebRequestMultimedia.GetAudioClip(CountdownBeepUrl, AudioType.MPEG))
        {
            yield return uwr.SendWebRequest();
            if (uwr.result == UnityWebRequest.Result.Success)
            {
                _countdownBeepClip = DownloadHandlerAudioClip.GetContent(uwr);
                Debug.Log("[ForestGameManager] Countdown beep loaded from CDN.");
            }
            else
            {
                // Fallback: generate a simple 880 Hz beep programmatically
                _countdownBeepClip = GenerateBeepClip(880f, 0.15f);
                Debug.LogWarning($"[ForestGameManager] CDN beep failed ({uwr.error}). Using generated beep.");
            }
        }
    }

    /// <summary>Generates a simple sine-wave beep clip as a fallback.</summary>
    private static AudioClip GenerateBeepClip(float frequency, float duration)
    {
        int sampleRate = 44100;
        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] data = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            // Envelope: quick attack, fast decay
            float envelope = Mathf.Clamp01(t / 0.01f) * Mathf.Clamp01(1f - (t - duration * 0.5f) / (duration * 0.5f));
            data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.7f;
        }
        var clip = AudioClip.Create("CountdownBeep", sampleCount, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ─── Server-side Match Flow ───────────────────────────────────────────

    private IEnumerator MatchFlowCoroutine()
    {
        // Reset state
        Team1Score.Value = 0;
        Team2Score.Value = 0;
        MatchTimeRemaining.Value = matchDuration;
        WinnerTeam.Value = 0;
        MatchEnded.Value = false;
        MatchStarted.Value = false;

        // Phase 0: Wait for required players
        float waitTimer = 0f;
        while (waitTimer < maxWaitForPlayersSeconds)
        {
            int connected = NetworkManager.ConnectedClientsList.Count;
            // Notify clients of current player count (negative = waiting state)
            NotifyWaitingClientRpc(connected, requiredPlayers);
            if (connected >= requiredPlayers) break;
            yield return new WaitForSeconds(1f);
            waitTimer += 1f;
        }

        // Add 3 seconds delay to allow all players to finish loading the scene before starting the countdown
        yield return new WaitForSeconds(3f);

        // Signal waiting is over (value -1 = all ready)
        NotifyWaitingClientRpc(-1, requiredPlayers);

        // Phase 1: Countdown
        float countdown = countdownDuration;
        while (countdown > 0f)
        {
            int secondsLeft = Mathf.CeilToInt(countdown);
            SendCountdownClientRpc(secondsLeft);
            yield return new WaitForSeconds(1f);
            countdown -= 1f;
        }
        SendCountdownClientRpc(0);

        // Phase 2: Match running
        MatchStartTime = Time.time;
        MatchStarted.Value = true;
        _matchRunning = true;
        Debug.Log("[ForestGameManager] Match started!");

        while (_matchRunning && !MatchEnded.Value)
        {
            MatchTimeRemaining.Value = Mathf.Max(0f, matchDuration - (Time.time - MatchStartTime));

            // Check win by score
            if (scoreToWin > 0)
            {
                if (Team1Score.Value >= scoreToWin) { EndMatch(1); yield break; }
                if (Team2Score.Value >= scoreToWin) { EndMatch(2); yield break; }
            }

            // Check time up
            if (MatchTimeRemaining.Value <= 0f)
            {
                int winner;
                if (Team1Score.Value > Team2Score.Value) winner = 1;
                else if (Team2Score.Value > Team1Score.Value) winner = 2;
                else winner = -1; // hòa
                EndMatch(winner);
                yield break;
            }

            yield return null;
        }
    }

    private void EndMatch(int winnerTeam)
    {
        if (MatchEnded.Value) return;
        _matchRunning = false;
        WinnerTeam.Value = winnerTeam;
        MatchEnded.Value = true;
        MatchTimeRemaining.Value = 0f;
        string result = winnerTeam == -1 ? "HÒA" : $"Team {winnerTeam} thắng";
        Debug.Log($"[ForestGameManager] Match ended. Result: {result} | Score: {Team1Score.Value}-{Team2Score.Value}");

        StartCoroutine(PostMatchCoroutine());
    }

    private void OnDisconnectedFromServer(ulong clientId)
    {
        // Client bị disconnect (server tắt) → cleanup và về MainMenu
        Debug.Log("[ForestGameManager] Disconnected from server after match. Returning to MainMenu...");
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnectedFromServer;
            NetworkManager.Singleton.Shutdown();
        }
        EdgeParty.UI.StitchUIController.ReturnedFromGame = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu", UnityEngine.SceneManagement.LoadSceneMode.Single);
    }

    private IEnumerator PostMatchCoroutine()
    {
        yield return new WaitForSeconds(delayAfterEnd);

        Debug.Log("[ForestGameManager] Post-match: shutting down server and terminating container.");

        // Ngắt kết nối tất cả clients trước (gửi disconnect message)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // Đợi thêm 1 giây để Netcode flush disconnect messages
        yield return new WaitForSecondsRealtime(1f);

        // Thoát process → Edgegap auto-terminates container
        Debug.Log("[ForestGameManager] Calling Application.Quit().");
        Application.Quit();
    }

    // ─── Cleanup ──────────────────────────────────────────────────────────

    public override void OnNetworkDespawn()
    {
        if (!IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnectedFromServer;
        }
    }

    // ─── Score Management ─────────────────────────────────────────────────


    public void AddScore(int teamID, int points)
    {
        if (!IsServer || MatchEnded.Value || !MatchStarted.Value) return;
        if (teamID == 1) Team1Score.Value += points;
        else if (teamID == 2) Team2Score.Value += points;
        Debug.Log($"[ForestGameManager] Team{teamID} +{points} | Score: {Team1Score.Value}-{Team2Score.Value}");
        // Notify all clients about the score event for the center guide popup
        NotifyScoreAddedClientRpc(teamID, points);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void AddScoreServerRpc(int teamID, int points)
    {
        AddScore(teamID, points);
    }

    // ─── Client RPCs ──────────────────────────────────────────────────────

    [Rpc(SendTo.ClientsAndHost)]
    private void SendCountdownClientRpc(int secondsLeft)
    {
        OnCountdown?.Invoke(secondsLeft);
        // Play beep on each countdown tick (not on 0 / GO!)
        if (secondsLeft > 0 && _countdownBeepClip != null)
            AudioManager.Instance?.PlaySFX(_countdownBeepClip);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyWaitingClientRpc(int currentPlayers, int needed)
    {
        // currentPlayers == -1 means waiting phase is over
        OnWaitingForPlayers?.Invoke(currentPlayers);
    }

    /// <summary>Called by ForestCrownPickup on spawn to announce the crown.</summary>
    [Rpc(SendTo.ClientsAndHost)]
    public void NotifyCrownSpawnedClientRpc()
    {
        OnCrownSpawned?.Invoke();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void NotifyScoreAddedClientRpc(int teamID, int points)
    {
        OnScoreAdded?.Invoke(teamID, points);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private void SpawnTestDummies()
    {
        if (NetworkManager.Singleton == null || !IsServer) return;

        var playerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        if (playerPrefab == null)
        {
            Debug.LogWarning("[ForestGameManager] Player prefab not found in NetworkConfig!");
            return;
        }

        // Spawn Dummy 1: Stand Still
        Vector3 pos1 = new Vector3(3f, 1.5f, 3f);
        var dummyGo1 = Instantiate(playerPrefab, pos1, Quaternion.identity);
        var dummy1 = dummyGo1.AddComponent<TestDummy>();
        dummy1.behavior = TestDummy.DummyBehavior.StandStill;
        
        var netObj1 = dummyGo1.GetComponent<NetworkObject>();
        if (netObj1 != null)
        {
            netObj1.Spawn();
        }

        // Spawn Dummy 2: Attack and Lock
        Vector3 pos2 = new Vector3(-3f, 1.5f, -3f);
        var dummyGo2 = Instantiate(playerPrefab, pos2, Quaternion.identity);
        var dummy2 = dummyGo2.AddComponent<TestDummy>();
        dummy2.behavior = TestDummy.DummyBehavior.AttackAndLock;
        dummy2.attackInterval = 1.8f; // Slightly longer than standard strike cooldown
        
        var netObj2 = dummyGo2.GetComponent<NetworkObject>();
        if (netObj2 != null)
        {
            netObj2.Spawn();
        }

        Debug.Log("[ForestGameManager] Spawned 2 test dummies successfully.");
    }

    public int GetScore(int teamID) => teamID == 1 ? Team1Score.Value : Team2Score.Value;

    public bool IsMatchActive => MatchStarted.Value && !MatchEnded.Value;
}