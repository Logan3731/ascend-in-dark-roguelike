using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 정체를 모르는 물약. 색(붉은·푸른·초록·노란)만 보이고, 무슨 효과인지는 써 봐야 안다.
// 효과는 여섯 가지인데 깨어날 때마다 그중 넷(치유는 늘 포함)이 네 색에 섞여 들어간다 → 지난번 지식이 그대로 통하지 않는다.
// 마셔도 되고 적에게 던져도 된다. 나쁜 물약(독·불꽃·잠)은 던지면 무기가 되고, 좋은 물약은 던지면 아깝다.
// 마시든 던지든 한 번 써 본 색은 그 회차 동안 이름이 드러난다.
public static class Potions
{
    public enum Effect { Heal, Poison, Fullness, Sight, Fire, Sleep }

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

    const string UnknownDesc = "정체를 모르는 물약.\n마셔 보거나 적에게 던져 보기 전엔 무슨 일이 일어날지 알 수 없다.\n(한 번 써 보면 이번 깨어남 동안은 기억한다)";

    const int SleepTurns = 4;          // 마시면 잠드는 턴
    const int EnemySleepTurns = 6;     // 던지면 적이 깊이 잠드는 턴 (그동안은 다가가도 깨지 않는다)
    const int EnemyPoisonTurns = 5;    // 던지면 적이 중독되는 턴 (턴마다 2)

    static ItemDef Make(string id, string name) => new ItemDef { id = id, name = name, kind = ItemKind.Potion, stackable = true, desc = UnknownDesc };

    // 깨어날 때마다: 치유 + 나머지 다섯 중 셋을 골라 네 색에 섞고, 아는 것도 잊는다
    public static void NewRun(int seed)
    {
        var rng = new System.Random(seed);
        var others = new List<Effect> { Effect.Poison, Effect.Fullness, Effect.Sight, Effect.Fire, Effect.Sleep };
        Shuffle(others, rng);
        var effects = new List<Effect> { Effect.Heal };
        effects.AddRange(others.GetRange(0, All.Length - 1));
        Shuffle(effects, rng);
        known.Clear();
        PlayerAsleep = false;   // 자다가 죽어서 다시 깨어난 경우
        for (int i = 0; i < All.Length; i++)
        {
            effect[All[i]] = effects[i];
            All[i].name = colorName[All[i]];
            All[i].desc = UnknownDesc;
        }
    }

