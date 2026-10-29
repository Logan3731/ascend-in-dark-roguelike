using System;
using System.Collections.Generic;
using UnityEngine;

// 화면 좌하단에 뜨는 진행 로그. HUD가 OnChanged를 구독해 최근 몇 줄을 그린다.
public static class GameLog
{
    public const int MaxLines = 5;

    public static readonly Color Info = new Color(0.85f, 0.82f, 0.74f);
    public static readonly Color Good = new Color(0.55f, 0.85f, 0.45f);
    public static readonly Color Warn = new Color(0.94f, 0.54f, 0.17f);
    public static readonly Color Bad = new Color(0.9f, 0.25f, 0.2f);

    public struct Line { public string text; public Color color; public float time; }

    static readonly List<Line> lines = new List<Line>();
    public static IReadOnlyList<Line> Lines => lines;
    public static event Action OnChanged;

    public static void Add(string text, Color color)
    {
        lines.Add(new Line { text = text, color = color, time = Time.time });
        if (lines.Count > MaxLines) lines.RemoveAt(0);
        OnChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        lines.Clear();
        OnChanged = null;
    }
}
