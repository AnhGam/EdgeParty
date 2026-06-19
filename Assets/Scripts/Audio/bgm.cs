using UnityEngine;

/// <summary>
/// Gắn script này vào bất kỳ GameObject nào trong scene (ví dụ cùng object với AudioManager,
/// hoặc 1 object riêng tên "BGMTrigger"). Kéo file nhạc nền vào field bgmClip.
/// Khi scene load, nhạc sẽ tự động phát qua AudioManager (đã bị ảnh hưởng bởi Music slider).
/// </summary>
public class BGMTrigger : MonoBehaviour
{
    [Tooltip("Kéo file nhạc nền (BGM) vào đây")]
    public AudioClip bgmClip;

    private void Start()
    {
        if (bgmClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlayBGM(bgmClip);
    }
}