using System.Collections.Generic;
using UnityEngine;

// 시야. 내 칸에서 벽(과 닫힌 문)에 막히지 않고 선이 닿는 칸만 보인다 — 벽 너머는 보이지 않는다.
// - 지금 보이는 칸: 나에게서 멀수록 어둡게 (횃불을 들면 멀리까지, 벽 횃불 등 빛이 있는 곳은 더 멀리서도 보인다)
// - 전에 본 칸: 흐릿하게 기억만 (적은 보이지 않는다)
// - 본 적 없는 칸: 아무것도 그리지 않는다 (까만 어둠)
// 바닥·벽 타일과 칸 위의 물건(상자·문·몬스터…)을 함께 다룬다. FloorGenerator가 맵을 만든 뒤 붙인다.
public class FogOfWar : MonoBehaviour
{
    public int radiusDark = 3;      // 횃불 없이 보이는 거리 (칸)
    public int radiusTorch = 6;     // 횃불을 들었을 때
    public int litRange = 10;       // 빛이 있는 칸은 이만큼 떨어져 있어도 (가리는 것만 없으면) 보인다
    const float RememberTint = 0.2f, FarTint = 0.45f;

    class Tile
    {
        public Renderer[] renderers;
        public Color[][] baseColors;   // 렌더러별·재질별 원래 색
        public int state = -1;         // 0 안 봄, 1 기억, 2 보임 (바뀔 때만 다시 칠한다)
        public float tint = -1f;
    }

    System.Func<Vector2Int, bool> blocksSight;
    readonly Dictionary<Vector2Int, Tile> tiles = new Dictionary<Vector2Int, Tile>();
    readonly HashSet<Vector2Int> explored = new HashSet<Vector2Int>();
    readonly Dictionary<Vector2Int, float> visible = new Dictionary<Vector2Int, float>();   // 칸 → 밝기
    readonly Dictionary<Component, Renderer[]> objectRenderers = new Dictionary<Component, Renderer[]>();
    readonly List<Transform> extraObjects = new List<Transform>();
    PlayerStats player;
    MaterialPropertyBlock block;
    Vector2Int lastCell = new Vector2Int(int.MinValue, 0);
    bool lastTorch;
    int lastTurn = -1;
    float nextObjectPass;
    bool dirty = true;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public void Init(IReadOnlyDictionary<Vector2Int, GameObject> floors, IReadOnlyDictionary<Vector2Int, GameObject> walls,
                     System.Func<Vector2Int, bool> blocks, IEnumerable<Transform> extras)
    {
        blocksSight = blocks;
        foreach (var kv in floors) AddTile(kv.Key, kv.Value);
        foreach (var kv in walls) AddTile(kv.Key, kv.Value);
        foreach (var t in extras) if (t != null) extraObjects.Add(t);
    }

    void AddTile(Vector2Int cell, GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        var t = new Tile { renderers = rs, baseColors = new Color[rs.Length][] };
        for (int i = 0; i < rs.Length; i++)
        {
            var mats = rs[i].sharedMaterials;
            t.baseColors[i] = new Color[mats.Length];
            for (int m = 0; m < mats.Length; m++)
                t.baseColors[i][m] = mats[m] != null && mats[m].HasProperty(BaseColorId) ? mats[m].GetColor(BaseColorId) : Color.white;
        }
        // 한 칸에 바닥과 벽이 둘 다 있을 일은 없지만, 있으면 합친다
        if (tiles.TryGetValue(cell, out var old))
        {
            var merged = new List<Renderer>(old.renderers); merged.AddRange(t.renderers);
            var colors = new List<Color[]>(old.baseColors); colors.AddRange(t.baseColors);
            old.renderers = merged.ToArray(); old.baseColors = colors.ToArray();
        }
        else tiles[cell] = t;
    }

    void Start()
    {
        block = new MaterialPropertyBlock();
        player = FindAnyObjectByType<PlayerStats>();
    }

    void OnEnable() => TurnManager.OnTurnEnded += MarkDirty;
    void OnDisable() => TurnManager.OnTurnEnded -= MarkDirty;
    void MarkDirty(int turn) => dirty = true;

