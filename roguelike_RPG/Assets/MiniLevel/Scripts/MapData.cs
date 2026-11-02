using System;
using System.Collections.Generic;
using UnityEngine;

// 미니맵용 격자 데이터: 칸마다 지형(바닥·벽)과 "본 적 있는가"·"밟은 적 있는가".
// 씬이 시작될 때 GridTile(바닥)·OccludingWall(벽)을 훑어서 만든다. 오브젝트 아이콘은 그릴 때마다 씬에서 찾는다.
public class MapData
{
    public enum Terrain { None, Floor, Wall }

    public readonly RectInt bounds;     // 맵 범위 (칸 좌표)
    readonly Terrain[,] terrain;
    readonly bool[,] explored;   // 눈으로 본 칸
    readonly bool[,] visited;    // 직접 밟은 칸

    public event Action<List<Vector2Int>> OnRevealed;   // 새로 밝혀진 칸들

    public MapData()
    {
        var floors = new List<Vector2Int>();
        var walls = new List<Vector2Int>();
        foreach (var t in UnityEngine.Object.FindObjectsByType<GridTile>(FindObjectsSortMode.None)) floors.Add(t.Cell);
        foreach (var w in UnityEngine.Object.FindObjectsByType<OccludingWall>(FindObjectsSortMode.None)) walls.Add(GridTile.WorldToCell(w.transform.position));

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in floors) Grow(c);
        foreach (var c in walls) Grow(c);
        void Grow(Vector2Int c) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y); }
        if (minX > maxX) { minX = minY = 0; maxX = maxY = 0; }

        bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        terrain = new Terrain[bounds.width, bounds.height];
        explored = new bool[bounds.width, bounds.height];
        visited = new bool[bounds.width, bounds.height];
        foreach (var c in walls) terrain[c.x - minX, c.y - minY] = Terrain.Wall;
        foreach (var c in floors) terrain[c.x - minX, c.y - minY] = Terrain.Floor;
    }

    public bool Contains(Vector2Int c) => bounds.Contains(c);
    public Terrain TerrainAt(Vector2Int c) => Contains(c) ? terrain[c.x - bounds.xMin, c.y - bounds.yMin] : Terrain.None;
    public bool IsExplored(Vector2Int c) => Contains(c) && explored[c.x - bounds.xMin, c.y - bounds.yMin];
    public bool IsVisited(Vector2Int c) => Contains(c) && visited[c.x - bounds.xMin, c.y - bounds.yMin];

    // 바닥 칸 중 본 적 있는 칸의 비율 (0~100)
    public float ExploredPercent()
    {
        int total = 0, seen = 0;
        for (int x = 0; x < bounds.width; x++)
            for (int y = 0; y < bounds.height; y++)
                if (terrain[x, y] == Terrain.Floor) { total++; if (explored[x, y]) seen++; }
        return total > 0 ? 100f * seen / total : 0f;
    }

    // 중심 칸에서 반경(정사각형) 안의 칸을 밝힌다. 반경 1 = 3×3, 반경 2 = 5×5
    public void Reveal(Vector2Int center, int radius)
    {
        var fresh = new List<Vector2Int>();
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                var c = center + new Vector2Int(dx, dy);
                if (!Contains(c)) continue;
                int x = c.x - bounds.xMin, y = c.y - bounds.yMin;
                if (explored[x, y]) continue;
                explored[x, y] = true;
                fresh.Add(c);
            }
        if (fresh.Count > 0) OnRevealed?.Invoke(fresh);
    }

    // 직접 밟은 칸 (지도에 또렷하게 그려지고, 전체 지도에서 눌러 이동할 수 있다)
    public void Visit(Vector2Int c)
    {
        if (!Contains(c)) return;
        int x = c.x - bounds.xMin, y = c.y - bounds.yMin;
        if (visited[x, y]) return;
        visited[x, y] = explored[x, y] = true;
        OnRevealed?.Invoke(new List<Vector2Int> { c });
    }
}
