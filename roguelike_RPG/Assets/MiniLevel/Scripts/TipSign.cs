using System.Collections.Generic;
using UnityEngine;

// 팁 표지판 (설계 A6). 클릭하면 팁 한 줄이 화면 상단에 뜬다. 처음 읽을 때 가끔 골드가 나온다.
// 읽은 팁은 기억되어(TowerMemory.MarkHint) 다음 회차엔 잘 안 나오지만, 가끔 다시 나온다.
// 계단 방에는 반드시 하나 있다 (FloorGenerator).
public class TipSign : Prop
{
    public const float GoldChance = 0.35f;
    public const int GoldMin = 3, GoldMax = 10;
    public const float ReshowChance = 0.25f;   // 이미 본 팁을 다시 고를 확률 (안 본 팁이 남아 있어도)
    public const float ModelFootprint = 1.4f;   // 모델 가로 기준 (세로가 두 배라 높이 약 2.8, 한 칸 = 4)

    // 표지판에 적히는 팁 (id는 기억용)
    static readonly (string id, string text)[] Pool =
    {
        ("tip_sneak", "잠든 적을 먼저 때리면 기습이다."),
        ("tip_light", "빛 안의 적은 약해진다."),
        ("tip_torch", "횃불을 들면 잠든 적이 잘 깬다."),
        ("tip_trap", "함정은 숨어 있다. 돋보기로 찾을 수 있다."),
        ("tip_dart", "다트를 던져 함정을 미리 터뜨릴 수 있다."),
        ("tip_fire", "불에 약한 적이 있다."),
        ("tip_potion", "물약은 마셔 보기 전엔 정체를 모른다."),
        ("tip_rubble", "무너진 벽 너머엔 숨겨진 방이 있다."),
        ("tip_grass", "풀숲을 헤치면 무언가 나올 때가 있다."),
        ("tip_burn", "풀이 타도 골드는 남는다."),
        ("tip_wolf", "늑대는 곧은 길에서 덮쳐 온다."),
        ("tip_shield", "방패로 때리면 적이 휘청일 때가 있다."),
    };

    public string tipId;
    public string message;
    bool read;

    // 안 본 팁을 먼저 고르고, 가끔은 본 팁도 다시 고른다
    public static (string id, string text) PickTip()
    {
        var unseen = new List<(string, string)>();
        foreach (var t in Pool) if (!TowerMemory.HintSeen(t.id)) unseen.Add(t);
        if (unseen.Count > 0 && Random.value >= ReshowChance) return unseen[Random.Range(0, unseen.Count)];
        return Pool[Random.Range(0, Pool.Length)];
    }

    // FloorGenerator가 만든다. message가 null이면 처음 읽을 때 팁을 고른다.
    public static TipSign Create(Vector2Int cell, Transform parent, Quaternion rot, string id = null, string message = null)
    {
        var go = new GameObject("TipSign");
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(GridTile.CellToWorld(cell), rot);
        if (ResourceModel.Spawn("Sign", go.transform, ModelFootprint) == null) BuildPrimitive(go.transform);
        var s = go.AddComponent<TipSign>();
        s.tipId = id;
        s.message = message;
        return s;
    }

    static void BuildPrimitive(Transform t)
    {
        var wood = new Color(0.4f, 0.27f, 0.15f);
        Part(PrimitiveType.Cube, t, new Vector3(0, 1.1f, 0), new Vector3(0.22f, 2.2f, 0.22f), wood);
        Part(PrimitiveType.Cube, t, new Vector3(0, 2.1f, 0.05f), new Vector3(1.8f, 0.9f, 0.12f), new Color(0.5f, 0.35f, 0.2f));
    }

    static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        var m = g.GetComponent<Renderer>().material;
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
    }

    public override void Interact(PlayerStats player)
    {
        if (message == null) (tipId, message) = PickTip();
        SpendTurn(player, transform.position);
        TipBanner.Show(message);
        if (!string.IsNullOrEmpty(tipId)) TowerMemory.MarkHint(tipId);
        if (read) return;
        read = true;
        if (Random.value < GoldChance)
        {
            int g = Random.Range(GoldMin, GoldMax + 1);
            player.AddGold(g);
            GameLog.Add($"표지판 밑에서 골드 {g}닢을 주웠다.", GameLog.Good);
            DamagePopup.Spawn(DamagePopup.Above(player), $"+{g} 골드", DamagePopup.Big, 0.9f);
        }
    }
}
