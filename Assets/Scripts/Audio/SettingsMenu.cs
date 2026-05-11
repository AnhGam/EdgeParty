using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using TMPro;

public class SettingsMenu : MonoBehaviour
{
    [Header("Audio Mixer")]
    public AudioMixer mixer;

    [Header("Volume Sliders")]
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;

    [Header("Mute Toggles (tuỳ chọn)")]
    public Toggle muteAllToggle;

    [Header("Resolution")]
    public TMP_Dropdown resolutionDropdown; // Cần TextMeshPro, hoặc đổi thành Dropdown thường

    [Header("Settings Panel")]
    // Kéo Panel Settings vào đây từ Inspector
    public GameObject settingsPanel;

    private Resolution[] _resolutions;
    private bool _isMuted = false;

    // ===================== KHỞI TẠO =====================

    void Start()
    {
        // Load giá trị volume đã lưu
        float master = PlayerPrefs.GetFloat("MasterVolume", 1f);
        float music = PlayerPrefs.GetFloat("MusicVolume", 1f);
        float sfx = PlayerPrefs.GetFloat("SFXVolume", 1f);

        masterSlider.value = master;
        musicSlider.value = music;
        sfxSlider.value = sfx;

        // Áp dụng ngay khi load
        ApplyMasterVolume(master);
        ApplyMusicVolume(music);
        ApplySFXVolume(sfx);

        // Kết nối slider với hàm
        masterSlider.onValueChanged.AddListener(SetMasterVolume);
        musicSlider.onValueChanged.AddListener(SetMusicVolume);
        sfxSlider.onValueChanged.AddListener(SetSFXVolume);

        // Mute toggle
        _isMuted = PlayerPrefs.GetInt("Muted", 0) == 1;
        if (muteAllToggle != null)
        {
            muteAllToggle.isOn = _isMuted;
            muteAllToggle.onValueChanged.AddListener(SetMuteAll);
            ApplyMuteAll(_isMuted);
        }

        // Load độ phân giải
        SetupResolutionDropdown();

        // Đảm bảo panel ẩn khi bắt đầu
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    // ===================== VOLUME =====================

    public void SetMasterVolume(float value)
    {
        ApplyMasterVolume(value);
        PlayerPrefs.SetFloat("MasterVolume", value);
    }

    public void SetMusicVolume(float value)
    {
        ApplyMusicVolume(value);
        PlayerPrefs.SetFloat("MusicVolume", value);
    }

    public void SetSFXVolume(float value)
    {
        ApplySFXVolume(value);
        PlayerPrefs.SetFloat("SFXVolume", value);
    }

    //Clamp giá trị tránh Log10(0) = -Infinity gây crash
    private void ApplyMasterVolume(float value)
    {
        float db = Mathf.Log10(Mathf.Max(value, 0.0001f)) * 20f;
        mixer.SetFloat("MasterVolume", db);
    }

    private void ApplyMusicVolume(float value)
    {
        float db = Mathf.Log10(Mathf.Max(value, 0.0001f)) * 20f;
        mixer.SetFloat("MusicVolume", db);
    }

    private void ApplySFXVolume(float value)
    {
        float db = Mathf.Log10(Mathf.Max(value, 0.0001f)) * 20f;
        mixer.SetFloat("SFXVolume", db);
    }

    // hai phần dưới đây có thể thêm vào sau
    // ===================== MUTE ALL =====================

    public void SetMuteAll(bool muted)
    {
        _isMuted = muted;
        ApplyMuteAll(muted);
        PlayerPrefs.SetInt("Muted", muted ? 1 : 0);
    }

    private void ApplyMuteAll(bool muted)
    {
        // -80dB = im lặng hoàn toàn; 0dB = bình thường
        mixer.SetFloat("MasterVolume", muted ? -80f : Mathf.Log10(Mathf.Max(masterSlider.value, 0.0001f)) * 20f);
    }

    // ===================== RESOLUTION =====================

    private void SetupResolutionDropdown()
    {
        if (resolutionDropdown == null) return;

        _resolutions = Screen.resolutions;
        resolutionDropdown.ClearOptions();

        int savedIndex = PlayerPrefs.GetInt("ResolutionIndex", 0);
        int currentIndex = 0;
        var options = new System.Collections.Generic.List<string>();

        for (int i = 0; i < _resolutions.Length; i++)
        {
            string option = $"{_resolutions[i].width} x {_resolutions[i].height} @ {_resolutions[i].refreshRateRatio.numerator}Hz";
            options.Add(option);

            if (_resolutions[i].width == Screen.currentResolution.width &&
                _resolutions[i].height == Screen.currentResolution.height)
                currentIndex = i;
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = PlayerPrefs.HasKey("ResolutionIndex") ? savedIndex : currentIndex;
        resolutionDropdown.RefreshShownValue();
        resolutionDropdown.onValueChanged.AddListener(SetResolution);
    }

    public void SetResolution(int index)
    {
        if (_resolutions == null || index >= _resolutions.Length) return;
        Resolution r = _resolutions[index];
        Screen.SetResolution(r.width, r.height, Screen.fullScreen);
        PlayerPrefs.SetInt("ResolutionIndex", index);
    }

    // ===================== MỞ / ĐÓNG MENU =====================

    /// <summary>Gọi từ nút Settings Button trong UI</summary>
    public void OpenSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    /// <summary>Gọi từ nút Close / Back trong Settings Panel</summary>
    public void CloseSettings()
    {
        if (settingsPanel != null)
            settingsPanel.SetActive(false);

        PlayerPrefs.Save(); // Lưu hẳn xuống disk
    }

    /// <summary>Toggle mở/đóng (dùng cho phím Esc hoặc nút toggle)</summary>
    public void ToggleSettings()
    {
        if (settingsPanel == null) return;
        bool isOpen = settingsPanel.activeSelf;
        if (isOpen) CloseSettings();
        else OpenSettings();
    }
}