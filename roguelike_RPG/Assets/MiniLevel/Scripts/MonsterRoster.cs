using System.Collections.Generic;
using UnityEngine;

// 몬스터 로스터 (worldbuilding/15_몬스터_로스터_밸런스.md). 종마다 체력·공격·행동·처음 나오는 층을 한 곳에 모은다.
// 체력은 문서 표 값 그대로. 공격은 플레이어 최대 체력(100) 대비 한 번에 깎는 비율로 정했다 (아래 표의 % = 피해 숫자).
// 같은 종이 처음 나온 층보다 깊으면 한 층마다 체력 +10%, 공격 +5% (Monster.RollStats, level = 1 + 깊이 차이).
public static class MonsterRoster
{
    public enum Species
    {
        Rat, Wolf, Slime, PoisonRat, Spitter, Corpse, Bat, SkeletonSoldier, ThiefGoblin, SkeletonArcher, BloodBat,
        GiantSlime, SkeletonCaptain,   // 준보스 (3·8층)
    }

    public enum Body { Rat, Wolf, Slime, Bat, Skeleton, Goblin }   // 어떤 모양을 빌려 쓰는가 (새 모델이 없어 임시)

    public class Def
    {
        public string name;
        public Body body;
        public int hp, atkMin, atkMax;          // atk = 플레이어 최대 체력 100에 대한 %
        public int firstFloor;
        public Monster.Behavior behavior = Monster.Behavior.Chaser;
        public int moveEvery = 1, strength = 3, sight = 3;
        public float poisonChance, extraBlock, lifesteal, parry, doubleChance;
        public bool humanoid, split, fireWeak, dropsFood;
        public int exp = 2, goldMin, goldMax = 3;
        public float weight = 1f;               // 같은 층 후보 중 뽑힐 비중
        public int maxPerFloor = 99;
        public bool newGroup;                   // 5층 엘리트 뒤 섞이는 신규 무리 (6층부터)
        public Color tint = Color.white;        // 빌린 모양에 덧칠 (변종 구분)
        public float scale = 1f;
    }

    public const int PlayerMaxHp = 100;   // 공격 % 기준

    public static readonly Dictionary<Species, Def> All = new Dictionary<Species, Def>
    {
        [Species.Rat] = new Def { name = "쥐", body = Body.Rat, hp = 11, atkMin = 4, atkMax = 6, firstFloor = 1, behavior = Monster.Behavior.Coward, strength = 3, exp = 2, weight = 3f },
        [Species.Wolf] = new Def { name = "늑대", body = Body.Wolf, hp = 14, atkMin = 5, atkMax = 8, firstFloor = 2, behavior = Monster.Behavior.Charger, strength = 6, doubleChance = 0.1f, exp = 4, goldMin = 1, goldMax = 4, weight = 2f },
        [Species.Slime] = new Def { name = "슬라임", body = Body.Slime, hp = 13, atkMin = 3, atkMax = 6, firstFloor = 2, moveEvery = 2, strength = 2, split = true, fireWeak = true, exp = 3, weight = 2f },
        [Species.PoisonRat] = new Def { name = "독쥐", body = Body.Rat, hp = 14, atkMin = 4, atkMax = 6, firstFloor = 3, behavior = Monster.Behavior.Coward, strength = 3, poisonChance = 0.3f, exp = 3, weight = 2f, tint = new Color(0.55f, 0.85f, 0.4f) },
        [Species.Spitter] = new Def { name = "침 뱉는 슬라임", body = Body.Slime, hp = 16, atkMin = 4, atkMax = 7, firstFloor = 4, behavior = Monster.Behavior.Spitter, moveEvery = 2, strength = 2, fireWeak = true, exp = 4, weight = 1.5f, tint = new Color(1f, 0.75f, 0.35f) },
        [Species.Corpse] = new Def { name = "썩은 시체", body = Body.Skeleton, hp = 25, atkMin = 8, atkMax = 12, firstFloor = 4, strength = 4, humanoid = true, exp = 8, goldMin = 2, goldMax = 6, weight = 0.8f, maxPerFloor = 1, tint = new Color(0.45f, 0.55f, 0.35f) },
        [Species.Bat] = new Def { name = "박쥐", body = Body.Bat, hp = 18, atkMin = 4, atkMax = 7, firstFloor = 6, behavior = Monster.Behavior.Skittish, strength = 4, exp = 3, weight = 2f, newGroup = true },
        [Species.SkeletonSoldier] = new Def { name = "해골 병사", body = Body.Skeleton, hp = 28, atkMin = 6, atkMax = 9, firstFloor = 6, strength = 4, sight = 4, extraBlock = 0.25f, humanoid = true, exp = 6, goldMin = 1, goldMax = 5, weight = 3f, newGroup = true },
        [Species.ThiefGoblin] = new Def { name = "도둑 고블린", body = Body.Goblin, hp = 21, atkMin = 4, atkMax = 7, firstFloor = 7, behavior = Monster.Behavior.Thief, strength = 5, sight = 4, humanoid = true, exp = 5, goldMin = 3, goldMax = 8, weight = 1.5f, newGroup = true },
        [Species.SkeletonArcher] = new Def { name = "해골 궁수", body = Body.Skeleton, hp = 28, atkMin = 6, atkMax = 9, firstFloor = 8, behavior = Monster.Behavior.Archer, moveEvery = 2, strength = 3, sight = 5, humanoid = true, exp = 6, goldMin = 1, goldMax = 5, weight = 1.5f, newGroup = true, tint = new Color(0.75f, 0.65f, 0.5f) },
        [Species.BloodBat] = new Def { name = "혈박쥐", body = Body.Bat, hp = 32, atkMin = 6, atkMax = 9, firstFloor = 8, behavior = Monster.Behavior.Skittish, strength = 5, lifesteal = 0.5f, exp = 8, weight = 0.5f, newGroup = true, tint = new Color(1f, 0.35f, 0.35f), scale = 1.2f },

        [Species.GiantSlime] = new Def { name = "거대 슬라임", body = Body.Slime, hp = 60, atkMin = 5, atkMax = 8, firstFloor = 3, moveEvery = 2, strength = 3, sight = 5, split = true, fireWeak = true, dropsFood = true, exp = 20, goldMin = 10, goldMax = 20, tint = new Color(0.6f, 0.9f, 0.7f), scale = 1.6f },
        [Species.SkeletonCaptain] = new Def { name = "해골 대장", body = Body.Skeleton, hp = 100, atkMin = 8, atkMax = 12, firstFloor = 8, strength = 6, sight = 5, parry = SkeletonCaptainParry, humanoid = true, dropsFood = true, exp = 30, goldMin = 15, goldMax = 30, tint = new Color(0.55f, 0.5f, 0.45f), scale = 1.4f },
    };

