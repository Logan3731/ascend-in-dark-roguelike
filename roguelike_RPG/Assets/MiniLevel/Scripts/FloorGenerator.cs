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

    // 개발자 모드 순간이동 자리 (없는 층이면 null)
    public Vector2Int? StairsCell { get; private set; }
    public Vector2Int? BossDoorCell { get; private set; }       // 엘리트 층: 엘리트 방으로 들어가기 직전
    public Vector2Int? SemiBossDoorCell { get; private set; }   // 준보스 층: 준보스 방 문 바깥

    // 작고 군더더기 없는 미로: 방마다 무언가 있고, 방과 방 사이 통로는 짧고 곧게.
    [Header("크기 (홀수)")]
    public int mazeWidth = 17, mazeHeight = 17;
    public int roomCount = 7;
    public int keepDeadEnds = 3;        // 남길 막다른 길 (끝에 상자)
    public int maxDeadEndLength = 4;    // 남긴 막다른 길이 너무 길면 이만큼으로 줄인다
    [Range(0f, 0.3f)] public float extraDoorChance = 0.03f;   // 고리를 만드는 여분의 문
    [Range(0f, 1f)] public float straightness = 0.75f;         // 통로가 곧게 이어질 확률 (낮을수록 구불구불)
    public int lockedRooms = 2;
    [Range(0f, 1f)] public float ironDoorChance = 0.3f;
    public int seed;   // 0이면 매번 무작위

    [Header("프리팹")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject wallTorchPrefab;
    public GameObject chestSmall, chestBig, chestElite;
    public GameObject rat, bat, slime, wolf;
    public GameObject anvilPrefab, branchPrefab, stonePrefab, potPrefab;
    public GameObject bookshelfPrefab, coffinPrefab, wellPrefab;

    [Header("배치 수")]
    public int wallTorches = 7;
    public int branches = 8, stones = 6;
    public int pots = 9;
    public int bookshelves = 2, coffins = 2, wells = 1, secretRooms = 1;

    // 방의 몬스터: 한 마리는 반드시, 두 번째는 이 확률 (층마다 +5%)
    public const float SecondMonsterChance = 0.3f, SecondMonsterChest = 0.5f;

    // ---- 맵 환경 (설계 A6) ----
    public const int ExtraSigns = 1;                  // 계단 방 말고 더 세우는 팁 표지판
    public const int GrassRooms = 3;                  // 풀이 자라는 방 수
    public const int GrassMin = 2, GrassMax = 4;      // 그 방의 풀 포기 수
    public const float FloorVariantChance = 0.45f;    // 방 바닥이 다른 종류(이끼·자갈)일 확률 (통로는 기본 돌바닥)
    public const int BonusGoldPiles = 5;              // 1층 보너스 방: 골드 더미 수
    public const int BonusGoldMin = 8, BonusGoldMax = 16;
    public const int WaterRooms = 2;                  // 물웅덩이가 있는 방 수
    public const int WaterMin = 2, WaterMax = 5;      // 웅덩이 크기(칸)

    [Header("씬에 있는 것 (위치만 옮긴다)")]
    public Transform player;
    public Transform giantDoor;
    public Transform tools;
    public Transform goblin;
    public Transform stairs;
    public Transform darkTrigger;   // 문을 나서면 "너무 어둡다" 안내

    public int Seed { get; private set; }

    enum C { Solid, Room, Corridor, Door }

    int width, height;

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
    readonly Dictionary<Vector2Int, GameObject> floorTiles = new Dictionary<Vector2Int, GameObject>();   // 시야(FogOfWar)가 쓴다
    readonly Dictionary<Vector2Int, GameObject> wallTiles = new Dictionary<Vector2Int, GameObject>();

    void Awake()
    {
        Instance = this;
        width = mazeWidth;
        height = mazeHeight;
        Seed = seed != 0 ? seed : System.Environment.TickCount;
        rng = new System.Random(Seed);
        if (!RunState.Continuing) Potions.NewRun(Seed);   // 새 회차에만 물약 색과 효과의 짝을 새로 섞는다 (층을 올라도 그대로)
        if (RunState.IsBossFloor)
        {
            width = EliteWidth; height = EliteHeight;
            GenerateElite();
            BuildEliteObjects();
        }
        else
        {
            Generate();
            BuildObjects();
        }
        GridNav.Invalidate();
        // 시야: 벽(낮은 벽 포함)과 닫힌 문이 시야를 막는다
        // (숨겨진 무너진 벽도 발견하기 전까지는 벽처럼 시야를 막는다)
        gameObject.AddComponent<FogOfWar>().Init(floorTiles, wallTiles, c => At(c) == C.Solid || Rubble.HiddenAt(c) || Grass.BlocksSight(c), new[] { giantDoor });
        gameObject.AddComponent<SecretSearch>();   // 매 턴 주변의 숨겨진 함정·무너진 벽을 확률로 찾는다
        Sfx.StartAmbience();
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
    bool Inner(Vector2Int c) => c.x >= 1 && c.y >= 1 && c.x < width - 1 && c.y < height - 1;   // 맵 테두리 안쪽
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
        OpenStartSides();                // 시작 방의 맵 끝이 아닌 세 면에 모두 문 (시작부터 길을 고른다)
        CarveSecretRooms(secretRooms);   // 무너진 벽 너머의 숨겨진 방 (곡괭이로 판다)
        CollectDoors();
        ComputeDistances();
    }

    void PlaceRooms()
    {
        // 시작 방: 네 변 중 한 곳의 가운데, 5×5. 등 뒤는 맵 끝이고 나머지 세 면에 문이 난다 (OpenStartSides)
        int side = R(0, 4);
        int midX = ((width - 5) / 2) | 1, midY = ((height - 5) / 2) | 1;   // 미로 칸(홀수)에 맞춘다
        var sr = side == 0 ? new RectInt(midX, 1, 5, 5) : side == 1 ? new RectInt(midX, height - 6, 5, 5)
               : side == 2 ? new RectInt(1, midY, 5, 5) : new RectInt(width - 6, midY, 5, 5);
        start = AddRoom(sr);

        for (int attempt = 0; attempt < 600 && rooms.Count < roomCount - 1; attempt++)
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

    int RoomSize() { float v = R01(); return v < 0.5f ? 3 : v < 0.9f ? 5 : 7; }

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

    // 시작 방의 세 면(맵 끝 쪽 빼고)에 문이 하나씩 있게 한다. 거대한 문이 있는 면은 그대로 두고,
    // 나머지 면은 바깥이 바닥인 칸을 골라 나무문을 낸다. 바깥이 막혀 있으면 바닥이 나올 때까지 통로를 판다.
    readonly List<Vector2Int> startDoors = new List<Vector2Int>();

    void OpenStartSides()
    {
        var r = start.rect;
        foreach (var d in Dirs4)
        {
            // 이 면의 바깥 줄 (모서리 제외)
            var cells = new List<Vector2Int>();
            if (d.x != 0) { int x = d.x > 0 ? r.xMax : r.xMin - 1; for (int y = r.yMin; y < r.yMax; y++) cells.Add(new Vector2Int(x, y)); }
            else { int y = d.y > 0 ? r.yMax : r.yMin - 1; for (int x = r.xMin; x < r.xMax; x++) cells.Add(new Vector2Int(x, y)); }
            if (!Inner(cells[0])) continue;                                  // 맵 끝 쪽 (등 뒤)
            if (cells.Exists(c => c == doorCell || IsFloor(c))) continue;   // 이미 문이 있다 (거대한 문)

            var mid = cells[cells.Count / 2];
            cells.Sort((a, b) => GridNav.Distance(a, mid).CompareTo(GridNav.Distance(b, mid)));   // 가운데에 가까운 칸부터
            int found = cells.FindIndex(c => IsFloor(c + d));
            var pick = found >= 0 ? cells[found] : mid;
            if (found < 0)
            {
                // 바깥이 바위: 가운데에서 바닥이 나올 때까지 곧게 판다
                for (var c = mid + d; Inner(c) && !IsFloor(c); c += d) grid[c.x, c.y] = C.Corridor;
            }
            grid[pick.x, pick.y] = C.Door;
            startDoors.Add(pick);
        }
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

        BuildTiles();
        PlaceStartRoom();
        PlaceWater();   // 물 칸은 바닥을 깔 때 정했다 — 다른 것을 놓기 전에 자리를 막는다

        // ---- 튜토리얼 순서: 문에서 가까운 방부터 하나씩 배우게 채운다 ----
        // 시작 방(항아리·재료·모루) → 첫 방(횃불 상자·잠든 쥐) → 물약 방(책장·잠든 슬라임) → 상인 방(고블린·우물)
        // → 열쇠 방(열쇠 상자·관) → 잠긴 방(엘리트 상자) → 계단 방(늑대 문지기). 방마다 처음 들어서면 안내가 한 번 뜬다.
        var byDistance = new List<Room>(rooms);
        byDistance.Sort((a, b) => RoomDist(a).CompareTo(RoomDist(b)));
        var firstRoom = byDistance[0];
        var lastRoom = byDistance[byDistance.Count - 1];

        // 잠긴 방: 문이 하나뿐인 방 (지나가는 길이 아니라서 잠가도 계단까지 막히지 않는다).
        // 튜토리얼 방(가까운 셋)은 빼고 먼 쪽부터 — 열쇠를 먼저 얻은 뒤에 만나게
        var lockable = byDistance.FindAll(r => r.doors.Count == 1 && r != firstRoom && r != lastRoom && byDistance.IndexOf(r) > 3);
        if (lockable.Count < 2) lockable = byDistance.FindAll(r => r.doors.Count == 1 && r != firstRoom && r != lastRoom);
        lockable.Reverse();
        int lockCount = Mathf.Min(lockedRooms, lockable.Count);
        if (lockCount < 2) lockCount = 0;   // 열쇠가 잠긴 문보다 적어야 하므로 둘 이상일 때만
        for (int i = 0; i < lockCount; i++) lockable[i].locked = true;

        var lessons = byDistance.FindAll(r => r != firstRoom && r != lastRoom && !r.locked);
        Room potionRoom = lessons.Count > 0 ? lessons[0] : null;
        Room shopRoom = lessons.Count > 1 ? lessons[1] : firstRoom;
        Room keyRoom = lessons.Count > 2 ? lessons[2] : null;

        BuildDoors();

        // 계단: 가장 먼 방 한가운데
        Put(stairs, Center(lastRoom.rect), Quaternion.Euler(0, 90f * R(0, 4), 0));
        StairsCell = Center(lastRoom.rect);

        // 시작 방: 반드시 무언가 나오는 항아리 하나 + 나뭇가지 둘·돌멩이 하나 → 깨 보고, 줍고, 모루에서 만들어 보게
        var startPot = FreeBackWallCell(start.rect);
        if (startPot != null) SpawnPot(startPot.Value, true);
        for (int i = 0; i < 3; i++)
        {
            var c = FreeCellIn(start.rect, false);
            if (c != null) SpawnPickup(i < 2 ? "branch" : "stone", i < 2 ? branchPrefab : stonePrefab, c.Value);
        }

        // 상자 (방 안 또는 막다른 길 끝에만)
        var keyChests = new List<ChestEvent>();   // 열쇠를 넣을 수 있는 상자 (잠긴 방 안·횃불 상자 제외)
        ChestEvent AddChest(GameObject prefab, Room r, bool torch = false)
        {
            var go = Spawn(prefab, FreeCellIn(r.rect, true), RandomYaw());
            if (go == null) return null;
            var ch = go.GetComponent<ChestEvent>();
            ch.givesTorch = torch;
            r.hasChest = true;
            if (!r.locked && !torch) keyChests.Add(ch);
            return ch;
        }
        AddChest(chestBig, firstRoom, true);                              // 첫 방: 낡은 횃불 (튜토리얼)
        foreach (var r in rooms) if (r.locked) AddChest(chestElite, r);    // 잠긴 방: 엘리트 상자
        AddChest(chestBig, lastRoom);
        var keyChest = keyRoom != null ? AddChest(chestSmall, keyRoom) : null;
        var others = rooms.FindAll(r => !r.locked && r != firstRoom && r != lastRoom && r != shopRoom && r != keyRoom);
        Shuffle(others);
        for (int i = 0; i < Mathf.Min(2, others.Count); i++) AddChest(R01() < 0.5f ? chestBig : chestSmall, others[i]);
        var tipChests = new List<Vector2Int>();
        foreach (var d in deadEnds)
        {
            if (occupied.Contains(d)) continue;
            var go = Spawn(chestSmall, d, RandomYaw());
            if (go != null) { keyChests.Add(go.GetComponent<ChestEvent>()); tipChests.Add(d); }
        }

        // 열쇠: 잠긴 문보다 하나 적게. 튜토리얼 열쇠 방의 상자에 넣는다 (없으면 아무 잠기지 않은 상자에)
        int keyCount = Mathf.Max(0, lockCount - 1);
        if (keyChest != null) keyChest.keys = keyCount;
        else
        {
            Shuffle(keyChests);
            for (int i = 0; i < keyCount && keyChests.Count > 0; i++) keyChests[i % keyChests.Count].keys++;
        }

        // 굶주린 고블린: 상인이 있는 층(1·4·9층)의 상인 방에만
        bool merchant = RunState.IsMerchantFloor;
        if (merchant) Put(goblin, FreeCellIn(shopRoom.rect, true), RandomYaw());
        else if (goblin != null) goblin.gameObject.SetActive(false);
        bool tutorial = RunState.IsTutorial;

        // 벽 횃불 (꺼진 채로): 가장 많은 바닥을 밝힐 수 있는 자리에
        PlaceWallTorches(wallTorches);

        // 몬스터 (15번 문서 4절): 층별 종류와 수. 방마다 한 마리는 반드시, 남는 수는 두 번째 자리·통로에.
        // 상인 방은 안전. 3·8층은 가장 큰 방에 준보스 혼자 (계단 방·상인 방은 아니다).
        var semiRoom = PlaceSemiBoss(merchant ? shopRoom : null, lastRoom);
        var (cmin, cmax) = MonsterRoster.CountOn(RunState.Floor);
        int target = R(cmin, cmax + 1);
        spawnedMonsters = 0;
        if (tutorial)
        {
            SpawnMonster(FreeCellIn(firstRoom.rect, false), MonsterRoster.Species.Rat, 0, 1f, false);
            if (potionRoom != null) SpawnMonster(FreeCellIn(potionRoom.rect, false), MonsterRoster.Species.Rat, 0, 1f, false);
        }
        SpawnMonster(FreeCellIn(lastRoom.rect, false), null, 1, 0.5f);   // 계단 방 문지기
        var filled = new List<Room>();
        foreach (var r in rooms)
        {
            if (tutorial && (r == firstRoom || r == potionRoom)) continue;
            if (merchant && r == shopRoom) continue;   // 상인 방은 안전
            if (r == lastRoom || r == semiRoom) continue;
            if (SpawnMonster(FreeCellIn(r.rect, false), null, r.locked ? 1 : 0) != null) filled.Add(r);
        }
        // 남는 수: 상자 방·잠긴 방부터 두 번째 자리, 그래도 남으면 통로를 떠도는 녀석 (깨어 있다)
        Shuffle(filled);
        filled.Sort((a, b) => (b.hasChest || b.locked).CompareTo(a.hasChest || a.locked));
        foreach (var r in filled)
            if (spawnedMonsters < target) SpawnMonster(FreeCellIn(r.rect, false));
        var corridorCells = new List<Vector2Int>();
        foreach (var kv in dist)
            if (At(kv.Key) == C.Corridor && kv.Value > 6 && !occupied.Contains(kv.Key)) corridorCells.Add(kv.Key);
        while (spawnedMonsters < target && corridorCells.Count > 0)
        {
            var c = Pick(corridorCells);
            corridorCells.Remove(c);
            SpawnMonster(c, null, 0, 0f);
        }

        // 방 소품: 책장은 물약 방, 관은 열쇠 방, 우물은 상인 방에 먼저 (나머지는 무작위), 그리고 숨겨진 방
        PlaceProps<Bookshelf>(bookshelfPrefab, bookshelves, true, potionRoom);
        PlaceProps<Coffin>(coffinPrefab, coffins, true, keyRoom);
        PlaceProps<Well>(wellPrefab, wells, false, shopRoom);
        BuildSecretRooms();

        // 물약 방: 반드시 무언가 나오는 항아리 하나 (물약을 일찍 손에 쥐게)
        if (potionRoom != null)
        {
            var c = FreeCellIn(potionRoom.rect, true);
            if (c != null) SpawnPot(c.Value, true);
        }

        // 함정 (설계 A4): 1층은 보이는 가시 함정 하나로 안내만, 2층부터 층마다 늘어난다
        PlaceTraps(tutorial);

        // 재료: 미로 곳곳에 나뭇가지·돌멩이 (밟으면 줍는다)
        ScatterPickups("branch", branchPrefab, branches + R(-2, 3));
        ScatterPickups("stone", stonePrefab, stones + R(-2, 3));

        // 항아리: 방 가장자리에 (깨 보면 무언가 나올지도)
        ScatterPots(pots + R(-3, 4));

        // 방마다 눌러 볼 것이 하나는 있게: 아무것도 없는 방엔 항아리
        foreach (var r in rooms)
        {
            bool any = false;
            for (int x = r.rect.xMin; x < r.rect.xMax && !any; x++)
                for (int y = r.rect.yMin; y < r.rect.yMax && !any; y++)
                    if (occupied.Contains(new Vector2Int(x, y))) any = true;
            if (any) continue;
            var c = FreeCellIn(r.rect, true);
            if (c != null) SpawnPot(c.Value, false);
        }

        // 안내 (한 번 본 것은 다음 회차에 다시 뜨지 않는다)
        AddHint("start", start.rect, "항아리와 상자는 클릭하면 연다. 나뭇가지와 돌멩이는 밟으면 줍는다. 모루에서 무기와 횃불을 만들 수 있다.");
        AddHint("first", firstRoom.rect, "잠든 적을 먼저 때리면 기습! [Q] 3연격, [W] 점프 찍기.");
        if (potionRoom != null) AddHint("potion", potionRoom.rect, "물약은 마시거나 던질 수 있다. 책장을 뒤지면 물약의 정체를 알 수도 있다.");
        if (shopRoom != firstRoom) AddHint("shop", shopRoom.rect, "고블린 상인에게 골드로 물건을 살 수 있다.");
        if (keyRoom != null && keyCount > 0) AddHint("key", keyRoom.rect, "이 방의 상자에 녹슨 열쇠가 있다. 잠긴 철문 하나를 열 수 있다.");
        AddHint("stairs", lastRoom.rect, "위로 오르는 계단이다.");

        // 팁 표지판: 계단 방엔 반드시 하나, 그 밖에 몇 방에 더
        PlaceSign(lastRoom.rect);
        var signCands = rooms.FindAll(r => r != lastRoom && !r.locked);
        Shuffle(signCands);
        for (int i = 0, n = 0; i < signCands.Count && n < ExtraSigns; i++) if (PlaceSign(signCands[i].rect)) n++;

        // 풀숲: 몇몇 방에 몇 포기씩
        PlaceGrass();

        root.gameObject.SetActive(true);
    }

    Transform Group(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    // ---- 벽 (Darkness Survival처럼) ----
    // 카메라는 -x·-z 쪽에서 +x·+z 쪽을 내려다본다. 벽 너머(+x·+z, 대각 포함)에 바닥이 있는 벽은 그 바닥을 가리므로
    // 낮은 벽(허리 높이)으로 세운다. 너머에 아무것도 없는 바깥 벽만 높게. 벽의 윗면은 모두 까맣게 칠한다.
    const float LowWallHeight = 1.6f;
    static Material wallTopMat;

    bool IsFrontWall(Vector2Int c) =>
        !IsFloor(c) && (IsFloor(c + Vector2Int.right) || IsFloor(c + Vector2Int.up) || IsFloor(c + Vector2Int.one));

    // 밟을 수 있는 칸에서 d 방향이 높은 벽인가 — 벽 횃불·책장·관은 여기에만 붙인다
    bool IsBackWall(Vector2Int floor, Vector2Int d) => At(floor + d) == C.Solid && !IsFrontWall(floor + d);

    // low: 낮은 벽으로 세울지 (null이면 IsFrontWall로 정한다 — 숨겨진 무너진 벽의 가짜 벽은 직접 정한다)
    GameObject BuildWall(Vector2Int c, Transform parent, bool? low = null)
    {
        var go = Instantiate(wallPrefab, GridTile.CellToWorld(c), Quaternion.identity, parent);
        go.AddComponent<WallTile>();   // 누르면 빨간 표시 (GridClickMover)
        float h = GridTile.CellSize;
        if (low ?? IsFrontWall(c))
        {
            // 프리팹의 벽돌 덩어리(자식)를 낮게 눌러 앉힌다
            float k = LowWallHeight / GridTile.CellSize;
            foreach (Transform child in go.transform)
            {
                var sc = child.localScale; var pos = child.localPosition;
                child.localScale = new Vector3(sc.x, sc.y * k, sc.z);
                child.localPosition = new Vector3(pos.x, pos.y * k, pos.z);
            }
            go.name = "WallLow";
            h = LowWallHeight;
        }
        if (wallTopMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            wallTopMat = new Material(sh) { color = Color.black };
            if (wallTopMat.HasProperty("_BaseColor")) wallTopMat.SetColor("_BaseColor", Color.black);
        }
        var top = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(top.GetComponent<Collider>());
        top.name = "Top";
        top.transform.SetParent(go.transform, false);
        top.transform.localPosition = new Vector3(0, h + 0.02f, 0);
        top.transform.localRotation = Quaternion.Euler(90, 0, 0);
        top.transform.localScale = new Vector3(GridTile.CellSize, GridTile.CellSize, 1);
        top.GetComponent<Renderer>().sharedMaterial = wallTopMat;
        return go;
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
        if (giantDoor != null) giantDoor.gameObject.SetActive(false);   // 거대한 문은 쓰지 않는다 (시작 방 출구는 그냥 트인 길)
        occupied.Add(outsideDoor);

        // 플레이어: 방 가운데, 문 쪽을 보고
        Put(player, Center(start.rect) - outDir, Quaternion.LookRotation(new Vector3(outDir.x, 0, outDir.y)));

        // 곡괭이·도끼: 문 바로 안쪽 옆
        var inside = doorCell - outDir;
        var side = new Vector2Int(outDir.y, outDir.x);
        Vector2Int? toolCell = null;   // 문 옆 칸이 막혀 있으면 반대쪽 옆, 그래도 안 되면 방 안 아무 벽가
        foreach (var c in new[] { inside + side, inside - side })
            if (toolCell == null && IsFloor(c) && start.rect.Contains(c) && !occupied.Contains(c)) toolCell = c;
        if (toolCell == null) toolCell = FreeCellIn(start.rect, true);
        if (RunState.IsTutorial) Put(tools, toolCell, Quaternion.LookRotation(new Vector3(-outDir.x, 0, -outDir.y)));
        else if (tools != null) tools.gameObject.SetActive(false);   // 입구의 도구는 1층에만

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
        // 시작 방 옆면의 나무문 (거대한 문 말고 두 개)
        foreach (var c in startDoors)
        {
            if (occupied.Contains(c)) continue;
            occupied.Add(c);
            bool alongX = IsFloor(c + Vector2Int.left) && IsFloor(c + Vector2Int.right);
            int toRoom = alongX ? (start.rect.Contains(c + Vector2Int.right) ? 1 : -1) : (start.rect.Contains(c + Vector2Int.up) ? 1 : -1);
            var go = new GameObject("StartDoor");
            go.transform.SetParent(doorRoot, false);
            go.transform.position = GridTile.CellToWorld(c);
            go.AddComponent<Door>().Build(Door.Kind.Wood, false, alongX, toRoom);
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

    // 뒷벽(세워진 벽)에 붙은 빈 칸 — 키 큰 소품이 앞을 가리지 않게. 없으면 아무 빈 칸.
    Vector2Int? FreeBackWallCell(RectInt r)
    {
        var cand = new List<Vector2Int>();
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                if (occupied.Contains(c) || BlocksEntrance(c, r)) continue;
                foreach (var d in Dirs4) if (IsBackWall(c, d)) { cand.Add(c); break; }
            }
        return cand.Count > 0 ? Pick(cand) : FreeCellIn(r, true);
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

    // 층이 오를수록: 레벨이 오르고(10층이면 +7), 깊은 곳의 종족(슬라임·늑대)이 앞으로 나오고, 덜 잠들어 있고, 정예가 섞인다.
    // 3층부터 슬라임 일부는 침 뱉는 슬라임(거리를 두고 예고한 칸에 산성 침).
    int FloorLevel => (RunState.Floor - 1) * 4 / 5;

    int spawnedMonsters, corpsesHere;

    // 일반 몬스터 하나 (species가 null이면 이 층 후보에서 뽑는다). 15번 문서의 종 수치를 넣는다.
    GameObject SpawnMonster(Vector2Int? cell, MonsterRoster.Species? species = null, int levelBonus = 0, float sleepChance = 0.85f, bool allowElite = true)
    {
        if (cell == null || !dist.ContainsKey(cell.Value)) return null;
        int fl = RunState.Floor;
        var s = species ?? PickSpecies(fl);
        var go = BuildBody(MonsterRoster.All[s].body, cell.Value);
        if (go == null) return null;
        var m = go.GetComponent<Monster>();
        MonsterRoster.Apply(m, s, fl, levelBonus);   // 아직 깨어나기(Awake) 전이라 능력치에 반영된다
        if (s == MonsterRoster.Species.Corpse) corpsesHere++;
        if (allowElite && fl >= 2 && R01() < 0.06f + 0.035f * (fl - 2))
        {
            m.elite = true;
            m.affix = (Monster.Affix)R(1, 6);
        }
        if (R01() < sleepChance * (1f - 0.04f * (fl - 1))) m.FallAsleep();   // 대부분 잠들어 있다 → 몰래 다가가 기습
        spawnedMonsters++;
        return go;
    }

    // 이 층 후보 중 비중대로. 6층부터(5층 엘리트를 쓰러뜨린 뒤)는 신규 무리가 SkeletonShare만큼, 기존 종은 나머지
    MonsterRoster.Species PickSpecies(int floor)
    {
        var pool = MonsterRoster.PoolOn(floor);
        pool.RemoveAll(sp => MonsterRoster.All[sp].maxPerFloor <= (sp == MonsterRoster.Species.Corpse ? corpsesHere : 0));
        if (RunState.SkeletonFloors)
        {
            bool newOnes = R01() < SkeletonShare;
            var part = pool.FindAll(sp => MonsterRoster.All[sp].newGroup == newOnes);
            if (part.Count > 0) pool = part;
        }
        float total = 0f;
        foreach (var sp in pool) total += MonsterRoster.All[sp].weight;
        float roll = R01() * total;
        foreach (var sp in pool) { roll -= MonsterRoster.All[sp].weight; if (roll <= 0f) return sp; }
        return pool[pool.Count - 1];
    }

    // 종의 모양 (새 모델이 없는 종은 기존 프리팹·도형을 빌린다)
    GameObject BuildBody(MonsterRoster.Body body, Vector2Int cell)
    {
        var rot = Quaternion.Euler(0, R(0, 360), 0);
        switch (body)
        {
            case MonsterRoster.Body.Rat: return Spawn(rat, cell, rot);
            case MonsterRoster.Body.Wolf: return Spawn(wolf, cell, rot);
            case MonsterRoster.Body.Slime: return Spawn(slime, cell, rot);
            case MonsterRoster.Body.Bat: return Spawn(bat, cell, rot);
            case MonsterRoster.Body.Goblin: return BuildGoblin(cell);
            default: return BuildSkeleton(cell, false, 1);
        }
    }

    // 도둑 고블린: 상인 고블린 모델을 복제해 몬스터로 (상인 이벤트는 떼어 낸다)
    GameObject BuildGoblin(Vector2Int cell)
    {
        if (goblin == null) return BuildSkeleton(cell, false, 1);
        occupied.Add(cell);
        var go = Instantiate(goblin.gameObject, GridTile.CellToWorld(cell), Quaternion.Euler(0, R(0, 360), 0), root);
        go.name = "ThiefGoblin";
        foreach (var ev in go.GetComponentsInChildren<GoblinEvent>(true)) DestroyImmediate(ev);
        if (go.GetComponent<GridBlocker>() == null) go.AddComponent<GridBlocker>();
        go.AddComponent<Monster>();
        go.SetActive(true);   // 층 전체(root)가 켜질 때 깨어난다
        return go;
    }

    // ---- 준보스 (3·8층, 15번 문서 3-2) ----
    // 가장 큰 방 한가운데에 혼자. 계단을 막지 않는 선택 전투이고, 그 방엔 상자·항아리가 평소보다 많다.
    public static readonly int[] SemiBossFloors = { 3, 8 };
    public const int SemiBossPots = 3, SemiBossChests = 1;

    Room PlaceSemiBoss(Room shopRoom, Room lastRoom)
    {
        if (System.Array.IndexOf(SemiBossFloors, RunState.Floor) < 0) return null;
        Room best = null;
        foreach (var r in rooms)
        {
            if (r == shopRoom || r == lastRoom || r.locked) continue;
            if (best == null || r.rect.width * r.rect.height > best.rect.width * best.rect.height) best = r;
        }
        if (best == null) return null;
        var center = Center(best.rect);
        Vector2Int? cell = occupied.Contains(center) ? FreeCellIn(best.rect, false) : center;
        if (cell == null) return best;
        var s = RunState.Floor == 3 ? MonsterRoster.Species.GiantSlime : MonsterRoster.Species.SkeletonCaptain;
        var go = BuildBody(MonsterRoster.All[s].body, cell.Value);
        if (go != null)
        {
            var m = go.GetComponent<Monster>();
            MonsterRoster.Apply(m, s, RunState.Floor);
            m.hpVariance = 0f;   // 준보스 체력은 표 값 그대로
        }
        // 보상: 큰 상자와 반드시 무언가 나오는 항아리
        for (int i = 0; i < SemiBossChests; i++)
        {
            var c = FreeCellIn(best.rect, true);
            if (c != null) Spawn(chestBig, c, RandomYaw());
        }
        for (int i = 0; i < SemiBossPots; i++)
        {
            var c = FreeCellIn(best.rect, true);
            if (c != null) SpawnPot(c.Value, true);
        }
        // 개발자 모드: 준보스 방 문 바깥 칸
        foreach (var door in best.doors)
            foreach (var d in Dirs4)
                if (!best.rect.Contains(door + d) && IsFloor(door + d)) { SemiBossDoorCell = door + d; break; }
        return best;
    }

    // ---- 엘리트 층 (5·10층, 설계 A5) ----
    // 미로 없이: 시작 방 → (거대한 문) → 넓은 엘리트 방 → (봉인된 철문) → 계단 방.
    // 엘리트 방에는 엘리트만 있고, 엘리트를 쓰러뜨리면 봉인이 풀린다 (Monster.OnEliteDown → Door.Unseal).
    public const int EliteWidth = 17, EliteHeight = 21;
    public const int EliteArenaHeight = 9;          // 엘리트 방 세로 (가로는 맵 폭 - 4)
    public const int EliteCoffins = 4, EliteBones = 5, EliteWallTorches = 6;

    Room arena, stairsRoom;
    Vector2Int gateCell;

    void GenerateElite()
    {
        grid = new C[width, height];
        region = new int[width, height];
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) region[x, y] = -1;

        int midX = ((width - 5) / 2) | 1;
        start = AddRoom(new RectInt(midX, 1, 5, 5));
        int cx = Center(start.rect).x;
        doorCell = new Vector2Int(cx, start.rect.yMax);
        grid[doorCell.x, doorCell.y] = C.Door;
        outsideDoor = doorCell + Vector2Int.up;

        arena = AddRoom(new RectInt(2, doorCell.y + 1, width - 4, EliteArenaHeight));
        rooms.Add(arena);
        gateCell = new Vector2Int(cx, arena.rect.yMax);
        grid[gateCell.x, gateCell.y] = C.Door;
        stairsRoom = AddRoom(new RectInt(midX, gateCell.y + 1, 5, Mathf.Min(3, height - 2 - gateCell.y)));
        stairsRoom.locked = true;
        rooms.Add(stairsRoom);

        CollectDoors();
        ComputeDistances();
    }

    void BuildEliteObjects()
    {
        root = new GameObject("Generated").transform;
        root.SetParent(transform, false);
        root.gameObject.SetActive(false);
        BuildTiles();
        PlaceStartRoom();

        // 봉인된 철문: 계단 방으로 가는 하나뿐인 문
        occupied.Add(gateCell);
        var gate = new GameObject("EliteGate");
        gate.transform.SetParent(Group("Doors"), false);
        gate.transform.position = GridTile.CellToWorld(gateCell);
        var door = gate.AddComponent<Door>();
        door.Build(Door.Kind.Iron, true, false, stairsRoom.rect.Contains(gateCell + Vector2Int.up) ? 1 : -1);
        door.sealedByBoss = true;

        Put(stairs, Center(stairsRoom.rect), Quaternion.Euler(0, 90f * R(0, 4), 0));
        StairsCell = Center(stairsRoom.rect);
        BossDoorCell = doorCell + Vector2Int.down;   // 시작 방 쪽, 엘리트 방 입구 바로 앞
        if (goblin != null) goblin.gameObject.SetActive(false);

        // 엘리트: 방 안쪽(계단 문 쪽)에서 기다린다
        var bossCell = Center(arena.rect) + new Vector2Int(0, arena.rect.height / 4);
        if (RunState.Floor == 5) BuildSkeleton(bossCell, true, 1);   // 체력 22 × 엘리트 5배 = 110 (15번 문서)
        else SpawnBoss(arena);

        // 5층 테마: 묘지·납골당 — 벽을 따라 관, 바닥에 흩어진 뼈
        if (RunState.Floor == 5)
        {
            for (int i = 0; i < EliteCoffins; i++)
            {
                var c = FreeBackWallCell(arena.rect);
                if (c == null) break;
                occupied.Add(c.Value);
                var face = Quaternion.Euler(0, R(0, 4) * 90f, 0);
                foreach (var d in Dirs4)
                    if (IsBackWall(c.Value, d)) { face = Quaternion.LookRotation(new Vector3(-d.x, 0, -d.y)); break; }
                var go = coffinPrefab != null ? Instantiate(coffinPrefab, GridTile.CellToWorld(c.Value), face, root) : null;
                if (go != null && go.GetComponent<Coffin>() == null) go.AddComponent<Coffin>();
            }
            var intro = FindAnyObjectByType<IntroSequence>();
            var bones = intro != null ? intro.bonesPrefab : null;
            for (int i = 0; i < EliteBones && bones != null; i++)
            {
                var c = FreeCellIn(arena.rect, R01() < 0.5f);
                if (c == null) break;
                occupied.Add(c.Value);
                var go = Instantiate(bones, GridTile.CellToWorld(c.Value), Quaternion.Euler(0, R(0, 360), 0), root);
                foreach (var b in go.GetComponentsInChildren<BonesEvent>(true)) Destroy(b);   // 장식만 (유품 이벤트 없음)
            }
        }

        PlaceWallTorches(EliteWallTorches);
        PlaceSign(stairsRoom.rect);   // 계단 방엔 팁 표지판
        root.gameObject.SetActive(true);
    }

    // ---- 해골 (모델이 없어 기본 도형으로 임시) ----
    // giant: 5층 엘리트 거인 해골. 아니면 해골 병사 (6층부터, 막기를 잘한다)
    public const float SkeletonShare = 0.6f;    // 6층부터 신규 무리(해골 병사·박쥐 등)의 비율 → 기존 종은 절반 이하

    public const float GiantModelHeight = 3.2f;   // 거인 해골 모델 키 (엘리트 크기 ×1.5가 더해진다)

    GameObject BuildSkeleton(Vector2Int cell, bool giant, int level)
    {
        occupied.Add(cell);
        var go = new GameObject(giant ? "GiantSkeleton" : "SkeletonSoldier");
        go.SetActive(false);
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(GridTile.CellToWorld(cell), Quaternion.Euler(0, R(0, 360), 0));

        // 모양: 첫 번째 자식이 몸 (공격·움찔 연출은 이것만 움직인다)
        var body = new GameObject("Model").transform;
        body.SetParent(go.transform, false);
        var bone = new Color(0.85f, 0.82f, 0.72f);
        void Part(PrimitiveType t, Vector3 pos, Vector3 scale, Color c, Vector3 rot = default)
        {
            var p = GameObject.CreatePrimitive(t);
            Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(body, false);
            p.transform.localPosition = pos;
            p.transform.localRotation = Quaternion.Euler(rot);
            p.transform.localScale = scale;
            var m = p.GetComponent<Renderer>().material;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
        // 거인 해골: 블렌더에서 부품을 뼈대에 조립하고 동작을 넣은 모델이 있으면 그것 (Resources/Models/GiantSkeleton)
        bool usedModel = giant && ResourceModel.SpawnCharacter("GiantSkeleton", body, GiantModelHeight) != null;
        if (!usedModel)
        {
        Part(PrimitiveType.Sphere, new Vector3(0, 2.75f, 0), new Vector3(0.6f, 0.65f, 0.6f), bone);                     // 머리
        Part(PrimitiveType.Sphere, new Vector3(0.13f, 2.8f, 0.26f), Vector3.one * 0.14f, Color.black);                  // 눈
        Part(PrimitiveType.Sphere, new Vector3(-0.13f, 2.8f, 0.26f), Vector3.one * 0.14f, Color.black);
        Part(PrimitiveType.Capsule, new Vector3(0, 1.85f, 0), new Vector3(0.55f, 0.55f, 0.35f), bone);                  // 가슴
        Part(PrimitiveType.Cylinder, new Vector3(0, 1.2f, 0), new Vector3(0.15f, 0.35f, 0.15f), bone);                  // 등뼈
        Part(PrimitiveType.Capsule, new Vector3(0.45f, 1.7f, 0.1f), new Vector3(0.16f, 0.55f, 0.16f), bone, new Vector3(-20, 0, 10));   // 팔
        Part(PrimitiveType.Capsule, new Vector3(-0.45f, 1.7f, 0.1f), new Vector3(0.16f, 0.55f, 0.16f), bone, new Vector3(-20, 0, -10));
        Part(PrimitiveType.Capsule, new Vector3(0.2f, 0.45f, 0), new Vector3(0.18f, 0.5f, 0.18f), bone);                // 다리
        Part(PrimitiveType.Capsule, new Vector3(-0.2f, 0.45f, 0), new Vector3(0.18f, 0.5f, 0.18f), bone);
        if (giant) Part(PrimitiveType.Cylinder, new Vector3(0.65f, 1.6f, 0.6f), new Vector3(0.22f, 0.9f, 0.22f), new Color(0.7f, 0.66f, 0.58f), new Vector3(60, 0, 0));   // 뼈 몽둥이
        else Part(PrimitiveType.Cube, new Vector3(-0.6f, 1.6f, 0.35f), new Vector3(0.12f, 0.9f, 0.7f), new Color(0.35f, 0.3f, 0.25f));                                  // 낡은 방패
        }

        go.AddComponent<GridBlocker>();
        var mon = go.AddComponent<Monster>();
        mon.level = level;
        if (giant)
        {
            mon.displayName = "거인 해골";
            mon.boss = true;
            mon.baseHp = 22; mon.attackMin = 6; mon.attackMax = 9; mon.strength = 2;   // 엘리트 보정 +2/+3 → 8~12
            mon.moveEvery = 2;            // 느리다
            mon.sight = 8;
            mon.behavior = Monster.Behavior.Giant;
            mon.expReward = 6; mon.goldMin = 5; mon.goldMax = 10;
            var b = go.AddComponent<Boss>();
            b.kind = Boss.Kind.GiantSkeleton;
            if (usedModel) mon.animImpact = 0.35f;   // Attack 동작에서 몽둥이가 내려찍히는 프레임 (10.5/30)
        }
        else
        {
            mon.displayName = "해골 병사";
            mon.baseHp = 15; mon.attackMin = 2; mon.attackMax = 5; mon.strength = 4;
            mon.sight = 4;
            mon.extraBlock = 0.25f;        // 막기 15% + 25% = 40%
            mon.behavior = Monster.Behavior.Chaser;
            mon.expReward = 4; mon.goldMin = 1; mon.goldMax = 5;
        }
        go.SetActive(true);   // 층 전체(root)가 켜질 때 깨어난다
        return go;
    }

    // 우두머리 (10층: 거대 슬라임 여왕. 5층은 거인 해골 — BuildSkeleton)
    void SpawnBoss(Room room)
    {
        bool top = RunState.Floor >= RunState.TopFloor;
        var kind = top ? slime : wolf;
        var go = Spawn(kind, FreeCellIn(room.rect, false), Quaternion.Euler(0, R(0, 360), 0));
        if (go == null) return;
        var m = go.GetComponent<Monster>();
        m.level = 2 + FloorLevel;
        m.boss = true;
        m.displayName = top ? "거대 슬라임 여왕" : "늑대 우두머리";
        var b = go.AddComponent<Boss>();
        b.kind = top ? Boss.Kind.SlimeQueen : Boss.Kind.PackLeader;
        b.minionPrefab = kind;
        b.minionLevel = 1 + FloorLevel;
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
            foreach (var d in Dirs4) if (IsBackWall(c, d)) cands.Add((c, d));
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
            SpawnPickup(id, prefab, c);
        }
    }

    // 바닥·벽 타일을 깐다 (보통 층·엘리트 층 공통)
    void BuildTiles()
    {
        var floorRoot = Group("Floors");
        var wallRoot = Group("Walls");
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var c = new Vector2Int(x, y);
                if (IsFloor(c))
                {
                    floorTiles[c] = Instantiate(floorPrefab, GridTile.CellToWorld(c), Quaternion.identity, floorRoot);
                    var variant = FloorVariantAt(c);
                    if (variant != null) foreach (var rend in floorTiles[c].GetComponentsInChildren<Renderer>()) rend.sharedMaterial = variant;
                }
                else if (TouchesFloor(c)) wallTiles[c] = BuildWall(c, wallRoot);
            }
    }

    // ---- 바닥 종류 ----
    // 방마다 FloorVariantChance 확률로 다른 바닥(이끼 낀 돌·자갈). 통로·시작 방은 기본 돌바닥.
    // 재질은 Resources/Floors/ (없으면 기본 바닥 그대로).
    Dictionary<RectInt, Material> roomFloors;

    Material FloorVariantAt(Vector2Int c)
    {
        if (roomFloors == null)
        {
            roomFloors = new Dictionary<RectInt, Material>();
            var variants = new List<Material>();
            foreach (var n in new[] { "M_Floor_Moss", "M_Floor_Cobble" })
            {
                var m = Resources.Load<Material>("Floors/" + n);
                if (m != null) variants.Add(m);
            }
            if (variants.Count > 0)
                foreach (var r in rooms)
                    if (R01() < FloorVariantChance) roomFloors[r.rect] = Pick(variants);
            PickWater();
        }
        if (waterMat != null && waterCells.Contains(c)) return waterMat;
        foreach (var kv in roomFloors) if (kv.Key.Contains(c)) return kv.Value;
        return null;
    }

    // ---- 물웅덩이 ----
    // 몇몇 방 안쪽에 몇 칸짜리 얕은 물 (설명 없이 둔다). 물 재질이 없으면 만들지 않는다.
    readonly HashSet<Vector2Int> waterCells = new HashSet<Vector2Int>();
    Material waterMat;

    void PickWater()
    {
        waterMat = Resources.Load<Material>("Floors/M_Floor_Water");
        if (waterMat == null || RunState.IsBossFloor) return;
        var cands = new List<Room>(rooms);
        Shuffle(cands);
        for (int i = 0; i < WaterRooms && i < cands.Count; i++)
        {
            var r = cands[i].rect;
            if (r.width < 3 || r.height < 3) continue;
            // 방 안쪽(가장자리 제외)에서 시작해 이웃 칸으로 번진다
            var inner = new RectInt(r.x + 1, r.y + 1, r.width - 2, r.height - 2);
            var seed = new Vector2Int(R(inner.xMin, inner.xMax), R(inner.yMin, inner.yMax));
            var patch = new List<Vector2Int> { seed };
            int want = R(WaterMin, WaterMax + 1);
            for (int tries = 0; patch.Count < want && tries < 40; tries++)
            {
                var n = Pick(patch) + Dirs4[R(0, 4)];
                if (r.Contains(n) && !patch.Contains(n)) patch.Add(n);
            }
            foreach (var c in patch) waterCells.Add(c);
        }
    }

    void PlaceWater()
    {
        var waterRoot = Group("Water");
        foreach (var c in waterCells)
        {
            occupied.Add(c);   // 물 위엔 다른 것을 두지 않는다
            WaterTile.Create(c, waterRoot);
        }
    }

    // ---- 풀숲 ----
    void PlaceGrass()
    {
        var grassRoot = Group("Grass");
        var cands = rooms.FindAll(r => !r.locked);
        Shuffle(cands);
        for (int i = 0; i < GrassRooms && i < cands.Count; i++)
        {
            var r = cands[i].rect;
            int n = R(GrassMin, GrassMax + 1);
            for (int k = 0; k < n; k++)
            {
                var c = FreeCellIn(r, false);
                if (c == null) break;
                occupied.Add(c.Value);
                Grass.Create(c.Value, grassRoot);
            }
        }
    }

    // ---- 함정 ----
    public const int TrapsBase = 2;              // 2층 함정 수 = TrapsBase + TrapsPerFloor
    public const int TrapsPerFloor = 1;          // 층이 하나 오를 때마다 +1 (10층 12개)
    public const float SpikeWeight = 0.5f, PoisonWeight = 0.3f;   // 나머지(0.2)는 경보
    const int TrapMinDistance = 4;               // 문 밖에서 이 걸음 수 안쪽엔 두지 않는다

    void PlaceTraps(bool tutorial)
    {
        var trapRoot = Group("Traps");
        var cells = new List<Vector2Int>();
        foreach (var kv in dist)
        {
            var c = kv.Key;
            if (kv.Value < TrapMinDistance || At(c) == C.Door || occupied.Contains(c) || NextToDoor(c) || Grow(start.rect, 1).Contains(c)) continue;
            cells.Add(c);
        }
        if (cells.Count == 0) return;

        if (tutorial)
        {
            // 1층: 처음 나가는 길목(통로)에 숨긴 가시 함정 하나 (함정 팁은 표지판 팁 목록에 있다)
            var near = cells.FindAll(c => At(c) == C.Corridor && dist[c] <= TrapMinDistance + 4);
            var c0 = near.Count > 0 ? Pick(near) : Pick(cells);
            occupied.Add(c0);
            Trap.Create(c0, Trap.Kind.Spike, trapRoot);
            return;
        }

        int count = TrapsBase + TrapsPerFloor * (RunState.Floor - 1);
        Shuffle(cells);
        for (int i = 0; i < count && i < cells.Count; i++)
        {
            float r = R01();
            var kind = r < SpikeWeight ? Trap.Kind.Spike : r < SpikeWeight + PoisonWeight ? Trap.Kind.Poison : Trap.Kind.Alarm;
            occupied.Add(cells[i]);
            Trap.Create(cells[i], kind, trapRoot);
        }
    }

    // ---- 숨겨진 방 ----
    // 바닥 바로 옆 벽 한 칸(W) 너머에 3×3 바위 덩어리가 통째로 비어 있으면 거기를 파서 방을 만들고, W에는 무너진 돌무더기를 둔다.
    readonly List<(Vector2Int rubble, RectInt room)> secrets = new List<(Vector2Int, RectInt)>();

    void CarveSecretRooms(int count)
    {
        var cands = new List<(Vector2Int w, Vector2Int d)>();
        for (int x = 2; x < width - 2; x++)
            for (int y = 2; y < height - 2; y++)
            {
                var w = new Vector2Int(x, y);
                if (grid[x, y] != C.Solid || Grow(start.rect, 2).Contains(w)) continue;
                foreach (var d in Dirs4)
                {
                    var f = w - d;   // 이쪽이 원래 바닥, 반대쪽(d)으로 방을 판다
                    if (!IsFloor(f) || At(f) == C.Door || NextToOpening(f)) continue;
                    if (FitsSecret(w, d)) cands.Add((w, d));
                }
            }
        Shuffle(cands);
        foreach (var (w, d) in cands)
        {
            if (secrets.Count >= count) break;
            if (!FitsSecret(w, d)) continue;   // 앞서 판 방과 겹칠 수 있으니 다시 확인
            var room = SecretRect(w, d);
            for (int x = room.xMin; x < room.xMax; x++) for (int y = room.yMin; y < room.yMax; y++) grid[x, y] = C.Room;
            grid[w.x, w.y] = C.Corridor;
            secrets.Add((w, room));
            occupied.Add(w);               // 돌무더기 자리와
            occupied.Add(Center(room));    // 상자 자리는 다른 것이 차지하지 못하게
        }
    }

    static RectInt SecretRect(Vector2Int w, Vector2Int d)
    {
        var c = w + d * 2;   // 3×3 방의 가운데
        return new RectInt(c.x - 1, c.y - 1, 3, 3);
    }

    // 방 자리와 그 둘레 한 칸이 모두 바위이고 맵 안이어야 한다 (W는 빼고)
    bool FitsSecret(Vector2Int w, Vector2Int d)
    {
        var r = Grow(SecretRect(w, d), 1);
        if (r.xMin < 1 || r.yMin < 1 || r.xMax > width - 1 || r.yMax > height - 1) return false;
        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                if (c != w && grid[x, y] != C.Solid) return false;
            }
        // W의 옆구리도 바위여야 통로가 한 칸으로 깔끔하다
        var side = new Vector2Int(d.y, d.x);
        return !IsFloor(w + side) && !IsFloor(w - side);
    }

    void BuildSecretRooms()
    {
        bool bonusLeft = RunState.IsTutorial;   // 1층: 첫 숨겨진 방은 골드·물건이 가득한 보너스 방 (돌무더기는 보이게 둔다)
        foreach (var (w, room) in secrets)
        {
            bool bonus = bonusLeft;
            bonusLeft = false;
            occupied.Add(w);
            // 무너진 돌무더기: 돌멩이 모델 여러 개를 크게 쌓는다
            var pile = new GameObject("Rubble");
            pile.transform.SetParent(root, false);
            pile.transform.position = GridTile.CellToWorld(w);
            for (int i = 0; i < 6; i++)
            {
                GameObject rock;
                if (stonePrefab != null) rock = Instantiate(stonePrefab, pile.transform);
                else { rock = GameObject.CreatePrimitive(PrimitiveType.Sphere); rock.transform.SetParent(pile.transform, false); }
                rock.transform.localPosition = new Vector3(R(-12, 13) * 0.1f, i < 4 ? 0f : 0.9f, R(-12, 13) * 0.1f);
                rock.transform.localRotation = Quaternion.Euler(R(0, 40), R(0, 360), R(0, 40));
                rock.transform.localScale = Vector3.one * (2.2f + R(0, 10) * 0.1f);
            }
            var rubble = pile.AddComponent<Rubble>();
            // 처음엔 숨겨 둔다: 주변 벽과 똑같은 가짜 벽 (카메라 쪽에 바닥이 있으면 낮은 벽 — IsFrontWall과 같은 기준)
            // (숨겨진 방 자체는 원래 바위였으니 셈에서 뺀다)
            bool OpenNotSecret(Vector2Int c) => IsFloor(c) && !room.Contains(c);
            bool low = OpenNotSecret(w + Vector2Int.right) || OpenNotSecret(w + Vector2Int.up) || OpenNotSecret(w + Vector2Int.one);
            if (!bonus) rubble.Hide(BuildWall(w, pile.transform, low));

            // 방 안: 큰 상자 하나 (가운데)
            var center = Center(room);
            occupied.Add(center);
            var chest = Instantiate(chestBig, GridTile.CellToWorld(center), RandomYaw(), root);
            chest.GetComponent<ChestEvent>().givesTorch = false;
            if (bonus) FillBonusRoom(room, w);
        }
    }

    // 보너스 방: 들어오는 칸을 뺀 자리에 골드 더미, 안쪽 구석 둘엔 반드시 무언가 나오는 항아리
    void FillBonusRoom(RectInt room, Vector2Int entrance)
    {
        var cells = new List<Vector2Int>();
        for (int x = room.xMin; x < room.xMax; x++)
            for (int y = room.yMin; y < room.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                if (!occupied.Contains(c) && GridNav.Distance(c, entrance) > 1) cells.Add(c);
            }
        // 입구에서 먼 칸부터: 항아리 둘 → 나머지는 골드
        cells.Sort((a, b) => GridNav.Distance(b, entrance).CompareTo(GridNav.Distance(a, entrance)));
        int pots = 0, piles = 0;
        foreach (var c in cells)
        {
            if (pots < 2) { SpawnPot(c, true); pots++; continue; }
            if (piles >= BonusGoldPiles) break;
            occupied.Add(c);
            Pickup.SpawnGold(c, R(BonusGoldMin, BonusGoldMax + 1), root);
            piles++;
        }
        // 입구 쪽 칸에도 골드 (밟고 들어오며 줍게)
        for (int x = room.xMin; x < room.xMax; x++)
            for (int y = room.yMin; y < room.yMax; y++)
            {
                var c = new Vector2Int(x, y);
                if (piles >= BonusGoldPiles || occupied.Contains(c)) continue;
                occupied.Add(c);
                Pickup.SpawnGold(c, R(BonusGoldMin, BonusGoldMax + 1), root);
                piles++;
            }
    }

    // ---- 방 소품 ----
    // againstWall: 뒷벽에 붙여 방 안쪽을 보게 (책장·관), 아니면 방 안쪽 칸 (우물)
    // first: 튜토리얼 순서상 이 소품을 꼭 둘 방 (먼저 놓고, 나머지는 무작위)
    void PlaceProps<T>(GameObject prefab, int count, bool againstWall, Room first = null) where T : Prop
    {
        var open = rooms.FindAll(r => !r.locked && r != first);
        for (int i = 0; i < count; i++)
        {
            Room r;
            if (i == 0 && first != null) r = first;
            else if (open.Count > 0) { r = Pick(open); open.Remove(r); }   // 한 방에 같은 소품은 하나
            else break;
            var cell = againstWall ? FreeBackWallCell(r.rect) : FreeCellIn(r.rect, false);
            if (cell == null) continue;
            occupied.Add(cell.Value);
            // 뒷벽을 등지고 방 안을 보게
            var face = Quaternion.Euler(0, R(0, 4) * 90f, 0);
            foreach (var d in Dirs4)
                if (IsBackWall(cell.Value, d)) { face = Quaternion.LookRotation(new Vector3(-d.x, 0, -d.y)); break; }
            GameObject go;
            if (prefab != null) go = Instantiate(prefab, GridTile.CellToWorld(cell.Value), face, root);
            else
            {
                go = new GameObject(typeof(T).Name);
                go.transform.SetParent(root, false);
                go.transform.SetPositionAndRotation(GridTile.CellToWorld(cell.Value), face);
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.SetParent(go.transform, false);
                box.transform.localPosition = new Vector3(0, 0.8f, 0);
                box.transform.localScale = new Vector3(2.4f, 1.6f, 1.2f);
            }
            if (go.GetComponent<T>() == null) go.AddComponent<T>();
        }
    }

    void ScatterPots(int count)
    {
        var open = rooms.FindAll(r => !r.locked);
        for (int i = 0; i < count && open.Count > 0; i++)
        {
            var r = Pick(open);
            var cell = FreeCellIn(r.rect, true);
            if (cell == null) { open.Remove(r); continue; }
            SpawnPot(cell.Value, false);
        }
    }

    // neverEmpty: 튜토리얼용 — 반드시 무언가 나온다 (빈 것·쥐 없음)
    void SpawnPot(Vector2Int cell, bool neverEmpty)
    {
        occupied.Add(cell);
        GameObject go;
        if (potPrefab != null) go = Instantiate(potPrefab, GridTile.CellToWorld(cell), Quaternion.Euler(0, R(0, 360), 0), root);
        else
        {
            go = new GameObject("Pot");
            go.transform.SetParent(root, false);
            go.transform.position = GridTile.CellToWorld(cell);
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0, 0.7f, 0);
            body.transform.localScale = new Vector3(1.2f, 1.4f, 1.2f);
            body.GetComponent<Renderer>().material.color = new Color(0.55f, 0.36f, 0.22f);
        }
        var pot = go.GetComponent<Pot>() ?? go.AddComponent<Pot>();
        pot.neverEmpty = neverEmpty;
    }

    // 나뭇가지·돌멩이 하나를 그 칸에
    void SpawnPickup(string id, GameObject prefab, Vector2Int c)
    {
        occupied.Add(c);
        var go = prefab != null ? Instantiate(prefab, GridTile.CellToWorld(c), Quaternion.Euler(0, R(0, 360), 0), root) : FallbackPickup(id, c);
        var p = go.GetComponent<Pickup>() ?? go.AddComponent<Pickup>();
        p.itemId = id;
        p.triggerOnEnter = true;
    }

    // 방 안내 → 그 방 뒷벽 쪽에 팁 표지판을 세운다 (클릭하면 읽는다).
    // 한 번 읽은 안내는 다음 회차엔 TipSign.ReshowChance 확률로만 다시 세운다.
    readonly HashSet<RectInt> signRooms = new HashSet<RectInt>();

    void AddHint(string id, RectInt area, string message)
    {
        if (!RunState.IsTutorial) return;
        if (TowerMemory.HintSeen(id) && R01() >= TipSign.ReshowChance) return;
        PlaceSign(area, id, message);
    }

    // 방 안 뒷벽 쪽 빈 칸에 표지판 (방마다 하나). message가 null이면 읽을 때 팁 목록에서 고른다.
    bool PlaceSign(RectInt room, string id = null, string message = null)
    {
        if (signRooms.Contains(room)) return false;
        var cell = FreeBackWallCell(room);
        if (cell == null) return false;
        occupied.Add(cell.Value);
        signRooms.Add(room);
        // 표지판 앞면이 방 가운데를 보게
        var to = (Vector2)(Center(room) - cell.Value);
        var rot = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(new Vector3(to.x, 0, to.y)) : RandomYaw();
        TipSign.Create(cell.Value, root, rot, id, message);
        return true;
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
