using System.Collections.Generic;
using UnityEngine;

// 얕은 물 칸 (설계 A6). 걸어 들어갈 수 있다. 물 칸엔 불이 붙지 않고, 불붙은 채 들어오면 꺼진다 (Fire).
// 바닥 타일은 FloorGenerator가 물 재질로 바꿔 깐다. 이 컴포넌트는 칸 표시와 밟았을 때 처리만 한다.
public class WaterTile : Interactable
{
    static readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
    public static bool At(Vector2Int c) => cells.Contains(c);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => cells.Clear();

    public static WaterTile Create(Vector2Int cell, Transform parent)
    {
        var go = new GameObject("Water");
        go.transform.SetParent(parent, false);
        go.transform.position = GridTile.CellToWorld(cell);
        var w = go.AddComponent<WaterTile>();
        w.triggerOnEnter = true;
        cells.Add(cell);   // 층을 만드는 동안(아직 깨어나기 전)에도 칸을 알 수 있게
        return w;
    }

    protected override void Awake()
    {
        triggerOnEnter = true;
        base.Awake();
        cells.Add(Cell);
    }

    void OnDestroy() => cells.Remove(Cell);

    public override void Interact(PlayerStats player) => Fire.Entered(player, Cell);
}
