using System.Collections.Generic;
using UnityEngine;

// 1층 미로를 깨어날 때마다 새로 만든다 (죽거나 처음 화면으로 돌아가면 씬을 다시 불러오므로 매번 다른 모양).
// "방 + 미로 채우기" 방식이라 빈 바위 없이 촘촘하다.
// 1) 홀수 칸 격자에 방을 흩어 놓는다 (시작 방 포함).
// 2) 남은 공간을 1칸 통로 미로로 가득 채운다.
// 3) 방과 통로 사이의 벽 한 칸을 뚫어(문) 전체를 하나로 잇고, 고리 몇 개를 더 연다.
// 4) 막다른 길은 대부분 메우고 몇 개만 남긴다 → 그 끝에 보물상자.
// 5) 방의 문은 나무문·철문. 문이 하나뿐인 방 몇 곳은 잠그고, 열쇠는 상자에서 나온다 (잠긴 문보다 적게).
// 시작 방의 출구는 거대한 문 하나. 문에서 가장 먼 방에 계단. 깊이 들어갈수록 몬스터가 세진다.
// 다른 스크립트가 바닥을 찾기 전에 끝나야 해서 가장 먼저 실행한다.
[DefaultExecutionOrder(-500)]
public class FloorGenerator : MonoBehaviour
{
    public static FloorGenerator Instance { get; private set; }

    [Header("크기 (홀수)")]
    public int width = 41, height = 41;
    public int maxRooms = 16;
    public int keepDeadEnds = 8;        // 남길 막다른 길 (끝에 상자)
    public int maxDeadEndLength = 7;    // 남긴 막다른 길이 너무 길면 이만큼으로 줄인다
    [Range(0f, 0.3f)] public float extraDoorChance = 0.12f;   // 고리를 만드는 여분의 문
    [Range(0f, 1f)] public float straightness = 0.4f;          // 통로가 곧게 이어질 확률 (낮을수록 구불구불)
    public int lockedRooms = 3;
    [Range(0f, 1f)] public float ironDoorChance = 0.3f;
    public int seed;   // 0이면 매번 무작위

