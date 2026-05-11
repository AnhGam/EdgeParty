using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance;

    public BoxCollider[] team1Zones;
    public BoxCollider[] team2Zones;

    private void Awake()
    {
        Instance = this;
    }

    public Vector3 GetSpawnPosition(int teamID)
    {
        BoxCollider zone = GetRandomZone(teamID);
        if (zone == null) return Vector3.zero;

        return GetRandomPointInZone(zone);
    }

    BoxCollider GetRandomZone(int teamID)
    {
        if (teamID == 1 && team1Zones.Length > 0)
            return team1Zones[Random.Range(0, team1Zones.Length)];

        if (team2Zones.Length > 0)
            return team2Zones[Random.Range(0, team2Zones.Length)];

        return null;
    }

    Vector3 GetRandomPointInZone(BoxCollider box)
    {
        Vector3 center = box.center + box.transform.position;
        Vector3 size = box.size;

        float x = Random.Range(-size.x / 2, size.x / 2);
        float z = Random.Range(-size.z / 2, size.z / 2);

        Vector3 pos = center + new Vector3(x, 0, z);

        // Raycast xuống đất
        if (Physics.Raycast(pos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 50f))
        {
            pos.y = hit.point.y + 0.5f;
        }

        return pos;
    }
}