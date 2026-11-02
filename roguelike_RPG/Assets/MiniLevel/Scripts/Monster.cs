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
    static void ResetHint() { darkHintShown = false; hinted.Clear(); }

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
        RollStats();
    }

    void Start() { bar = MonsterHealthBar.Create(this); }

    void OnDestroy() { if (bar != null) Destroy(bar.gameObject); ClearWarning(); }

    // 레벨에 따라 강해지고, 체력은 개체마다 조금씩 다르다
    void RollStats()
    {
        int lv = Mathf.Max(1, level) - 1;
        float hp = baseHp * (1f + 0.3f * lv) * Random.Range(1f - hpVariance, 1f + hpVariance);
        MaxHp = Mathf.Max(1, Mathf.RoundToInt(hp));
        Hp = MaxHp;
        AtkMin = attackMin + lv;
        AtkMax = attackMax + lv;
        Strength = strength + lv;
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
        TurnResolver.BeginTurn(GridTile.WorldToCell(player.transform.position), mover != null ? mover.moveSeconds : 0.3f);
        TurnResolver.ResolveAttacks(player, this);
    }

    // 차례가 와서 플레이어에게 맞는다 (delay: 연출 시작까지 기다릴 시간)
    public void ReceivePlayerAttack(PlayerStats player, float delay)
    {
        var mover = player.GetComponent<GridClickMover>();
        if (mover != null) mover.PlayLunge(transform.position, delay);

        float impact = delay + LungeImpact;   // 내지른 공격이 닿는 순간
        bool strike = player.ConsumeStrike();  // [Q] 강타: 피할 수 없고 두 배
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
    }

    // 던진 돌·횃불에 맞는다 (피하지 못한다)
    public void HitByThrow(PlayerStats player, int dmg, string what, float delay)
    {
        if (fireWeak && what == "횃불")
        {
            dmg *= 2;
            GameLog.Add($"{displayName}{Josa("은", "는")} 불에 약하다!", GameLog.Good);
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
        Hp = Mathf.Max(0, Hp - amount);
        awake = true;
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
        if (!awake && dist <= sight)
        {
            WakeUp();
            if (behavior == Behavior.Coward) CallPack();
        }
        if (!awake) return;

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
            default: Chase(me, playerCell, turn, moveSeconds, null); break;
        }
    }

    void WakeUp()
    {
        awake = true;
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
        chargeCooldown = 4;
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

    // ---- 쥐: 다치면 달아나고, 동료를 부른다 ----

    void PlanCoward(Vector2Int me, Vector2Int playerCell, int turn, float moveSeconds)
    {
        if (!fleeing && Hp <= MaxHp * 0.4f)
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
            if (m == this || m.awake || m.Dead || !m.enabled || m.behavior != Behavior.Coward) continue;
            if (GridNav.Distance(m.Cell, Cell) > 6) continue;
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
