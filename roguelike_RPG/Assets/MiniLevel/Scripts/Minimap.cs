using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 미니맵: 가 본 곳만 안개가 걷힌다. 한 칸 밟을 때마다 주변(횃불을 들면 반경 2, 없으면 1)이 밝혀진다.
// - 미리보기(우상단): 내 주변만 보이고 나를 따라온다. 화면과 같은 방향이 되도록 45° 돌려서 그린다.
// - 미리보기를 클릭하거나 [M]: 전체 맵 + 범례 + 탐색 %. 다시 클릭·[M]·[Esc]로 닫는다.
// 지형은 텍스처에 칸 단위로 칠하고(안 밝힌 칸은 투명 = 안개), 오브젝트는 그 위에 그림 아이콘으로 얹는다.
// 아이콘 그림은 Resources/MapIcons (VARCO로 만든 픽셀 아이콘).
public class Minimap : MonoBehaviour
{
    public PlayerStats player;
    public int radiusWithTorch = 2;
    public int radiusWithoutTorch = 1;

    public static bool FullOpen => instance != null && instance.fullRoot != null && instance.fullRoot.activeSelf;
    public static int EscFrame { get; private set; } = -1;
    static Minimap instance;

    const int Px = 8;   // 한 칸 = 텍스처 8×8 픽셀
    static readonly Color Unexplored = new Color(0, 0, 0, 0);
    static readonly Color FloorColor = new Color(0.46f, 0.43f, 0.38f);
    static readonly Color FloorEdge = new Color(0.34f, 0.31f, 0.27f);
    static readonly Color WallColor = new Color(0.17f, 0.15f, 0.13f);
    static readonly Color SeenColor = new Color(0.25f, 0.55f, 0.8f, 0.45f);   // 보이기만 한 바닥 (밟으면 또렷해진다)
    static readonly Color SeenEdge = new Color(0.2f, 0.45f, 0.7f, 0.55f);

    // 아이콘 색 (그림이 흰색인 것만 이 색으로 물든다)
    static readonly Color MeColor = new Color(1f, 0.86f, 0.2f);
    static readonly Color EnemyColor = new Color(1f, 0.22f, 0.18f);
    static readonly Color TorchOff = Color.white, TorchOn = new Color(1f, 0.6f, 0.15f);
    static readonly Color DoorWood = new Color(0.78f, 0.52f, 0.3f), DoorIron = new Color(0.7f, 0.74f, 0.8f);

    MapData map;
    Texture2D tex;
    MapView preview, full;
    Canvas fullCanvasComp;
    GridClickMover mover;
    GameObject fullRoot;
    Text fullTitle;
    Vector2Int lastCell = new Vector2Int(int.MinValue, 0);
    bool lastTorch;
    float iconTimer;
    readonly List<Icon> icons = new List<Icon>();

    public struct Icon
    {
        public Vector2Int cell;
        public string sprite;     // Resources/MapIcons 안의 이름
        public Color color;
        public float size;
        public float? yaw;        // 있으면 그 방향을 가리키는 화살표, 없으면 똑바로 선 아이콘
    }

    static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
    static Sprite IconSprite(string name)
    {
        if (!sprites.TryGetValue(name, out var s)) sprites[name] = s = Resources.Load<Sprite>("MapIcons/" + name);
        return s;
    }

    void Awake() => instance = this;

