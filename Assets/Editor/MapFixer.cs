#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility that fixes colliders on the Forest demo scene.
/// Run via: EdgeParty > Map > Fix Map Colliders
/// 
/// Rules applied:
///   - ForestGrass01, ForestGrass02         → Remove all MeshColliders (decorative)
///   - ForestTree* (all variants)            → Replace MeshCollider with CapsuleCollider (trunk)
///   - ForestLadder, ForestLadderExtra       → Replace MeshCollider with BoxCollider (smooth)
///   - ForestCoin                            → MeshCollider.isTrigger = true (pickup trigger)
/// </summary>
public static class MapFixer
{
    // ─── Name matching ─────────────────────────────────────────────────────────

    static readonly string[] GRASS_NAMES  = { "ForestGrass01", "ForestGrass02" };
    static readonly string[] TREE_PREFIXES = {
        "ForestTreePineTall",  "ForestTreePineShort",
        "ForestTreePineTall2", "ForestTreePineShort2",
        "ForestTreeAppleTall", "ForestTreeAppleShort",
        "ForestTreeDTall",     "ForestTreeDShort"
    };
    static readonly string[] LADDER_NAMES  = { "ForestLadder", "ForestLadderExtra" };
    static readonly string[] COIN_NAMES    = { "ForestCoin" };

    // ─── Entry Point ───────────────────────────────────────────────────────────

    [MenuItem("EdgeParty/Map/Fix Map Colliders")]
    public static void FixMapColliders()
    {
        int grassFixed = 0, treeFixed = 0, ladderFixed = 0, coinFixed = 0;

        var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var coinSound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/UI/click.ogg");

        foreach (var go in allObjects)
        {
            string n = go.name;

            // ── Grass: remove all mesh and box colliders ──────────────────────
            if (MatchesAny(n, GRASS_NAMES))
            {
                var cols = go.GetComponents<Collider>();
                foreach (var col in cols)
                {
                    Undo.DestroyObjectImmediate(col);
                    grassFixed++;
                }
                continue;
            }

            // ── Trees: replace MeshCollider with CapsuleCollider ──────────────
            if (StartsWithAny(n, TREE_PREFIXES))
            {
                var meshCols = go.GetComponents<MeshCollider>();
                if (meshCols.Length > 0)
                {
                    foreach (var col in meshCols)
                        Undo.DestroyObjectImmediate(col);

                    var cap = Undo.AddComponent<CapsuleCollider>(go);
                    // Estimate trunk size from bounds
                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        Bounds b = renderer.bounds;
                        // Use 1/4 of width as trunk radius, full height
                        float radius = Mathf.Min(b.extents.x, b.extents.z) * 0.35f;
                        radius = Mathf.Clamp(radius, 0.12f, 0.5f);
                        cap.radius = radius;
                        cap.height = b.size.y;
                        cap.center = go.transform.InverseTransformPoint(b.center);
                    }
                    else
                    {
                        cap.radius = 0.25f;
                        cap.height = 3f;
                        cap.center = new Vector3(0f, 1.5f, 0f);
                    }
                    treeFixed++;
                }
                continue;
            }

            // ── Ladders: replace MeshCollider with BoxCollider and tilt ───────
            if (MatchesAny(n, LADDER_NAMES))
            {
                var meshCols = go.GetComponents<MeshCollider>();
                if (meshCols.Length > 0)
                {
                    foreach (var col in meshCols)
                        Undo.DestroyObjectImmediate(col);

                    var box = Undo.AddComponent<BoxCollider>(go);
                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        Bounds b = renderer.bounds;
                        box.size   = go.transform.InverseTransformVector(b.size);
                        box.center = go.transform.InverseTransformPoint(b.center);
                    }
                    ladderFixed++;
                }

                // Tilt the ladder visually and physically to form a ramp
                if (go.transform.localRotation.eulerAngles.x == 0f && go.transform.localRotation.eulerAngles.z == 0f)
                {
                    Undo.RecordObject(go.transform, "Tilt Ladder to Ramp");
                    go.transform.localRotation = Quaternion.Euler(35f, go.transform.localRotation.eulerAngles.y, 0f);
                }
                continue;
            }

            // ── Coins: replace MeshCollider with SphereCollider trigger and setup script
            if (MatchesAny(n, COIN_NAMES))
            {
                var meshCol = go.GetComponent<MeshCollider>();
                if (meshCol != null)
                {
                    Undo.DestroyObjectImmediate(meshCol);
                }

                var sphereCol = go.GetComponent<SphereCollider>();
                if (sphereCol == null)
                {
                    sphereCol = Undo.AddComponent<SphereCollider>(go);
                }
                sphereCol.isTrigger = true;
                sphereCol.radius = 0.5f;

                var netObj = go.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj == null)
                {
                    Undo.AddComponent<Unity.Netcode.NetworkObject>(go);
                }

                var forestCoin = go.GetComponent<ForestCoin>();
                if (forestCoin == null)
                {
                    forestCoin = Undo.AddComponent<ForestCoin>(go);
                }
                if (forestCoin.collectSound == null)
                {
                    Undo.RecordObject(forestCoin, "Set Coin Sound");
                    forestCoin.collectSound = coinSound;
                }

                coinFixed++;
            }
        }

        // Mark scene dirty so Unity knows to save
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[MapFixer] Done!\n" +
                  $"  Grass colliders removed: {grassFixed}\n" +
                  $"  Trees fixed (CapsuleCollider): {treeFixed}\n" +
                  $"  Ladders fixed (BoxCollider + Tilt): {ladderFixed}\n" +
                  $"  Coin triggers configured: {coinFixed}");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static string CleanName(string name)
    {
        int spaceIndex = name.IndexOf(' ');
        if (spaceIndex >= 0)
        {
            name = name.Substring(0, spaceIndex);
        }
        int parenIndex = name.IndexOf('(');
        if (parenIndex >= 0)
        {
            name = name.Substring(0, parenIndex);
        }
        return name.Trim();
    }

    static bool MatchesAny(string name, string[] list)
    {
        string clean = CleanName(name);
        foreach (var s in list)
            if (clean == s) return true;
        return false;
    }

    static bool StartsWithAny(string name, string[] prefixes)
    {
        string clean = CleanName(name);
        foreach (var p in prefixes)
            if (clean.StartsWith(p)) return true;
        return false;
    }
}
#endif
