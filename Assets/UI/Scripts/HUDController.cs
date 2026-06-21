using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

public class HUDController : MonoBehaviour
{
    public static HUDController Instance { get; private set; }

    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Texture2D customCursor;
    
    private VisualElement root;
    private VisualElement dynamicBar;
    
    private Label redScoreLabel;
    private Label blueScoreLabel;
    private Label matchTimerLabel;
    
    private Button soundButton;
    private Button settingsButton;
    private Button exitButton;

    // Detailed settings panel references
    private VisualElement settingsPanel;
    private VisualElement musicFill;
    private VisualElement musicThumb;
    private VisualElement sfxFill;
    private VisualElement sfxThumb;

    private bool isSoundOn = true;

    private SettingsMenu settingsMenu;
    private VisualElement pingFpsCounter;
    private Label pingFpsLabel;
    private float fpsTimer = 0f;
    private int fpsCount = 0;
    private float lastFps = 0f;

    // ── Guide panel (DynamicBar) state ──────────────────────────────────────
    private bool _guideIsCountdown  = false;  // True while showing countdown
    private bool _guideIsWaiting    = true;   // True while waiting for players
    private Coroutine _guideHideCoroutine;
    private Label _staminaValueLabel;
    private VisualElement _staminaFill;

    // ── Team colors (match HUDStyle.uss) ───────────────────────────────────
    private const string Team1Hex = "#FF5252";
    private const string Team2Hex = "#40C4FF";
    private static readonly Color RedTeamColor = new Color(1f, 82f/255f, 82f/255f);
    private static readonly Color BlueTeamColor = new Color(64f/255f, 196f/255f, 1f);

    public bool IsSettingsOpen => (settingsMenu != null && settingsMenu.IsOpen) || (settingsPanel != null && settingsPanel.style.display == DisplayStyle.Flex);

    private void Start()
    {
        float musicVol = PlayerPrefs.GetFloat("MusicVolume", 1f);
        float sfxVol = PlayerPrefs.GetFloat("SFXVolume", 1f);

        InitSliderVisuals(musicFill, musicThumb, musicVol);
        InitSliderVisuals(sfxFill, sfxThumb, sfxVol);

        ApplyVolumes(musicVol, sfxVol);
        StartCoroutine(SubscribeToGameManagerEvents());
    }

    private IEnumerator SubscribeToGameManagerEvents()
    {
        while (ForestGameManager.Instance == null)
            yield return null;

        var gm = ForestGameManager.Instance;
        gm.OnWaitingForPlayers += OnWaiting;
        gm.OnCountdown         += OnCountdown;
        gm.OnMatchStarted      += OnMatchStarted;
        gm.OnCrownSpawned      += OnCrownSpawned;
        gm.OnScoreAdded        += OnScoreAdded;
    }

    void OnEnable()
    {
        Instance = this;
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) return;

        root = uiDocument.rootVisualElement;
        if (root == null) return;
        
        redScoreLabel = root.Q<Label>("RedScore");
        blueScoreLabel = root.Q<Label>("BlueScore");
        matchTimerLabel = root.Q<Label>("MatchTimer");
        
        soundButton = root.Q<Button>("SoundButton");
        settingsButton = root.Q<Button>("SettingsButton");
        exitButton = root.Q<Button>("ExitButton");
        
        dynamicBar = root.Q<VisualElement>("DynamicBar");
        
        // Hook up main bar events
        soundButton?.RegisterCallback<ClickEvent>(evt => ToggleSound());
        settingsButton?.RegisterCallback<ClickEvent>(evt => OpenSettings());
        exitButton?.RegisterCallback<ClickEvent>(evt => QuitGame());

        settingsPanel = root.Q<VisualElement>("SettingsPanel");
        if (settingsPanel != null)
        {
            settingsMenu = GetComponent<SettingsMenu>();
            if (settingsMenu == null)
            {
                settingsMenu = gameObject.AddComponent<SettingsMenu>();
            }

            settingsMenu.InitializeWithRoot(settingsPanel);
            
            // Subscribe to settings menu open/close events to keep cursor synced
            settingsMenu.OnOpenSettingsEvent -= SyncCursorOnOpen;
            settingsMenu.OnOpenSettingsEvent += SyncCursorOnOpen;

            settingsMenu.OnCloseSettingsEvent -= SyncCursorOnClose;
            settingsMenu.OnCloseSettingsEvent += SyncCursorOnClose;
        }
        
