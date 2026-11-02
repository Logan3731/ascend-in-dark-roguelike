using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 방의 문. 나무문은 한 번에 열리고, 철문은 무거워서 두 번 밀어야 열린다(한 번 = 한 턴).
// 잠긴 문은 열쇠를 써야 열린다 (열쇠는 상자에서 나오고, 잠긴 문보다 적다).
// 닫힌 문은 칸을 막는다 — 몬스터도 못 지나간다. 플레이어는 길을 찾을 때 잠기지 않은 문은 지나갈 수 있는 것으로 보고,
// 문 앞에 다다르면 연다. 한 번 열린 문은 계속 열려 있다.
// 모양은 코드로 만든다: 문틀 위 상인방 + 경첩에 달린 문짝(가로 띠) + 잠긴 문엔 금빛 자물쇠.
[RequireComponent(typeof(GridBlocker))]
public class Door : Interactable
{
    public enum Kind { Wood, Iron }

    public Kind kind;
    public bool locked;
    public bool IsOpen { get; private set; }

    static readonly Dictionary<Vector2Int, Door> all = new Dictionary<Vector2Int, Door>();
    public static Door At(Vector2Int cell) => all.TryGetValue(cell, out var d) ? d : null;
    public static IEnumerable<Door> All => all.Values;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => all.Clear();

    Transform hinge;
    float openYaw;
    int pushes;

    static readonly Color WoodColor = new Color(0.42f, 0.24f, 0.11f), WoodTrim = new Color(0.17f, 0.09f, 0.04f);
    static readonly Color IronColor = new Color(0.2f, 0.21f, 0.24f), IronTrim = new Color(0.09f, 0.09f, 0.1f);
    static readonly Color LockColor = new Color(0.86f, 0.66f, 0.22f);
    static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();

    void OnEnable() => all[Cell] = this;
    void OnDisable() { if (At(Cell) == this) all.Remove(Cell); }

    // alongX: 통로가 x 방향 (문짝은 그에 수직). toRoom: 방이 통로 축의 + 쪽이면 1 (문짝이 방 쪽으로 젖혀진다)
    public void Build(Kind kind, bool locked, bool alongX, int toRoom)
    {
        this.kind = kind;
        this.locked = locked;
        transform.rotation = Quaternion.Euler(0, alongX ? 90f : 0f, 0);   // 로컬: 통로 = z, 문짝 = x 방향
        bool iron = kind == Kind.Iron;
        var body = iron ? IronColor : WoodColor;
        var trim = iron ? IronTrim : WoodTrim;

        Box(transform, "Lintel", new Vector3(0, 3.65f, 0), new Vector3(4f, 0.7f, 0.9f), trim, iron);
        hinge = new GameObject("Hinge").transform;
        hinge.SetParent(transform, false);
        hinge.localPosition = new Vector3(-1.9f, 0, 0);
        Box(hinge, "Panel", new Vector3(1.9f, 1.65f, 0), new Vector3(3.8f, 3.3f, 0.3f), body, iron);
        for (int i = 0; i < 3; i++)   // 나무: 판자 이음 / 철: 리벳 띠
            Box(hinge, "Band", new Vector3(1.9f, 0.45f + i * 1.2f, 0), new Vector3(3.8f, iron ? 0.22f : 0.14f, 0.38f), trim, iron);
        if (!iron)   // 나무문: 세로 판자 줄
            for (int i = 1; i < 4; i++) Box(hinge, "Plank", new Vector3(i * 0.95f, 1.65f, 0), new Vector3(0.06f, 3.3f, 0.34f), trim, false);
        if (locked)   // 금빛 자물쇠 + 쇠사슬 띠
        {
            Box(hinge, "Chain", new Vector3(1.9f, 1.75f, 0), new Vector3(3.8f, 0.3f, 0.46f), new Color(0.35f, 0.3f, 0.22f), true);
            Box(hinge, "Lock", new Vector3(1.9f, 1.6f, 0), new Vector3(0.8f, 1f, 0.62f), LockColor, true);
        }
        openYaw = toRoom > 0 ? -90f : 90f;
    }

    static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Color color, bool metal)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = Mat(color, metal);
    }

    static Material Mat(Color c, bool metal)
    {
        if (mats.TryGetValue(c, out var m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metal ? 0.3f : 0f);
        m.SetFloat("_Smoothness", metal ? 0.45f : 0.15f);
        mats[c] = m;
        return m;
    }

    public override void Interact(PlayerStats player)
    {
        if (IsOpen) return;
        if (!locked) { Push(player, false); return; }

        int keys = player.Bag.Count(Items.Key);
        if (keys <= 0)
        {
            GameLog.Add("잠긴 철문이다. 열쇠가 있어야 열 수 있다.", GameLog.Warn);
            return;
        }
        DialogUI.Show($"잠긴 철문이다. 열쇠를 쓸까?\n(가진 열쇠 {keys}개 · 쓰면 자물쇠에 남는다)",
            new DialogUI.Choice("열쇠를 쓴다", () =>
            {
                player.Bag.Remove(Items.Key);
                locked = false;
                GameLog.Add("철컥. 자물쇠가 풀렸다.", GameLog.Good);
                Push(player, true);
            }),
            new DialogUI.Choice("그만둔다", null));
    }

    // 한 턴을 써서 문을 민다 (철문은 두 번 밀어야 열린다). 그사이 몬스터도 움직인다.
    public void Push(PlayerStats player, bool openNow)
    {
        if (IsOpen || locked) return;
        var mover = player.GetComponent<GridClickMover>();
        if (mover != null) mover.FaceTo(transform.position);
        TurnResolver.BeginTurn(GridTile.WorldToCell(player.transform.position), mover != null ? mover.moveSeconds : 0.3f);
        if (kind == Kind.Iron && !openNow && ++pushes < 2)
        {
            GameLog.Add("무거운 철문이 조금 밀렸다…", GameLog.Info);
            StartCoroutine(Nudge());
        }
        else
        {
            Open();
            GameLog.Add(kind == Kind.Wood ? "나무문을 열었다." : "철문이 끼이익 열렸다.", GameLog.Info);
        }
        TurnResolver.ResolveAttacks(player, null);
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + 0.35f);
    }

    void Open()
    {
        IsOpen = true;
        GetComponent<GridBlocker>().enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;   // 열린 문 너머 바닥을 누를 수 있게
        StartCoroutine(Swing(Quaternion.Euler(0, openYaw, 0), 0.4f));
    }

    IEnumerator Nudge()
    {
        yield return Swing(Quaternion.Euler(0, openYaw * 0.12f, 0), 0.15f);
        yield return Swing(Quaternion.identity, 0.2f);
    }

    IEnumerator Swing(Quaternion to, float seconds)
    {
        var from = hinge.localRotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            hinge.localRotation = Quaternion.Slerp(from, to, 1f - (1f - t) * (1f - t));
            yield return null;
        }
        hinge.localRotation = to;
    }
}
