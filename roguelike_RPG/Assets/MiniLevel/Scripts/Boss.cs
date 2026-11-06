using System.Collections.Generic;
using UnityEngine;

// 우두머리 (5층·10층 계단 방). 몬스터에 덧붙여 패턴을 더한다. 쓰러뜨려야 계단을 오를 수 있다 (StairsEvent).
// - 늑대 우두머리(5층): 돌진하는 늑대. 몇 턴마다 울부짖어 늑대를 부른다. 체력이 절반 아래면 광폭(자주 두 번 덤빈다).
// - 거대 슬라임 여왕(10층): 느리지만 단단하다. 몇 턴마다 작은 슬라임을 낳는다. 절반 아래면 더 자주 낳는다. 불에 약하다.
[RequireComponent(typeof(Monster))]
public class Boss : MonoBehaviour
{
    public enum Kind { PackLeader, SlimeQueen, GiantSkeleton }   // 거인 해골(5층): 패턴은 Monster(Behavior.Giant)가 맡는다

    public Kind kind;
    public GameObject minionPrefab;
    public int minionLevel = 1;

    Monster monster;
    int every, counter;
    bool enraged;

    public string Title => monster != null ? monster.displayName : name;
    public bool Alive => monster != null && !monster.Dead;

    void Awake()
    {
        monster = GetComponent<Monster>();
    }

    void OnEnable() => TurnManager.OnTurnEnded += Tick;
    void OnDisable() => TurnManager.OnTurnEnded -= Tick;

    void Tick(int turn)
    {
        if (!Alive || !monster.IsAwake) return;
        if (kind == Kind.GiantSkeleton) return;   // 졸개를 부르지 않는다
        if (every == 0) every = kind == Kind.PackLeader ? 6 : 5;   // (AddComponent 직후 kind를 정하므로 여기서)

        if (!enraged && monster.Hp <= monster.MaxHp / 2)
        {
            enraged = true;
            if (kind == Kind.PackLeader)
            {
                monster.doubleAttackChance = 0.5f;
                GameLog.Add($"{Title}{Korean.Josa(Title, "이", "가")} 피를 흘리며 포효한다!", GameLog.Bad);
            }
            else
            {
                every = 3;
                GameLog.Add($"{Title}{Korean.Josa(Title, "이", "가")} 부르르 떨며 몸을 쪼갠다!", GameLog.Bad);
            }
            CameraFollowOcclusion.Shake(0.3f, 0.3f);
            counter = every - 1;   // 곧바로 한 번 부른다
        }

        if (++counter < every) return;
        counter = 0;
        Summon(2);
    }

    void Summon(int count)
    {
        if (minionPrefab == null) return;
        var player = FindAnyObjectByType<PlayerStats>();
        var playerCell = player != null ? GridTile.WorldToCell(player.transform.position) : new Vector2Int(int.MinValue, 0);
        var free = GridNav.Walkable(GridNav.Floors());
        free.Remove(playerCell);
        var spots = new List<Vector2Int>();
        foreach (var d in GridNav.Dirs8)
        {
            var c = monster.Cell + d;
            if (free.Contains(c)) spots.Add(c);
        }
        int made = 0;
        for (int i = 0; i < count && spots.Count > 0; i++)
        {
            var c = spots[Random.Range(0, spots.Count)];
            spots.Remove(c);
            var go = Instantiate(minionPrefab, GridTile.CellToWorld(c), Quaternion.Euler(0, Random.Range(0, 360f), 0));
            var m = go.GetComponent<Monster>();
            m.SetLevel(minionLevel);
            m.splitOnDeath = false;   // 졸개는 갈라지지 않는다
            m.ForceAwake();
            made++;
        }
        if (made == 0) return;
        GameLog.Add(kind == Kind.PackLeader ? $"{Title}{Korean.Josa(Title, "이", "가")} 울부짖자 늑대 {made}마리가 달려든다!"
                                            : $"{Title}{Korean.Josa(Title, "이", "가")} 작은 슬라임 {made}마리를 낳았다!", GameLog.Bad);
    }
}
