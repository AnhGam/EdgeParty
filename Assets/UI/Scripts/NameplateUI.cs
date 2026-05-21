using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NameplateUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text playerNameText;
    public Image micImage;

    public void SetPlayerName(string playerName)
    {
        if (playerNameText != null)
            playerNameText.text = playerName;
    }

    public void SetMicLevel(float value)
    {
        if (micImage == null) return;

        value = Mathf.Clamp(value, 0f, 100f);
        float t = value / 100f;

        Color minColor = new Color(0f, 0.25f, 0f); // Xanh lá đậm
        Color maxColor = new Color(0f, 1f, 0f);    // Xanh lá tươi

        micImage.color = Color.Lerp(minColor, maxColor, t); // Đổi màu dựa trên giá trị 0-100
    }
}