using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NameplateUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text playerNameText;
    public Image micImage;
    [Tooltip("Background panel của nameplate để đổi màu theo team")]
    public Image backgroundImage;

    // Team colors
    public static readonly Color Team1Color = new Color(0.9f, 0.2f, 0.2f, 0.85f); // Đỏ
    public static readonly Color Team2Color = new Color(0.2f, 0.5f, 0.9f, 0.85f); // Xanh
    public static readonly Color DefaultColor = new Color(0.1f, 0.1f, 0.1f, 0.7f);

    private void Awake()
    {
        // Fix khẩn cấp: Xóa EventSystem nếu vô tình bị dính vào NameplateUI prefab
        // (Đây chính là nguyên nhân làm liệt input và hỏng nhân vật)
        Transform esObj = transform.Find("EventSystem");
        if (esObj != null) Destroy(esObj.gameObject);

        // Tự động tìm cục Background nếu bạn quên kéo vào (để chống lỗi Type Mismatch)
        if (backgroundImage == null)
        {
            Transform bgTransform = transform.Find("Background");
            if (bgTransform != null)
            {
                backgroundImage = bgTransform.GetComponent<Image>();
            }
            else
            {
                // Nếu tên khác, tìm đại cái Image nào đó nằm ngay dưới gốc
                foreach (Transform child in transform)
                {
                    Image img = child.GetComponent<Image>();
                    if (img != null && img.gameObject.name.Contains("Background"))
                    {
                        backgroundImage = img;
                        break;
                    }
                }
            }
        }
    }

    public void SetPlayerName(string playerName)
    {
        if (playerNameText != null)
            playerNameText.text = playerName;
    }

    /// <summary>Đổi màu nameplate theo team. teamID = 1 (Đỏ) hoặc 2 (Xanh).</summary>
    public void SetTeamColor(int teamID)
    {
        if (backgroundImage == null) return;
        backgroundImage.color = teamID switch
        {
            1 => Team1Color,
            2 => Team2Color,
            _ => DefaultColor
        };
    }

    public void SetMicLevel(float value)
    {
        if (micImage == null) return;

        value = Mathf.Clamp(value, 0f, 100f);
        float t = value / 100f;

        Color minColor = new Color(0f, 0.25f, 0f);
        Color maxColor = new Color(0f, 1f, 0f);

        micImage.color = Color.Lerp(minColor, maxColor, t);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            // Luôn xoay Nameplate về phía Camera để chữ luôn dựng đứng và nhìn rõ (Billboard)
            transform.forward = cam.transform.forward;
        }
    }
}