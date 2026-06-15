using UnityEngine;

public class ForestEffectsRestorer : MonoBehaviour
{
    private void Start()
    {
        Debug.Log("[ForestEffectsRestorer] Restoring environment effects...");
        RestoreWaterEffects();
        RestoreRotations();
        RestoreParticleEffects();
    }

    private void RestoreWaterEffects()
    {
        // Find all renderers with names related to water
        Renderer[] allRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var rend in allRenderers)
        {
            if (rend == null || rend.gameObject == null) continue;
            string nameLower = rend.gameObject.name.ToLower();
            if (nameLower.Contains("river") || nameLower.Contains("water") || nameLower.Contains("waterfall") || nameLower.Contains("stream"))
            {
                if (rend.gameObject.GetComponent<MaterialMover>() == null)
                {
                    var mover = rend.gameObject.AddComponent<MaterialMover>();
                    // Adjust speed based on name
                    if (nameLower.Contains("waterfall"))
                    {
                        mover.scrollSpeed = 1.2f;
                    }
                    else
                    {
                        mover.scrollSpeed = 0.3f;
                    }
                    count++;
                }
            }
        }
        Debug.Log($"[ForestEffectsRestorer] Added MaterialMover to {count} water GameObjects.");
    }

    private void RestoreRotations()
    {
        // Find all GameObjects that might be collectibles or interactive elements
        Transform[] allTransforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var t in allTransforms)
        {
            if (t == null || t.gameObject == null) continue;
            string nameLower = t.gameObject.name.ToLower();
            if (nameLower.Contains("coin") || nameLower.Contains("gold") || nameLower.Contains("star") || nameLower.Contains("crystal") || nameLower.Contains("gem"))
            {
                if (t.gameObject.GetComponent<Rotator>() == null)
                {
                    var rotator = t.gameObject.AddComponent<Rotator>();
                    rotator.speed = 100f; // Spin speed
                    count++;
                }
            }
        }
        Debug.Log($"[ForestEffectsRestorer] Added Rotator to {count} collectible GameObjects.");
    }

    private void RestoreParticleEffects()
    {
        ParticleSystem[] allParticles = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        foreach (var ps in allParticles)
        {
            if (ps == null) continue;
            // Activate parent / GameObject if disabled
            if (!ps.gameObject.activeSelf)
            {
                ps.gameObject.SetActive(true);
            }
            
            if (!ps.isPlaying)
            {
                ps.Play();
                count++;
            }
        }
        Debug.Log($"[ForestEffectsRestorer] Verified and played {count} Particle Systems.");
    }
}
