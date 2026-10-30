using System;
using UnityEngine;

// 한 칸 이동 = 한 턴. 다른 시스템(허기, 횃불 내구도, 적 행동)은 OnTurnEnded를 구독한다.
public static class TurnManager
{
    public static int Turn { get; private set; }
    public static event Action<int> OnTurnEnded;

    public static void EndTurn()
    {
        Turn++;
        Debug.Log($"[Turn] {Turn}");
        OnTurnEnded?.Invoke(Turn);
    }

    // 새로 깨어날 때(씬을 다시 열 때) 턴 수를 0으로
    public static void ResetTurn() => Turn = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        Turn = 0;
        OnTurnEnded = null;
    }
}
