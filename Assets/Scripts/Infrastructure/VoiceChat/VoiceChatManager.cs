using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using System.Threading.Tasks;
using System;

public class VoiceChatManager : MonoBehaviour
{
    public static VoiceChatManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private string channelName = "MainLobby";
    
    private bool isInitialized = false;

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
        await InitializeServices();
    }

    private async Task InitializeServices()
    {
        try
        {
            // 1. Initialize UGS
            await UnityServices.InitializeAsync();
            Debug.Log("UGS Initialized");
            
            // 2. Authenticate
            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                    Debug.Log($"Signed in anonymously as: {AuthenticationService.Instance.PlayerId}");
                }
            }

            // 3. Initialize Vivox
            await VivoxService.Instance.InitializeAsync();
            
            // 4. Login to Vivox
            await VivoxService.Instance.LoginAsync();
            
            isInitialized = true;
            Debug.Log("Vivox Initialized and Logged In");
            
            // 5. Join a default positional channel
            await JoinChannel(channelName);
        }
        catch (Exception e)
        {
            Debug.LogError($"Vivox Initialization Error: {e.Message}");
        }
    }

    public async Task JoinChannel(string name)
    {
        if (!isInitialized) return;

        try
        {
            // Joining a positional channel for 3D audio
            await VivoxService.Instance.JoinPositionalChannelAsync(name, ChatCapability.TextAndAudio, new Channel3DProperties());
            Debug.Log($"Joined Positional Voice Channel: {name}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error joining Vivox channel: {e.Message}");
        }
    }

    public async Task LeaveChannel(string name)
    {
        if (!isInitialized) return;
        await VivoxService.Instance.LeaveChannelAsync(name);
    }

    // Update position for 3D audio
    public void UpdateParticipantPosition(GameObject participant, string channel)
    {
        if (!isInitialized || !VivoxService.Instance.IsLoggedIn) return;
        
        // This maps the Unity world position to Vivox 3D space
        VivoxService.Instance.Set3DPosition(participant, channel);
    }
}