    void Start()
    {
        map = new MapData();
        tex = new Texture2D(map.bounds.width * Px, map.bounds.height * Px, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        var clear = new Color[tex.width * tex.height];
        for (int i = 0; i < clear.Length; i++) clear[i] = Unexplored;
        tex.SetPixels(clear);
        tex.Apply();
        map.OnRevealed += Paint;
        if (player != null) mover = player.GetComponent<GridClickMover>();
        Build();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (FullOpen && kb.escapeKey.wasPressedThisFrame) { EscFrame = Time.frameCount; SetFull(false); }
            else if (kb.mKey.wasPressedThisFrame && !TitleScreen.IsOpen && !InventoryUI.IsOpen && !PauseMenu.IsOpen && !CraftUI.IsOpen) SetFull(!FullOpen);
        }
        if (player == null || map == null || TitleScreen.IsOpen) return;

        // 칸이 바뀌었거나 횃불을 들고 내렸으면 안개를 걷는다
        var cell = GridTile.WorldToCell(player.transform.position);
        if (cell != lastCell || player.TorchLit != lastTorch)
        {
            lastCell = cell;
            lastTorch = player.TorchLit;
            map.Reveal(cell, player.TorchLit ? radiusWithTorch : radiusWithoutTorch);
            map.Visit(cell);
            iconTimer = 0f;
            if (FullOpen) RefreshTitle();
        }

        // 아이콘(몬스터 위치 등)은 자주 바뀌니 짧은 간격으로 다시 모은다
        iconTimer -= Time.unscaledDeltaTime;
        if (iconTimer <= 0f)
        {
            iconTimer = 0.15f;
            GatherIcons();
            preview.SetIcons(icons);
            if (FullOpen) full.SetIcons(icons);
        }

        // 미리보기: 플레이어(걷는 중에도 부드럽게)가 가운데 오도록
        var p = player.transform.position / GridTile.CellSize;
        preview.CenterOn(new Vector2(p.x, p.z));
        float yaw = player.transform.eulerAngles.y;
        preview.SetPlayer(new Vector2(p.x, p.z), yaw);
        if (FullOpen) full.SetPlayer(new Vector2(p.x, p.z), yaw);
        if (FullOpen) full.SetHover(TravelCell());
    }

    void SetFull(bool open)
    {
        fullRoot.SetActive(open);
        if (open) { GatherIcons(); full.SetIcons(icons); RefreshTitle(); }
    }

    void RefreshTitle() => fullTitle.text = $"지도   <size=22><color=#9e9480>탐색 {Mathf.FloorToInt(map.ExploredPercent())}%</color></size>";

    // 전체 지도에서 마우스 아래 칸 — 직접 밟아 본 곳이고 지금 서 있는 칸이 아니면 이동할 수 있다
    Vector2Int? TravelCell()
    {
        if (Mouse.current == null || player == null) return null;
        var cam = fullCanvasComp.renderMode == RenderMode.ScreenSpaceOverlay ? null : fullCanvasComp.worldCamera;
        var cell = full.CellAt(Mouse.current.position.ReadValue(), cam);
        if (cell == null || !map.IsVisited(cell.Value) || cell.Value == GridTile.WorldToCell(player.transform.position)) return null;
        return cell;
    }

    void TryTravel()
    {
        var cell = TravelCell();
        if (cell == null || mover == null) return;
        var target = cell.Value;
        SetFull(false);
        DialogUI.Show("이곳으로 이동하시겠습니까?",
            new DialogUI.Choice("이동", () =>
            {
                if (!mover.SetDestination(target)) GameLog.Add("지금은 그곳으로 가는 길이 막혀 있다.", GameLog.Warn);
            }),
            new DialogUI.Choice("취소", null));
    }

    // 새로 밝혀진 칸만 칠한다
    void Paint(List<Vector2Int> cells)
    {
        foreach (var c in cells)
        {
            var t = map.TerrainAt(c);
            int x0 = (c.x - map.bounds.xMin) * Px, y0 = (c.y - map.bounds.yMin) * Px;
            for (int x = 0; x < Px; x++)
                for (int y = 0; y < Px; y++)
                {
                    bool edge = x == 0 || y == 0 || x == Px - 1 || y == Px - 1;
                    Color col = t == MapData.Terrain.Wall ? WallColor
                        : t == MapData.Terrain.Floor ? (map.IsVisited(c) ? (edge ? FloorEdge : FloorColor) : (edge ? SeenEdge : SeenColor))
                        : Unexplored;
                    tex.SetPixel(x0 + x, y0 + y, col);
                }
        }
        tex.Apply();
    }

