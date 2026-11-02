using System.Collections.Generic;
using UnityEngine;

// 1층 미로를 깨어날 때마다 새로 만든다 (죽거나 처음 화면으로 돌아가면 씬을 다시 불러오므로 매번 다른 모양).
// 1) 시작 방: 플레이어·곡괭이와 도끼. 출구는 거대한 문 하나뿐.
// 2) 방 여러 개를 흩어 놓고, 구불구불한 1칸 통로로 잇는다 (최소 연결 + 고리 몇 개 + 막다른 길).
// 3) 문에서 가장 먼 방에 위로 가는 계단. 가까운 방엔 횃불 상자, 중간쯤 굶주린 고블린.
// 4) 깊이 들어갈수록 몬스터가 세진다. 방 벽엔 꺼진 벽 횃불, 막다른 길 끝엔 작은 상자.
// 다른 스크립트가 바닥을 찾기 전에 끝나야 해서 가장 먼저 실행한다.
[DefaultExecutionOrder(-500)]
public class FloorGenerator : MonoBehaviour
{
    public static FloorGenerator Instance { get; private set; }

    [Header("크기")]
    public int width = 48, height = 48;
    public int maxRooms = 20;
    public int seed;   // 0이면 매번 무작위

    [Header("프리팹")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject wallTorchPrefab;
    public GameObject chestSmall, chestBig, chestElite;
    public GameObject rat, bat, slime, wolf;

    [Header("씬에 있는 것 (위치만 옮긴다)")]
    public Transform player;
    public Transform giantDoor;
    public Transform tools;
    public Transform goblin;
    public Transform stairs;
    public Transform darkTrigger;   // 문을 나서면 "너무 어둡다" 안내

    enum C { Solid, Room, Corridor }
    C[,] grid;
    System.Random rng;
    RectInt startRoom;
    readonly List<RectInt> rooms = new List<RectInt>();      // 시작 방 제외
    readonly List<Vector2Int> deadEnds = new List<Vector2Int>();
    readonly HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
    Dictionary<Vector2Int, int> dist;   // 문 밖 첫 칸에서 걸음 수
    int maxDist;
    Vector2Int doorCell, outsideDoor;

    public int Seed { get; private set; }

    void Awake()
    {
        Instance = this;
        Seed = seed != 0 ? seed : System.Environment.TickCount;
        rng = new System.Random(Seed);
        Generate();
        BuildObjects();
        GridNav.Invalidate();
        Debug.Log($"[1층 생성] seed={Seed} 방 {rooms.Count + 1}개, 막다른 길 {deadEnds.Count}개, 가장 먼 방까지 {maxDist}걸음");
    }

    int R(int min, int maxExclusive) => rng.Next(min, maxExclusive);
    float R01() => (float)rng.NextDouble();
    T Pick<T>(IList<T> list) => list[R(0, list.Count)];

    bool In(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;
    bool IsFloor(Vector2Int c) => In(c) && grid[c.x, c.y] != C.Solid;

    // ---- 1. 지형 ----

    void Generate()
    {
        grid = new C[width, height];

        // 시작 방: 네 모서리 중 한 곳, 5×5
        int corner = R(0, 4);
        int sx = (corner & 1) == 0 ? 2 : width - 7, sy = (corner & 2) == 0 ? 2 : height - 7;
        startRoom = new RectInt(sx, sy, 5, 5);
        Carve(startRoom, C.Room);

        // 문: 맵 가운데를 향한 벽의 한가운데
        var center = new Vector2(width / 2f, height / 2f);
        var toCenter = center - startRoom.center;
        Vector2Int outDir = Mathf.Abs(toCenter.x) > Mathf.Abs(toCenter.y)
            ? new Vector2Int(toCenter.x > 0 ? 1 : -1, 0)
            : new Vector2Int(0, toCenter.y > 0 ? 1 : -1);
        var mid = new Vector2Int(startRoom.x + 2, startRoom.y + 2);
        doorCell = mid + outDir * 3;
        outsideDoor = doorCell + outDir;
        grid[doorCell.x, doorCell.y] = C.Corridor;
        grid[outsideDoor.x, outsideDoor.y] = C.Corridor;
        doorDir = outDir;

        // 다른 방들 (시작 방과 겹치지 않게, 서로 벽 2칸 이상 떨어지게)
        var startZone = Grow(startRoom, 3);
        for (int attempt = 0; attempt < 600 && rooms.Count < maxRooms - 1; attempt++)
        {
            int w = R(3, 9), h = R(3, 9);
            var r = new RectInt(R(2, width - w - 2), R(2, height - h - 2), w, h);
            if (Grow(r, 2).Overlaps(startZone)) continue;
            bool clash = false;
            foreach (var o in rooms) if (Grow(o, 2).Overlaps(r)) { clash = true; break; }
            if (clash) continue;
            rooms.Add(r);
            Carve(r, C.Room);
        }

        // 통로: 문 밖 → 가장 가까운 방, 그다음 방들끼리 최소 연결(프림) + 고리 몇 개
        var noise = new float[width, height];
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) noise[x, y] = R01() * 4f;

        int nearest = 0;
        for (int i = 1; i < rooms.Count; i++)
            if (Vector2.Distance(rooms[i].center, outsideDoor) < Vector2.Distance(rooms[nearest].center, outsideDoor)) nearest = i;
        Connect(outsideDoor, rooms[nearest], noise);

        var linked = new List<int> { nearest };
        var edges = new HashSet<(int, int)>();
        while (linked.Count < rooms.Count)
        {
            float best = float.MaxValue; int a = -1, b = -1;
            foreach (var i in linked)
                for (int j = 0; j < rooms.Count; j++)
                {
                    if (linked.Contains(j)) continue;
                    float d = Vector2.Distance(rooms[i].center, rooms[j].center);
                    if (d < best) { best = d; a = i; b = j; }
                }
            Connect(Center(rooms[a]), rooms[b], noise);
            edges.Add((Mathf.Min(a, b), Mathf.Max(a, b)));
            linked.Add(b);
        }
        int loops = 0;
        for (int i = 0; i < rooms.Count && loops < 7; i++)
            for (int j = i + 1; j < rooms.Count && loops < 7; j++)
                if (!edges.Contains((i, j)) && Vector2.Distance(rooms[i].center, rooms[j].center) < 13f && R01() < 0.3f)
                {
                    Connect(Center(rooms[i]), rooms[j], noise);
                    loops++;
                }

        // 막다른 길: 통로에서 옆으로 짧게 뻗은 가지 (미로 느낌 + 작은 상자 자리)
        var corridors = new List<Vector2Int>();
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            if (grid[x, y] == C.Corridor) corridors.Add(new Vector2Int(x, y));
        for (int n = 0, tries = 0; n < 9 && tries < 120; tries++)
            if (Spur(Pick(corridors))) n++;

        ComputeDistances();
    }

    Vector2Int doorDir;

    static RectInt Grow(RectInt r, int m) => new RectInt(r.x - m, r.y - m, r.width + m * 2, r.height + m * 2);
    static Vector2Int Center(RectInt r) => new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);

