using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 미니맵: 가 본 곳만 안개가 걷힌다. 한 칸 밟을 때마다 주변(횃불을 들면 반경 2, 없으면 1)이 밝혀진다.
// - 미리보기(우상단): 내 주변만 보이고 나를 따라온다. 화면과 같은 방향이 되도록 45° 돌려서 그린다.
// - 미리보기를 클릭하거나 [M]: 전체 맵 + 범례. 다시 클릭·[M]·[Esc]로 닫는다.
// 지형은 텍스처에 칸 단위로 칠하고(안 밝힌 칸은 투명 = 안개), 오브젝트는 그 위에 아이콘으로 얹는다.
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

    MapData map;
    Transform door;
    Texture2D tex;
    MapView preview, full;
    GameObject fullRoot;
    Vector2Int lastCell = new Vector2Int(int.MinValue, 0);
    bool lastTorch;
    float iconTimer;
    readonly List<Icon> icons = new List<Icon>();

    public struct Icon
    {
        public Vector2Int cell;
        public string glyph;
        public Color color;
        public float size;
    }

    void Awake() => instance = this;

    void Start()
    {
        map = new MapData();
        var doorGo = GameObject.Find("P_GiantDoor");
        door = doorGo != null ? doorGo.transform : null;
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
        Build();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (FullOpen && kb.escapeKey.wasPressedThisFrame) { EscFrame = Time.frameCount; SetFull(false); }
            else if (kb.mKey.wasPressedThisFrame && !TitleScreen.IsOpen && !InventoryUI.IsOpen && !PauseMenu.IsOpen) SetFull(!FullOpen);
        }
        if (player == null || map == null || TitleScreen.IsOpen) return;

        // 칸이 바뀌었거나 횃불을 들고 내렸으면 안개를 걷는다
        var cell = GridTile.WorldToCell(player.transform.position);
        if (cell != lastCell || player.TorchLit != lastTorch)
        {
            lastCell = cell;
            lastTorch = player.TorchLit;
            map.Reveal(cell, player.TorchLit ? radiusWithTorch : radiusWithoutTorch);
            iconTimer = 0f;
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
    }

    void SetFull(bool open)
    {
        fullRoot.SetActive(open);
        if (open) { GatherIcons(); full.SetIcons(icons); }
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
                    Color col = t == MapData.Terrain.Wall ? WallColor
                        : t == MapData.Terrain.Floor ? ((x == 0 || y == 0 || x == Px - 1 || y == Px - 1) ? FloorEdge : FloorColor)
                        : Unexplored;
                    tex.SetPixel(x0 + x, y0 + y, col);
                }
        }
        tex.Apply();
    }

    // 밝혀진 칸에 있는 오브젝트를 아이콘으로 (자동 매핑)
    void GatherIcons()
    {
        icons.Clear();
        void Add(Vector2Int c, string g, Color col, float size = 1f)
        {
            if (map.IsExplored(c)) icons.Add(new Icon { cell = c, glyph = g, color = col, size = size });
        }
        foreach (var s in FindObjectsByType<StairsEvent>(FindObjectsSortMode.None)) Add(s.Cell, "▲", new Color(0.55f, 0.78f, 1f), 1.2f);
        if (door != null) Add(GridTile.WorldToCell(door.position), "▬", new Color(0.86f, 0.72f, 0.42f), 1.2f);
        foreach (var c in FindObjectsByType<ChestEvent>(FindObjectsSortMode.None)) if (!c.Used) Add(c.Cell, "■", new Color(0.95f, 0.78f, 0.3f));
        foreach (var w in FindObjectsByType<WallTorch>(FindObjectsSortMode.None)) Add(w.Cell, "●", w.IsOn ? new Color(1f, 0.62f, 0.2f) : new Color(0.45f, 0.36f, 0.28f), 0.8f);
        foreach (var b in FindObjectsByType<BonesEvent>(FindObjectsSortMode.None)) if (!b.Used) Add(b.Cell, "×", new Color(0.92f, 0.9f, 0.82f), 1.2f);
        foreach (var t in FindObjectsByType<ToolsEvent>(FindObjectsSortMode.None)) if (!t.Used) Add(t.Cell, "♦", new Color(0.72f, 0.72f, 0.78f));
        foreach (var g in FindObjectsByType<GoblinEvent>(FindObjectsSortMode.None)) Add(g.Cell, "●", new Color(0.5f, 0.85f, 0.4f));

        // 몬스터는 지금 보이는 곳(밝히는 반경 바로 바깥까지, 또는 빛 안)에 있을 때만
        int sight = (player.TorchLit ? radiusWithTorch : radiusWithoutTorch) + 1;
        var me = GridTile.WorldToCell(player.transform.position);
        foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
        {
            if (!m.enabled || m.Dead) continue;
            var d = m.Cell - me;
            if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) <= sight || m.InLight) Add(m.Cell, "●", new Color(0.95f, 0.25f, 0.2f));
        }
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
        fullRoot = fullCanvas.gameObject;
        var dim = UIKit.Image(fullCanvas, "Dim", new Color(0, 0, 0, 0.7f), true);
        UIKit.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<UIClickCatcher>().onClick = () => SetFull(false);

        var panel = UIKit.Image(fullCanvas, "Panel", new Color(0.05f, 0.045f, 0.04f, 0.97f), true);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1180, 860));
        panel.gameObject.AddComponent<Outline>().effectColor = new Color(0.55f, 0.45f, 0.3f, 0.8f);
        panel.gameObject.AddComponent<UIClickCatcher>().onClick = () => SetFull(false);
        var title = UIKit.Text(panel.rectTransform, "지도", 34, TextAnchor.UpperLeft, UIKit.Gold);
        UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(36, -24), new Vector2(300, 44));
        var close = UIKit.Text(panel.rectTransform, "[M] 또는 클릭하면 닫기", 18, TextAnchor.UpperRight, UIKit.Dim);
        UIKit.Place(close.rectTransform, new Vector2(1, 1), new Vector2(-36, -30), new Vector2(300, 30));

        var area = new GameObject("MapArea", typeof(RectTransform)).GetComponent<RectTransform>();
        area.SetParent(panel.rectTransform, false);
        UIKit.Place(area, new Vector2(0, 0.5f), new Vector2(40, -20), new Vector2(820, 760));
        // 45° 돌린 지도가 영역 안에 들어가는 칸 크기
        float unit = Mathf.Min(40f, 760f * 1.414f / (map.bounds.width + map.bounds.height));
        full = new MapView(area, tex, map.bounds, unit);
        full.CenterOn(new Vector2(map.bounds.center.x - 0.5f, map.bounds.center.y - 0.5f));

        // 범례
        (string g, Color c, string label)[] legend =
        {
            ("▲", new Color(1f, 0.92f, 0.6f), "나"),
            ("●", new Color(0.95f, 0.25f, 0.2f), "몬스터 (보일 때만)"),
            ("●", new Color(0.5f, 0.85f, 0.4f), "고블린"),
            ("■", new Color(0.95f, 0.78f, 0.3f), "상자"),
            ("●", new Color(1f, 0.62f, 0.2f), "벽 횃불 (불 붙음)"),
            ("●", new Color(0.45f, 0.36f, 0.28f), "벽 횃불 (꺼짐)"),
            ("×", new Color(0.92f, 0.9f, 0.82f), "누군가의 뼈"),
            ("♦", new Color(0.72f, 0.72f, 0.78f), "곡괭이·도끼"),
            ("▬", new Color(0.86f, 0.72f, 0.42f), "거대한 문"),
            ("▲", new Color(0.55f, 0.78f, 1f), "계단"),
        };
        var lh = UIKit.Text(panel.rectTransform, "범례", 24, TextAnchor.UpperLeft, UIKit.Gold);
        UIKit.Place(lh.rectTransform, new Vector2(1, 1), new Vector2(-40, -110), new Vector2(260, 32));
        for (int i = 0; i < legend.Length; i++)
        {
            var g = UIKit.Text(panel.rectTransform, legend[i].g, 22, TextAnchor.MiddleCenter, legend[i].c);
            UIKit.Place(g.rectTransform, new Vector2(1, 1), new Vector2(-270, -156 - i * 38), new Vector2(30, 32));
            var t = UIKit.Text(panel.rectTransform, legend[i].label, 19, TextAnchor.MiddleLeft, GameLog.Info);
            UIKit.Place(t.rectTransform, new Vector2(1, 1), new Vector2(-40, -156 - i * 38), new Vector2(220, 32));
        }
        var fog = UIKit.Text(panel.rectTransform, "검은 곳은 아직 가 보지 않은 곳.\n횃불을 들면 더 넓게 밝혀진다.", 17, TextAnchor.UpperLeft, UIKit.Dim);
        UIKit.Place(fog.rectTransform, new Vector2(1, 1), new Vector2(-40, -156 - legend.Length * 38 - 10), new Vector2(260, 60));

        fullRoot.SetActive(false);
    }

    // 지도 한 장(텍스처 + 아이콘)을 45° 돌려 그리는 도구. 미리보기와 전체 지도가 같이 쓴다.
    class MapView
    {
        readonly RectTransform content, iconLayer;
        readonly RectInt bounds;
        readonly float unit;
        readonly List<Text> pool = new List<Text>();
        readonly Text playerMark;
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

            playerMark = MakeGlyph("▲", new Color(1f, 0.92f, 0.6f), 1.3f);
        }

        // 칸 좌표(소수 가능) → 지도 안 위치
        Vector2 Local(Vector2 cell) => new Vector2(
            (cell.x - bounds.xMin + 0.5f) * unit - bounds.width * unit * 0.5f,
            (cell.y - bounds.yMin + 0.5f) * unit - bounds.height * unit * 0.5f);

        public void CenterOn(Vector2 cell) => content.anchoredPosition = -(Vector2)(Rot * Local(cell));

        public void SetPlayer(Vector2 cell, float yaw)
        {
            playerMark.rectTransform.anchoredPosition = Local(cell);
            // 세계 방향(yaw: +Z가 0°, 시계 방향) → 지도 안 회전 (지도 위쪽이 +Z)
            playerMark.rectTransform.localRotation = Quaternion.Euler(0, 0, -yaw);
            playerMark.transform.SetAsLastSibling();
        }

        public void SetIcons(List<Icon> icons)
        {
            while (pool.Count < icons.Count) pool.Add(MakeGlyph("", Color.white, 1f));
            for (int i = 0; i < pool.Count; i++)
            {
                var t = pool[i];
                if (i >= icons.Count) { t.enabled = false; continue; }
                var ic = icons[i];
                t.enabled = true;
                t.text = ic.glyph;
                t.color = ic.color;
                t.fontSize = Mathf.RoundToInt(unit * 0.8f * ic.size);
                t.rectTransform.anchoredPosition = Local(ic.cell);
            }
            playerMark.transform.SetAsLastSibling();
        }

        Text MakeGlyph(string g, Color c, float size)
        {
            var t = UIKit.Text(iconLayer, g, Mathf.RoundToInt(unit * 0.8f * size), TextAnchor.MiddleCenter, c);
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(unit * 1.6f, unit * 1.6f);
            r.localRotation = Quaternion.Euler(0, 0, -45f);   // 아이콘은 똑바로 서 있게
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);
            return t;
        }
    }
}