    static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
    }

    public static bool IsKnown(ItemDef def) => known.Contains(def);

    // 아무 색이나 하나 (상자·상인용)
    public static ItemDef RandomPotion() => All[Random.Range(0, All.Length)];

    static string EffectName(Effect e) => e switch
    {
        Effect.Heal => "치유의 물약",
        Effect.Poison => "독 물약",
        Effect.Fullness => "포만의 물약",
        Effect.Sight => "밝은 눈의 물약",
        Effect.Fire => "불꽃 물약",
        _ => "잠의 물약",
    };

    static string DrinkDesc(Effect e) => e switch
    {
        Effect.Heal => "마시면: 체력 +40, 독도 빠진다.",
        Effect.Poison => "마시면: 6턴 동안 중독된다.",
        Effect.Fullness => "마시면: 허기 +50.",
        Effect.Sight => "마시면: 15턴 동안 내 주변이 환해진다. (빛 안의 적은 약해진다)",
        Effect.Fire => "마시면: 속이 타들어 간다. 체력 -8.",
        _ => $"마시면: {SleepTurns}턴 동안 잠든다. 맞으면 깬다.",
    };

    static string ThrowDesc(Effect e) => e switch
    {
        Effect.Heal => "던지면: 맞은 적의 상처가 아문다. (아깝다)",
        Effect.Poison => $"던지면: 맞은 곳 주변의 적이 {EnemyPoisonTurns}턴 동안 중독된다. (턴마다 2)",
        Effect.Fullness => "던지면: 바닥에 쏟아질 뿐이다.",
        Effect.Sight => "던지면: 터진 자리가 8턴 동안 환해진다. (빛 안의 적은 약해진다)",
        Effect.Fire => "던지면: 불길이 번져 주변 적에게 불 피해 5~8. 불에 약한 적은 두 배.",
        _ => $"던지면: 주변 적이 {EnemySleepTurns}턴 동안 깊이 잠든다. 그사이 때리면 기습!",
    };

    static string FullDesc(ItemDef def, Effect e) => $"{colorName[def]}.\n{DrinkDesc(e)}\n{ThrowDesc(e)}";

    // 정체를 알게 된다: 이름과 설명을 바꾸고, 처음이면 기록을 남긴다
    static void Learn(ItemDef def, Effect e)
    {
        bool first = known.Add(def);
        def.name = EffectName(e);
        def.desc = FullDesc(def, e);
        string color = colorName[def];
        if (first) GameLog.Add($"{color}{Korean.Josa(color, "은", "는")} {EffectName(e)}이었다!", GameLog.Warn);
    }

    // 책장 등에서 기록을 찾았을 때: 아직 모르는 색 하나의 정체를 알려 준다 (모두 알면 null)
    public static string RevealOne()
    {
        var unknown = new List<ItemDef>();
        foreach (var d in All) if (!known.Contains(d)) unknown.Add(d);
        if (unknown.Count == 0) return null;
        var def = unknown[Random.Range(0, unknown.Count)];
        var e = effect[def];
        known.Add(def);
        def.name = EffectName(e);
        def.desc = FullDesc(def, e);
        return $"{colorName[def]} = {EffectName(e)}";
    }

    // 사용 확인 창에 보여 줄 줄들
    public static string Preview(ItemDef def) => known.Contains(def) ? DrinkDesc(effect[def]) : "무슨 일이 일어날지 모른다…";
    public static string ThrowPreview(ItemDef def) => known.Contains(def) ? ThrowDesc(effect[def]) : "적에게 던져서 정체를 알아낼 수도 있다.";

    // 던질 때 날아가는 병의 색
    public static Color BottleColor(ItemDef def) =>
        def == Red ? new Color(0.85f, 0.15f, 0.12f) : def == Blue ? new Color(0.2f, 0.4f, 0.95f)
        : def == Green ? new Color(0.3f, 0.8f, 0.25f) : new Color(0.95f, 0.8f, 0.2f);

    public static void Drink(PlayerStats p, ItemDef def)
    {
        if (!effect.ContainsKey(def) || !p.Bag.Remove(def)) return;
        var e = effect[def];
        Learn(def, e);

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
                PotionGlow.Create(GridTile.WorldToCell(p.transform.position), 15, new Color(0.75f, 0.85f, 1f), p.transform, 3.5f);
                GameLog.Add("눈앞이 환해진다. 어둠 속이 또렷이 보인다.", GameLog.Good);
                break;
            case Effect.Fire:
                GameLog.Add("목구멍이 타들어 간다!", GameLog.Bad);
                DamagePopup.Spawn(DamagePopup.Above(p), "-8", DamagePopup.Hurt, 1.2f);
                p.Damage(8);
                break;
            case Effect.Sleep:
                var mover = p.GetComponent<GridClickMover>();
                if (mover != null) mover.StartCoroutine(Sleep(p, mover));
                break;
        }
    }

    // 마신 잠의 물약에 취해 자는 중 (GridClickMover가 조작을 막는다)
    public static bool PlayerAsleep { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => PlayerAsleep = false;

    // 마신 잠의 물약: 정해진 턴 동안 저절로 턴이 넘어간다 (그사이 적은 움직이고 때린다). 맞으면 깬다.
    static IEnumerator Sleep(PlayerStats p, GridClickMover mover)
    {
        PlayerAsleep = true;
        GameLog.Add("눈꺼풀이 무거워진다… 잠이 쏟아진다.", GameLog.Bad);
        DamagePopup.Spawn(DamagePopup.Above(p, 1.2f), "Zzz", DamagePopup.Miss, 1.3f);
        GridClickMover.StopWalking();
        var ui = Object.FindAnyObjectByType<InventoryUI>();
        if (ui != null) ui.Close();
        bool hurt = false;
        for (int i = 0; i < SleepTurns && !p.Dead; i++)
        {
            while (Time.time < TurnResolver.BusyUntil) yield return null;   // 앞 턴 연출이 끝날 때까지
            yield return new WaitForSeconds(0.4f);
            if (p.Dead) break;
            TurnResolver.BeginTurn(GridTile.WorldToCell(p.transform.position), mover.moveSeconds);
            TurnResolver.ResolveAttacks(p, null);
            TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + mover.moveSeconds);
            if (TurnResolver.AttackedThisTurn) { hurt = true; break; }
        }
        PlayerAsleep = false;
        if (!p.Dead) GameLog.Add(hurt ? "아픔에 퍼뜩 잠이 깼다!" : "잠에서 깼다.", hurt ? GameLog.Warn : GameLog.Info);
    }

    // 던진 물약이 깨진다: 효과는 바로 적용하고(이번 턴 적의 행동보다 먼저), 연출만 delay 뒤에 보인다.
    public static void Shatter(PlayerStats p, ItemDef def, Vector2Int land, float delay)
    {
        if (!effect.ContainsKey(def)) return;
        var e = effect[def];
        Learn(def, e);

        // 깨진 자리와 그 주변 한 칸의 적
        var targets = new List<Monster>();
        foreach (var m in Object.FindObjectsByType<Monster>(FindObjectsSortMode.None))
            if (m.enabled && !m.Dead && GridNav.Distance(m.Cell, land) <= 1) targets.Add(m);

        switch (e)
        {
            case Effect.Heal:
                if (targets.Count == 0) GameLog.Add("물약이 바닥에 스며들었다.", GameLog.Info);
                foreach (var m in targets) m.HealBy(40, delay);
                break;
            case Effect.Poison:
                if (targets.Count == 0) GameLog.Add("독한 냄새가 피어올랐다가 흩어졌다.", GameLog.Info);
                foreach (var m in targets) m.Poison(p, EnemyPoisonTurns, delay);
                break;
            case Effect.Fullness:
                GameLog.Add("걸쭉한 물약이 바닥에 쏟아졌다. 아깝다…", GameLog.Info);
                break;
            case Effect.Sight:
                GameLog.Add("물약이 터지며 눈부신 빛이 번진다!", GameLog.Good);
                PotionGlow.Create(land, 8, new Color(0.75f, 0.85f, 1f));
                break;
            case Effect.Fire:
                GameLog.Add("물약이 터지며 불길이 번진다!", GameLog.Good);
                PotionGlow.Create(land, 3, new Color(1f, 0.45f, 0.15f));
                foreach (var m in targets) m.HitByThrow(p, Random.Range(5, 9), "불꽃 물약", delay, true);
                break;
            case Effect.Sleep:
                if (targets.Count == 0) GameLog.Add("달큰한 안개가 피어올랐다가 흩어졌다.", GameLog.Info);
                foreach (var m in targets) m.PutToSleep(EnemySleepTurns, delay);
                break;
        }
    }
}
