using UnityEngine;

// 튜토리얼 안내: 플레이어가 이 구역(방)에 처음 들어선 턴에 로그에 한 번 띄운다.
// 한 번 본 안내는 TowerMemory에 기록되어 다음 회차엔 만들어지지 않는다. FloorGenerator가 방마다 만든다.
public class RoomHint : MonoBehaviour
{
    public string id;
    public RectInt area;
    [TextArea] public string message;

    PlayerStats player;

    void OnEnable() => TurnManager.OnTurnEnded += Check;
    void OnDisable() => TurnManager.OnTurnEnded -= Check;

    void Check(int turn)
    {
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        if (player == null || player.Dead) return;
        if (!area.Contains(GridTile.WorldToCell(player.transform.position))) return;
        TowerMemory.MarkHint(id);
        GameLog.Add(message, GameLog.Warn);
        Destroy(gameObject);
    }
}
