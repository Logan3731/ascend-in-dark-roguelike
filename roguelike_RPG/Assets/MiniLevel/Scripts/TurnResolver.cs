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
    // ---- 공격 연출 타이밍 (한 곳에 모은다) ----
    // 플레이어 공격 모션: Torch_Attack 2.0초를 Animator에서 0.85초로 감는다(MixamoSetup, 속도 ×2.35).
    // 손이 가장 빠른 순간(타격)은 원본 28/60프레임 = 45% 지점 → 트리거 뒤 약 0.38초 (Blender로 잼).
    public const float PlayerAttackLength = 0.85f;
    public const float PlayerImpact = 0.38f;        // 플레이어 공격이 닿는 순간 (트리거 뒤)
    public const float PlayerAttackGap = 0.65f;     // 플레이어가 휘두른 뒤 다음 공격자가 시작할 때까지
    public const float PlayerComboGap = 0.5f;       // 연속 공격·보조칸 타격 사이 (앞 타격이 닿은 뒤 바로)
    public const float TripleImpact = 0.27f;        // 3연격 첫 타격 (Triple 모션은 원래 속도)
    public const float TripleGap = 0.35f;           // 3연격 타격 사이 (Triple 모션의 타격 0.27·0.62·0.97초에 맞춤)
    // 몬스터 공격: 모션이 없어 코드로 웅크렸다 튀어 오른다 (Monster.Lunge). 플레이어 공격과 같은 길이·타격 시점.
    public const float MonsterAttackLength = PlayerAttackLength;
    public const float MonsterImpact = PlayerImpact;
    public const float AttackGap = PlayerAttackGap; // 몬스터 공격 사이 간격
    const float AttackAnim = 0.65f;                 // 마지막 공격이 시작된 뒤 연출이 끝날 때까지 (플레이어 모션 기준)

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
                delay += a.m.ReceivePlayerAttack(player, delay);   // 3연격·추가 타격이면 더 길다 (PlayerAttackGap 기준)
            }
            else if (!a.m.WillAttack) continue;   // 먼저 맞고 기절했다 (방패)
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
