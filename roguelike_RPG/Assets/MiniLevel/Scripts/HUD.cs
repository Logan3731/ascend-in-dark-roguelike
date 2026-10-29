using UnityEngine;
using UnityEngine.UI;

// 화면 HUD: 좌상단 HP·MP·허기·횃불 바, 우상단 턴 수, 좌하단 진행 로그. UI는 실행 시 코드로 만든다.
public class HUD : MonoBehaviour
{
    public PlayerStats stats;
    public float logFadeAfter = 6f;

    Font font;
    RectTransform hpFill, mpFill, hungerFill, torchFill;
    Text hpText, mpText, hungerText, torchText, turnText;
    Text[] logLines;

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildCanvas();
    }

    void OnEnable()
    {
        GameLog.OnChanged += RefreshLog;
        TurnManager.OnTurnEnded += RefreshTurn;
        if (stats != null) stats.OnChanged += RefreshBars;
    }

    void OnDisable()
    {
        GameLog.OnChanged -= RefreshLog;
        TurnManager.OnTurnEnded -= RefreshTurn;
        if (stats != null) stats.OnChanged -= RefreshBars;
    }

    void Start()
    {
        RefreshBars();
        RefreshTurn(TurnManager.Turn);
        RefreshLog();
    }

    void Update()
    {
        // 오래된 로그는 서서히 흐려진다
        var lines = GameLog.Lines;
        int offset = logLines.Length - lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            float age = Time.time - lines[i].time;
            var c = lines[i].color;
            c.a = Mathf.Clamp01(1f - (age - logFadeAfter) / 2f);
            logLines[offset + i].color = c;
        }
    }

    void RefreshBars()
    {
        if (stats == null) return;
        SetBar(hpFill, hpText, stats.Hp, stats.maxHp);
        SetBar(mpFill, mpText, stats.Mp, stats.maxMp);
        SetBar(hungerFill, hungerText, stats.Hunger, stats.maxHunger);
        SetBar(torchFill, torchText, stats.Torch, stats.maxTorch);
    }

    void RefreshTurn(int turn) => turnText.text = $"턴 {turn}";

    void RefreshLog()
    {
        var lines = GameLog.Lines;
        int offset = logLines.Length - lines.Count;
        for (int i = 0; i < logLines.Length; i++)
        {
            if (i < offset) { logLines[i].text = ""; continue; }
            logLines[i].text = lines[i - offset].text;
            logLines[i].color = lines[i - offset].color;
        }
    }

    static void SetBar(RectTransform fill, Text label, int value, int max)
    {
        float t = max > 0 ? (float)value / max : 0f;
        fill.anchorMax = new Vector2(t, 1f);
        label.text = $"{value}/{max}";
    }

    // ---- UI 생성 ----

    void BuildCanvas()
    {
        var canvasGo = new GameObject("HUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = canvasGo.GetComponent<RectTransform>();

        float y = -30f;
        hpFill = MakeBar(root, "HP", new Color(0.75f, 0.2f, 0.17f), ref y, out hpText);
        mpFill = MakeBar(root, "MP", new Color(0.25f, 0.42f, 0.72f), ref y, out mpText);
        hungerFill = MakeBar(root, "허기", new Color(0.55f, 0.62f, 0.25f), ref y, out hungerText);
        torchFill = MakeBar(root, "횃불", new Color(0.94f, 0.54f, 0.17f), ref y, out torchText);

        turnText = MakeText(root, "Turn", 30, TextAnchor.UpperRight);
        var tr = turnText.rectTransform;
        tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(1, 1);
        tr.anchoredPosition = new Vector2(-40, -28);
        tr.sizeDelta = new Vector2(300, 44);

        logLines = new Text[GameLog.MaxLines];
        for (int i = 0; i < logLines.Length; i++)
        {
            var t = MakeText(root, "Log" + i, 28, TextAnchor.LowerLeft);
            var r = t.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 0);
            r.anchoredPosition = new Vector2(40, 40 + (logLines.Length - 1 - i) * 38);
            r.sizeDelta = new Vector2(900, 38);
            logLines[i] = t;
        }
    }

    RectTransform MakeBar(RectTransform root, string label, Color color, ref float y, out Text valueText)
    {
        var name = MakeText(root, label + "Label", 22, TextAnchor.MiddleLeft);
        name.text = label;
        var nr = name.rectTransform;
        nr.anchorMin = nr.anchorMax = nr.pivot = new Vector2(0, 1);
        nr.anchoredPosition = new Vector2(40, y);
        nr.sizeDelta = new Vector2(70, 26);

        var bg = new GameObject(label + "Bar", typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(root, false);
        bg.color = new Color(0, 0, 0, 0.55f);
        var br = bg.rectTransform;
        br.anchorMin = br.anchorMax = br.pivot = new Vector2(0, 1);
        br.anchoredPosition = new Vector2(112, y - 5);
        br.sizeDelta = new Vector2(340, 16);

        var fill = new GameObject("Fill", typeof(Image)).GetComponent<Image>();
        fill.transform.SetParent(bg.transform, false);
        fill.color = color;
        var fr = fill.rectTransform;
        fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one;
        fr.offsetMin = fr.offsetMax = Vector2.zero;

        valueText = MakeText(root, label + "Value", 18, TextAnchor.MiddleLeft);
        var vr = valueText.rectTransform;
        vr.anchorMin = vr.anchorMax = vr.pivot = new Vector2(0, 1);
        vr.anchoredPosition = new Vector2(462, y);
        vr.sizeDelta = new Vector2(120, 26);

        y -= 32f;
        return fr;
    }

    Text MakeText(RectTransform root, string name, int size, TextAnchor anchor)
    {
        var t = new GameObject(name, typeof(Text), typeof(Shadow)).GetComponent<Text>();
        t.transform.SetParent(root, false);
        t.font = font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = GameLog.Info;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        t.GetComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
        return t;
    }
}