    // 밝혀진 칸에 있는 오브젝트를 아이콘으로
    void GatherIcons()
    {
        icons.Clear();
        void Add(Vector2Int c, string s, Color col, float size = 1f, float? yaw = null)
        {
            if (map.IsExplored(c)) icons.Add(new Icon { cell = c, sprite = s, color = col, size = size, yaw = yaw });
        }
        foreach (var s in FindObjectsByType<StairsEvent>(FindObjectsSortMode.None)) Add(s.Cell, s.down ? "stairs_down" : "stairs_up", Color.white, 1.3f);
        foreach (var a in FindObjectsByType<Anvil>(FindObjectsSortMode.None)) Add(a.Cell, "anvil", Color.white, 1.2f);
        foreach (var c in FindObjectsByType<ChestEvent>(FindObjectsSortMode.None)) if (!c.Used) Add(c.Cell, "chest", Color.white, 1.1f);
        foreach (var w in FindObjectsByType<WallTorch>(FindObjectsSortMode.None)) Add(w.Cell, "torch", w.IsOn ? TorchOn : TorchOff, 1.5f);
        foreach (var d in Door.All)
            if (!d.IsOpen) Add(d.Cell, d.locked ? "door_locked" : "door", d.locked ? Color.white : d.kind == Door.Kind.Iron ? DoorIron : DoorWood, 1f);
        foreach (var b in FindObjectsByType<BonesEvent>(FindObjectsSortMode.None)) if (!b.Used) Add(b.Cell, "skull", Color.white, 1.1f);

        // 적(몬스터·고블린)은 지금 보이는 곳(밝히는 반경 바로 바깥까지, 또는 빛 안)에 있을 때만, 바라보는 쪽을 가리키는 빨간 화살표
        int sight = (player.TorchLit ? radiusWithTorch : radiusWithoutTorch) + 1;
        var me = GridTile.WorldToCell(player.transform.position);
        bool Visible(Vector2Int c) { var d = c - me; return Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) <= sight || LightSources.IsLit(c); }
        foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
            if (m.enabled && !m.Dead && Visible(m.Cell)) Add(m.Cell, "arrow", EnemyColor, 1.1f, m.transform.eulerAngles.y);
        foreach (var g in FindObjectsByType<GoblinEvent>(FindObjectsSortMode.None))
            if (!g.Ally && Visible(g.Cell)) Add(g.Cell, "arrow", EnemyColor, 1.1f, g.transform.eulerAngles.y);
    }

    // ---- UI 생성 ----

    void Build()
    {
        var root = UIKit.Canvas(transform, "MinimapCanvas", 1);

        // 미리보기 (턴 판 아래)
        var frame = UIKit.Image(root, "MinimapFrame", new Color(0.03f, 0.025f, 0.02f, 0.85f), true);
        UIKit.Place(frame.rectTransform, new Vector2(1, 1), new Vector2(-20, -80), new Vector2(270, 270));
        frame.gameObject.AddComponent<Outline>().effectColor = new Color(0.55f, 0.45f, 0.3f, 0.6f);
        frame.gameObject.AddComponent<UIClickCatcher>().onClick = () => SetFull(true);

        var viewport = UIKit.Image(frame.rectTransform, "Viewport", Color.white);
        UIKit.Stretch(viewport.rectTransform);
        viewport.rectTransform.offsetMin = new Vector2(4, 4); viewport.rectTransform.offsetMax = new Vector2(-4, -4);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        preview = new MapView(viewport.rectTransform, tex, map.bounds, 26f);

        var hint = UIKit.Text(frame.rectTransform, "[M] 전체 지도", 15, TextAnchor.LowerRight, new Color(1, 1, 1, 0.45f));
        UIKit.Place(hint.rectTransform, new Vector2(1, 0), new Vector2(-8, 6), new Vector2(200, 20));

        // 전체 지도
        var fullCanvas = UIKit.Canvas(transform, "FullMapCanvas", 16);
        fullCanvasComp = fullCanvas.GetComponent<Canvas>();
        fullRoot = fullCanvas.gameObject;
        var dim = UIKit.Image(fullCanvas, "Dim", new Color(0, 0, 0, 0.7f), true);
        UIKit.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<UIClickCatcher>().onClick = () => SetFull(false);

        var panel = UIKit.Image(fullCanvas, "Panel", new Color(0.05f, 0.045f, 0.04f, 0.97f), true);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 860));
        panel.gameObject.AddComponent<Outline>().effectColor = new Color(0.55f, 0.45f, 0.3f, 0.8f);
        panel.gameObject.AddComponent<UIClickCatcher>().onClick = TryTravel;   // 지나온 곳을 누르면 그리로 이동
        fullTitle = UIKit.Text(panel.rectTransform, "지도", 34, TextAnchor.UpperLeft, UIKit.Gold);
        UIKit.Place(fullTitle.rectTransform, new Vector2(0, 1), new Vector2(36, -24), new Vector2(500, 44));
        var close = UIKit.Text(panel.rectTransform, "지나온 곳을 누르면 그곳으로 이동 · [M] 닫기", 18, TextAnchor.UpperRight, UIKit.Dim);
        UIKit.Place(close.rectTransform, new Vector2(1, 1), new Vector2(-36, -30), new Vector2(600, 30));

        var area = new GameObject("MapArea", typeof(RectTransform)).GetComponent<RectTransform>();
        area.SetParent(panel.rectTransform, false);
        UIKit.Place(area, new Vector2(0, 0.5f), new Vector2(40, -20), new Vector2(820, 760));
        // 45° 돌린 지도가 영역 안에 들어가는 칸 크기
        float unit = Mathf.Min(40f, 760f * 1.414f / (map.bounds.width + map.bounds.height));
        full = new MapView(area, tex, map.bounds, unit);
        full.CenterOn(new Vector2(map.bounds.center.x - 0.5f, map.bounds.center.y - 0.5f));

        // 범례 (간단하게)
        (string sprite, Color c, float rot, string label)[] legend =
        {
            ("arrow", MeColor, 0f, "나"),
            ("arrow", EnemyColor, 0f, "적 (보일 때만)"),
            ("chest", Color.white, 0f, "보물상자"),
            ("skull", Color.white, 0f, "누군가의 시체"),
            ("torch", TorchOn, 0f, "벽 횃불 (흰색 = 꺼짐)"),
            ("door", DoorWood, 0f, "문"),
            ("door_locked", Color.white, 0f, "잠긴 문"),
            ("stairs_up", Color.white, 0f, "올라가는 계단"),
            ("anvil", Color.white, 0f, "모루 (대장간)"),
        };
        var lh = UIKit.Text(panel.rectTransform, "범례", 24, TextAnchor.UpperLeft, UIKit.Gold);
        UIKit.Place(lh.rectTransform, new Vector2(1, 1), new Vector2(-40, -110), new Vector2(260, 32));
        for (int i = 0; i < legend.Length; i++)
        {
            var g = UIKit.Image(panel.rectTransform, "LegendIcon", legend[i].c);
            g.sprite = IconSprite(legend[i].sprite);
            g.preserveAspect = true;
            UIKit.Place(g.rectTransform, new Vector2(1, 1), new Vector2(-262, -150 - i * 40), new Vector2(32, 32));
            g.rectTransform.localRotation = Quaternion.Euler(0, 0, legend[i].rot);
            var t = UIKit.Text(panel.rectTransform, legend[i].label, 19, TextAnchor.MiddleLeft, GameLog.Info);
            UIKit.Place(t.rectTransform, new Vector2(1, 1), new Vector2(-40, -150 - i * 40), new Vector2(210, 32));
        }
        var fog = UIKit.Text(panel.rectTransform, "파란 곳은 눈으로만 본 곳, 밟은 곳은 또렷하게.\n횃불을 들면 더 넓게 보인다.", 17, TextAnchor.UpperLeft, UIKit.Dim);
        UIKit.Place(fog.rectTransform, new Vector2(1, 1), new Vector2(-40, -150 - legend.Length * 40 - 6), new Vector2(270, 60));

        fullRoot.SetActive(false);
    }

    // 지도 한 장(텍스처 + 아이콘)을 45° 돌려 그리는 도구. 미리보기와 전체 지도가 같이 쓴다.
    class MapView
    {
        readonly RectTransform content, iconLayer;
        readonly RectInt bounds;
        readonly float unit;
        readonly List<Image> pool = new List<Image>();
        Image hover;
        readonly Image playerMark;
        static readonly Quaternion Rot = Quaternion.Euler(0, 0, 45f);

        public MapView(RectTransform parent, Texture tex, RectInt bounds, float unit)
        {
            this.bounds = bounds;
            this.unit = unit;
            content = new GameObject("MapContent", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(parent, false);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = new Vector2(bounds.width * unit, bounds.height * unit);
            content.localRotation = Rot;   // 화면 위쪽 = 세계의 +X+Z 대각선 (카메라 방향과 맞춤)

            var img = new GameObject("Terrain", typeof(RawImage)).GetComponent<RawImage>();
            img.transform.SetParent(content, false);
            img.texture = tex;
            img.raycastTarget = false;
            UIKit.Stretch(img.rectTransform);

            iconLayer = new GameObject("Icons", typeof(RectTransform)).GetComponent<RectTransform>();
            iconLayer.SetParent(content, false);
            UIKit.Stretch(iconLayer);

            playerMark = MakeIcon();
            playerMark.sprite = IconSprite("arrow");
            playerMark.color = MeColor;
            playerMark.rectTransform.sizeDelta = Vector2.one * unit * 1.5f;
        }

        // 칸 좌표(소수 가능) → 지도 안 위치
        Vector2 Local(Vector2 cell) => new Vector2(
            (cell.x - bounds.xMin + 0.5f) * unit - bounds.width * unit * 0.5f,
            (cell.y - bounds.yMin + 0.5f) * unit - bounds.height * unit * 0.5f);

        // 세계 방향(yaw: +Z가 0°, 시계 방향) → 지도 안 회전. 화살표 그림은 오른쪽(+X)을 가리킨다.
        static Quaternion ArrowRotation(float yaw) => Quaternion.Euler(0, 0, 90f - yaw);

        public void CenterOn(Vector2 cell) => content.anchoredPosition = -(Vector2)(Rot * Local(cell));

        // 화면 좌표 → 칸 (45° 돌린 것까지 거꾸로 계산)
        public Vector2Int? CellAt(Vector2 screen, Camera cam)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screen, cam, out var local)) return null;
            float x = (local.x + bounds.width * unit * 0.5f) / unit - 0.5f + bounds.xMin;
            float y = (local.y + bounds.height * unit * 0.5f) / unit - 0.5f + bounds.yMin;
            var c = new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
            return bounds.Contains(c) ? c : (Vector2Int?)null;
        }

        // 이동할 수 있는 칸 위에 마우스를 올리면 그 칸을 밝게 표시
        public void SetHover(Vector2Int? cell)
        {
            if (hover == null)
            {
                hover = new GameObject("Hover", typeof(Image)).GetComponent<Image>();
                hover.transform.SetParent(iconLayer, false);
                hover.color = new Color(1f, 0.92f, 0.6f, 0.45f);
                hover.raycastTarget = false;
                hover.rectTransform.sizeDelta = new Vector2(unit, unit);
            }
            hover.enabled = cell != null;
            if (cell != null) hover.rectTransform.anchoredPosition = Local(cell.Value);
        }

        public void SetPlayer(Vector2 cell, float yaw)
        {
            playerMark.rectTransform.anchoredPosition = Local(cell);
            playerMark.rectTransform.localRotation = ArrowRotation(yaw);
            playerMark.transform.SetAsLastSibling();
        }

        public void SetIcons(List<Icon> icons)
        {
            while (pool.Count < icons.Count) pool.Add(MakeIcon());
            for (int i = 0; i < pool.Count; i++)
            {
                var img = pool[i];
                if (i >= icons.Count) { img.enabled = false; continue; }
                var ic = icons[i];
                img.enabled = true;
                img.sprite = IconSprite(ic.sprite);
                img.color = ic.color;
                img.rectTransform.sizeDelta = Vector2.one * unit * 1.2f * ic.size;
                img.rectTransform.anchoredPosition = Local(ic.cell);
                // 화살표는 바라보는 방향으로, 나머지는 화면에서 똑바로 서 있게
                img.rectTransform.localRotation = ic.yaw != null ? ArrowRotation(ic.yaw.Value) : Quaternion.Euler(0, 0, -45f);
            }
            playerMark.transform.SetAsLastSibling();
        }

        Image MakeIcon()
        {
            var img = new GameObject("Icon", typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(iconLayer, false);
            img.raycastTarget = false;
            img.preserveAspect = true;
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }
    }
}