    public const float SkeletonCaptainParry = 0.25f;   // 해골 대장 쳐내기 확률

    // 층별 몬스터 수 (15번 문서 4절, 준보스 제외)
    public static (int min, int max) CountOn(int floor) => floor switch
    {
        1 => (3, 4), 2 => (5, 6), 3 => (5, 6), 4 => (6, 7), 6 => (5, 6), 7 => (6, 7), 8 => (5, 7), _ => (7, 8),
    };

    // 이 층에 나올 수 있는 일반 종
    public static List<Species> PoolOn(int floor)
    {
        var list = new List<Species>();
        foreach (var kv in All)
            if (kv.Key != Species.GiantSlime && kv.Key != Species.SkeletonCaptain && kv.Value.firstFloor <= floor) list.Add(kv.Key);
        return list;
    }

    // 몬스터에 종 수치를 넣는다 (Awake 전에 불러야 한다)
    public static void Apply(Monster m, Species s, int floor, int levelBonus = 0)
    {
        var d = All[s];
        m.displayName = d.name;
        m.baseHp = d.hp;
        m.attackMin = Mathf.Max(1, Mathf.RoundToInt(d.atkMin * PlayerMaxHp / 100f));
        m.attackMax = Mathf.Max(m.attackMin, Mathf.RoundToInt(d.atkMax * PlayerMaxHp / 100f));
        m.level = 1 + Mathf.Max(0, floor - d.firstFloor) + levelBonus;
        m.behavior = d.behavior;
        m.moveEvery = d.moveEvery;
        m.strength = d.strength;
        m.sight = d.sight;
        m.poisonChance = d.poisonChance;
        m.poisonTurns = 4;
        m.extraBlock = d.extraBlock;
        m.lifesteal = d.lifesteal;
        m.parryChance = d.parry;
        m.doubleAttackChance = d.doubleChance;
        m.humanoid = d.humanoid;
        m.splitOnDeath = d.split;
        m.fireWeak = d.fireWeak;
        m.dropsFood = d.dropsFood;
        m.expReward = d.exp;
        m.goldMin = d.goldMin;
        m.goldMax = d.goldMax;
        if (d.scale != 1f) m.transform.localScale *= d.scale;
        if (d.tint != Color.white)
            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.GetColor("_BaseColor") * d.tint);
                    else mat.color *= d.tint;
                }
    }
}
