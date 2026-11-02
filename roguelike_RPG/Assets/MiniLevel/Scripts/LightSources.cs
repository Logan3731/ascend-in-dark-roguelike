using System.Collections.Generic;
using UnityEngine;

// 지금 켜져 있는 빛(플레이어가 든 횃불, 불붙은 벽 횃불)을 모아 두고, 어떤 칸이 빛 안에 있는지 알려 준다.
// 빛 안의 몬스터는 약해지고(이름 흰색), 어둠 속의 몬스터는 사나워진다(이름 빨간색).
public static class LightSources
{
    public interface ISource
    {
        bool IsOn { get; }
        Vector2Int Cell { get; }
        float RadiusCells { get; }
    }

    static readonly List<ISource> sources = new List<ISource>();

    public static void Register(ISource s) { if (!sources.Contains(s)) sources.Add(s); }
    public static void Unregister(ISource s) => sources.Remove(s);

    // 플레이어가 든 횃불을 뺀 빛(벽 횃불·던진 횃불)만 — 박쥐는 이 빛 안으로 들어오지 않는다
    public static bool IsLitByFixed(Vector2Int cell)
    {
        foreach (var s in sources)
        {
            if (s == null || !s.IsOn || s is PlayerStats) continue;
            var d = cell - s.Cell;
            if (d.x * d.x + d.y * d.y <= s.RadiusCells * s.RadiusCells) return true;
        }
        return false;
    }

    public static bool IsLit(Vector2Int cell)
    {
        foreach (var s in sources)
        {
            if (s == null || !s.IsOn) continue;
            var d = cell - s.Cell;
            if (d.x * d.x + d.y * d.y <= s.RadiusCells * s.RadiusCells) return true;
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => sources.Clear();
}
