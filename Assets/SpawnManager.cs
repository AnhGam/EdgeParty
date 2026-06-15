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
        // Get a random point in the local space of the box
        float x = Random.Range(-box.size.x / 2f, box.size.x / 2f);
        float y = Random.Range(-box.size.y / 2f, box.size.y / 2f);
        float z = Random.Range(-box.size.z / 2f, box.size.z / 2f);

        Vector3 localPoint = box.center + new Vector3(x, y, z);
        
        // Transform the local point to world space (handles rotation, scale, and offset properly)
        Vector3 worldPoint = box.transform.TransformPoint(localPoint);

        // Start raycast from top of the box downwards
        Vector3 rayStart = worldPoint;
        rayStart.y = box.bounds.max.y + 1f;

        // Raycast xuống đất
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, box.bounds.size.y + 50f))
        {
            worldPoint.y = hit.point.y + 0.5f;
        }

        return worldPoint;
    }
}