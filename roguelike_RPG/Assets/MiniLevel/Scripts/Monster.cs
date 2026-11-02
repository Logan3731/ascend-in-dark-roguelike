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
    float Evasion => InLight ? evasion * 0.5f : Mathf.Min(0.9f, evasion + 0.1f);
    static bool darkHintShown;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHint() => darkHintShown = false;

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

    void OnDestroy() { if (bar != null) Destroy(bar.gameObject); }

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
        if (Random.value < Evasion)
        {
            GameLog.Add($"{displayName}{Josa("이", "가")} 공격을 피했다.", GameLog.Info);
            DamagePopup.Spawn(DamagePopup.Above(this), "빗나감", DamagePopup.Miss, 0.9f, impact);
            return;
        }
        int dmg = player.RollAttack();
        // 최대치로 때리면 더 크고 주황색으로
        bool best = dmg >= player.AttackMax && player.AttackMax > player.AttackMin;
        DamagePopup.Spawn(DamagePopup.Above(this), best ? $"{dmg}!" : dmg.ToString(), best ? DamagePopup.Big : DamagePopup.Hit, best ? 1.45f : 1.1f, impact);
        StartCoroutine(FlashRed(impact));
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
        int dist = Mathf.Abs(me.x - playerCell.x) + Mathf.Abs(me.y - playerCell.y);
        if (!awake && dist <= sight)
        {
            awake = true;
            GameLog.Add($"{displayName}{Josa("이", "가")} 이쪽을 노려본다!", GameLog.Warn);
            if (!InLight && !darkHintShown)
            {
                darkHintShown = true;
                GameLog.Add("어둠 속의 적은 더 사납다. 빛 안에서 싸워야 한다…", GameLog.Info);
            }
        }
        if (!awake) return;

        if (dist == 1) { WillAttack = true; return; }
        if (moveEvery > 1 && turn % moveEvery != 0) return;
        var next = StepToward(me, playerCell);
        if (next != null) StartCoroutine(MoveTo(next.Value, moveSeconds));
    }

    void HitPlayer(PlayerStats player, float delay)
    {
        int dmg = Mathf.Max(1, Mathf.RoundToInt(Random.Range(AtkMin, AtkMax + 1) * (InLight ? LitDamage : DarkDamage)));
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
        var free = new HashSet<Vector2Int>();
        foreach (var t in FindObjectsByType<GridTile>(FindObjectsSortMode.None)) free.Add(t.Cell);
        foreach (var b in FindObjectsByType<GridBlocker>(FindObjectsSortMode.None))
            if (b.enabled) free.Remove(b.Cell);
        free.Remove(playerCell);
        return free;
    }

    // 플레이어 옆 칸까지의 BFS 경로에서 첫 걸음
    static Vector2Int? StepToward(Vector2Int from, Vector2Int target)
    {
        var free = FreeCells(target);
        var prev = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
        var open = new Queue<Vector2Int>();
        open.Enqueue(from);
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            var d0 = cur - target;
            if (Mathf.Abs(d0.x) + Mathf.Abs(d0.y) == 1)
            {
                if (cur == from) return null;
                var c = cur;
                while (prev[c] != from) c = prev[c];
                return c;
            }
            if (prev.Count > 400) break;
            foreach (var d in Dirs)
            {
                var n = cur + d;
                if (!free.Contains(n) || prev.ContainsKey(n)) continue;
                prev[n] = cur;
                open.Enqueue(n);
            }
        }
        return null;
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
