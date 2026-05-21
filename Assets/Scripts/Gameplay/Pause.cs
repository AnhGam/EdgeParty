using UnityEngine;
using UnityEngine.InputSystem;

public class PauseMenu : MonoBehaviour
{
    [Header("References")]
    // Kéo SettingsMenu component vào đây
    public SettingsMenu settingsMenu;

    private bool _isPaused = false;

    void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePause();
        }
    }

    public void TogglePause()
    {
        _isPaused = !_isPaused;

        if (_isPaused)
            OpenPause();
        else
            ClosePause();
    }

    public void OpenPause()
    {
        _isPaused = true;
        settingsMenu?.OpenSettings();
    }

    public void ClosePause()
    {
        _isPaused = false;
        settingsMenu?.CloseSettings();
    }
}