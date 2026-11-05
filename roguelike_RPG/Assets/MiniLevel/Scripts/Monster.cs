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
    [Range(0f, 1f)] public float evasion = 0.1f;          // 플레이어 공격을 피할 확률
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
    float Evasion => stunned > 0 ? 0f : InLight ? evasion * 0.5f : Mathf.Min(0.9f, evasion + 0.1f);

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

    const float LungeImpact = 0.12f;   // 공격 연출(0.25초)에서 몸이 가장 앞으로 나간 순간
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
        else GameLog.Add($"{displayName} — {AffixHint(affix)}.", GameLog.Bad);
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
        GameLog.Add($"{displayName}{Josa("이", "가")} 픽 쓰러져 잠들었다. (기습 기회!)", GameLog.Good);
        DamagePopup.Spawn(DamagePopup.Above(this), "Zzz", DamagePopup.Miss, 1.3f, delay);
    }

    public void HealBy(int amount, float delay)
    {
        int before = Hp;
        Hp = Mathf.Min(MaxHp, Hp + amount);
        if (bar != null) bar.Refresh();
        GameLog.Add($"{displayName}의 상처가 아물었다… (+{Hp - before})", GameLog.Bad);
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

    // 레벨에 따라 강해지고, 체력은 개체마다 조금씩 다르다
    void RollStats()
    {
        int lv = Mathf.Max(1, level) - 1;
        float hp = baseHp * (1f + 0.3f * lv) * Random.Range(1f - hpVariance, 1f + hpVariance);
        MaxHp = Mathf.Max(1, Mathf.RoundToInt(hp));
        AtkMin = attackMin + lv;
        AtkMax = attackMax + lv;
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
    float lifesteal;           // 준 피해 중 회복하는 비율 (흡혈)
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
            transform.localScale *= 1.9f;
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
            case Affix.Swift: evasion = Mathf.Min(0.5f, evasion + 0.15f); break;
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

    // 차례가 와서 플레이어에게 맞는다 (delay: 연출 시작까지 기다릴 시간)
    public void ReceivePlayerAttack(PlayerStats player, float delay)
    {
        var mover = player.GetComponent<GridClickMover>();
        if (mover != null) mover.PlayLunge(transform.position, delay);

        float impact = delay + LungeImpact;   // 내지른 공격이 닿는 순간
        bool strike = player.ConsumeStrike();  // [Q] 강타: 피할 수 없고 두 배
        if (Asleep)
        {
            // 기습: 잠든 적은 피하지 못하고 세 배 (강타면 거기에 두 배 더)
            int sneak = player.RollAttack() * 3 * (strike ? 2 : 1);
            GameLog.Add(strike ? "기습 강타!!" : "기습!", GameLog.Good);
            CameraFollowOcclusion.Shake(0.3f, 0.25f);
            DamagePopup.Spawn(DamagePopup.Above(this), $"기습 {sneak}!", DamagePopup.Big, 1.8f, impact);
            StartCoroutine(FlashRed(impact));
            TakeDamage(sneak, player, delay);
            return;
        }
        // 힘보다 무거운 무기: 휘청이며 헛손질할 수 있다
        if (!strike && player.MissChance > 0f && Random.value < player.MissChance)
        {
            GameLog.Add("무기가 무거워 휘청이며 헛손질했다.", GameLog.Info);
            DamagePopup.Spawn(DamagePopup.Above(this), "헛손질", DamagePopup.Miss, 0.9f, impact);
            return;
        }
        if (!strike && Random.value < Evasion)
        {
            GameLog.Add($"{displayName}{Josa("이", "가")} 공격을 피했다.", GameLog.Info);
            DamagePopup.Spawn(DamagePopup.Above(this), "빗나감", DamagePopup.Miss, 0.9f, impact);
            return;
        }
        int dmg = player.RollAttack() * (strike ? 2 : 1);
        if (strike)
        {
            GameLog.Add("강타!", GameLog.Good);
            CameraFollowOcclusion.Shake(0.25f, 0.2f);
            DamagePopup.Spawn(DamagePopup.Above(this), $"강타 {dmg}!", DamagePopup.Big, 1.6f, impact);
            StartCoroutine(FlashRed(impact));
            TakeDamage(dmg, player, delay);
            return;
        }
        // 최대치로 때리면 더 크고 주황색으로
        bool best = dmg >= player.AttackMax && player.AttackMax > player.AttackMin;
        DamagePopup.Spawn(DamagePopup.Above(this), best ? $"{dmg}!" : dmg.ToString(), best ? DamagePopup.Big : DamagePopup.Hit, best ? 1.45f : 1.1f, impact);
        StartCoroutine(FlashRed(impact));
        TakeDamage(dmg, player, delay);

        // 가벼운 무기(힘이 남는 무기): 가끔 재빠르게 한 번 더 때린다
        if (!Dead && player.DoubleChance > 0f && Random.value < player.DoubleChance)
        {
            float again = delay + 0.22f;
            if (mover != null) mover.PlayLunge(transform.position, again);
            int dmg2 = player.RollAttack();
            GameLog.Add("재빠르게 한 번 더!", GameLog.Good);
            DamagePopup.Spawn(DamagePopup.Above(this), dmg2.ToString(), DamagePopup.Hit, 1.1f, again + LungeImpact);
            StartCoroutine(FlashRed(again + LungeImpact));
            TakeDamage(dmg2, player, again);
        }
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

    // 차례가 와서 플레이어를 공격한다. 연출에 쓴 시간(다음 공격까지 간격)을 돌려준다.
    public float AttackPlayer(PlayerStats player, float delay)
    {
        StartCoroutine(Lunge(player.transform.position, delay));
        HitPlayer(player, delay);
        if (!player.Dead && Random.value < doubleAttackChance)
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
        if (Asleep) { Asleep = false; deepSleep = 0; WakeUp(); }
        awake = true;
        MakeNoise(4, 0.4f);   // 싸우는 소리에 근처의 잠든 적이 깰 수 있다
        if (bar != null) bar.Refresh();
        if (!Dead)
        {
            StartCoroutine(Flinch(delay));
            GameLog.Add($"{displayName}에게 {amount}의 피해.", GameLog.Info);
            return;
        }

        GameLog.Add($"{displayName}에게 {amount}의 피해. {displayName}{Josa("을", "를")} 쓰러뜨렸다.", GameLog.Good);
        int exp = expReward + (level - 1);
        player.AddExp(exp);
        int gold = Random.Range(goldMin, goldMax + 1) + (level - 1);
        if (gold > 0) player.AddGold(gold);
        GameLog.Add(gold > 0 ? $"경험 +{exp}   골드 +{gold}" : $"경험 +{exp}", GameLog.Good);
        if (splitOnDeath && !isSplitChild) Split();
        enabled = false;
        ClearWarning();
        if (bar != null) Destroy(bar.gameObject);
        FadeOut.Run(this, 0.8f, delay + 0.2f);
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
            if (!TryWake(dist)) return;
        }
        else if (!awake && dist <= sight)
        {
            WakeUp();
            if (behavior == Behavior.Coward) CallPack();
        }
        if (!awake) { Wander(me, playerCell, moveSeconds); return; }

        if (stunned > 0)
        {
            if (--stunned == 0) GameLog.Add($"{displayName}{Josa("이", "가")} 숨을 고른다.", GameLog.Info);
            return;
        }
        if (chargeCooldown > 0) chargeCooldown--;

        switch (behavior)
        {
            case Behavior.Charger: PlanCharger(me, playerCell, turn, moveSeconds); break;
            case Behavior.Skittish: PlanSkittish(me, playerCell, moveSeconds); break;
            case Behavior.Coward: PlanCoward(me, playerCell, turn, moveSeconds); break;
            case Behavior.Spitter: PlanSpitter(me, playerCell, turn, moveSeconds); break;
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
        if (!sleepHintShown && dist <= 4)
        {
            sleepHintShown = true;
            GameLog.Add("잠든 적이 있다. 몰래 다가가 먼저 때리면 기습(세 배)! 횃불을 들고 있으면 깨우기 쉽다.", GameLog.Info);
        }
        float chance = dist <= 1 ? 0.35f : dist == 2 ? 0.18f : dist == 3 ? 0.08f : dist == 4 ? 0.03f : 0f;
        var player = FindAnyObjectByType<PlayerStats>();
        if (player != null && player.TorchLit) chance *= 2f;
        if (Random.value >= chance) return false;
        Asleep = false;
        GameLog.Add($"{displayName}{Josa("이", "가")} 잠에서 깼다!", GameLog.Bad);
        DamagePopup.Spawn(DamagePopup.Above(this), "!", DamagePopup.Big, 1.6f);
        WakeUp();
        if (behavior == Behavior.Coward) CallPack();
        return true;
    }

    // 소리: 반경 안의 잠든 적이 확률로 깬다
    void MakeNoise(int radius, float chance)
    {
        foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
        {
            if (m == this || !m.Asleep || m.deepSleep > 0 || m.Dead || !m.enabled) continue;
            if (GridNav.Distance(m.Cell, Cell) > radius || Random.value >= chance) continue;
            m.Asleep = false;
            GameLog.Add($"소란에 {m.displayName}{Korean.Josa(m.displayName, "이", "가")} 깼다!", GameLog.Bad);
            m.WakeUp();
        }
    }

    void WakeUp()
    {
        awake = true;
        AnnounceIfElite();
        GameLog.Add($"{displayName}{Josa("이", "가")} 이쪽을 노려본다!", GameLog.Warn);
        if (hinted.Add(behavior))   // 종족마다 처음 한 번, 대처법 힌트
        {
            string hint = behavior switch
            {
                Behavior.Charger => "늑대는 곧은 길에서 돌진해 온다. 예고가 보이면 옆으로 비켜서라.",
                Behavior.Skittish => "박쥐는 횃불 빛을 싫어한다. 벽 횃불이나 던진 횃불 곁이라면 다가오지 못한다.",
                Behavior.Coward => "쥐는 무리를 짓는다. 다치면 달아나지만 느리다.",
                _ => fireWeak ? "슬라임은 불에 약해 보인다. 횃불을 던져 볼까?" : null,
            };
            if (hint != null) GameLog.Add(hint, GameLog.Info);
        }
        GridClickMover.StopWalking();   // 멀리 걸어가던 중이면 멈춰서 상황을 보게
        if (!InLight && !darkHintShown)
        {
            darkHintShown = true;
            GameLog.Add("어둠 속의 적은 더 사납다. 빛 안에서 싸워야 한다…", GameLog.Info);
        }
    }

    // 기본: 붙어 있으면 공격, 아니면 한 칸 다가간다 (avoid: 들어가지 않을 칸)
    void Chase(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds, System.Func<Vector2Int, bool> avoid)
    {
        if (GridNav.Adjacent(me, playerCell)) { WillAttack = true; return; }
        if (moveEvery > 1 && turn % moveEvery != 0) return;
        var next = StepToward(me, playerCell, avoid);
        if (next != null) StartCoroutine(MoveTo(next.Value, moveSeconds));
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
            GameLog.Add($"{displayName}{Josa("이", "가")} 몸을 낮춘다… 곧 덮쳐 온다! (빨간 길에서 비켜서라)", GameLog.Bad);
            DamagePopup.Spawn(DamagePopup.Above(this), "!", DamagePopup.Big, 1.8f);
            GridClickMover.StopWalking();
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
            GameLog.Add($"{displayName}{Josa("이", "가")} 허공을 가르고 헐떡인다. (한 턴 무방비 — 지금이 기회다)", GameLog.Good);
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
            q.transform.position = GridTile.CellToWorld(c) + Vector3.up * 0.04f;
            q.transform.rotation = Quaternion.Euler(90, 0, 0);
            q.transform.localScale = Vector3.one * (GridTile.CellSize * 0.9f);
            var m = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.15f, 0.1f, 0.35f - k * 0.07f) };
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
            else GameLog.Add("산성 침이 바닥에 떨어져 지글거린다. (피했다)", GameLog.Good);
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
            GameLog.Add($"{displayName}{Josa("이", "가")} 몸을 부풀린다… (주황 칸에서 벗어나라)", GameLog.Bad);
            DamagePopup.Spawn(DamagePopup.Above(this), "!", DamagePopup.Big, 1.6f);
            GridClickMover.StopWalking();
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

    void ShowWarnTiles(List<Vector2Int> cells, Color color)
    {
        ClearWarning();
        foreach (var c in cells)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "Warning";
            q.transform.position = GridTile.CellToWorld(c) + Vector3.up * 0.04f;
            q.transform.rotation = Quaternion.Euler(90, 0, 0);
            q.transform.localScale = Vector3.one * (GridTile.CellSize * 0.9f);
            q.GetComponent<Renderer>().material = new Material(Shader.Find("Sprites/Default")) { color = color };
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
            if (GridNav.Distance(m.Cell, Cell) > 6) continue;
            m.Asleep = false;
            m.awake = true;
            called++;
        }
        if (called > 0) GameLog.Add($"찍찍— {displayName}{Josa("이", "가")} 동료를 불렀다! ({called}마리)", GameLog.Bad);
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
        GameLog.Add($"{displayName}의 공격! {dmg}의 피해.", GameLog.Bad);
        DamagePopup.Spawn(DamagePopup.Above(player), $"-{dmg}", DamagePopup.Hurt, dmg >= AtkMax && AtkMax > AtkMin ? 1.4f : 1.15f, impact);
        ScreenEffects.PlayerHit((float)dmg / player.maxHp, impact);
        player.Damage(dmg);
        if (!player.Dead) GridClickMover.PlayAnim("Hit");
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
    static Vector2Int? StepToward(Vector2Int from, Vector2Int target, System.Func<Vector2Int, bool> avoid = null)
    {
        var floors = GridNav.Floors();
        var free = FreeCells(target);
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
        var v = Visual;
        if (v == null) yield break;
        var offset = from - to;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            v.position = transform.TransformPoint(visualHome) + offset * (1f - t);
            yield return null;
        }
        v.localPosition = visualHome;
    }

    // ---- 연출 ----

    void Face(Vector3 worldPos)
    {
        var dir = worldPos - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir);
    }

    IEnumerator Lunge(Vector3 targetPos, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Face(targetPos);
        var v = Visual;
        if (v == null) yield break;
        var dir = targetPos - transform.position; dir.y = 0;
        var push = dir.normalized * 1.2f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            v.position = transform.TransformPoint(visualHome) + push * Mathf.Sin(t * Mathf.PI);
            yield return null;
        }
        v.localPosition = visualHome;
    }

    IEnumerator Flinch(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay + 0.1f);
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
