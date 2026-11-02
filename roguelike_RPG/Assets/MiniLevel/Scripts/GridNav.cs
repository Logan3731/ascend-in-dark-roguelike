using System.Collections.Generic;
using UnityEngine;

// 격자 이동 규칙 (플레이어·몬스터 공통). 8방향으로 움직이고 대각선도 한 칸 = 한 턴.
// 단, 벽 모서리를 비스듬히 뚫고 지나갈 수는 없다: 대각선 두 옆 칸이 모두 바닥이어야 한다.
// (옆 칸에 몬스터가 서 있는 건 괜찮다. 막는 건 벽뿐)
public static class GridNav
{
    public static readonly Vector2Int[] Dirs8 =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,   // 곧은 방향을 먼저 (같은 거리면 곧게 간다)
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
    };

    public static HashSet<Vector2Int> Floors()
    {
        var floors = new HashSet<Vector2Int>();
        foreach (var t in Object.FindObjectsByType<GridTile>(FindObjectsSortMode.None)) floors.Add(t.Cell);
        return floors;
    }

    // 바닥 칸에서 막는 것(몬스터·상자 등)을 뺀, 지금 설 수 있는 칸
    public static HashSet<Vector2Int> Walkable(HashSet<Vector2Int> floors)
    {
        var walkable = new HashSet<Vector2Int>(floors);
        foreach (var b in Object.FindObjectsByType<GridBlocker>(FindObjectsSortMode.None))
            if (b.enabled) walkable.Remove(b.Cell);
        return walkable;
    }

    public static int Distance(Vector2Int a, Vector2Int b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    // from → from+d 로 한 걸음: 대각선이면 벽 모서리를 끼고 돌 수 없다
    public static bool CornerFree(Vector2Int from, Vector2Int d, HashSet<Vector2Int> floors)
    {
        if (d.x == 0 || d.y == 0) return true;
        return floors.Contains(from + new Vector2Int(d.x, 0)) && floors.Contains(from + new Vector2Int(0, d.y));
    }

    // 바로 옆 칸인가 (대각선 포함, 벽 모서리 너머는 아님) — 공격·상호작용 거리
    public static bool Adjacent(Vector2Int a, Vector2Int b, HashSet<Vector2Int> floors)
    {
        if (a == b || Distance(a, b) != 1) return false;
        return CornerFree(a, b - a, floors);
    }

    public static bool Adjacent(Vector2Int a, Vector2Int b) => Adjacent(a, b, Floors());

    // 시작 칸에서 목표 조건을 만족하는 칸까지 가장 짧은 경로 (시작 칸 제외). 없으면 null.
    // 걸음 수가 같으면 대각선을 덜 쓰는 길을 고른다 (지그재그 대신 곧은 길).
    public static List<Vector2Int> FindPath(Vector2Int start, System.Func<Vector2Int, bool> isGoal,
                                            HashSet<Vector2Int> walkable, HashSet<Vector2Int> floors, int limit = 2000)
    {
        const float DiagCost = 1.001f;
        var cost = new Dictionary<Vector2Int, float> { [start] = 0f };
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var done = new HashSet<Vector2Int>();
        var open = new List<Vector2Int> { start };
        while (open.Count > 0 && done.Count < limit)
        {
            // 칸 수가 적어서(수백 칸) 단순히 가장 싼 칸을 고른다
            int best = 0;
            for (int i = 1; i < open.Count; i++) if (cost[open[i]] < cost[open[best]]) best = i;
            var cur = open[best];
            open.RemoveAt(best);
            if (!done.Add(cur)) continue;

            if (cur != start && isGoal(cur))
            {
                var route = new List<Vector2Int>();
                for (var c = cur; c != start; c = prev[c]) route.Add(c);
                route.Reverse();
                return route;
            }
            foreach (var d in Dirs8)
            {
                var next = cur + d;
                if (!walkable.Contains(next) || done.Contains(next) || !CornerFree(cur, d, floors)) continue;
                float c = cost[cur] + (d.x != 0 && d.y != 0 ? DiagCost : 1f);
                if (cost.TryGetValue(next, out var old) && old <= c) continue;
                cost[next] = c;
                prev[next] = cur;
                open.Add(next);
            }
        }
        return null;
    }
}