    void Carve(RectInt r, C kind)
    {
        for (int x = r.xMin; x < r.xMax; x++) for (int y = r.yMin; y < r.yMax; y++) grid[x, y] = kind;
    }

    // 시작 방과 그 벽 둘레는 문 말고는 뚫지 않는다
    bool Forbidden(Vector2Int c) =>
        c.x <= 0 || c.y <= 0 || c.x >= width - 1 || c.y >= height - 1 ||
        (Grow(startRoom, 1).Contains(c) && c != doorCell);

    // from에서 room 안 아무 칸까지 구불구불한 통로를 판다 (이미 뚫린 곳은 싸게, 바위는 비싸고 들쭉날쭉하게)
    void Connect(Vector2Int from, RectInt room, float[,] noise)
    {
        var cost = new Dictionary<Vector2Int, float> { [from] = 0f };
        var prev = new Dictionary<Vector2Int, Vector2Int>();
        var done = new HashSet<Vector2Int>();
        var open = new List<Vector2Int> { from };
        Vector2Int? end = null;
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (open.Count > 0)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++) if (cost[open[i]] < cost[open[bi]]) bi = i;
            var cur = open[bi]; open.RemoveAt(bi);
            if (!done.Add(cur)) continue;
            if (room.Contains(cur)) { end = cur; break; }
            foreach (var d in dirs)
            {
                var n = cur + d;
                if (!In(n) || Forbidden(n) || done.Contains(n)) continue;
                float step = grid[n.x, n.y] != C.Solid ? 1f : 2f + noise[n.x, n.y];
                float c = cost[cur] + step;
                if (cost.TryGetValue(n, out var old) && old <= c) continue;
                cost[n] = c; prev[n] = cur; open.Add(n);
            }
        }
        if (end == null) return;
        for (var c = end.Value; c != from; c = prev[c])
            if (grid[c.x, c.y] == C.Solid) grid[c.x, c.y] = C.Corridor;
    }

    // 통로 한 칸에서 바위 쪽으로 3~7칸 뻗는 막다른 길. 다른 길과 붙지 않게 판다.
    bool Spur(Vector2Int start)
    {
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        var dir = Pick(dirs);
        var path = new List<Vector2Int>();
        var cur = start;
        int len = R(3, 8);
        for (int i = 0; i < len; i++)
        {
            if (i > 0 && R01() < 0.3f) dir = R01() < 0.5f ? new Vector2Int(dir.y, -dir.x) : new Vector2Int(-dir.y, dir.x);
            var n = cur + dir;
            if (!In(n) || Forbidden(n) || grid[n.x, n.y] != C.Solid || path.Contains(n)) break;
            // 새 칸 둘레(8칸)에 다른 바닥이 있으면 안 된다 (첫 칸은 출발 칸 둘레의 통로, 이후엔 이번 가지 자신은 괜찮다)
            bool touches = false;
            for (int dx = -1; dx <= 1 && !touches; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var m = n + new Vector2Int(dx, dy);
                    if (m == n || m == cur || path.Contains(m)) continue;
                    if (path.Count == 0 && GridNav.Distance(m, cur) <= 1) continue;
                    if (IsFloor(m)) { touches = true; break; }
                }
            if (touches) break;
            path.Add(n);
            cur = n;
        }
        if (path.Count < 3) return false;
        foreach (var c in path) grid[c.x, c.y] = C.Corridor;
        deadEnds.Add(path[path.Count - 1]);
        return true;
    }

    void ComputeDistances()
    {
        dist = new Dictionary<Vector2Int, int> { [outsideDoor] = 0 };
        var q = new Queue<Vector2Int>();
        q.Enqueue(outsideDoor);
        maxDist = 0;
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var d in GridNav.Dirs8)
            {
                var n = c + d;
                if (!IsFloor(n) || dist.ContainsKey(n) || Grow(startRoom, 1).Contains(n)) continue;
                if (d.x != 0 && d.y != 0 && !(IsFloor(c + new Vector2Int(d.x, 0)) && IsFloor(c + new Vector2Int(0, d.y)))) continue;
                dist[n] = dist[c] + 1;
                maxDist = Mathf.Max(maxDist, dist[n]);
                q.Enqueue(n);
            }
        }
    }

    // ---- 2. 오브젝트 만들기 ----

    Transform root;

    void BuildObjects()
    {
        root = new GameObject("Generated").transform;
        root.SetParent(transform, false);
        root.gameObject.SetActive(false);   // 몬스터 레벨 등을 정한 뒤 한꺼번에 깨운다

        var floorRoot = Group("Floors");
        var wallRoot = Group("Walls");
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var c = new Vector2Int(x, y);
                if (IsFloor(c)) Instantiate(floorPrefab, GridTile.CellToWorld(c), Quaternion.identity, floorRoot);
                else if (TouchesFloor(c)) Instantiate(wallPrefab, GridTile.CellToWorld(c), Quaternion.identity, wallRoot);
            }

        PlaceStartRoom();
        var byDistance = new List<RectInt>(rooms);
        byDistance.Sort((a, b) => RoomDist(a).CompareTo(RoomDist(b)));
        var firstRoom = byDistance[0];
        var lastRoom = byDistance[byDistance.Count - 1];

        // 계단: 가장 먼 방 한가운데
        Put(stairs, Center(lastRoom), Quaternion.Euler(0, 90f * R(0, 4), 0));

        // 첫 방: 낡은 횃불이 든 상자 (튜토리얼)
        var torchChest = Spawn(chestBig, FreeCellIn(firstRoom, true), RandomYaw());
        if (torchChest != null) torchChest.GetComponent<ChestEvent>().givesTorch = true;

        // 굶주린 고블린: 중간쯤 되는 방
        var midRoom = byDistance[byDistance.Count / 2];
        if (midRoom.Equals(lastRoom) && byDistance.Count > 2) midRoom = byDistance[byDistance.Count / 2 - 1];
        Put(goblin, FreeCellIn(midRoom, true), RandomYaw());

        // 상자: 엘리트는 계단 방, 큰 상자 둘은 아무 방, 작은 상자는 막다른 길 끝
        Spawn(chestElite, FreeCellIn(lastRoom, true), RandomYaw());
        for (int i = 0; i < 2; i++)
        {
            var r = byDistance[R(1, byDistance.Count - 1)];
            Spawn(chestBig, FreeCellIn(r, true), RandomYaw());
        }
        int smalls = 0;
        foreach (var d in deadEnds)
            if (smalls < 5 && !occupied.Contains(d)) { Spawn(chestSmall, d, Quaternion.identity); smalls++; }

        // 벽 횃불: 시작 방 말고 방마다 하나씩 (꺼진 채로)
        foreach (var r in rooms) PlaceWallTorch(r);

        // 몬스터: 깊이에 따라 종류·레벨이 달라진다
        foreach (var r in rooms)
        {
            if (r.Equals(firstRoom)) continue;   // 첫 방은 숨 돌릴 곳
            int count = r.width * r.height >= 36 ? 2 : R01() < 0.65f ? 1 : 0;
            for (int i = 0; i < count; i++) SpawnMonster(FreeCellIn(r, false));
        }
        // 계단 방 문지기
        SpawnMonster(FreeCellIn(lastRoom, false), wolf, 2);
        // 통로에도 몇 마리
        var corridorCells = new List<Vector2Int>();
        foreach (var kv in dist)
            if (grid[kv.Key.x, kv.Key.y] == C.Corridor && kv.Value > 8 && !occupied.Contains(kv.Key)) corridorCells.Add(kv.Key);
        for (int i = 0; i < 4 && corridorCells.Count > 0; i++)
        {
            var c = Pick(corridorCells);
            corridorCells.Remove(c);
            SpawnMonster(c);
        }

        root.gameObject.SetActive(true);
    }

    Transform Group(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    bool TouchesFloor(Vector2Int c)
    {
        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            if ((dx != 0 || dy != 0) && IsFloor(c + new Vector2Int(dx, dy))) return true;
        return false;
    }

    int RoomDist(RectInt r) => dist.TryGetValue(Center(r), out var d) ? d : int.MaxValue / 2;

    void PlaceStartRoom()
    {
        // 거대한 문: 문 칸에 통로 방향으로 세운다
        Put(giantDoor, doorCell, Quaternion.Euler(0, doorDir.x != 0 ? 90f : 0f, 0));
        occupied.Add(doorCell);
        occupied.Add(outsideDoor);

        // 곡괭이·도끼: 문 바로 안쪽 옆
        var inside = doorCell - doorDir;
        var side = new Vector2Int(doorDir.y, doorDir.x);
        Put(tools, inside + side, Quaternion.LookRotation(new Vector3(-doorDir.x, 0, -doorDir.y)));

        // 플레이어: 방 가운데, 문 쪽을 보고
        var p = Center(startRoom) - doorDir;
        Put(player, p, Quaternion.LookRotation(new Vector3(doorDir.x, 0, doorDir.y)));

        // 문을 나서자마자 "너무 어둡다"
        Put(darkTrigger, outsideDoor, Quaternion.identity);
    }

    void Put(Transform t, Vector2Int? cell, Quaternion rot)
    {
        if (t == null || cell == null) return;
        t.SetPositionAndRotation(GridTile.CellToWorld(cell.Value), rot);
        occupied.Add(cell.Value);
    }

    GameObject Spawn(GameObject prefab, Vector2Int? cell, Quaternion rot)
    {
        if (prefab == null || cell == null) return null;
        occupied.Add(cell.Value);
        return Instantiate(prefab, GridTile.CellToWorld(cell.Value), rot, root);
    }

    Quaternion RandomYaw() => Quaternion.Euler(0, 90f * R(0, 4), 0);

    // 방 안의 빈 칸. edge면 벽에 붙은 칸을 고르되, 통로 입구는 막지 않는다
    Vector2Int? FreeCellIn(RectInt r, bool edge)
    {
        var cand = new List<Vector2Int>();
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                if (occupied.Contains(c) || BlocksEntrance(c, r)) continue;
                bool onEdge = x == r.xMin || y == r.yMin || x == r.xMax - 1 || y == r.yMax - 1;
                if (edge == onEdge || r.width <= 3 || r.height <= 3) cand.Add(c);
            }
        if (cand.Count == 0) return null;
        return Pick(cand);
    }

    // 방 둘레 바깥의 통로와 맞닿은 칸(입구)과 그 옆은 비워 둔다
    bool BlocksEntrance(Vector2Int c, RectInt r)
    {
        foreach (var d in GridNav.Dirs8)
        {
            var n = c + d;
            if (!r.Contains(n) && IsFloor(n)) return true;
        }
        return false;
    }

    GameObject SpawnMonster(Vector2Int? cell, GameObject kind = null, int level = 0)
    {
        if (cell == null || !dist.TryGetValue(cell.Value, out var d)) return null;
        float depth = maxDist > 0 ? (float)d / maxDist : 0f;
        if (kind == null)
        {
            if (depth < 0.35f) kind = R01() < 0.6f ? rat : bat;
            else if (depth < 0.7f) kind = Pick(new[] { rat, bat, slime });
            else kind = R01() < 0.55f ? wolf : slime;
        }
        if (level == 0) level = 1 + (depth > 0.6f && R01() < 0.5f ? 1 : 0);
        var go = Spawn(kind, cell, Quaternion.Euler(0, R(0, 360), 0));
        if (go != null) go.GetComponent<Monster>().level = level;   // 아직 잠든 상태(Awake 전)라 능력치에 반영된다
        return go;
    }

    void PlaceWallTorch(RectInt r)
    {
        if (wallTorchPrefab == null) return;
        var options = new List<(Vector2Int cell, Vector2Int wall)>();
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                if (occupied.Contains(c) || BlocksEntrance(c, r)) continue;
                foreach (var d in dirs)
                    if (!IsFloor(c + d)) options.Add((c, d));
            }
        if (options.Count == 0) return;
        var pick = Pick(options);
        Spawn(wallTorchPrefab, pick.cell, Quaternion.LookRotation(new Vector3(pick.wall.x, 0, pick.wall.y)));
    }

    // ---- 다른 스크립트가 쓰는 것 ----

    // 이전 회차의 뼈: 지난 자리가 이번 미로에서도 바닥이면 그대로, 아니면 아무 방에 (회차마다 같은 방식으로)
    public Vector2Int PlaceBones(Vector2Int lastCell, int run)
    {
        if (IsFloor(lastCell) && !Grow(startRoom, 1).Contains(lastCell) && !occupied.Contains(lastCell)) return lastCell;
        var r = new System.Random(Seed ^ (run * 7919));
        for (int i = 0; i < 50; i++)
        {
            var room = rooms[r.Next(rooms.Count)];
            var c = new Vector2Int(r.Next(room.xMin, room.xMax), r.Next(room.yMin, room.yMax));
            if (!occupied.Contains(c)) return c;
        }
        return Center(rooms[0]);
    }

    // 디버그용: 지형을 글자로
    public string Ascii()
    {
        var sb = new System.Text.StringBuilder();
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                var c = new Vector2Int(x, y);
                sb.Append(c == doorCell ? 'D' : grid[x, y] == C.Room ? '.' : grid[x, y] == C.Corridor ? ',' : TouchesFloor(c) ? '#' : ' ');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
