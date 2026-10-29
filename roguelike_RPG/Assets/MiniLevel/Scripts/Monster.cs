using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 턴제 몬스터. 플레이어가 한 턴을 쓰면 몬스터도 한 번 움직인다.
// 시야 안에 플레이어가 들어오면 쫓아오고, 붙으면 공격한다. 클릭하면 플레이어가 다가가서 때린다.
[RequireComponent(typeof(GridBlocker))]
public class Monster : Interactable
{
    public string displayName = "몬스터";
    public int maxHp = 10;
    public int attack = 2;
    [Range(0f, 1f)] public float evasion = 0.1f;    // 플레이어 공격을 피할 확률
    public int sight = 3;                            // 이 거리(칸) 안에 들어오면 깨어난다
    public int moveEvery = 1;                        // 몇 턴마다 한 번 움직이는가 (슬라임 2)
    [Range(0f, 1f)] public float erratic = 0f;       // 무작위로 움직일 확률 (박쥐)
    public float hoverHeight = 0f;
    [Header("떨어뜨리는 것")]
    [Range(0f, 1f)] public float foodDropChance = 0f;
    public int goldDrop = 0;

    public int Hp { get; private set; }
    public bool Dead => Hp <= 0;
    bool awake;

    // 연출은 모델(첫 번째 자식)만 움직인다
    Transform Visual => transform.childCount > 0 ? transform.GetChild(0) : null;
    Vector3 visualHome;

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    protected override void Awake()
    {
        base.Awake();
        Hp = maxHp;
        if (Visual != null) visualHome = Visual.localPosition;
    }

    void OnEnable() { TurnManager.OnTurnEnded += TakeTurn; }
    void OnDisable() { TurnManager.OnTurnEnded -= TakeTurn; }

    // 이름 끝 글자의 받침에 맞는 조사 (쥐가 / 늑대를 / 슬라임이)
    string Josa(string withBatchim, string without)
    {
        char c = displayName[displayName.Length - 1];
        if (c < 0xAC00 || c > 0xD7A3) return without;
        return (c - 0xAC00) % 28 > 0 ? withBatchim : without;
    }

    // 플레이어가 클릭해서 다가와 공격
    public override void Interact(PlayerStats player)
    {
        if (Dead || player.Dead) return;
        var mover = player.GetComponent<GridClickMover>();
        if (mover != null) mover.PlayLunge(transform.position);

        if (Random.value < evasion)
            GameLog.Add($"{displayName}{Josa("이", "가")} 공격을 피했다.", GameLog.Info);
        else
            TakeDamage(player.AttackPower, player);

        TurnManager.EndTurn();
    }

    void TakeDamage(int amount, PlayerStats player)
    {
        Hp = Mathf.Max(0, Hp - amount);
        awake = true;
        StartCoroutine(Flinch());
        if (!Dead)
        {
            GameLog.Add($"{displayName}에게 {amount}의 피해. (남은 체력 {Hp})", GameLog.Info);
            return;
        }

        GameLog.Add($"{displayName}{Josa("을", "를")} 쓰러뜨렸다.", GameLog.Good);
        if (foodDropChance > 0f && Random.value < foodDropChance)
        {
            player.AddFood(1);
            GameLog.Add("고기를 얻었다. (식량 +1)", GameLog.Good);
        }
        if (goldDrop > 0)
        {
            player.AddGold(goldDrop);
            GameLog.Add($"골드 +{goldDrop}", GameLog.Good);
        }
        enabled = false;
        FadeOut.Run(this, 0.8f, 0.2f);
    }

    void TakeTurn(int turn)
    {
        if (Dead) return;
        var player = FindAnyObjectByType<PlayerStats>();
        if (player == null || player.Dead) return;

        var me = Cell;
        var target = GridTile.WorldToCell(player.transform.position);
        int dist = Mathf.Abs(me.x - target.x) + Mathf.Abs(me.y - target.y);
        if (!awake && dist <= sight)
        {
            awake = true;
            GameLog.Add($"{displayName}{Josa("이", "가")} 이쪽을 노려본다!", GameLog.Warn);
        }
        if (!awake) return;

        if (dist == 1)
        {
            StartCoroutine(Lunge(GridTile.CellToWorld(target)));
            int dmg = attack;
            GameLog.Add($"{displayName}의 공격! {dmg}의 피해.", GameLog.Bad);
            player.Damage(dmg);
            return;
        }

        if (moveEvery > 1 && turn % moveEvery != 0) return;

        Vector2Int? next = Random.value < erratic ? RandomStep(me, target) : StepToward(me, target);
        if (next != null) StartCoroutine(MoveTo(next.Value));
    }

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

    static Vector2Int? RandomStep(Vector2Int from, Vector2Int playerCell)
    {
        var free = FreeCells(playerCell);
        var options = new List<Vector2Int>();
        foreach (var d in Dirs) if (free.Contains(from + d)) options.Add(from + d);
        return options.Count > 0 ? options[Random.Range(0, options.Count)] : (Vector2Int?)null;
    }

    // 칸 위치(루트)는 즉시 옮겨서 다른 몬스터와 겹치지 않게 하고, 모델(자식)만 미끄러지듯 따라간다
    IEnumerator MoveTo(Vector2Int cell)
    {
        var from = transform.position;
        var to = GridTile.CellToWorld(cell) + Vector3.up * hoverHeight;
        Face(to);
        transform.position = to;
        var v = Visual;
        if (v == null) yield break;
        var offset = from - to;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
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

    IEnumerator Lunge(Vector3 targetPos)
    {
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

    IEnumerator Flinch()
    {
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