    void LateUpdate()
    {
        if (player == null) { player = FindAnyObjectByType<PlayerStats>(); if (player == null) return; }
        var cell = GridTile.WorldToCell(player.transform.position);
        if (cell != lastCell || player.TorchLit != lastTorch || TurnManager.Turn != lastTurn) dirty = true;
        if (dirty)
        {
            dirty = false;
            lastCell = cell;
            lastTorch = player.TorchLit;
            lastTurn = TurnManager.Turn;
            ComputeVisible(cell);
            PaintTiles();
            PaintObjects();
        }
        else if (Time.time >= nextObjectPass) PaintObjects();   // 몬스터는 턴 사이에도 움직인다
    }

    // ---- 무엇이 보이는가 ----

    void ComputeVisible(Vector2Int from)
    {
        visible.Clear();
        float radius = (player.TorchLit ? radiusTorch : radiusDark) + 0.5f;
        int reach = Mathf.Max(litRange, Mathf.CeilToInt(radius));
        for (int dx = -reach; dx <= reach; dx++)
            for (int dy = -reach; dy <= reach; dy++)
            {
                var c = from + new Vector2Int(dx, dy);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                bool near = d <= radius;
                if (!near && (d > litRange || !LightSources.IsLit(c))) continue;
                if (!LineClear(from, c)) continue;
                // 가까울수록 밝게. 멀리서 빛 덕에 보이는 칸은 어둑하게
                float tint = near ? Mathf.Lerp(1f, FarTint, Mathf.Clamp01((d - 1f) / Mathf.Max(1f, radius - 1f))) : FarTint;
                visible[c] = tint;
                explored.Add(c);
            }
    }

    // 두 칸 사이에 시야를 막는 칸(벽·닫힌 문)이 있는가. 끝 칸 자체는 막아도 보인다 (벽면이 보이게)
    bool LineClear(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(b.x - a.x), dy = Mathf.Abs(b.y - a.y);
        int sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1;
        int err = dx - dy;
        var c = a;
        while (c != b)
        {
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; c.x += sx; }
            if (e2 < dx) { err += dx; c.y += sy; }
            if (c == b) break;
            if (Opaque(c)) return false;
        }
        return true;
    }

    bool Opaque(Vector2Int c)
    {
        if (blocksSight != null && blocksSight(c)) return true;
        var door = Door.At(c);
        return door != null && !door.IsOpen;
    }

    // ---- 칠하기 ----

    void PaintTiles()
    {
        foreach (var kv in tiles)
        {
            var t = kv.Value;
            int state = visible.ContainsKey(kv.Key) ? 2 : explored.Contains(kv.Key) ? 1 : 0;
            float tint = state == 2 ? visible[kv.Key] : state == 1 ? RememberTint : 0f;
            if (state == t.state && Mathf.Abs(tint - t.tint) < 0.01f) continue;
            t.state = state;
            t.tint = tint;
            for (int i = 0; i < t.renderers.Length; i++)
            {
                var r = t.renderers[i];
                if (r == null) continue;
                r.forceRenderingOff = state == 0;
                if (state == 0) continue;
                for (int m = 0; m < t.baseColors[i].Length; m++)
                {
                    var bc = t.baseColors[i][m];
                    block.Clear();
                    block.SetColor(BaseColorId, new Color(bc.r * tint, bc.g * tint, bc.b * tint, bc.a));
                    r.SetPropertyBlock(block, m);
                }
            }
        }
    }

    // 칸 위의 물건: 지금 보이는 칸이면 보이고, 기억하는 칸이면 (적이 아닌 것만) 보이고, 아니면 숨긴다.
    // forceRenderingOff를 쓴다 — 물건이 스스로 켜고 끄는 렌더러(꺼진 불꽃 등)를 건드리지 않게.
    void PaintObjects()
    {
        nextObjectPass = Time.time + 0.15f;
        foreach (var it in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
        {
            if (it is ThrownTorch || it is MessageTrigger) continue;
            var cell = it.Cell;
            bool see = visible.ContainsKey(cell);
            bool show = it is Monster ? see : see || explored.Contains(cell);
            if (it is Monster m) m.Unseen = !show;
            SetShown(it, show);
        }
        foreach (var t in extraObjects)
            if (t != null)
            {
                var cell = GridTile.WorldToCell(t.position);
                SetShown(t, visible.ContainsKey(cell) || explored.Contains(cell));
            }
    }

    void SetShown(Component c, bool show)
    {
        if (!objectRenderers.TryGetValue(c, out var rs) || rs.Length == 0 || HasNull(rs))
            objectRenderers[c] = rs = c.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rs) if (r != null) r.forceRenderingOff = !show;
    }

    static bool HasNull(Renderer[] rs)
    {
        foreach (var r in rs) if (r == null) return true;
        return false;
    }
}