    [Header("프리팹")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject wallTorchPrefab;
    public GameObject chestSmall, chestBig, chestElite;
    public GameObject rat, bat, slime, wolf;
    public GameObject anvilPrefab, branchPrefab, stonePrefab;

    [Header("배치 수")]
    public int wallTorches = 12;
    public int branches = 14, stones = 10;

    [Header("씬에 있는 것 (위치만 옮긴다)")]
    public Transform player;
    public Transform giantDoor;
    public Transform tools;
    public Transform goblin;
    public Transform stairs;
    public Transform darkTrigger;   // 문을 나서면 "너무 어둡다" 안내

    public int Seed { get; private set; }

    enum C { Solid, Room, Corridor, Door }

    class Room
    {
        public RectInt rect;
        public int region;
        public readonly List<Vector2Int> doors = new List<Vector2Int>();
        public bool locked, hasChest;
    }

    static readonly Vector2Int[] Dirs4 = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    C[,] grid;
    int[,] region;
    int regionCount;
    System.Random rng;
    Room start;
    readonly List<Room> rooms = new List<Room>();   // 시작 방 제외
    readonly List<Vector2Int> deadEnds = new List<Vector2Int>();
    readonly HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
    Dictionary<Vector2Int, int> dist;   // 문 밖 첫 칸에서 걸음 수
    int maxDist;
    Vector2Int doorCell, outsideDoor;
    Transform root;

    void Awake()
    {
        Instance = this;
        Seed = seed != 0 ? seed : System.Environment.TickCount;
        rng = new System.Random(Seed);
        Potions.NewRun(Seed);   // 물약 색과 효과의 짝을 새로 섞는다
        Generate();
        BuildObjects();
        GridNav.Invalidate();
        Debug.Log($"[1층 생성] seed={Seed} 방 {rooms.Count + 1}개, 바닥 {CountFloors()}칸, 막다른 길 {deadEnds.Count}개, 잠긴 방 {rooms.FindAll(r => r.locked).Count}개, 가장 먼 방까지 {maxDist}걸음");
    }

    int R(int min, int maxExclusive) => rng.Next(min, maxExclusive);
    float R01() => (float)rng.NextDouble();
    T Pick<T>(IList<T> list) => list[R(0, list.Count)];
    void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--) { int j = R(0, i + 1); (list[i], list[j]) = (list[j], list[i]); }
    }

    bool In(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;
    C At(Vector2Int c) => In(c) ? grid[c.x, c.y] : C.Solid;
    bool IsFloor(Vector2Int c) => At(c) != C.Solid;
    int CountFloors() { int n = 0; foreach (var c in grid) if (c != C.Solid) n++; return n; }

    int OpenNeighbors(Vector2Int c)
    {
        int n = 0;
        foreach (var d in Dirs4) if (IsFloor(c + d)) n++;
        return n;
    }

    // ---- 1. 지형 ----

    void Generate()
    {
        grid = new C[width, height];
        region = new int[width, height];
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) region[x, y] = -1;

        PlaceRooms();
        for (int y = 1; y < height - 1; y += 2)
            for (int x = 1; x < width - 1; x += 2)
                if (grid[x, y] == C.Solid && !Grow(start.rect, 1).Contains(new Vector2Int(x, y))) GrowMaze(new Vector2Int(x, y));
        ConnectRegions();
        TrimDeadEnds();
        CollectDoors();
        ComputeDistances();
    }

    void PlaceRooms()
    {
        // 시작 방: 네 모서리 중 한 곳, 5×5
        int corner = R(0, 4);
        start = AddRoom(new RectInt((corner & 1) == 0 ? 1 : width - 6, (corner & 2) == 0 ? 1 : height - 6, 5, 5));

        for (int attempt = 0; attempt < 600 && rooms.Count < maxRooms - 1; attempt++)
        {
            int w = RoomSize(), h = RoomSize();
            var r = new RectInt(R(0, (width - w) / 2) * 2 + 1, R(0, (height - h) / 2) * 2 + 1, w, h);
            if (r.xMax > width - 1 || r.yMax > height - 1) continue;
            if (Grow(start.rect, 2).Overlaps(r)) continue;   // 시작 방 둘레는 비워 둔다 (거대한 문 하나로만 나가게)
            bool clash = false;
            foreach (var o in rooms) if (Grow(o.rect, 1).Overlaps(r)) { clash = true; break; }
            if (!clash) rooms.Add(AddRoom(r));
        }
    }

    int RoomSize() { float v = R01(); return v < 0.45f ? 3 : v < 0.85f ? 5 : 7; }

    Room AddRoom(RectInt r)
    {
        var room = new Room { rect = r, region = regionCount++ };
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++) { grid[x, y] = C.Room; region[x, y] = room.region; }
        return room;
    }

    static RectInt Grow(RectInt r, int m) => new RectInt(r.x - m, r.y - m, r.width + m * 2, r.height + m * 2);
    static Vector2Int Center(RectInt r) => new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);

    // 빈 홀수 칸에서 시작해 막힐 때까지 두 칸씩 파 나가는 미로 (되돌아가며 갈래를 친다)
    void GrowMaze(Vector2Int from)
    {
        int reg = regionCount++;
        void Dig(Vector2Int c) { grid[c.x, c.y] = C.Corridor; region[c.x, c.y] = reg; }
        Dig(from);
        var stack = new List<Vector2Int> { from };
        Vector2Int? lastDir = null;
        while (stack.Count > 0)
        {
            var cell = stack[stack.Count - 1];
            var open = new List<Vector2Int>();
            foreach (var d in Dirs4)
            {
                var n2 = cell + d * 2;
                if (n2.x < 1 || n2.y < 1 || n2.x >= width - 1 || n2.y >= height - 1) continue;
                if (grid[n2.x, n2.y] == C.Solid && !Grow(start.rect, 1).Contains(n2)) open.Add(d);
            }
            if (open.Count == 0) { stack.RemoveAt(stack.Count - 1); lastDir = null; continue; }
            var dir = lastDir != null && open.Contains(lastDir.Value) && R01() < straightness ? lastDir.Value : Pick(open);
            Dig(cell + dir);
            Dig(cell + dir * 2);
            stack.Add(cell + dir * 2);
            lastDir = dir;
        }
    }

    // 서로 다른 구역 사이의 벽 한 칸을 뚫어 하나로 잇는다. 시작 방은 딱 한 번만(거대한 문).
    void ConnectRegions()
    {
        var connectors = new Dictionary<Vector2Int, List<int>>();
        for (int x = 1; x < width - 1; x++)
            for (int y = 1; y < height - 1; y++)
            {
                if (grid[x, y] != C.Solid) continue;
                var regs = new List<int>();
                foreach (var d in Dirs4)
                {
                    int r = region[x + d.x, y + d.y];
                    if (r >= 0 && !regs.Contains(r)) regs.Add(r);
                }
                if (regs.Count >= 2) connectors[new Vector2Int(x, y)] = regs;
            }

        var merged = new int[regionCount];
        for (int i = 0; i < regionCount; i++) merged[i] = i;
        int open = regionCount;
        var list = new List<Vector2Int>(connectors.Keys);
        bool startLinked = false;

        while (open > 1 && list.Count > 0)
        {
            var con = Pick(list);
            var regs = connectors[con];
            bool touchesStart = regs.Contains(start.region);
            OpenConnector(con, touchesStart);
            if (touchesStart) startLinked = true;

            // 이어진 구역들을 하나로 합친다
            int dest = merged[regs[0]];
            var sources = new List<int>();
            foreach (var r in regs) if (merged[r] != dest && !sources.Contains(merged[r])) sources.Add(merged[r]);
            for (int i = 0; i < regionCount; i++) if (sources.Contains(merged[i])) merged[i] = dest;
            open -= sources.Count;

            // 이제 쓸모없어진 연결 칸 정리 (가끔은 고리로 열어 둔다)
            list.RemoveAll(c =>
            {
                if (c == con) return true;
                if (GridNav.Distance(c, con) < 2) return true;   // 문이 나란히 붙지 않게
                var rs = connectors[c];
                if (startLinked && rs.Contains(start.region)) return true;   // 시작 방 출구는 하나뿐
                var roots = new HashSet<int>();
                foreach (var r in rs) roots.Add(merged[r]);
                if (roots.Count > 1) return false;
                if (R01() < extraDoorChance && !NextToOpening(c)) OpenConnector(c, false);
                return true;
            });
        }
    }

    bool NextToOpening(Vector2Int c)
    {
        foreach (var d in GridNav.Dirs8) if (At(c + d) == C.Door) return true;
        return false;
    }

    void OpenConnector(Vector2Int c, bool giant)
    {
        bool byRoom = false;
        foreach (var d in Dirs4) if (At(c + d) == C.Room) byRoom = true;
        grid[c.x, c.y] = byRoom ? C.Door : C.Corridor;
        if (!giant) return;
        doorCell = c;
        foreach (var d in Dirs4)
            if (start.rect.Contains(c - d)) outsideDoor = c + d;   // 시작 방 반대쪽
    }

    // 막다른 길을 메운다. 무작위로 몇 개만 남기고, 남긴 것도 너무 길면 줄인다.
    void TrimDeadEnds()
    {
        var ends = new List<Vector2Int>();
        for (int x = 1; x < width - 1; x++)
            for (int y = 1; y < height - 1; y++)
            {
                var c = new Vector2Int(x, y);
                if (grid[x, y] == C.Corridor && OpenNeighbors(c) <= 1) ends.Add(c);
            }
        Shuffle(ends);
        var keep = new HashSet<Vector2Int>();
        for (int i = 0; i < ends.Count && keep.Count < keepDeadEnds; i++) keep.Add(ends[i]);

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int x = 1; x < width - 1; x++)
                for (int y = 1; y < height - 1; y++)
                {
                    var c = new Vector2Int(x, y);
                    var t = grid[x, y];
                    if ((t != C.Corridor && t != C.Door) || keep.Contains(c) || c == doorCell) continue;
                    if (OpenNeighbors(c) <= 1) { grid[x, y] = C.Solid; changed = true; }
                }
        }

        // 남긴 막다른 길: 갈림길까지 거슬러 올라가 길이를 재고, 길면 끝을 메워 줄인다
        foreach (var tip in keep)
        {
            var chain = new List<Vector2Int> { tip };
            var prev = tip;
            var cur = tip;
            while (true)
            {
                Vector2Int? next = null;
                foreach (var d in Dirs4)
                {
                    var n = cur + d;
                    if (n != prev && IsFloor(n)) next = n;
                }
                if (next == null || At(next.Value) != C.Corridor || OpenNeighbors(next.Value) != 2) break;
                prev = cur;
                cur = next.Value;
                chain.Add(cur);
            }
            int cut = Mathf.Max(0, chain.Count - maxDeadEndLength);
            for (int i = 0; i < cut; i++) grid[chain[i].x, chain[i].y] = C.Solid;
            if (chain.Count - cut >= 2) deadEnds.Add(chain[cut]);   // 너무 짧은 건 상자 자리로 안 쓴다
        }
    }

    void CollectDoors()
    {
        foreach (var r in rooms) r.doors.Clear();
        for (int x = 1; x < width - 1; x++)
            for (int y = 1; y < height - 1; y++)
            {
                if (grid[x, y] != C.Door) continue;
                var c = new Vector2Int(x, y);
                if (c == doorCell) continue;
                foreach (var r in rooms)
                    foreach (var d in Dirs4)
                        if (r.rect.Contains(c + d) && !r.doors.Contains(c)) r.doors.Add(c);
            }
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
                if (!IsFloor(n) || dist.ContainsKey(n) || n == doorCell || start.rect.Contains(n)) continue;
                if (d.x != 0 && d.y != 0 && !(IsFloor(c + new Vector2Int(d.x, 0)) && IsFloor(c + new Vector2Int(0, d.y)))) continue;
                dist[n] = dist[c] + 1;
                maxDist = Mathf.Max(maxDist, dist[n]);
                q.Enqueue(n);
            }
        }
    }

    // ---- 2. 오브젝트 ----

    void BuildObjects()
    {
        root = new GameObject("Generated").transform;
        root.SetParent(transform, false);
        root.gameObject.SetActive(false);   // 몬스터 레벨·문 모양 등을 정한 뒤 한꺼번에 깨운다

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

        var byDistance = new List<Room>(rooms);
        byDistance.Sort((a, b) => RoomDist(a).CompareTo(RoomDist(b)));
        var firstRoom = byDistance[0];
        var lastRoom = byDistance[byDistance.Count - 1];
        var midRoom = byDistance[byDistance.Count / 2];
        if (midRoom == lastRoom && byDistance.Count > 2) midRoom = byDistance[byDistance.Count / 2 - 1];

        // 잠긴 방: 문이 하나뿐인 방 (지나가는 길이 아니라서 잠가도 계단까지 막히지 않는다)
        var lockable = rooms.FindAll(r => r.doors.Count == 1 && r != firstRoom && r != lastRoom && r != midRoom);
        Shuffle(lockable);
        int lockCount = Mathf.Min(lockedRooms, lockable.Count);
        if (lockCount < 2) lockCount = 0;   // 열쇠가 잠긴 문보다 적어야 하므로 둘 이상일 때만
        for (int i = 0; i < lockCount; i++) lockable[i].locked = true;

        BuildDoors();

        // 계단: 가장 먼 방 한가운데
        Put(stairs, Center(lastRoom.rect), Quaternion.Euler(0, 90f * R(0, 4), 0));

        // 상자 (방 안 또는 막다른 길 끝에만)
        var keyChests = new List<ChestEvent>();   // 열쇠를 넣을 수 있는 상자 (잠긴 방 안·횃불 상자 제외)
        void AddChest(GameObject prefab, Room r, bool torch = false)
        {
            var go = Spawn(prefab, FreeCellIn(r.rect, true), RandomYaw());
            if (go == null) return;
            var ch = go.GetComponent<ChestEvent>();
            ch.givesTorch = torch;
            r.hasChest = true;
            if (!r.locked && !torch) keyChests.Add(ch);
        }
        AddChest(chestBig, firstRoom, true);                              // 첫 방: 낡은 횃불 (튜토리얼)
        foreach (var r in rooms) if (r.locked) AddChest(chestElite, r);    // 잠긴 방: 엘리트 상자
        AddChest(chestBig, lastRoom);
        var others = rooms.FindAll(r => !r.locked && r != firstRoom && r != lastRoom && r != midRoom);
        Shuffle(others);
        for (int i = 0; i < Mathf.Min(3, others.Count); i++) AddChest(R01() < 0.5f ? chestBig : chestSmall, others[i]);
        var tipChests = new List<Vector2Int>();
        foreach (var d in deadEnds)
        {
            if (occupied.Contains(d)) continue;
            var go = Spawn(chestSmall, d, RandomYaw());
            if (go != null) { keyChests.Add(go.GetComponent<ChestEvent>()); tipChests.Add(d); }
        }

        // 열쇠: 잠긴 문보다 하나 적게, 잠기지 않은 상자에 나눠 넣는다
        Shuffle(keyChests);
        for (int i = 0; i < Mathf.Max(0, lockCount - 1) && keyChests.Count > 0; i++) keyChests[i % keyChests.Count].keys++;

        // 굶주린 고블린: 중간쯤 되는 방
        Put(goblin, FreeCellIn(midRoom.rect, true), RandomYaw());

        // 벽 횃불 (꺼진 채로): 가장 많은 바닥을 밝힐 수 있는 자리에
        PlaceWallTorches(wallTorches);

        // 몬스터: 상자가 있는 방은 지키는 녀석이 있을 확률이 높다. 깊이에 따라 종류·레벨이 달라진다
        foreach (var r in rooms)
        {
            if (r == firstRoom) continue;   // 첫 방은 숨 돌릴 곳
            float chance = r.locked ? 1f : r.hasChest ? 0.8f : 0.3f;
            if (R01() < chance) SpawnMonster(FreeCellIn(r.rect, false), null, r.locked ? 1 : 0);
            if (r.hasChest && r.rect.width * r.rect.height >= 25 && R01() < 0.4f) SpawnMonster(FreeCellIn(r.rect, false));
        }
        SpawnMonster(FreeCellIn(lastRoom.rect, false), wolf, 2);   // 계단 방 문지기
        foreach (var tip in tipChests)   // 막다른 길 상자 앞을 지키는 녀석
            if (R01() < 0.35f) SpawnMonster(StepBack(tip));
        var corridorCells = new List<Vector2Int>();
        foreach (var kv in dist)
            if (At(kv.Key) == C.Corridor && kv.Value > 10 && !occupied.Contains(kv.Key)) corridorCells.Add(kv.Key);
        for (int i = 0; i < 3 && corridorCells.Count > 0; i++)
        {
            var c = Pick(corridorCells);
            corridorCells.Remove(c);
            SpawnMonster(c);
        }

        // 재료: 미로 곳곳에 나뭇가지·돌멩이 (밟으면 줍는다)
        ScatterPickups("branch", branchPrefab, branches + R(-2, 3));
        ScatterPickups("stone", stonePrefab, stones + R(-2, 3));

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

    int RoomDist(Room r) => dist.TryGetValue(Center(r.rect), out var d) ? d : int.MaxValue / 2;

    // 막다른 길 끝에서 한 칸 안쪽
    Vector2Int? StepBack(Vector2Int tip)
    {
        foreach (var d in Dirs4)
            if (IsFloor(tip + d) && !occupied.Contains(tip + d)) return tip + d;
        return null;
    }

    void PlaceStartRoom()
    {
        var outDir = outsideDoor - doorCell;
        // 거대한 문: 문 칸에 통로 방향으로 세운다
        Put(giantDoor, doorCell, Quaternion.Euler(0, outDir.x != 0 ? 90f : 0f, 0));
        occupied.Add(outsideDoor);

        // 곡괭이·도끼: 문 바로 안쪽 옆
        var inside = doorCell - outDir;
        var side = new Vector2Int(outDir.y, outDir.x);
        Put(tools, inside + side, Quaternion.LookRotation(new Vector3(-outDir.x, 0, -outDir.y)));

        // 플레이어: 방 가운데, 문 쪽을 보고
        Put(player, Center(start.rect) - outDir, Quaternion.LookRotation(new Vector3(outDir.x, 0, outDir.y)));

        // 문을 나서자마자 "너무 어둡다"
        Put(darkTrigger, outsideDoor, Quaternion.identity);

        // 모루(대장간): 카메라 반대쪽(+x+z) 벽가 칸 중 비어 있고 문 앞이 아닌 곳 — 앞쪽 벽에 가려지지 않게
        Vector2Int? anvilCell = null;
        for (int x = start.rect.xMin; x < start.rect.xMax; x++)
            for (int y = start.rect.yMin; y < start.rect.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                bool edge = x == start.rect.xMax - 1 || y == start.rect.yMax - 1;
                if (!edge || occupied.Contains(c) || GridNav.Distance(c, doorCell) <= 1 || GridNav.Distance(c, Center(start.rect) - outDir) <= 1) continue;
                if (anvilCell == null || c.x + c.y > anvilCell.Value.x + anvilCell.Value.y) anvilCell = c;
            }
        if (anvilCell == null) return;
        occupied.Add(anvilCell.Value);
        var toCenter = (Vector2)(Center(start.rect) - anvilCell.Value);
        SpawnAnvil(anvilCell.Value, Quaternion.LookRotation(new Vector3(toCenter.x, 0, toCenter.y)));
    }

    void BuildDoors()
    {
        var doorRoot = Group("Doors");
        foreach (var r in rooms)
            foreach (var c in r.doors)
            {
                if (occupied.Contains(c)) continue;   // 방 두 개 사이의 문은 한 번만
                occupied.Add(c);
                bool alongX = IsFloor(c + Vector2Int.left) && IsFloor(c + Vector2Int.right);
                int toRoom = alongX ? (r.rect.Contains(c + Vector2Int.right) ? 1 : -1) : (r.rect.Contains(c + Vector2Int.up) ? 1 : -1);
                var go = new GameObject("Door");
                go.transform.SetParent(doorRoot, false);
                go.transform.position = GridTile.CellToWorld(c);
                var door = go.AddComponent<Door>();
                door.Build(r.locked || R01() < ironDoorChance ? Door.Kind.Iron : Door.Kind.Wood, r.locked, alongX, toRoom);
            }
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

    // 방 안의 빈 칸. edge면 벽에 붙은 칸을 고르되, 문 앞은 막지 않는다
    Vector2Int? FreeCellIn(RectInt r, bool edge)
    {
        var cand = new List<Vector2Int>();
        var any = new List<Vector2Int>();
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                if (occupied.Contains(c) || BlocksEntrance(c, r)) continue;
                any.Add(c);
                bool onEdge = x == r.xMin || y == r.yMin || x == r.xMax - 1 || y == r.yMax - 1;
                if (edge == onEdge) cand.Add(c);
            }
        if (cand.Count > 0) return Pick(cand);
        if (any.Count > 0) return Pick(any);
        return null;
    }

    // 방 밖의 바닥(문·통로)과 맞닿은 칸은 비워 둔다
    bool BlocksEntrance(Vector2Int c, RectInt r)
    {
        foreach (var d in GridNav.Dirs8)
        {
            var n = c + d;
            if (!r.Contains(n) && IsFloor(n)) return true;
        }
        return false;
    }

    GameObject SpawnMonster(Vector2Int? cell, GameObject kind = null, int levelBonus = 0)
    {
        if (cell == null || !dist.TryGetValue(cell.Value, out var d)) return null;
        float depth = maxDist > 0 ? (float)d / maxDist : 0f;
        if (kind == null)
        {
            if (depth < 0.35f) kind = R01() < 0.6f ? rat : bat;
            else if (depth < 0.7f) kind = Pick(new[] { rat, bat, slime });
            else kind = R01() < 0.55f ? wolf : slime;
        }
        int level = kind == wolf && levelBonus >= 2 ? levelBonus : 1 + (depth > 0.6f && R01() < 0.5f ? 1 : 0) + levelBonus;
        var go = Spawn(kind, cell, Quaternion.Euler(0, R(0, 360), 0));
        if (go != null) go.GetComponent<Monster>().level = level;   // 아직 잠든 상태(Awake 전)라 능력치에 반영된다
        return go;
    }

    // 벽 횃불: 아직 안 밝힌 바닥을 가장 많이 비추는 자리부터 (상위 몇 곳 중 무작위라 매번 조금씩 다르다)
    void PlaceWallTorches(int count)
    {
        if (wallTorchPrefab == null) return;
        const float Radius = 2.5f;   // WallTorch.radiusCells 와 같게
        var lit = new HashSet<Vector2Int>();
        var cands = new List<(Vector2Int cell, Vector2Int wall)>();
        foreach (var kv in dist)
        {
            var c = kv.Key;
            if (At(c) == C.Door || occupied.Contains(c) || NextToDoor(c)) continue;
            foreach (var d in Dirs4) if (!IsFloor(c + d)) cands.Add((c, d));
        }
        int NewlyLit(Vector2Int c)
        {
            int n = 0;
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                {
                    var o = c + new Vector2Int(dx, dy);
                    if (dx * dx + dy * dy <= Radius * Radius && IsFloor(o) && !lit.Contains(o)) n++;
                }
            return n;
        }
        for (int n = 0; n < count && cands.Count > 0; n++)
        {
            var scored = new List<(int score, int index)>();
            for (int i = 0; i < cands.Count; i++) scored.Add((NewlyLit(cands[i].cell), i));
            scored.Sort((a, b) => b.score.CompareTo(a.score));
            if (scored[0].score < 6) break;   // 더 놓아도 밝힐 곳이 별로 없다
            var pick = cands[scored[R(0, Mathf.Min(4, scored.Count))].index];
            Spawn(wallTorchPrefab, pick.cell, Quaternion.LookRotation(new Vector3(pick.wall.x, 0, pick.wall.y)));
            for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    if (dx * dx + dy * dy <= Radius * Radius) lit.Add(pick.cell + new Vector2Int(dx, dy));
            cands.RemoveAll(c => c.cell == pick.cell);
        }
    }

    bool NextToDoor(Vector2Int c)
    {
        foreach (var d in GridNav.Dirs8) if (At(c + d) == C.Door) return true;
        return false;
    }

    // 나뭇가지·돌멩이를 미로 곳곳(시작 방 밖)의 빈 칸에 흩어 놓는다
    void ScatterPickups(string id, GameObject prefab, int count)
    {
        var cells = new List<Vector2Int>();
        foreach (var kv in dist)
            if (At(kv.Key) != C.Door && !occupied.Contains(kv.Key) && kv.Value > 2) cells.Add(kv.Key);
        for (int i = 0; i < count && cells.Count > 0; i++)
        {
            var c = Pick(cells);
            cells.Remove(c);
            occupied.Add(c);
            GameObject go;
            if (prefab != null) go = Instantiate(prefab, GridTile.CellToWorld(c), Quaternion.Euler(0, R(0, 360), 0), root);
            else go = FallbackPickup(id, c);
            var p = go.GetComponent<Pickup>() ?? go.AddComponent<Pickup>();
            p.itemId = id;
            p.triggerOnEnter = true;
        }
    }

    // 3D 모델이 없을 때 쓰는 간단한 모양
    GameObject FallbackPickup(string id, Vector2Int c)
    {
        var go = new GameObject(id == "stone" ? "Stone" : "Branch");
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(GridTile.CellToWorld(c), Quaternion.Euler(0, R(0, 360), 0));
        for (int i = 0; i < 3; i++)
        {
            var part = GameObject.CreatePrimitive(id == "stone" ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
            Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(go.transform, false);
            if (id == "stone")
            {
                part.transform.localPosition = new Vector3((i - 1) * 0.45f, 0.18f, (i % 2) * 0.3f);
                part.transform.localScale = new Vector3(0.5f, 0.35f, 0.45f);
            }
            else
            {
                part.transform.localPosition = new Vector3(0, 0.08f + i * 0.05f, (i - 1) * 0.25f);
                part.transform.localRotation = Quaternion.Euler(90, i * 35f, 0);
                part.transform.localScale = new Vector3(0.12f, 0.8f, 0.12f);
            }
            part.GetComponent<Renderer>().material.color = id == "stone" ? new Color(0.45f, 0.44f, 0.42f) : new Color(0.4f, 0.27f, 0.15f);
        }
        return go;
    }

    void SpawnAnvil(Vector2Int c, Quaternion rot)
    {
        GameObject go;
        if (anvilPrefab != null) go = Instantiate(anvilPrefab, GridTile.CellToWorld(c), rot, root);
        else
        {
            go = new GameObject("Anvil");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(GridTile.CellToWorld(c), rot);
            var stump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stump.transform.SetParent(go.transform, false);
            stump.transform.localPosition = new Vector3(0, 0.5f, 0);
            stump.transform.localScale = new Vector3(1.4f, 0.5f, 1.4f);
            stump.GetComponent<Renderer>().material.color = new Color(0.35f, 0.22f, 0.12f);
            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.transform.SetParent(go.transform, false);
            top.transform.localPosition = new Vector3(0, 1.25f, 0);
            top.transform.localScale = new Vector3(0.9f, 0.5f, 2f);
            top.GetComponent<Renderer>().material.color = new Color(0.22f, 0.22f, 0.24f);
        }
        if (go.GetComponent<Anvil>() == null) go.AddComponent<Anvil>();
    }

    // ---- 다른 스크립트가 쓰는 것 ----

    // 이전 회차의 뼈: 지난 자리가 이번 미로에서도 바닥이면 그대로, 아니면 아무 방에 (회차마다 같은 방식으로)
    public Vector2Int PlaceBones(Vector2Int lastCell, int run)
    {
        if (At(lastCell) == C.Room || At(lastCell) == C.Corridor)
            if (!Grow(start.rect, 1).Contains(lastCell) && !occupied.Contains(lastCell)) return lastCell;
        var r = new System.Random(Seed ^ (run * 7919));
        for (int i = 0; i < 50; i++)
        {
            var room = rooms[r.Next(rooms.Count)];
            if (room.locked) continue;
            var c = new Vector2Int(r.Next(room.rect.xMin, room.rect.xMax), r.Next(room.rect.yMin, room.rect.yMax));
            if (!occupied.Contains(c)) return c;
        }
        return Center(rooms[0].rect);
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
                var d = Door.At(c);
                sb.Append(c == doorCell ? 'G' : d != null ? (d.locked ? 'L' : d.kind == Door.Kind.Iron ? 'I' : 'W')
                    : grid[x, y] == C.Room ? '.' : grid[x, y] == C.Corridor ? ',' : grid[x, y] == C.Door ? '+' : '#');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