        // Start with waiting message instead of hardcoded BOOST
        ShowWaitingMessage(0);
        SetCursorState(false);

        // ── Build stamina bar inside the existing BottomArea ───────────────
        BuildStaminaBar();
    }

    // ─── Stamina Bar (UIToolkit, bottom-left) ──────────────────────────────

    private void BuildStaminaBar()
    {
        if (root == null) return;

        // Container anchored bottom-left (same style as .bottom-center-area)
        var container = new VisualElement();
        container.name = "StaminaBarContainer";
        container.style.position   = Position.Absolute;
        container.style.bottom     = 20;
        container.style.left       = 20;
        container.style.minWidth   = 180;

        // Label row: "STAMINA" text matching instruction-label style
        var labelRow = new VisualElement();
        labelRow.style.flexDirection = FlexDirection.Row;
        labelRow.style.alignItems    = Align.Center;
        labelRow.style.marginBottom  = 4;

        var staminaTitle = new Label("STAMINA");
        staminaTitle.AddToClassList("instruction-label");
        staminaTitle.style.marginRight = 0;
        staminaTitle.style.fontSize    = 11;
        labelRow.Add(staminaTitle);

        _staminaValueLabel = new Label("100");
        _staminaValueLabel.style.color      = new StyleColor(new UnityEngine.Color(1f, 0.85f, 0.15f)); // gold
        _staminaValueLabel.style.fontSize   = 10;
        _staminaValueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        _staminaValueLabel.style.marginLeft = 6;
        labelRow.Add(_staminaValueLabel);

        container.Add(labelRow);

        // Track bar (same dark bg as .dynamic-bar)
        var track = new VisualElement();
        track.name = "StaminaTrack";
        track.style.width           = 160;
        track.style.height          = 6;
        track.style.backgroundColor = new StyleColor(new UnityEngine.Color(0.1f, 0.1f, 0.1f, 0.7f));
        track.style.borderTopLeftRadius     = 3;
        track.style.borderTopRightRadius    = 3;
        track.style.borderBottomLeftRadius  = 3;
        track.style.borderBottomRightRadius = 3;

        // Fill bar
        _staminaFill = new VisualElement();
        _staminaFill.name = "StaminaFill";
        _staminaFill.style.width            = Length.Percent(100);
        _staminaFill.style.height           = Length.Percent(100);
        _staminaFill.style.backgroundColor  = new StyleColor(new UnityEngine.Color(1f, 0.85f, 0.15f)); // gold — matches --color-primary
        _staminaFill.style.borderTopLeftRadius     = 3;
        _staminaFill.style.borderTopRightRadius    = 3;
        _staminaFill.style.borderBottomLeftRadius  = 3;
        _staminaFill.style.borderBottomRightRadius = 3;

        track.Add(_staminaFill);
        container.Add(track);

        root.Add(container);
        StartCoroutine(PollStamina());
    }

    private IEnumerator PollStamina()
    {
        EdgeParty.Gameplay.Character.PlayerStats localStats = null;

        while (localStats == null)
        {
            yield return new WaitForSeconds(0.3f);
            var all = Object.FindObjectsByType<EdgeParty.Gameplay.Character.PlayerStats>(FindObjectsSortMode.None);
            foreach (var ps in all)
            {
                var nb = ps.GetComponent<Unity.Netcode.NetworkBehaviour>();
                if (nb != null && nb.IsLocalPlayer) { localStats = ps; break; }
            }
        }

        while (localStats != null)
        {
            float pct  = localStats.StaminaPct;
            int   val  = Mathf.RoundToInt(pct * localStats.maxStamina);

            if (_staminaFill  != null) _staminaFill.style.width  = Length.Percent(pct * 100f);
            if (_staminaValueLabel != null) _staminaValueLabel.text = val.ToString();

            yield return new WaitForSeconds(0.05f);
        }
    }

    // ─── Guide Panel Event Handlers ────────────────────────────────────────

    private void OnWaiting(int currentPlayers)
    {
        if (currentPlayers == -1) return;  // waiting over, countdown will come next
        if (_guideIsCountdown) return;
        _guideIsWaiting = true;
        ShowWaitingMessage(currentPlayers);
    }

    private void OnCountdown(int secondsLeft)
    {
        _guideIsWaiting    = false;
        _guideIsCountdown  = secondsLeft > 0;
        CancelGuideHide();

        if (secondsLeft > 0)
        {
            // Show the countdown number as the instruction label, no key-caps
            SetGuideText(secondsLeft.ToString());
        }
        else
        {
            // GO!
            SetGuideText("GO!");
            ScheduleGuideHide(1.2f);
        }
    }

    private void OnMatchStarted()
    {
        _guideIsCountdown = false;
        _guideIsWaiting   = false;
        // Do not show any fallback guide when match starts; let GO! fade out naturally.
    }

    private void OnCrownSpawned()
    {
        if (_guideIsCountdown || _guideIsWaiting) return;
        SetGuideText("Vương miện đã xuất hiện!", new Color(1f, 0.85f, 0.15f));
        ScheduleGuideHide(3f);
    }

    private void OnScoreAdded(int teamID, int points)
    {
        if (_guideIsCountdown || _guideIsWaiting) return;
        string teamName = teamID == 1 ? "Đội Đỏ" : "Đội Xanh";
        Color teamColor = teamID == 1 ? RedTeamColor : BlueTeamColor;
        SetScoreGuideText(teamName, teamColor, $"+{points}");
        ScheduleGuideHide(2f);
    }

    private void ShowWaitingMessage(int current)
    {
        int needed = ForestGameManager.Instance != null ? ForestGameManager.Instance.requiredPlayers : 2;
        SetGuideText($"Đang chờ người chơi... {current}/{needed}");
    }

    private void SetGuideVisible(bool visible)
    {
        if (dynamicBar != null)
        {
            dynamicBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Sets the DynamicBar to show a single centered text message (no key-caps).</summary>
    private void SetGuideText(string message, Color? textColor = null)
    {
        if (dynamicBar == null) return;
        SetGuideVisible(true);
        dynamicBar.Clear();
        var lbl = new Label(message);
        lbl.AddToClassList("instruction-label");
        lbl.style.fontSize   = 16;
        lbl.style.marginLeft = 0;
        lbl.style.marginRight= 0;
        if (textColor.HasValue)
        {
            lbl.style.color = new StyleColor(textColor.Value);
        }
        dynamicBar.Add(lbl);
    }

    private void SetScoreGuideText(string teamName, Color teamColor, string scoreText)
    {
        if (dynamicBar == null) return;
        SetGuideVisible(true);
        dynamicBar.Clear();

        var teamLbl = new Label(teamName);
        teamLbl.AddToClassList("instruction-label");
        teamLbl.style.fontSize = 16;
        teamLbl.style.marginLeft = 0;
        teamLbl.style.marginRight = 0;
        teamLbl.style.color = new StyleColor(teamColor);
        dynamicBar.Add(teamLbl);

        var scoreLbl = new Label(scoreText);
        scoreLbl.AddToClassList("instruction-label");
        scoreLbl.style.fontSize = 16;
        scoreLbl.style.marginLeft = 4;
        scoreLbl.style.marginRight = 0;
        scoreLbl.style.color = new StyleColor(Color.white);
        dynamicBar.Add(scoreLbl);
    }

    private void ScheduleGuideHide(float delay)
    {
        CancelGuideHide();
        _guideHideCoroutine = StartCoroutine(HideGuideAfter(delay));
    }

    private void CancelGuideHide()
    {
        if (_guideHideCoroutine != null) { StopCoroutine(_guideHideCoroutine); _guideHideCoroutine = null; }
    }

    private IEnumerator HideGuideAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        SetGuideVisible(false);
        _guideHideCoroutine = null;
    }



    void Update()
    {
        if (ForestGameManager.Instance != null)
        {
            if (redScoreLabel != null)
                redScoreLabel.text = ForestGameManager.Instance.Team1Score.Value.ToString("00");
            if (blueScoreLabel != null)
                blueScoreLabel.text = ForestGameManager.Instance.Team2Score.Value.ToString("00");
            if (matchTimerLabel != null)
            {
                float timeRemaining = ForestGameManager.Instance.MatchTimeRemaining.Value;
                int minutes = Mathf.FloorToInt(timeRemaining / 60f);
                int seconds = Mathf.FloorToInt(timeRemaining % 60f);
                matchTimerLabel.text = $"{minutes:00}:{seconds:00}";
            }
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (settingsMenu != null)
            {
                if (settingsMenu.IsOpen)
                    settingsMenu.HandleBackButton();
                else
                    settingsMenu.OpenSettings();
            }
            else if (settingsPanel != null)
            {
                if (settingsPanel.style.display == DisplayStyle.Flex)
                    CloseSettings();
                else
                    OpenSettings();
            }
        }

        // Enforce cursor state every frame while Alt is held or settings are open
        bool isSettingsOpen = (settingsMenu != null && settingsMenu.IsOpen) || (settingsPanel != null && settingsPanel.style.display == DisplayStyle.Flex);
        bool shouldShowCursor = (Keyboard.current != null && Keyboard.current.leftAltKey.isPressed) || isSettingsOpen;

        if (shouldShowCursor)
        {
            if (!UnityEngine.Cursor.visible || UnityEngine.Cursor.lockState != CursorLockMode.None)
            {
                SetCursorState(true);
            }
        }
        else
        {
            if (UnityEngine.Cursor.visible || UnityEngine.Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorState(false);
            }
        }

        // Update Ping and FPS counter if enabled
        bool showPingFps = PlayerPrefs.GetInt("ShowPingFPS", 0) == 1;
        if (showPingFps)
        {
            if (pingFpsCounter == null && root != null)
            {
                pingFpsCounter = new VisualElement();
                pingFpsCounter.AddToClassList("ping-fps-counter");
                
                pingFpsLabel = new Label("FPS: -- | Ping: -- ms");
                pingFpsLabel.AddToClassList("ping-fps-text");
                pingFpsCounter.Add(pingFpsLabel);
                
                root.Add(pingFpsCounter);
            }

            if (pingFpsCounter != null)
            {
                pingFpsCounter.style.display = DisplayStyle.Flex;

                fpsTimer += Time.unscaledDeltaTime;
                fpsCount++;
                if (fpsTimer >= 0.5f)
                {
                    lastFps = fpsCount / fpsTimer;
                    fpsTimer = 0f;
                    fpsCount = 0;
                }

                int pingVal = 0;
                if (Unity.Netcode.NetworkManager.Singleton != null && 
                    Unity.Netcode.NetworkManager.Singleton.IsClient && 
                    Unity.Netcode.NetworkManager.Singleton.IsConnectedClient)
                {
                    var transport = Unity.Netcode.NetworkManager.Singleton.NetworkConfig.NetworkTransport;
                    if (transport is Unity.Netcode.Transports.UTP.UnityTransport utp)
                    {
                        pingVal = (int)utp.GetCurrentRtt(Unity.Netcode.NetworkManager.ServerClientId);
                    }
                }

                if (pingFpsLabel != null)
                {
                    string pingText = pingVal > 0 ? $"{pingVal} ms" : "-- ms";
                    pingFpsLabel.text = $"FPS: {Mathf.RoundToInt(lastFps)} | Ping: {pingText}";
                }
            }
        }
        else
        {
            if (pingFpsCounter != null)
            {
                pingFpsCounter.style.display = DisplayStyle.None;
            }
        }
    }

    private void SetCursorState(bool visible)
    {
        UnityEngine.Cursor.visible = visible;
        UnityEngine.Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        
        if (visible && customCursor != null)
        {
            UnityEngine.Cursor.SetCursor(customCursor, Vector2.zero, CursorMode.Auto);
        }
        else
        {
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }

    public void UpdateInstructionBar(string actionName, string[] keys)
    {
        if (dynamicBar == null) return;
        
        dynamicBar.Clear();
        
        foreach (var key in keys)
        {
            var keyCap = new Label(key);
            keyCap.AddToClassList("key-cap");
            dynamicBar.Add(keyCap);
        }
        
        var actionLabel = new Label(actionName.ToUpper());
        actionLabel.AddToClassList("instruction-label");
        dynamicBar.Add(actionLabel);
    }

    private void ToggleSound()
    {
        isSoundOn = !isSoundOn;
        AudioListener.volume = isSoundOn ? 1f : 0f;

        var icon = soundButton?.Q<VisualElement>("SoundIcon");
        if (icon != null)
        {
            if (isSoundOn)
            {
                icon.RemoveFromClassList("icon-sound-off");
                icon.AddToClassList("icon-sound");
            }
            else
            {
                icon.RemoveFromClassList("icon-sound");
                icon.AddToClassList("icon-sound-off");
            }
        }
        
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        Debug.Log("Toggle Sound: " + (isSoundOn ? "ON" : "OFF"));
    }

    private void SyncCursorOnOpen() => SetCursorState(true);
    private void SyncCursorOnClose() => SetCursorState(false);

    private void OpenSettings()
    {
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        if (settingsMenu != null)
        {
            settingsMenu.OpenSettings();
        }
        else if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.Flex;
            SetCursorState(true);
        }
    }

    private void CloseSettings()
    {
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        if (settingsMenu != null)
        {
            settingsMenu.CloseSettings();
        }
        else if (settingsPanel != null)
        {
            settingsPanel.style.display = DisplayStyle.None;
            SetCursorState(false);
        }
    }

    private void QuitGame()
    {
        EdgeParty.Core.AudioManager.Instance?.PlaySFX("Click");
        Debug.Log("Quit Game clicked - Returning to MainMenu");

        // Unlock and show cursor
        UnityEngine.Cursor.visible = true;
        UnityEngine.Cursor.lockState = CursorLockMode.None;

        var networkManager = Unity.Netcode.NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        EdgeParty.UI.StitchUIController.ReturnedFromGame = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    private void RegisterSliderEvents(VisualElement track, VisualElement fill, VisualElement thumb, string prefKey, System.Action<float> onVolumeChanged)
    {
        if (track == null || fill == null || thumb == null) return;

        System.Action<float> updateSlider = (localX) =>
        {
            float width = track.resolvedStyle.width;
            if (width <= 0f) width = 300f; // fallback width
            float ratio = Mathf.Clamp01(localX / width);
            
            thumb.style.left = Length.Percent(ratio * 100f);
            fill.style.width = Length.Percent(ratio * 100f);
            
            onVolumeChanged?.Invoke(ratio);
            PlayerPrefs.SetFloat(prefKey, ratio);
            PlayerPrefs.Save();
        };

        track.RegisterCallback<PointerDownEvent>(evt =>
        {
            track.CapturePointer(evt.pointerId);
            updateSlider(evt.localPosition.x);
            evt.StopPropagation();
        });

        track.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (track.HasPointerCapture(evt.pointerId))
            {
                updateSlider(evt.localPosition.x);
                evt.StopPropagation();
            }
        });

        track.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (track.HasPointerCapture(evt.pointerId))
            {
                track.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }
        });
    }

    private void InitSliderVisuals(VisualElement fill, VisualElement thumb, float volume)
    {
        if (fill == null || thumb == null) return;
        thumb.style.left = Length.Percent(volume * 100f);
        fill.style.width = Length.Percent(volume * 100f);
    }

    private void ApplyVolumes(float musicVol, float sfxVol)
    {
        if (EdgeParty.Core.AudioManager.Instance != null)
        {
            EdgeParty.Core.AudioManager.Instance.SetMusicVolume(musicVol);
            EdgeParty.Core.AudioManager.Instance.SetSFXVolume(sfxVol);
        }
        if (AudioManager.Instance != null)
        {
            if (AudioManager.Instance.bgmSource != null)
                AudioManager.Instance.bgmSource.volume = musicVol;
            if (AudioManager.Instance.sfxSource != null)
                AudioManager.Instance.sfxSource.volume = sfxVol;
        }
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
        if (ForestGameManager.Instance != null)
        {
            ForestGameManager.Instance.OnWaitingForPlayers -= OnWaiting;
            ForestGameManager.Instance.OnCountdown         -= OnCountdown;
            ForestGameManager.Instance.OnMatchStarted      -= OnMatchStarted;
            ForestGameManager.Instance.OnCrownSpawned      -= OnCrownSpawned;
            ForestGameManager.Instance.OnScoreAdded        -= OnScoreAdded;
        }
    }
}
