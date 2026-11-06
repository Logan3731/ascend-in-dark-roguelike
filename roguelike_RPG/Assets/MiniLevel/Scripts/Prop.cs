using UnityEngine;

// 방 소품(책장·관·우물·무너진 벽)의 공통 부분: 칸을 막고, 다루는 데 한 턴을 쓴다.
[RequireComponent(typeof(GridBlocker))]
public abstract class Prop : Interactable
{
    // 플레이어가 이 소품을 다루며 한 턴을 보낸다 (그사이 몬스터도 움직인다)
    protected static void SpendTurn(PlayerStats player, Vector3 facing, float busy = 0.3f)
    {
        var mover = player.GetComponent<GridClickMover>();
        if (mover != null) mover.FaceTo(facing);
        TurnResolver.BeginTurn(GridTile.WorldToCell(player.transform.position), mover != null ? mover.moveSeconds : 0.3f);
        TurnResolver.ResolveAttacks(player, null);
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + Mathf.Max(busy, GridClickMover.TurnSeconds));   // 걷기와 같은 박자
    }

    // 다 쓴 소품은 어둡게 (다시 눌러도 반응만 한다)
    protected void Dim()
    {
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(0.45f, 0.42f, 0.4f));
        foreach (var r in GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
    }
}
