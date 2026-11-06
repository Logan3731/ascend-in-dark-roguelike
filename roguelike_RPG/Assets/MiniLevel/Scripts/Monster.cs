using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 턴제 몬스터. 플레이어가 한 턴을 쓰면 몬스터도 한 번 움직인다.
// 시야(칸) 안에 플레이어가 들어오면 그때부터 쫓아오고, 붙으면 공격한다. 클릭하면 플레이어가 다가가서 때린다.
[RequireComponent(typeof(GridBlocker))]
public class Monster : Interactable
{
    public string displayName = "몬스터";

    [Header("레벨 1 기준 능력치")]
    public int level = 1;
    public int baseHp = 10;
    [Range(0f, 0.5f)] public float hpVariance = 0.15f;   // 같은 레벨·종족이어도 체력이 ±이만큼 달라진다
    public int attackMin = 1, attackMax = 3;
    public int strength = 3;                               // 같은 턴에 서로 때리면 힘이 높은 쪽이 먼저
    [Range(0f, 1f)] public float evasion = 0.1f;          // (쓰지 않음: 회피는 모든 종족이 DodgeChance로 같다 — 설계 A2)
    public int sight = 3;                                  // 이 거리(칸) 안에 들어오면 움직이기 시작
    public int moveEvery = 1;                              // 몇 턴마다 한 번 움직이는가 (슬라임 2)

    [Header("종족 특징")]
    [Range(0f, 1f)] public float doubleAttackChance;       // 늑대: 가끔 두 번 공격
    [Range(0f, 1f)] public float poisonChance;             // 거대 쥐: 확률로 중독
    public int poisonTurns = 4;
    public bool splitOnDeath;                              // 슬라임: 죽으면 절반 능력치 둘로 분열 (한 번만)

    // 종족마다 다르게 움직인다 — 상대에 따라 대응을 바꾸게
    public enum Behavior
    {
        Chaser,     // 그냥 쫓아온다 (슬라임)
        Charger,    // 일직선 2~3칸이면 몸을 낮춰 예고 → 다음 턴 돌진. 비키면 헐떡이며 한 턴 무방비 (늑대)
        Skittish,   // 벽 횃불·던진 횃불의 빛엔 들어오지 않는다. 치고 빠진다 (박쥐)
        Coward,     // 다치면 달아난다(느리게). 깨어나면 근처 동료를 부른다 (쥐)
        Spitter,    // 거리를 두고 산성 침을 뱉는다: 주황 칸으로 예고 → 다음 턴 그 칸에 떨어진다 (침 뱉는 슬라임)
        Archer,     // 느리다. 가로·세로·대각선 일직선에 트여 있으면 화살을 쏜다, 아니면 쫓아온다 (해골 궁수)
        Thief,      // 가끔 공격 대신 물건을 훔치고 겁에 질려 달아난다. 쓰러뜨리면 돌려받는다 (도둑 고블린)
        Giant,      // 느리고 아프다. 붙으면 가끔 붉은 칸으로 예고 → 다음 턴 휘두르기(전방 3칸)·포효(주변 8칸 + 밀쳐내기). 맞으면 파편이 튄다 (거인 해골)
    }
    [Header("행동")]
    public Behavior behavior = Behavior.Chaser;
    public bool fireWeak;                                  // 슬라임: 던진 횃불에 두 배 피해

    [Header("처치 보상")]
    public int goldMin = 0, goldMax = 3;
    public int expReward = 2;                              // 쓰러뜨리면 쌓이는 전투 경험 (죽으면 경험의 정수)

    public int MaxHp { get; private set; }
    public int Hp { get; private set; }
    public bool Dead => Hp <= 0;
    public int AtkMin { get; private set; }
    public int AtkMax { get; private set; }
    public int Strength { get; private set; }
    public bool WillAttack { get; private set; }           // 이번 턴에 이동 대신 공격한다
    public bool Unseen { get; set; }                       // 시야 밖이라 보이지 않는다 (FogOfWar가 정한다 — 체력바도 숨긴다)
    public string Label => $"Lv.{level} {displayName}";

    // 빛 안이면 약해지고(이름 흰색) 어둠 속이면 사나워진다(이름 빨간색)
    public bool InLight => LightSources.IsLit(Cell);
    const float LitDamage = 0.75f, DarkDamage = 1.3f;

    // ---- 회피·막기 (설계 A2): 플레이어와 모든 몬스터 공통 ----
    public const float DodgeChance = 0.15f;                // 회피: 피해 0
    public const float BlockChance = 0.15f;                // 막기: 피해를 80~100% 줄인다
    public const float BlockCutMin = 0.8f, BlockCutMax = 1f;
    const float SwiftBonus = 0.15f;                        // 재빠른 정예: 회피 +15%
    float BaseEvasion => DodgeChance + (elite && affix == Affix.Swift ? SwiftBonus : 0f);
    // 빛 안이면 절반, 어둠 속이면 +10% (기절·헐떡임 중에는 피하지도 막지도 못한다)
    float Evasion => stunned > 0 ? 0f : InLight ? BaseEvasion * 0.5f : Mathf.Min(0.9f, BaseEvasion + 0.1f);
    float Block => stunned > 0 ? 0f : BlockChance + extraBlock;
    [Range(0f, 0.6f)] public float extraBlock;             // 막기 확률 추가 (해골 병사)
    public static int BlockedDamage(int dmg) => Mathf.RoundToInt(dmg * (1f - Random.Range(BlockCutMin, BlockCutMax)));

    const float TripleGap = TurnResolver.TripleGap;     // 3연격 타격 사이 (모션이 타격 프레임까지 가도록)
    const float FollowUpGap = TurnResolver.PlayerComboGap;   // 연속 공격·보조칸 추가 타격 사이

    // 행동 상태
    int stunned;               // 남은 무방비 턴 (헐떡임)
    bool charging;             // 돌진 예고 중
    Vector2Int chargeDir;
    int chargeCooldown;
    bool retreat;              // 박쥐: 방금 쳤으니 이번 턴엔 물러난다
    bool fleeing;              // 쥐: 겁먹고 달아나는 중
    float nextHitMult = 1f;    // 돌진으로 덮친 공격은 두 배
    readonly List<GameObject> warnTiles = new List<GameObject>();
    static bool darkHintShown;
    static readonly HashSet<Behavior> hinted = new HashSet<Behavior>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHint() { darkHintShown = false; hinted.Clear(); sleepHintShown = false; }

    bool awake;
    bool isSplitChild;
    MonsterHealthBar bar;

    // 연출은 모델(첫 번째 자식)만 움직인다
    Transform Visual => transform.childCount > 0 ? transform.GetChild(0) : null;
    Vector3 visualHome;

    float LungeImpact => HasClips ? animImpact : TurnResolver.MonsterImpact;   // 몬스터 공격이 닿는 순간 (동작 모델이면 그 타격 프레임, 아니면 튀어 오른 정점)
    const float PlayerImpact = TurnResolver.PlayerImpact;   // 플레이어 공격이 닿는 순간 (모션의 타격 프레임)
    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    protected override void Awake()
    {
        base.Awake();
        if (Visual != null) visualHome = Visual.localPosition;
        ApplyTraits();
        RollStats();
        RollTemperament();
    }

    void Start()
    {
        InitAnimation();
        bar = MonsterHealthBar.Create(this);
        home = Cell;
        AddEliteGlow();
    }

    public bool IsAwake => awake && !Asleep;

    // 우두머리가 불러낸 졸개 등: 바로 깨어 덤빈다
    public void ForceAwake()
    {
        Asleep = false;
        deepSleep = 0;
        awake = true;
    }

    // 정예를 처음 마주쳤을 때 한 번: 어떤 성질인지 알려 준다
    bool announced;
    void AnnounceIfElite()
    {
        if (announced || (!elite && !boss)) return;
        announced = true;
        if (boss) GameLog.Add($"{displayName}{Josa("이", "가")} 모습을 드러냈다!", GameLog.Bad);
        else GameLog.Add($"{displayName}{Josa("이", "가")} 나타났다!", GameLog.Bad);
    }

    void OnDestroy() { if (bar != null) Destroy(bar.gameObject); ClearWarning(); }

    void OnEnable() => TurnManager.OnTurnEnded += OnTurnEnded;
    void OnDisable() => TurnManager.OnTurnEnded -= OnTurnEnded;

    // ---- 던진 물약의 효과: 중독·깊은 잠·회복 ----

    int poisonLeft;
    PlayerStats poisoner;      // 독으로 쓰러지면 경험은 던진 사람에게
    int deepSleep;             // 잠의 물약에 취한 남은 턴: 이동안은 다가가도·소리가 나도 깨지 않는다

    public void Poison(PlayerStats by, int turns, float delay)
    {
        poisoner = by;
        poisonLeft = Mathf.Max(poisonLeft, turns);
        GameLog.Add($"{displayName}{Josa("이", "가")} 중독되었다!", GameLog.Good);
        DamagePopup.Spawn(DamagePopup.Above(this), "중독!", DamagePopup.Poison, 1f, delay);
    }

    public void PutToSleep(int turns, float delay)
    {
        Asleep = true;
        deepSleep = turns;
        WillAttack = false;
        charging = false;
        ClearWarning();
        GameLog.Add($"{displayName}{Josa("이", "가")} 픽 쓰러져 잠들었다.", GameLog.Good);
        DamagePopup.Spawn(DamagePopup.Above(this), "Zzz", DamagePopup.Miss, 1.3f, delay);
    }

