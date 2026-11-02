using System.Collections.Generic;
using UnityEngine;

// 정체를 모르는 물약. 색(붉은·푸른·초록·노란)만 보이고, 무슨 효과인지는 마셔 봐야 안다.
// 색과 효과의 짝은 깨어날 때마다 새로 섞이고, 한 번 마셔 본 색은 그 회차 동안 이름이 드러난다.
// 좋은 것(치유·포만·밝은 눈)도 있고 나쁜 것(독)도 있어서 "지금 마셔 볼까?"가 선택이 된다.
public static class Potions
{
    public enum Effect { Heal, Poison, Fullness, Sight }

    public static readonly ItemDef Red = Make("potion_red", "붉은 물약");
    public static readonly ItemDef Blue = Make("potion_blue", "푸른 물약");
    public static readonly ItemDef Green = Make("potion_green", "초록 물약");
    public static readonly ItemDef Yellow = Make("potion_yellow", "노란 물약");
    public static readonly ItemDef[] All = { Red, Blue, Green, Yellow };

    static readonly Dictionary<ItemDef, string> colorName = new Dictionary<ItemDef, string>
    {
        [Red] = "붉은 물약", [Blue] = "푸른 물약", [Green] = "초록 물약", [Yellow] = "노란 물약",
    };
    static readonly Dictionary<ItemDef, Effect> effect = new Dictionary<ItemDef, Effect>();
    static readonly HashSet<ItemDef> known = new HashSet<ItemDef>();

    const string UnknownDesc = "정체를 모르는 물약.\n마셔 보기 전엔 무슨 일이 일어날지 알 수 없다.\n(한 번 마셔 보면 이번 깨어남 동안은 기억한다)";

    static ItemDef Make(string id, string name) => new ItemDef { id = id, name = name, kind = ItemKind.Potion, stackable = true, desc = UnknownDesc };

    // 깨어날 때마다: 색과 효과를 새로 섞고, 아는 것도 잊는다
    public static void NewRun(int seed)
    {
        var effects = new List<Effect> { Effect.Heal, Effect.Poison, Effect.Fullness, Effect.Sight };
        var rng = new System.Random(seed);
        for (int i = effects.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (effects[i], effects[j]) = (effects[j], effects[i]); }
        known.Clear();
        for (int i = 0; i < All.Length; i++)
        {
            effect[All[i]] = effects[i];
            All[i].name = colorName[All[i]];
            All[i].desc = UnknownDesc;
        }
    }

    public static bool IsKnown(ItemDef def) => known.Contains(def);

    // 아무 색이나 하나 (상자·상인용)
    public static ItemDef RandomPotion() => All[Random.Range(0, All.Length)];

    static string EffectName(Effect e) => e switch
    {
        Effect.Heal => "치유의 물약",
        Effect.Poison => "독 물약",
        Effect.Fullness => "포만의 물약",
        _ => "밝은 눈의 물약",
    };

    static string EffectDesc(Effect e) => e switch
    {
        Effect.Heal => "상처가 아문다. 체력 +40, 독도 빠진다.",
        Effect.Poison => "쓰다. 마시면 6턴 동안 중독된다.\n…적에게 쓸 방법은 없을까?",
        Effect.Fullness => "걸쭉하고 든든하다. 허기 +50.",
        _ => "눈앞이 환해진다. 주변 반경 7칸의 지도가 밝혀진다.",
    };

    // 책장 등에서 기록을 찾았을 때: 아직 모르는 색 하나의 정체를 알려 준다 (모두 알면 null)
    public static string RevealOne()
    {
        var unknown = new List<ItemDef>();
        foreach (var d in All) if (!known.Contains(d)) unknown.Add(d);
        if (unknown.Count == 0) return null;
        var def = unknown[Random.Range(0, unknown.Count)];
        var e = effect[def];
        known.Add(def);
        string color = colorName[def];
        def.name = EffectName(e);
        def.desc = $"{color}.\n{EffectDesc(e)}";
        return $"{color} = {EffectName(e)}";
    }

    // 사용 확인 창에 보여 줄 한 줄
    public static string Preview(ItemDef def) => known.Contains(def) ? EffectDesc(effect[def]) : "무슨 일이 일어날지 모른다…";

    public static void Drink(PlayerStats p, ItemDef def)
    {
        if (!effect.ContainsKey(def) || !p.Bag.Remove(def)) return;
        var e = effect[def];
        bool first = known.Add(def);
        string color = colorName[def];
        def.name = $"{EffectName(e)}";
        def.desc = $"{color}.\n{EffectDesc(e)}";
        if (first) GameLog.Add($"{color}{Korean.Josa(color, "은", "는")} {EffectName(e)}이었다!", e == Effect.Poison ? GameLog.Bad : GameLog.Good);

        switch (e)
        {
            case Effect.Heal:
                p.Heal(40);
                p.CurePoison();
                break;
            case Effect.Poison:
                GameLog.Add("속이 뒤틀린다…", GameLog.Bad);
                p.Poison(6);
                break;
            case Effect.Fullness:
                p.Feed(50);
                break;
            case Effect.Sight:
                Minimap.RevealAround(GridTile.WorldToCell(p.transform.position), 7);
                GameLog.Add("주변의 길이 머릿속에 그려진다.", GameLog.Good);
                break;
        }
    }
}
