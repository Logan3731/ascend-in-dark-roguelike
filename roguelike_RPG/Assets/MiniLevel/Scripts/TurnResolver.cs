using System.Collections.Generic;
using UnityEngine;

// 한 턴의 진행 순서.
// 1) 이동은 동시에: 플레이어가 한 칸 움직이기 시작하면 몬스터들도 같은 시간에 움직인다.
// 2) 공격은 차례로: 이번 턴에 공격하는 쪽(플레이어 + 붙어 있던 몬스터)을 힘이 높은 순서로 한 명씩 때린다.
//    힘이 같으면 플레이어가 먼저. 먼저 맞고 쓰러지면 그 공격은 하지 못한다.
// 3) 허기·횃불·독 같은 턴 경과 처리 (TurnManager.EndTurn).
// 공격 연출이 다 끝나는 시각(BusyUntil)까지 플레이어는 다음 행동을 할 수 없다.
public static class TurnResolver
{
    public const float AttackGap = 0.3f;     // 공격 연출 사이 간격(초)
    const float AttackAnim = 0.35f;          // 마지막 공격 연출이 끝날 때까지

    public static float BusyUntil { get; set; }
    public static bool AttackedThisTurn { get; private set; }   // 이번 턴에 몬스터에게 공격받았는가

    // 플레이어가 이번 턴에 서 있게 될 칸이 정해졌을 때 (이동 시작 또는 제자리 공격)
    public static void BeginTurn(Vector2Int playerCell, float moveSeconds)
    {
        foreach (var m in Object.FindObjectsByType<Monster>(FindObjectsSortMode.None))
            if (m.enabled && !m.Dead) m.PlanAndMove(playerCell, TurnManager.Turn + 1, moveSeconds);
    }

    // 이동이 끝난 뒤: 공격을 힘 순서대로 주고받고 턴을 끝낸다. playerTarget이 null이면 플레이어는 공격하지 않음.
    public static void ResolveAttacks(PlayerStats player, Monster playerTarget)
    {
        AttackedThisTurn = false;
        var attackers = new List<(int str, bool isPlayer, Monster m)>();
        if (playerTarget != null) attackers.Add((player.TurnStrength, true, playerTarget));   // 무거운 무기는 늦다
        foreach (var m in Object.FindObjectsByType<Monster>(FindObjectsSortMode.None))
            if (m.enabled && !m.Dead && m.WillAttack) attackers.Add((m.Strength, false, m));

        // 힘 내림차순, 같으면 플레이어 먼저
        attackers.Sort((a, b) => a.str != b.str ? b.str.CompareTo(a.str) : b.isPlayer.CompareTo(a.isPlayer));

        float delay = 0f;
        foreach (var a in attackers)
        {
            if (player.Dead || a.m.Dead) continue;
            if (a.isPlayer)
            {
                a.m.ReceivePlayerAttack(player, delay);
                delay += AttackGap;
            }
            else
            {
                AttackedThisTurn = true;
                delay += a.m.AttackPlayer(player, delay);
            }
        }
        if (attackers.Count > 0) BusyUntil = Mathf.Max(BusyUntil, Time.time + delay - AttackGap + AttackAnim);
        TurnManager.EndTurn();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        BusyUntil = 0f;
        AttackedThisTurn = false;
    }
}