    public void HealBy(int amount, float delay)
    {
        int before = Hp;
        Hp = Mathf.Min(MaxHp, Hp + amount);
        if (bar != null) bar.Refresh();
        GameLog.Add($"{displayName}의 상처가 아물었다…", GameLog.Bad);
        DamagePopup.Spawn(DamagePopup.Above(this), $"+{Hp - before}", DamagePopup.Poison, 1.1f, delay);
    }

    void OnTurnEnded(int turn)
    {
        if (Dead) return;
        if (deepSleep > 0) deepSleep--;
        if (poisonLeft > 0 && poisoner != null)
        {
            poisonLeft--;
            DamagePopup.Spawn(DamagePopup.Above(this), "-2", DamagePopup.Poison, 0.9f);
            TakeDamage(2, poisoner, 0f);
        }
    }

    public const float HpPerLevel = 0.1f, AtkPerLevel = 0.05f;
    public const float BossScale = 1.5f;   // 엘리트 크기 (예전 1.9는 너무 커서 체력바가 화면 밖으로 나갔다)

    // 레벨에 따라 강해지고, 체력은 개체마다 조금씩 다르다
    void RollStats()
    {
        // level 1 = 그 종이 처음 나오는 층. 한 층 깊을 때마다 체력 +10%, 공격 +5% (15번 문서)
        int lv = Mathf.Max(1, level) - 1;
        float hp = baseHp * (1f + HpPerLevel * lv) * Random.Range(1f - hpVariance, 1f + hpVariance);
        MaxHp = Mathf.Max(1, Mathf.RoundToInt(hp));
        AtkMin = Mathf.Max(1, Mathf.RoundToInt(attackMin * (1f + AtkPerLevel * lv)));
        AtkMax = Mathf.Max(AtkMin, Mathf.RoundToInt(attackMax * (1f + AtkPerLevel * lv)));
        Strength = strength + lv;
        if (boss)
        {
            MaxHp *= 5;
            AtkMin += 2; AtkMax += 3; Strength += 2;
        }
        else if (elite)
        {
            MaxHp *= 2;
            AtkMin += 1; AtkMax += 2; Strength += 1;
        }
        Hp = MaxHp;
    }

    // 레벨을 다시 정한다 (실행 중에 불려 나온 졸개 등 — Awake 뒤라서 능력치를 다시 굴린다)
    public void SetLevel(int lv)
    {
        level = lv;
        RollStats();
        if (bar != null) bar.Refresh();
    }

    // ---- 정예·우두머리 (FloorGenerator가 Awake 전에 정한다) ----
    // 정예: 체력 두 배·공격 +1~2·보상 세 배 + 성질 하나. 몸이 크고 붉은 기운이 돈다.
    public enum Affix { None, Frenzy, Venom, Tough, Swift, Leech }
    [Header("정예·우두머리")]
    public bool elite;
    public Affix affix;
    public bool boss;
    float damageTaken = 1f;    // 받는 피해 배율 (단단함)
    [HideInInspector] public float lifesteal;   // 준 피해 중 회복하는 비율 (흡혈: 피를 빠는 정예·혈박쥐)
    bool traitsApplied;

    static string AffixName(Affix a) => a switch
    {
        Affix.Frenzy => "광폭한",
        Affix.Venom => "독니의",
        Affix.Tough => "단단한",
        Affix.Swift => "재빠른",
        Affix.Leech => "피를 빠는",
        _ => "",
    };

    static string AffixHint(Affix a) => a switch
    {
        Affix.Frenzy => "자주 두 번 덤빈다",
        Affix.Venom => "물리면 중독된다",
        Affix.Tough => "피해를 덜 받는다",
        Affix.Swift => "공격을 잘 피한다",
        Affix.Leech => "때릴 때마다 체력을 빨아들인다",
        _ => "",
    };

    void ApplyTraits()
    {
        if (traitsApplied) return;
        traitsApplied = true;
        if (boss)
        {
            expReward *= 8; goldMin += 20; goldMax += 40;
            transform.localScale *= BossScale;
            splitOnDeath = false;
            sight = Mathf.Max(sight, 6);
            return;
        }
        if (!elite) return;
        expReward *= 3; goldMin *= 2; goldMax = goldMax * 3 + 3;
        transform.localScale *= 1.25f;
        switch (affix)
        {
            case Affix.Frenzy: doubleAttackChance = Mathf.Max(doubleAttackChance, 0.35f); break;
            case Affix.Venom: poisonChance = Mathf.Max(poisonChance, 0.4f); poisonTurns = Mathf.Max(poisonTurns, 4); break;
            case Affix.Tough: damageTaken = 0.65f; break;
            case Affix.Swift: break;   // 회피 +15%는 BaseEvasion에서
            case Affix.Leech: lifesteal = 0.5f; break;
        }
        if (!displayName.Contains("정예")) displayName = $"{AffixName(affix)} 정예 {displayName}";
    }

    // 정예의 붉은 기운 (작은 빛)
    void AddEliteGlow()
    {
        if (!elite && !boss) return;
        var l = new GameObject("EliteGlow").AddComponent<Light>();
        l.transform.SetParent(transform, false);
        l.transform.localPosition = Vector3.up * 1.5f;
        l.type = LightType.Point;
        l.color = boss ? new Color(0.8f, 0.2f, 1f) : new Color(1f, 0.2f, 0.12f);
        l.range = boss ? 9f : 6f;
        l.intensity = boss ? 6f : 4f;
        l.shadows = LightShadows.None;
    }

    // ---- 성격: 같은 종족이어도 개체마다 조금씩 다르게 ----
    bool brave;                // 쥐: 겁먹어도 달아나지 않는 녀석
    Vector2Int home;           // 처음 있던 자리 (어슬렁거리는 범위의 중심)

    void RollTemperament()
    {
        sight = Mathf.Max(2, sight + Random.Range(-1, 2));
        brave = behavior == Behavior.Coward && Random.value < 0.3f;
        if (behavior == Behavior.Charger) chargeCooldown = Random.Range(0, 3);
    }

