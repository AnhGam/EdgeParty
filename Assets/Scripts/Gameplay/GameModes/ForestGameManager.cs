using Unity.Netcode;
using UnityEngine;

public class ForestGameManager : NetworkBehaviour
{
    public static ForestGameManager Instance;

    public NetworkVariable<int> Team1Score = new NetworkVariable<int>(0);
    public NetworkVariable<int> Team2Score = new NetworkVariable<int>(0);

    private void Awake() => Instance = this;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.K))
        {
            // Ví dụ: Nhấn K thì cộng 10 điểm cho Team 1
            AddScoreServerRpc(1, 10);
            Debug.Log("Đã bấm K: Cộng 10 điểm cho Team 1");
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void AddScoreServerRpc(int teamID, int points)
    {
        if (teamID == 1) Team1Score.Value += points;
        else if (teamID == 2) Team2Score.Value += points;
    }
}