    // 분열한 슬라임: 부모의 절반 능력치, 다시 분열하지 않음
    void InitAsSplit(Monster parent)
    {
        isSplitChild = true;
        splitOnDeath = false;
        MaxHp = Mathf.Max(1, parent.MaxHp / 2);
        Hp = MaxHp;
        AtkMin = Mathf.Max(1, parent.AtkMin / 2);
        AtkMax = Mathf.Max(AtkMin, parent.AtkMax / 2);
        Strength = Mathf.Max(1, parent.Strength / 2);
        goldMin = parent.goldMin / 2;
        goldMax = Mathf.Max(goldMin, parent.goldMax / 2);
        expReward = Mathf.Max(1, parent.expReward / 2);
        awake = true;
        transform.localScale = parent.transform.localScale * 0.7f;
        if (Visual != null) { visualHome = parent.visualHome; Visual.localPosition = visualHome; }
        // 복제는 원본과 같은 재질을 같이 쓴다 → 원본이 쓰러지며 투명해질 때 같이 사라졌다. 재질을 따로 갖는다
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++) if (ms[i] != null) ms[i] = new Material(ms[i]);
            r.sharedMaterials = ms;
        }
    }

    // 플레이어가 클릭해서 다가와 공격: 제자리에서 한 턴을 쓴다 (다른 몬스터는 그동안 움직이고, 공격은 힘 순서대로)
    public override void Interact(PlayerStats player)
    {
        if (Dead || player.Dead) return;
        var mover = player.GetComponent<GridClickMover>();
        ambushed = Asleep;   // 자는 채로 맞으면 기습 — 이번 턴엔 깨는 판정을 하지 않는다
        TurnResolver.BeginTurn(GridTile.WorldToCell(player.transform.position), mover != null ? mover.moveSeconds : 0.3f);
        TurnResolver.ResolveAttacks(player, this);
        ambushed = false;
    }

    // 차례가 와서 플레이어에게 맞는다 (delay: 연출 시작까지 기다릴 시간).
    // 연출에 쓴 시간(다음 공격까지 간격)을 돌려준다 — 3연격·추가 타격이 있으면 길어진다.
    public float ReceivePlayerAttack(PlayerStats player, float delay)
    {
        var mover = player.GetComponent<GridClickMover>();
        bool triple = player.ConsumeTriple();   // [Q] 3연격: 세 번 친다 (타격마다 회피·막기를 따로 굴린다)
        if (triple)
        {
            GameLog.Add("3연격!", GameLog.Good);
            CameraFollowOcclusion.Shake(0.2f, 0.15f);
        }
        int hits = triple ? PlayerStats.TripleHits : 1;
        float mult = triple ? PlayerStats.TripleHitMult : 1f;
        float t = delay;
        bool landed = false;
        for (int i = 0; i < hits && !Dead; i++)
        {
            if (i > 0) t += TripleGap;
            if (mover != null)
            {
                if (triple && GridClickMover.HasAnim("Triple")) { if (i == 0) mover.PlaySkill("Triple", transform.position, t); }
                else mover.PlayLunge(transform.position, t);
            }
            bool hit = PlayerStrike(player, t, mult, !triple, triple && GridClickMover.HasAnim("Triple") ? TurnResolver.TripleImpact : PlayerImpact);
            if (i == 0) landed = hit;
        }
        if (triple) return t - delay + TurnResolver.PlayerAttackGap;

        // 가벼운 무기(힘이 남는 무기): 가끔 재빠르게 한 번 더 때린다
        if (landed && !Dead && player.DoubleChance > 0f && Random.value < player.DoubleChance)
        {
            t += FollowUpGap;
            if (mover != null) mover.PlayLunge(transform.position, t);
            GameLog.Add("재빠르게 한 번 더!", GameLog.Good);
            PlayerStrike(player, t, 1f, false);
        }
        // 보조칸(방패·한손 무기): 일반 공격이 맞으면 가끔 한 번 더 친다
        if (landed && !Dead && player.Offhand != null && Random.value < PlayerStats.OffhandChance)
        {
            t += FollowUpGap;
            if (mover != null)
            {
                if (!player.Offhand.IsWeapon && GridClickMover.HasAnim("ShieldBash")) mover.PlaySkill("ShieldBash", transform.position, t);
                else mover.PlayLunge(transform.position, t);
            }
            OffhandStrike(player, t);
        }
        return t - delay + TurnResolver.PlayerAttackGap;
    }

    // 플레이어의 한 타. 잠들어 있으면 기습(세 배, 못 피함), 아니면 헛손질·회피·막기를 굴린다. 회피·헛손질이 아니면 true.
    bool PlayerStrike(PlayerStats player, float t, float mult, bool showBest, float impactAt = PlayerImpact)
    {
        float impact = t + impactAt;   // 휘두른 모션이 닿는 순간
        if (Asleep)
        {
            // 기습: 잠든 적은 피하지도 막지도 못하고 세 배 (3연격이면 첫 타만 — 맞으면 깨어난다)
            int sneak = Mathf.Max(1, Mathf.RoundToInt(player.RollAttack() * 3 * mult));
            GameLog.Add("기습!", GameLog.Good);
            CameraFollowOcclusion.Shake(0.3f, 0.25f);
            DamagePopup.Spawn(DamagePopup.Above(this), $"기습 {sneak}!", DamagePopup.Big, 1.8f, impact);
            StartCoroutine(FlashRed(impact));
            HitSound(player.Weapon, impact);
            TakeDamage(sneak, player, t);
            return true;
        }
        // 힘보다 무거운 무기: 휘청이며 헛손질할 수 있다
        if (player.MissChance > 0f && Random.value < player.MissChance)
        {
            GameLog.Add("무기가 무거워 휘청이며 헛손질했다.", GameLog.Info);
            DamagePopup.Spawn(DamagePopup.Above(this), "헛손질", DamagePopup.Miss, 0.9f, impact);
            return false;
        }
        if (TryParry(player, impact)) return false;
        int dmg = Mathf.Max(1, Mathf.RoundToInt(player.RollAttack() * mult));
        int got = Defend(dmg, impact);
        if (got < 0) return false;
        if (got == dmg)
        {
            // 최대치로 때리면 더 크고 주황색으로
            bool best = showBest && dmg >= player.AttackMax && player.AttackMax > player.AttackMin;
            DamagePopup.Spawn(DamagePopup.Above(this), best ? $"{dmg}!" : dmg.ToString(), best ? DamagePopup.Big : DamagePopup.Hit, best ? 1.45f : 1.1f, impact);
            StartCoroutine(FlashRed(impact));
        }
        if (got > 0) { HitSound(player.Weapon, impact); TakeDamage(got, player, t); }
        return true;
    }

    bool BoneBody => displayName.Contains("해골") || behavior == Behavior.Giant;

    // 몬스터 종류별 소리 이름 (die_slime·attack_rat 등). 박쥐를 쥐보다 먼저 본다.
    string Voice
    {
        get
        {
            var n = displayName ?? "";
            if (n.Contains("슬라임")) return "slime";
            if (n.Contains("박쥐")) return "bat";
            if (n.Contains("쥐")) return "rat";
            if (n.Contains("늑대")) return "wolf";
            if (n.Contains("시체")) return "zombie";
            if (n.Contains("고블린")) return "goblin";
            if (BoneBody) return "skeleton";
            return null;
        }
    }

    // 맞은 소리: 무기별 타격음에 몸 소리를 겹친다 (해골류는 뼈, 슬라임은 끈적한 소리)
    void HitSound(ItemDef weapon, float impact)
    {
        Sfx.PlayWeapon("hit", weapon, impact);
        if (BoneBody) Sfx.Play("hit_bone", impact, 0.6f);
        else if (Voice == "slime") Sfx.Play("hit_slime", impact, 0.8f);
    }

    // 회피·막기 판정: 피하면 -1, 막으면 줄어든 피해(0일 수 있다), 아니면 그대로. 팝업·로그도 여기서.
    int Defend(int dmg, float impact)
    {
        if (Random.value < Evasion)
        {
            GameLog.Add($"{displayName}{Josa("이", "가")} 공격을 피했다.", GameLog.Info);
            DamagePopup.Spawn(DamagePopup.Above(this), "회피", DamagePopup.Miss, 0.95f, impact);
            Sfx.Play("dodge", impact);
            return -1;
        }
        if (Random.value < Block)
        {
            int cut = BlockedDamage(dmg);
            GameLog.Add($"{displayName}{Josa("이", "가")} 공격을 막았다.", GameLog.Info);
            DamagePopup.Spawn(DamagePopup.Above(this), cut > 0 ? $"막기 {cut}" : "막기", DamagePopup.Miss, 0.95f, impact);
            Sfx.Play("block", impact);
            return cut;
        }
        return dmg;
    }

    // 보조칸 추가 타격: 한손 무기는 공격력이 낮아지고(PlayerStats.OffhandWeaponMult), 방패는 약하지만 맞으면 기절시킬 수 있다
    void OffhandStrike(PlayerStats player, float t)
    {
        var o = player.Offhand;
        float impact = t + PlayerImpact;
        GameLog.Add(o.IsWeapon ? $"{o.name}{Korean.Josa(o.name, "으로", "로")} 한 번 더!" : $"{o.name}{Korean.Josa(o.name, "으로", "로")} 후려친다!", GameLog.Good);
        int dmg = player.RollOffhand();
        int got = Defend(dmg, impact);
        if (got < 0) return;
        if (got == dmg)
        {
            DamagePopup.Spawn(DamagePopup.Above(this), dmg.ToString(), DamagePopup.Hit, 1.05f, impact);
            StartCoroutine(FlashRed(impact));
        }
        if (got > 0) { HitSound(o, impact); TakeDamage(got, player, t); }
        if (!Dead && got > 0 && o.stunChance > 0f && Random.value < o.stunChance) Stun(1, impact + 0.15f);
    }

    // ---- 기절 (점프 찍기·방패) ----
    // turns: 이 몬스터가 건너뛸 자기 차례 수. 이번 턴에 하려던 공격·돌진·침 뱉기도 취소된다.
    public void Stun(int turns, float delay)
    {
        if (Dead || turns <= 0) return;
        stunned = Mathf.Max(stunned, turns);
        WillAttack = false;
        if (charging || spitTarget != null || giantMove != GiantMove.None) { charging = false; spitTarget = null; giantMove = GiantMove.None; ClearWarning(); }
        GameLog.Add($"{displayName}{Josa("이", "가")} 기절했다!", GameLog.Good);
        DamagePopup.Spawn(DamagePopup.Above(this, 1.1f), "기절", DamagePopup.Big, 1.1f, delay);
    }

    // [W] 점프 찍기에 맞는다: 피하거나 막지 못하고, 살아남으면 기절
    public void HitBySlam(PlayerStats player, int dmg, float delay)
    {
        DamagePopup.Spawn(DamagePopup.Above(this), dmg.ToString(), DamagePopup.Big, 1.3f, delay);
        StartCoroutine(FlashRed(delay));
        TakeDamage(dmg, player, delay);
        if (!Dead) Stun(PlayerStats.SlamStunTurns, delay + 0.15f);
    }

    // 던진 돌·횃불·불꽃 물약에 맞는다 (피하지 못한다). fire면 불에 약한 적에게 두 배.
    public void HitByThrow(PlayerStats player, int dmg, string what, float delay, bool fire = false)
    {
        if (fireWeak && fire)
        {
            dmg *= 2;
            GameLog.Add($"{displayName}{Josa("은", "는")} 불에 약하다!", GameLog.Good);
        }
        if (Asleep)
        {
            dmg *= 2;   // 자는 적에게 던지면 기습 (두 배)
            GameLog.Add("기습!", GameLog.Good);
        }
        DamagePopup.Spawn(DamagePopup.Above(this), dmg.ToString(), DamagePopup.Hit, 1.1f, delay);
        StartCoroutine(FlashRed(delay));
        GameLog.Add($"던진 {what}{Korean.Josa(what, "이", "가")} {displayName}에게 맞았다.", GameLog.Info);
        TakeDamage(dmg, player, delay);
    }

    // 불타는 중 턴마다 (Burning). 불에 약한 적은 두 배
    public void TakeBurnDamage(int dmg, PlayerStats by)
    {
        if (fireWeak) dmg *= 2;
        var p = by != null ? by : FindAnyObjectByType<PlayerStats>();
        if (p == null) return;
        DamagePopup.Spawn(DamagePopup.Above(this), $"-{dmg}", DamagePopup.Starve, 0.9f);
        TakeDamage(dmg, p, 0f);
    }

    // 차례가 와서 플레이어를 공격한다. 연출에 쓴 시간(다음 공격까지 간격)을 돌려준다.
    public float AttackPlayer(PlayerStats player, float delay)
    {
        StartCoroutine(Lunge(player.transform.position, delay));
        if (TrySteal(player, delay)) return TurnResolver.AttackGap;
        HitPlayer(player, delay);
        bool twice = forceDouble;
        forceDouble = false;
        if (!player.Dead && (twice || Random.value < doubleAttackChance))
        {
            GameLog.Add($"{displayName}{Josa("이", "가")} 한 번 더 덤벼든다!", GameLog.Bad);
            StartCoroutine(Lunge(player.transform.position, delay + TurnResolver.AttackGap));
            HitPlayer(player, delay + TurnResolver.AttackGap);
            return TurnResolver.AttackGap * 2f;
        }
        return TurnResolver.AttackGap;
    }

    void TakeDamage(int amount, PlayerStats player, float delay)
    {
        if (damageTaken < 1f) amount = Mathf.Max(1, Mathf.RoundToInt(amount * damageTaken));   // 단단한 정예
        Hp = Mathf.Max(0, Hp - amount);
        if (Asleep) { Asleep = false; deepSleep = 0; WakeUp(false); }   // 맞아서 깬 것: 발견 연출(!·멈춤) 없이
        awake = true;
        MakeNoise(4, 0.4f);   // 싸우는 소리에 근처의 잠든 적이 깰 수 있다
        if (bar != null) bar.Refresh();
        if (!Dead)
        {
            StartCoroutine(Flinch(delay));
            if (behavior == Behavior.Giant) StartCoroutine(Shards(delay));
            GameLog.Add($"{displayName}에게 {amount}의 피해.", GameLog.Info);
            return;
        }

        GameLog.Add($"{displayName}에게 {amount}의 피해. {displayName}{Josa("을", "를")} 쓰러뜨렸다.", GameLog.Good);
        if (boss) OnEliteDown();
        Sfx.PlayKind("die", Voice, delay + 0.1f);
        DropOnDeath(player);
        int exp = expReward + (level - 1);
        player.AddExp(exp);
        int gold = Random.Range(goldMin, goldMax + 1) + (level - 1);
        if (gold > 0) player.AddGold(gold);
        GameLog.Add(gold > 0 ? $"경험 +{exp}   골드 +{gold}" : $"경험 +{exp}", GameLog.Good);
        if (splitOnDeath && !isSplitChild) Split();
        enabled = false;
        ClearWarning();
        if (bar != null) Destroy(bar.gameObject);
        if (HasClips) { StartCoroutine(PlayClipLater("Die", delay + 0.1f)); FadeOut.Run(this, 0.8f, delay + 1.6f); }   // 쓰러지는 동작을 보여 준 뒤 사라진다
        else FadeOut.Run(this, 0.8f, delay + 0.2f);
    }

    void Split()
    {
        var me = Cell;
        var player = GridTile.WorldToCell(FindAnyObjectByType<PlayerStats>().transform.position);
        var free = FreeCells(player);
        var spots = new List<Vector2Int> { me };              // 죽은 자리는 곧 비므로 그대로 쓴다
        foreach (var d in Dirs) if (free.Contains(me + d) && spots.Count < 2) spots.Add(me + d);

        foreach (var cell in spots)
        {
            var clone = Instantiate(gameObject, GridTile.CellToWorld(cell), transform.rotation, transform.parent);
            clone.name = name + " (분열)";
            clone.GetComponent<Monster>().InitAsSplit(this);
        }
        // 이 몬스터의 칸 막기를 먼저 풀어야 분열체가 같은 칸에 설 수 있다
        GetComponent<GridBlocker>().enabled = false;
        GameLog.Add($"{displayName}{Josa("이", "가")} 둘로 갈라졌다!", GameLog.Warn);
    }

    // 이번 턴 계획: 플레이어가 서게 될 칸 옆이면 공격 대기, 아니면 플레이어와 동시에 한 칸 다가간다
    public void PlanAndMove(Vector2Int playerCell, int turn, float moveSeconds)
    {
        WillAttack = false;
        var me = Cell;
        int dist = GridNav.Distance(me, playerCell);
        if (Asleep)
        {
            if (ambushed) return;   // 지금 기습당하는 중
            if (deepSleep > 0) return;   // 잠의 물약: 아직 곯아떨어져 있다
            if (!GridNav.LineOfSight(me, playerCell) || !TryWake(dist)) return;   // 벽 너머(다른 방)에선 깨지 않는다
        }
        else if (!awake && dist <= sight && GridNav.LineOfSight(me, playerCell))   // 보이는 곳에서만 알아챈다
        {
            WakeUp();
            if (behavior == Behavior.Coward) CallPack();
        }
        if (!awake)
        {
            if (lure != null) GoToLure(me, playerCell, turn, moveSeconds);   // 경보가 울린 곳으로 간다
            else Wander(me, playerCell, moveSeconds);
            return;
        }

        if (stunned > 0)
        {
            if (--stunned == 0) GameLog.Add($"{displayName}{Josa("이", "가")} 숨을 고른다.", GameLog.Info);
            return;
        }
        if (chargeCooldown > 0) chargeCooldown--;
        NotePlayerCell(me, playerCell);

        switch (behavior)
        {
            case Behavior.Archer: PlanArcher(me, playerCell, turn, moveSeconds); break;
            case Behavior.Thief: PlanThief(me, playerCell, turn, moveSeconds); break;
            case Behavior.Charger: PlanCharger(me, playerCell, turn, moveSeconds); break;
            case Behavior.Skittish: PlanSkittish(me, playerCell, moveSeconds); break;
            case Behavior.Coward: PlanCoward(me, playerCell, turn, moveSeconds); break;
            case Behavior.Spitter: PlanSpitter(me, playerCell, turn, moveSeconds); break;
            case Behavior.Giant: PlanGiant(me, playerCell, turn, moveSeconds); break;
            default: Chase(me, playerCell, turn, moveSeconds, null); break;
        }
    }

    // ---- 잠든 적 ----
    // 가까이 갈수록 잘 깨고, 플레이어가 횃불을 들고 있으면 두 배로 잘 깬다 (빛에 눈을 뜬다).
    // 몰래 다가가 먼저 때리면 기습(세 배). 싸우는 소리에 근처의 잠든 적이 깰 수도 있다.

    public bool Asleep { get; private set; }
    bool ambushed;
    static bool sleepHintShown;

    public void FallAsleep() => Asleep = true;

    // 소리(곡괭이 등)에 깬다
    public void WakeFromNoise()
    {
        if (!Asleep || deepSleep > 0) return;
        Asleep = false;
        GameLog.Add($"소리에 {displayName}{Josa("이", "가")} 깼다!", GameLog.Bad);
        WakeUp();
    }

    bool TryWake(int dist)
    {
        if (!sleepHintShown && dist <= 4 && !Unseen)   // 보이는 적일 때만
        {
            sleepHintShown = true;
            TipBanner.Show("잠든 적이 있다. 몰래 다가가 먼저 때리면 기습! 횃불을 들고 있으면 깨우기 쉽다.");
        }
        float chance = dist <= 1 ? 0.35f : dist == 2 ? 0.18f : dist == 3 ? 0.08f : dist == 4 ? 0.03f : 0f;
        var player = FindAnyObjectByType<PlayerStats>();
        if (player != null && player.TorchLit) chance *= 2f;
        if (Random.value >= chance) return false;
        Asleep = false;
        GameLog.Add($"{displayName}{Josa("이", "가")} 잠에서 깼다!", GameLog.Bad);
        WakeUp();
        if (behavior == Behavior.Coward) CallPack();
        return true;
    }

    // ---- 경보 함정 (Trap) ----
    // 반경 안의 잠든 적은 깨고(잠의 물약에 취한 녀석은 빼고), 아직 나를 못 본 적은 소리가 난 곳으로 모여든다.
    // 이미 나를 쫓는 적은 그대로 나를 쫓는다. 소리를 들은 수를 돌려준다.
    Vector2Int? lure;   // 가 보려는 소리 난 곳

    public static int HearAlarm(Vector2Int at, int radius)
    {
        int n = 0;
        foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
        {
            if (m.Dead || !m.enabled || GridNav.Distance(m.Cell, at) > radius) continue;
            if (m.Asleep)
            {
                if (m.deepSleep > 0) continue;
                m.Asleep = false;
                GameLog.Add($"종소리에 {m.displayName}{Korean.Josa(m.displayName, "이", "가")} 깼다!", GameLog.Bad);
            }
            if (m.awake) continue;
            m.lure = at;
            n++;
        }
        return n;
    }

    void GoToLure(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds)
    {
        var at = lure.Value;
        if (GridNav.Distance(me, at) <= 1) { lure = null; return; }   // 도착: 두리번거린다 (이후엔 평소처럼 어슬렁)
        if (moveEvery > 1 && turn % moveEvery != 0) return;
        var floors = GridNav.Floors();
        var free = FreeCells(playerCell);
        var route = GridNav.FindPath(me, c => GridNav.Distance(c, at) <= 1, free, floors, 400);
        if (route == null || route.Count == 0) { lure = null; return; }
        home = at;   // 도착한 뒤엔 그 근처를 어슬렁거린다
        StartCoroutine(MoveTo(route[0], moveSeconds));
    }

    // 소리: 반경 안의 잠든 적이 확률로 깬다
    void MakeNoise(int radius, float chance)
    {
        foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
        {
            if (m == this || !m.Asleep || m.deepSleep > 0 || m.Dead || !m.enabled) continue;
            if (GridNav.Distance(m.Cell, Cell) > radius || !GridNav.LineOfSight(m.Cell, Cell) || Random.value >= chance) continue;   // 벽 너머에선 안 들린다
            m.Asleep = false;
            GameLog.Add($"소란에 {m.displayName}{Korean.Josa(m.displayName, "이", "가")} 깼다!", GameLog.Bad);
            m.WakeUp();
        }
    }

    // 적 발견 연출 (설계 A3): 적과 내 머리 위에 `!` + 걷던 길을 끊고 잠깐 모든 입력을 막는다.
    // 적이 안개 속이라 안 보여도 나는 멈춘다 (적의 `!`만 감춘다).
    void Alert(float size = 1.6f)
    {
        if (!Unseen) DamagePopup.Spawn(DamagePopup.Above(this), "!", DamagePopup.Big, size);
        GridClickMover.Alert();
    }

    // alert: 발견 연출을 할지 (플레이어에게 맞아서 깬 것은 false)
    void WakeUp(bool alert = true)
    {
        awake = true;
        lure = null;
        if (alert) Alert();
        AnnounceIfElite();
        GameLog.Add($"{displayName}{Josa("이", "가")} 이쪽을 노려본다!", GameLog.Warn);
        if (!Unseen && hinted.Add(behavior))   // 종족마다 처음 한 번, 대처법 팁 (보이는 적일 때만 — 안 보이는 슬라임 팁이 뜨지 않게)
        {
            string hint = behavior switch
            {
                Behavior.Charger => "늑대는 곧은 길에서 돌진해 온다. 예고가 보이면 옆으로 비켜서라.",
                Behavior.Skittish => "박쥐는 횃불 빛을 싫어한다. 벽 횃불이나 던진 횃불 곁이라면 다가오지 못한다.",
                Behavior.Coward => "쥐는 무리를 짓는다. 다치면 달아나지만 느리다.",
                _ => fireWeak ? "슬라임은 불에 약해 보인다. 횃불을 던져 볼까?" : null,
            };
            if (hint != null) TipBanner.Show(hint);   // 팁은 화면 상단에
        }
        GridClickMover.StopWalking();   // 멀리 걸어가던 중이면 멈춰서 상황을 보게
        if (!InLight && !darkHintShown && !Unseen)
        {
            darkHintShown = true;
            TipBanner.Show("어둠 속의 적은 더 사납다. 빛 안에서 싸워야 한다…");
        }
    }

    // 기본: 붙어 있으면 공격, 아니면 한 칸 다가간다 (avoid: 들어가지 않을 칸)
    void Chase(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds, System.Func<Vector2Int, bool> avoid)
    {
        if (GridNav.Adjacent(me, playerCell)) { WillAttack = true; return; }
        if (moveEvery > 1 && turn % moveEvery != 0) return;
        var next = StepToward(me, playerCell, avoid, humanoid);
        if (next == null) return;
        if (humanoid && OpenDoorOnWay(next.Value)) return;   // 이번 턴은 문을 연다
        StartCoroutine(MoveTo(next.Value, moveSeconds));
    }

    // ---- 늑대: 돌진 ----

    void PlanCharger(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds)
    {
        if (charging) { Charge(me, playerCell, moveSeconds); return; }
        if (!GridNav.Adjacent(me, playerCell) && chargeCooldown == 0 && CanCharge(me, playerCell, out var dir))
        {
            charging = true;
            chargeDir = dir;
            ShowChargeWarning(me);
            GameLog.Add($"{displayName}{Josa("이", "가")} 몸을 낮춘다… 곧 덮쳐 온다!", GameLog.Bad);
            Alert(1.8f);
            Face(GridTile.CellToWorld(me + dir));
            return;
        }
        Chase(me, playerCell, turn, moveSeconds, null);
    }

    // 플레이어가 곧은 줄(가로·세로·대각) 2~3칸 앞에 있고 그 사이가 비어 있으면 돌진할 수 있다
    bool CanCharge(Vector2Int me, Vector2Int target, out Vector2Int dir)
    {
        var d = target - me;
        dir = new Vector2Int(System.Math.Sign(d.x), System.Math.Sign(d.y));
        int n = GridNav.Distance(me, target);
        if (n < 2 || n > 3) return false;
        if (!(d.x == 0 || d.y == 0 || Mathf.Abs(d.x) == Mathf.Abs(d.y))) return false;
        var floors = GridNav.Floors();
        var free = FreeCells(target);
        var c = me;
        for (int k = 1; k < n; k++)
        {
            if (!GridNav.CornerFree(c, dir, floors)) return false;
            c += dir;
            if (!free.Contains(c)) return false;
        }
        return GridNav.CornerFree(c, dir, floors);
    }

    // 예고한 방향으로 최대 3칸 돌진. 그 길에 플레이어가 있으면 덮친다(두 배 피해), 없으면 헐떡이며 한 턴 무방비.
    void Charge(Vector2Int me, Vector2Int playerCell, float moveSeconds)
    {
        charging = false;
        chargeCooldown = Random.Range(3, 6);   // 언제 다시 덮칠지 일정하지 않게
        ClearWarning();
        var floors = GridNav.Floors();
        var free = FreeCells(playerCell);
        var pos = me;
        bool hit = false;
        for (int k = 0; k < 3; k++)
        {
            if (!GridNav.CornerFree(pos, chargeDir, floors)) break;
            var n = pos + chargeDir;
            if (n == playerCell) { hit = true; break; }
            if (!free.Contains(n)) break;
            pos = n;
        }
        if (pos != me) StartCoroutine(MoveTo(pos, moveSeconds * 0.6f));
        if (hit)
        {
            WillAttack = true;
            nextHitMult = 2f;
            GameLog.Add($"{displayName}{Josa("이", "가")} 덮쳐 왔다!", GameLog.Bad);
        }
        else
        {
            stunned = 1;
            GameLog.Add($"{displayName}{Josa("이", "가")} 허공을 가르고 헐떡인다.", GameLog.Good);
            DamagePopup.Spawn(DamagePopup.Above(this), "헐떡임", DamagePopup.Miss, 0.9f, moveSeconds * 0.6f);
        }
    }

    void ShowChargeWarning(Vector2Int me)
    {
        ClearWarning();
        var floors = GridNav.Floors();
        var c = me;
        for (int k = 0; k < 3; k++)
        {
            c += chargeDir;
            if (!floors.Contains(c)) break;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "ChargeWarning";
            q.transform.position = GridTile.CellToWorld(c) + Vector3.up * WarnHeight;
            q.transform.rotation = Quaternion.Euler(90, 0, 0);
            q.transform.localScale = Vector3.one * (GridTile.CellSize * 0.9f);
            var m = WarnMat(new Color(1f, 0.15f, 0.1f, 0.45f - k * 0.08f));
            q.GetComponent<Renderer>().material = m;
            warnTiles.Add(q);
        }
    }

    void ClearWarning()
    {
        foreach (var t in warnTiles) if (t != null) Destroy(t);
        warnTiles.Clear();
    }

    // ---- 박쥐: 빛을 피하고 치고 빠진다 ----

    void PlanSkittish(Vector2Int me, Vector2Int playerCell, float moveSeconds)
    {
        // 방금 쳤으면 이번 턴엔 물러난다
        if (retreat)
        {
            retreat = false;
            var away = StepAway(me, playerCell, true);
            if (away != null) { StartCoroutine(MoveTo(away.Value, moveSeconds)); return; }
        }
        // 횃불 빛(벽 횃불·던진 횃불) 안에 있으면 어둠으로 달아난다
        if (LightSources.IsLitByFixed(me))
        {
            var dark = StepAway(me, playerCell, true, true);
            if (dark != null) { StartCoroutine(MoveTo(dark.Value, moveSeconds)); return; }
        }
        if (GridNav.Adjacent(me, playerCell)) { WillAttack = true; retreat = true; return; }
        // 가끔은 갈팡질팡 날아다닌다
        if (Random.value < 0.25f)
        {
            var free = FreeCells(playerCell);
            var floors = GridNav.Floors();
            var opts = new List<Vector2Int>();
            foreach (var d in GridNav.Dirs8)
                if (free.Contains(me + d) && GridNav.CornerFree(me, d, floors) && !LightSources.IsLitByFixed(me + d)) opts.Add(me + d);
            if (opts.Count > 0) { StartCoroutine(MoveTo(opts[Random.Range(0, opts.Count)], moveSeconds)); return; }
        }
        Chase(me, playerCell, 1, moveSeconds, LightSources.IsLitByFixed);
    }

    // ---- 아직 나를 못 본 깨어 있는 녀석: 제자리 근처를 어슬렁거린다 (마주치는 곳이 매번 달라진다) ----

    void Wander(Vector2Int me, Vector2Int playerCell, float moveSeconds)
    {
        if (Asleep || Random.value < 0.5f) return;
        var free = FreeCells(playerCell);
        var floors = GridNav.Floors();
        var opts = new List<Vector2Int>();
        foreach (var d in GridNav.Dirs8)
        {
            var n = me + d;
            if (free.Contains(n) && GridNav.CornerFree(me, d, floors) && GridNav.Distance(n, home) <= 4) opts.Add(n);
        }
        if (opts.Count > 0) StartCoroutine(MoveTo(opts[Random.Range(0, opts.Count)], moveSeconds));
    }

    // ---- 침 뱉는 슬라임: 거리를 두고, 예고한 칸에 산성 침을 떨어뜨린다 ----

    Vector2Int? spitTarget;
    int spitCooldown;

    void PlanSpitter(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds)
    {
        // 지난 턴에 예고한 칸에 침이 떨어진다 (그 칸에서 벗어났으면 피한 것)
        if (spitTarget != null)
        {
            var t = spitTarget.Value;
            spitTarget = null;
            spitCooldown = Random.Range(2, 4);
            ClearWarning();
            var player = FindAnyObjectByType<PlayerStats>();
            if (player != null && playerCell == t)
            {
                int dmg = Mathf.Max(1, Mathf.RoundToInt(Random.Range(AtkMin, AtkMax + 2) * (InLight ? LitDamage : DarkDamage)));
                GameLog.Add($"{displayName}의 산성 침에 맞았다! {dmg}의 피해.", GameLog.Bad);
                DamagePopup.Spawn(DamagePopup.Above(player), $"-{dmg}", DamagePopup.Hurt, 1.2f, moveSeconds);
                player.Damage(dmg);
                if (!player.Dead) GridClickMover.PlayAnim("Hit");
            }
            else GameLog.Add("산성 침이 바닥에 떨어져 지글거린다.", GameLog.Good);
            return;
        }
        if (spitCooldown > 0) spitCooldown--;
        int dist = GridNav.Distance(me, playerCell);
        if (GridNav.Adjacent(me, playerCell)) { WillAttack = true; return; }   // 붙으면 그냥 문다

        // 2~4칸이고 사이가 트여 있으면 침을 예고한다
        if (spitCooldown == 0 && dist >= 2 && dist <= 4 && Thrower.Trace(me, playerCell, out var land, out _) && land == playerCell)
        {
            spitTarget = playerCell;
            ShowWarnTiles(new List<Vector2Int> { playerCell }, new Color(1f, 0.55f, 0.1f, 0.4f));
            GameLog.Add($"{displayName}{Josa("이", "가")} 몸을 부풀린다…", GameLog.Bad);
            Alert();
            return;
        }
        if (moveEvery > 1 && turn % moveEvery != 0) return;
        // 너무 가까우면 물러나고, 너무 멀면 다가간다
        if (dist < 2)
        {
            var away = StepAway(me, playerCell, false);
            if (away != null) { StartCoroutine(MoveTo(away.Value, moveSeconds)); return; }
        }
        if (dist > 3) Chase(me, playerCell, turn, moveSeconds, null);
    }

    // ---- 로스터 (15번 문서): 인간형·쳐내기·확정 드롭·도둑·궁수·느린 적의 2연타 ----
    [Header("로스터")]
    public bool humanoid;                      // 인간형: 닫힌 문을 열고 지나간다 (동물·슬라임은 못 연다)
    [Range(0f, 1f)] public float parryChance;  // 근접 공격을 쳐내고 곧바로 반격 (해골 대장). 기절 중에도
    public bool dropsFood;                     // 쓰러지면 식량 하나를 떨군다 (준보스, 분열체는 빼고)

    public const float StealChance = 0.35f;    // 도둑 고블린: 공격 대신 훔칠 확률
    public const int ArcherRange = 5;          // 해골 궁수 사거리
    public const int ArcherReload = 1;         // 쏜 뒤 쉬는 턴 (느리다)

    ItemDef stolen;                            // 도둑 고블린이 훔친 물건 (쓰러뜨리면 돌려받는다)
    int archerCooldown;
    Vector2Int? lastPlayerCell;                // 느린 근접 적: 플레이어가 지난 턴에 어디 있었는지
    bool forceDouble;                          // 이번 공격은 2연타

    // 느린 근접 적(이동 2턴인 쫓는 적): 가만히 있는데 플레이어가 다가와 붙으면 2연타
    bool SlowMelee => moveEvery > 1 && behavior == Behavior.Chaser;

    void NotePlayerCell(Vector2Int me, Vector2Int playerCell)
    {
        if (SlowMelee && lastPlayerCell != null && !GridNav.Adjacent(me, lastPlayerCell.Value) && GridNav.Adjacent(me, playerCell))
            forceDouble = true;
        lastPlayerCell = playerCell;
    }

    // ---- 해골 궁수: 가로·세로·대각선 일직선에 트여 있으면 쏜다. 아니면 쫓아온다 ----
    void PlanArcher(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds)
    {
        if (archerCooldown > 0) { archerCooldown--; return; }
        var d = playerCell - me;
        int dist = GridNav.Distance(me, playerCell);
        bool line = d.x == 0 || d.y == 0 || Mathf.Abs(d.x) == Mathf.Abs(d.y);
        if (line && dist >= 2 && dist <= ArcherRange && Thrower.Trace(me, playerCell, out var land, out _) && land == playerCell)
        {
            archerCooldown = ArcherReload;
            Face(GridTile.CellToWorld(playerCell));
            var player = FindAnyObjectByType<PlayerStats>();
            if (player != null) StartCoroutine(Arrow(player, moveSeconds * 0.5f));
            return;
        }
        Chase(me, playerCell, turn, moveSeconds, null);
    }

    IEnumerator Arrow(PlayerStats player, float delay)
    {
        var a = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(a.GetComponent<Collider>());
        a.transform.localScale = new Vector3(0.08f, 0.6f, 0.08f);
        a.GetComponent<Renderer>().material.color = new Color(0.45f, 0.35f, 0.2f);
        var from = transform.position + Vector3.up * 2f;
        var to = player.transform.position + Vector3.up * 1.6f;
        a.transform.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f) { a.transform.position = Vector3.Lerp(from, to, t); yield return null; }
        Destroy(a);
        if (delay > 0.2f) yield return new WaitForSeconds(delay - 0.2f);
        if (!player.Dead && !Dead) HitPlayer(player, 0f);
    }

    // ---- 도둑 고블린: 가끔 공격 대신 물건을 훔치고 겁에 질려 달아난다 ----
    bool TrySteal(PlayerStats player, float delay)
    {
        if (behavior != Behavior.Thief || stolen != null || Random.value >= StealChance) return false;
        var bag = player.Bag.bag;
        if (bag.Count == 0) return false;
        var pick = bag[Random.Range(0, bag.Count)].def;
        if (!player.Bag.Remove(pick)) return false;
        stolen = pick;
        fleeing = true;
        GameLog.Add($"{displayName}{Josa("이", "가")} {pick.name}{Korean.Josa(pick.name, "을", "를")} 훔쳐 달아난다!", GameLog.Bad);
        DamagePopup.Spawn(DamagePopup.Above(player), $"-{pick.name}", DamagePopup.Hurt, 1.1f, delay + LungeImpact);
        return true;
    }

    void PlanThief(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds)
    {
        if (stolen == null) { Chase(me, playerCell, turn, moveSeconds, null); return; }
        var away = FleeStep(me, playerCell);   // 겁에 질려 달아난다 (매 턴)
        if (away != null) { StartCoroutine(MoveTo(away.Value, moveSeconds)); return; }
        if (GridNav.Adjacent(me, playerCell)) WillAttack = true;   // 몰리면 덤빈다
    }

    // ---- 쳐내기 (해골 대장): 근접 공격을 쳐내고 곧바로 반격. 기절 중에도 ----
    bool TryParry(PlayerStats player, float impact)
    {
        if (parryChance <= 0f || Random.value >= parryChance) return false;
        GameLog.Add($"{displayName}{Josa("이", "가")} 공격을 쳐내고 반격한다!", GameLog.Bad);
        DamagePopup.Spawn(DamagePopup.Above(this), "쳐내기", DamagePopup.Miss, 1.1f, impact);
        StartCoroutine(Lunge(player.transform.position, impact));
        HitPlayer(player, impact);
        return true;
    }

    // 쓰러질 때 떨구는 것: 준보스의 식량, 도둑이 훔친 물건
    void DropOnDeath(PlayerStats player)
    {
        if (dropsFood && !isSplitChild) Pickup.SpawnItem(Cell, "food", transform.parent);
        if (stolen != null)
        {
            if (player.Bag.Add(stolen)) GameLog.Add($"{stolen.name}{Korean.Josa(stolen.name, "을", "를")} 되찾았다.", GameLog.Good);
            else GameLog.Add("가방이 꽉 찼다.", GameLog.Warn);
            stolen = null;
        }
    }

    // 인간형: 닫힌 문(잠기지 않은)도 길로 본다. 다음 칸이 닫힌 문이면 이번 턴은 문을 연다
    bool OpenDoorOnWay(Vector2Int next)
    {
        var door = Door.At(next);
        if (door == null || door.IsOpen) return false;
        door.OpenByMonster(this);
        return true;
    }

    // ---- 동작 모델 (블렌더에서 부품을 뼈대에 조립한 FBX, 레거시 Animation) ----
    // 동작이 있으면 코드 연출(웅크렸다 튀기·움찔) 대신 동작을 재생한다. 동작 이름: Idle·Walk·Attack·Windup·Sweep·Roar·Hit·Die
    Animation clipAnim;
    readonly Dictionary<string, string> clipNames = new Dictionary<string, string>();
    [HideInInspector] public float animImpact = 0.3f;   // 동작 모델의 공격이 닿는 순간 (Attack 동작의 타격 프레임)
    bool HasClips => clipAnim != null && clipNames.Count > 0;

    void InitAnimation()
    {
        clipAnim = GetComponentInChildren<Animation>();
        if (clipAnim == null) return;
        foreach (AnimationState st in clipAnim)
        {
            string n = st.name;
            int bar = n.LastIndexOf('|');
            string key = bar >= 0 ? n.Substring(bar + 1) : n;
            clipNames[key] = n;
            st.wrapMode = key == "Idle" || key == "Walk" ? WrapMode.Loop : key == "Die" || key == "Windup" ? WrapMode.ClampForever : WrapMode.Once;
        }
        PlayClip("Idle", 0f);
    }

    // 동작 하나를 부드럽게 이어 재생한다. 한 번짜리 동작은 끝나면 대기로 돌아간다. 없으면 false
    bool PlayClip(string key, float fade = 0.15f)
    {
        if (!HasClips || !clipNames.TryGetValue(key, out var n)) return false;
        clipAnim.CrossFade(n, fade);
        bool loopsOrHolds = key == "Idle" || key == "Walk" || key == "Die" || key == "Windup";
        if (!loopsOrHolds && clipNames.TryGetValue("Idle", out var idle)) clipAnim.CrossFadeQueued(idle, 0.2f, QueueMode.CompleteOthers);
        return true;
    }

    IEnumerator PlayClipLater(string key, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        PlayClip(key);
    }

    // ---- 엘리트를 쓰러뜨렸다: 엘리트 층의 봉인된 문이 열리고, 5층이면 기록한다 (6층부터 해골 병사) ----
    void OnEliteDown()
    {
        RunState.MarkEliteSlain();
        foreach (var d in Door.All)
            if (d.sealedByBoss) { d.Unseal(); break; }
    }

    // ---- 거인 해골 ----
    public const float GiantPatternChance = 0.35f;   // 붙어 있을 때 패턴을 쓸 확률
    public const int GiantPatternCooldown = 3;       // 패턴 뒤 쉬는 턴
    public const float SweepMult = 1.5f, RoarMult = 1f;
    public const float ShardChance = 0.5f;           // 맞았을 때 붙어 있는 플레이어가 파편에 다칠 확률
    public const int ShardDamage = 1;
    enum GiantMove { None, Sweep, Roar }
    GiantMove giantMove;
    readonly List<Vector2Int> giantCells = new List<Vector2Int>();
    int giantCooldown;
    static readonly Color GiantWarnColor = new Color(1f, 0.1f, 0.06f, 0.45f);

    void PlanGiant(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds)
    {
        // 지난 턴에 예고한 패턴이 떨어진다 (붉은 칸에서 벗어났으면 피한 것)
        if (giantMove != GiantMove.None)
        {
            var move = giantMove;
            giantMove = GiantMove.None;
            ClearWarning();
            giantCooldown = GiantPatternCooldown;
            StartCoroutine(Lunge(GridTile.CellToWorld(playerCell), 0f, move == GiantMove.Sweep ? "Sweep" : "Roar"));
            Sfx.Play(move == GiantMove.Sweep ? "giant_slam" : "giant_roar", 0.15f);
            CameraFollowOcclusion.Shake(0.35f, 0.3f);
            var player = FindAnyObjectByType<PlayerStats>();
            if (player == null || player.Dead) return;
            if (!giantCells.Contains(playerCell))
            {
                GameLog.Add(move == GiantMove.Sweep ? $"{displayName}의 휘두르기가 허공을 갈랐다." : $"{displayName}의 포효가 닿지 않았다.", GameLog.Good);
                return;
            }
            float mult = (move == GiantMove.Sweep ? SweepMult : RoarMult) * (InLight ? LitDamage : DarkDamage);
            int dmg = Mathf.Max(1, Mathf.RoundToInt(Random.Range(AtkMin, AtkMax + 1) * mult));
            float impact = moveSeconds * 0.5f;
            GameLog.Add(move == GiantMove.Sweep ? $"{displayName}의 휘두르기! {dmg}의 피해." : $"{displayName}의 포효! {dmg}의 피해.", GameLog.Bad);
            DamagePopup.Spawn(DamagePopup.Above(player), $"-{dmg}", DamagePopup.Hurt, 1.5f, impact);
            ScreenEffects.PlayerHit((float)dmg / player.maxHp, impact);
            player.Damage(dmg);
            if (player.Dead) return;
            GridClickMover.PlayAnim("Hit");
            if (move == GiantMove.Roar)
            {
                var d = playerCell - me;
                GridClickMover.Knockback(new Vector2Int(System.Math.Sign(d.x), System.Math.Sign(d.y)));
            }
            return;
        }
        if (giantCooldown > 0) giantCooldown--;

        // 붙어 있으면 가끔 패턴을 예고한다
        if (giantCooldown == 0 && GridNav.Adjacent(me, playerCell) && Random.value < GiantPatternChance)
        {
            var floors = GridNav.Floors();
            giantCells.Clear();
            bool roar = Random.value < 0.5f;
            if (roar)
            {
                foreach (var d in GridNav.Dirs8) if (floors.Contains(me + d)) giantCells.Add(me + d);
            }
            else
            {
                // 전방 3칸: 플레이어 쪽 칸과 그 양옆
                var dir = new Vector2Int(System.Math.Sign(playerCell.x - me.x), System.Math.Sign(playerCell.y - me.y));
                var front = new List<Vector2Int> { dir };
                if (dir.x != 0 && dir.y != 0) { front.Add(new Vector2Int(dir.x, 0)); front.Add(new Vector2Int(0, dir.y)); }
                else { var perp = new Vector2Int(dir.y, dir.x); front.Add(dir + perp); front.Add(dir - perp); }
                foreach (var d in front) if (floors.Contains(me + d)) giantCells.Add(me + d);
            }
            giantMove = roar ? GiantMove.Roar : GiantMove.Sweep;
            ShowWarnTiles(giantCells, GiantWarnColor);
            GameLog.Add(roar ? $"{displayName}{Josa("이", "가")} 숨을 크게 들이마신다…" : $"{displayName}{Josa("이", "가")} 커다란 뼈 팔을 치켜든다…", GameLog.Bad);
            Face(GridTile.CellToWorld(playerCell));
            PlayClip("Windup");
            Alert(1.8f);
            return;
        }
        Chase(me, playerCell, turn, moveSeconds, null);
    }

    // 맞을 때마다 뼛조각이 튄다: 붙어 있던 플레이어는 가끔 다친다
    IEnumerator Shards(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay + 0.1f);
        var me = Cell;
        for (int i = 0; i < 8; i++)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(shard.GetComponent<Collider>());
            shard.transform.position = transform.position + Vector3.up * 2.5f;
            shard.transform.localScale = Vector3.one * Random.Range(0.15f, 0.35f);
            shard.GetComponent<Renderer>().material.color = new Color(0.86f, 0.83f, 0.74f);
            var rb = shard.AddComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(Random.Range(-4f, 4f), Random.Range(3f, 6f), Random.Range(-4f, 4f));
            Destroy(shard, 1f);
        }
        // (예전엔 주변 8칸을 붉게 번쩍였는데, 포효 예고와 똑같아 보여 가짜 경고처럼 보였다 → 뼛조각만 튄다)
        var player = FindAnyObjectByType<PlayerStats>();
        if (player != null && !player.Dead && GridNav.Adjacent(me, GridTile.WorldToCell(player.transform.position)) && Random.value < ShardChance)
        {
            GameLog.Add("튀어 오른 뼛조각에 맞았다.", GameLog.Bad);
            DamagePopup.Spawn(DamagePopup.Above(player), $"-{ShardDamage}", DamagePopup.Hurt, 0.9f);
            player.Damage(ShardDamage);
        }
    }

    // 예고 칸: 바닥(윗면 y=0)과 겹쳐 깜빡이지 않게 띄우고, 다른 투명한 것보다 위에 그린다
    const float WarnHeight = 0.15f;
    static Material WarnMat(Color c)
    {
        var m = new Material(Shader.Find("Sprites/Default")) { color = c };
        m.renderQueue = 3500;
        return m;
    }

    void ShowWarnTiles(List<Vector2Int> cells, Color color)
    {
        ClearWarning();
        foreach (var c in cells)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Warning";
            q.transform.position = GridTile.CellToWorld(c) + Vector3.up * WarnHeight;
            q.transform.rotation = Quaternion.Euler(90, 0, 0);
            q.transform.localScale = Vector3.one * (GridTile.CellSize * 0.9f);
            q.GetComponent<Renderer>().material = WarnMat(color);
            warnTiles.Add(q);
        }
    }

    // ---- 쥐: 다치면 달아나고, 동료를 부른다 ----

    void PlanCoward(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds)
    {
        if (!fleeing && !brave && Hp <= MaxHp * 0.4f)
        {
            fleeing = true;
            GameLog.Add($"{displayName}{Josa("이", "가")} 겁에 질려 달아난다!", GameLog.Info);
            DamagePopup.Spawn(DamagePopup.Above(this), "겁먹음", DamagePopup.Miss, 0.9f);
        }
        if (fleeing)
        {
            if (turn % 2 == 0) return;   // 다쳐서 느리다 — 쫓아가면 잡을 수 있다
            var away = FleeStep(me, playerCell);
            if (away != null) { StartCoroutine(MoveTo(away.Value, moveSeconds)); return; }
            // 몰리면 문다
            if (GridNav.Adjacent(me, playerCell)) WillAttack = true;
            return;
        }
        Chase(me, playerCell, turn, moveSeconds, null);
    }

    void CallPack()
    {
        int called = 0;
        foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
        {
            if (m == this || (m.awake && !m.Asleep) || m.Dead || !m.enabled || m.behavior != Behavior.Coward) continue;
            if (GridNav.Distance(m.Cell, Cell) > 6 || !GridNav.LineOfSight(m.Cell, Cell)) continue;
            m.Asleep = false;
            m.awake = true;
            called++;
        }
        if (called > 0) GameLog.Add($"찍찍— {displayName}{Josa("이", "가")} 동료를 불렀다!", GameLog.Bad);
    }

    // 달아나기: 8걸음 안에 갈 수 있는 칸 중 플레이어에게서 가장 먼(어두운 곳 우선) 칸으로 가는 첫 걸음.
    // 모퉁이에 막혀도 돌아서 빠져나간다. 지금보다 멀어질 곳이 없으면 null (몰렸다).
    Vector2Int? FleeStep(Vector2Int me, Vector2Int playerCell)
    {
        var free = FreeCells(playerCell);
        var floors = GridNav.Floors();
        var first = new Dictionary<Vector2Int, Vector2Int>();
        var depth = new Dictionary<Vector2Int, int> { [me] = 0 };
        var q = new Queue<Vector2Int>();
        q.Enqueue(me);
        Vector2Int? best = null;
        float bestScore = GridNav.Distance(me, playerCell) + 0.4f;
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (depth[c] >= 8) continue;
            foreach (var d in GridNav.Dirs8)
            {
                var n = c + d;
                if (!free.Contains(n) || depth.ContainsKey(n) || !GridNav.CornerFree(c, d, floors)) continue;
                if (GridNav.Adjacent(n, playerCell, floors)) continue;   // 플레이어 옆을 스쳐 가지는 않는다
                depth[n] = depth[c] + 1;
                first[n] = c == me ? n : first[c];
                q.Enqueue(n);
                float score = GridNav.Distance(n, playerCell) + (LightSources.IsLit(n) ? 0f : 0.5f) - depth[n] * 0.05f;
                if (score > bestScore) { bestScore = score; best = first[n]; }
            }
        }
        return best;
    }

    // 플레이어에게서 멀어지는 한 걸음 (어두운 칸 우선). mustBeDark면 횃불 빛이 없는 칸만.
    Vector2Int? StepAway(Vector2Int me, Vector2Int playerCell, bool preferDark, bool mustBeDark = false)
    {
        var free = FreeCells(playerCell);
        var floors = GridNav.Floors();
        Vector2Int? best = null;
        float bestScore = float.MinValue;
        int now = GridNav.Distance(me, playerCell);
        foreach (var d in GridNav.Dirs8)
        {
            var n = me + d;
            if (!free.Contains(n) || !GridNav.CornerFree(me, d, floors)) continue;
            bool lit = LightSources.IsLitByFixed(n);
            if (mustBeDark && lit) continue;
            int dist = GridNav.Distance(n, playerCell);
            if (!mustBeDark && dist <= now) continue;
            float score = dist + (preferDark && !lit ? 0.5f : 0f) + Random.value * 0.1f;
            if (score > bestScore) { bestScore = score; best = n; }
        }
        return best;
    }

    void HitPlayer(PlayerStats player, float delay)
    {
        int dmg = Mathf.Max(1, Mathf.RoundToInt(Random.Range(AtkMin, AtkMax + 1) * (InLight ? LitDamage : DarkDamage) * nextHitMult));
        nextHitMult = 1f;
        float impact = delay + LungeImpact;
        if (behavior != Behavior.Giant) Sfx.PlayKind("attack", Voice, delay);

        // 플레이어의 회피·막기 (몬스터와 같은 확률)
        if (Random.value < DodgeChance)
        {
            GameLog.Add($"{displayName}의 공격을 피했다!", GameLog.Good);
            DamagePopup.Spawn(DamagePopup.Above(player), "회피", DamagePopup.Miss, 1f, impact);
            Sfx.Play("dodge", impact);
            return;
        }
        if (Random.value < BlockChance)
        {
            int cut = BlockedDamage(dmg);
            GameLog.Add($"{displayName}의 공격을 막았다!", GameLog.Good);
            DamagePopup.Spawn(DamagePopup.Above(player), cut > 0 ? $"막기 -{cut}" : "막기", DamagePopup.Miss, 1f, impact);
            Sfx.Play("block", impact);
            if (cut <= 0) return;
            dmg = cut;
        }
        else
        {
            GameLog.Add($"{displayName}의 공격! {dmg}의 피해.", GameLog.Bad);
            DamagePopup.Spawn(DamagePopup.Above(player), $"-{dmg}", DamagePopup.Hurt, dmg >= AtkMax && AtkMax > AtkMin ? 1.4f : 1.15f, impact);
        }
        ScreenEffects.PlayerHit((float)dmg / player.maxHp, impact);
        Sfx.Play("player_hurt", impact);
        player.Damage(dmg);
        if (!player.Dead) { GridClickMover.FaceAttacker(transform.position); GridClickMover.PlayAnim("Hit", impact); }   // 때린 쪽을 보고, 맞는 동작은 공격이 닿는 순간에
        if (lifesteal > 0f && Hp < MaxHp)   // 피를 빠는 정예
        {
            int heal = Mathf.Max(1, Mathf.RoundToInt(dmg * lifesteal));
            Hp = Mathf.Min(MaxHp, Hp + heal);
            if (bar != null) bar.Refresh();
            DamagePopup.Spawn(DamagePopup.Above(this), $"+{heal}", DamagePopup.Poison, 0.9f, impact);
        }
        if (!player.Dead && poisonChance > 0f && Random.value < poisonChance)
        {
            player.Poison(poisonTurns);
            DamagePopup.Spawn(DamagePopup.Above(player, 1.3f), "중독!", DamagePopup.Poison, 0.9f, impact + 0.15f);
        }
    }

    // 맞은 순간 몸이 잠깐 붉게 번쩍인다
    IEnumerator FlashRed(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        var renderers = GetComponentsInChildren<Renderer>();
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(1f, 0.3f, 0.25f));
        foreach (var r in renderers) r.SetPropertyBlock(block);
        yield return new WaitForSeconds(0.12f);
        foreach (var r in renderers) if (r != null) r.SetPropertyBlock(null);
    }

    // 이름 끝 글자의 받침에 맞는 조사 (쥐가 / 늑대를 / 슬라임이)
    string Josa(string withBatchim, string without) => Korean.Josa(displayName, withBatchim, without);

    // ---- 이동 ----

    static HashSet<Vector2Int> FreeCells(Vector2Int playerCell)
    {
        var free = GridNav.Walkable(GridNav.Floors());
        free.Remove(playerCell);
        return free;
    }

    // 플레이어 옆 칸(대각선 포함)까지의 최단 경로에서 첫 걸음
    // opensDoors: 인간형은 잠기지 않은 닫힌 문도 지나갈 길로 본다 (다다르면 연다)
    static Vector2Int? StepToward(Vector2Int from, Vector2Int target, System.Func<Vector2Int, bool> avoid = null, bool opensDoors = false)
    {
        var floors = GridNav.Floors();
        var free = FreeCells(target);
        if (opensDoors) foreach (var d in Door.All) if (!d.IsOpen && !d.locked) free.Add(d.Cell);
        if (avoid != null) free.RemoveWhere(c => avoid(c));
        var route = GridNav.FindPath(from, c => GridNav.Adjacent(c, target, floors), free, floors, 400);
        return route != null && route.Count > 0 ? route[0] : (Vector2Int?)null;
    }

    // 칸 위치(루트)는 즉시 옮겨서 다른 몬스터와 겹치지 않게 하고, 모델(자식)만 미끄러지듯 따라간다
    IEnumerator MoveTo(Vector2Int cell, float seconds)
    {
        var from = transform.position;
        var to = GridTile.CellToWorld(cell);
        Face(to);
        transform.position = to;
        Fire.Entered(this, cell);   // 물에 들어가면 불이 꺼진다
        bool walked = PlayClip("Walk");
        var v = Visual;
        if (v == null) yield break;
        var offset = from - to;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            v.position = transform.TransformPoint(visualHome) + offset * (1f - t);
            yield return null;
        }
        v.localPosition = visualHome;
        if (walked) PlayClip("Idle", 0.25f);
    }

    // ---- 연출 ----

    void Face(Vector3 worldPos)
    {
        var dir = worldPos - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir);
    }

    // 공격 연출 (모션이 없는 몬스터): 몸을 웅크렸다가 대상 쪽으로 짧게 튀어 오른다. 정점이 타격 순간 (MonsterImpact).
    const float SpringForward = 0.35f, SpringUp = 0.35f;   // 튀어 오르는 거리·높이 (칸 = 4라 아주 짧다)
    IEnumerator Lunge(Vector3 targetPos, float delay, string clip = "Attack")
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Face(targetPos);
        if (PlayClip(clip)) yield break;   // 동작 모델이면 동작으로
        var v = Visual;
        if (v == null) yield break;
        var dir = targetPos - transform.position; dir.y = 0;
        dir = dir.normalized;
        var baseScale = v.localScale;
        float len = TurnResolver.MonsterAttackLength;
        float peak = TurnResolver.MonsterImpact / len;   // 튀어 오른 정점 = 타격
        float rise = Mathf.Min(0.1f, peak * 0.5f), sit = peak - rise;
        for (float t = 0f; t < 1f; t += Time.deltaTime / len)
        {
            // 0~sit: 웅크림, sit~peak: 튀어 오름(정점=타격), peak~1: 내려와 제자리
            float crouch = t < sit ? Mathf.SmoothStep(0f, 1f, t / sit) : Mathf.Max(0f, 1f - (t - sit) / rise);
            float spring = t < sit ? 0f : t < peak ? (t - sit) / rise : 1f - Mathf.SmoothStep(0f, 1f, (t - peak) / (1f - peak));
            v.localScale = new Vector3(baseScale.x * (1f + 0.08f * crouch), baseScale.y * (1f - 0.18f * crouch), baseScale.z * (1f + 0.08f * crouch));
            v.position = transform.TransformPoint(visualHome) + dir * (SpringForward * spring) + Vector3.up * (SpringUp * spring);
            yield return null;
        }
        v.localScale = baseScale;
        v.localPosition = visualHome;
    }

    IEnumerator Flinch(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay + 0.1f);
        if (giantMove == GiantMove.None && PlayClip("Hit", 0.05f)) yield break;   // 동작 모델 (예고 자세 중엔 그대로)
        var v = Visual;
        if (v == null) yield break;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f)
        {
            v.localPosition = visualHome + new Vector3(Mathf.Sin(t * 40f), 0, Mathf.Cos(t * 37f)) * 0.15f * (1f - t);
            yield return null;
        }
        v.localPosition = visualHome;
    }
}
