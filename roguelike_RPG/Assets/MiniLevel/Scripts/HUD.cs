using UnityEngine;
using UnityEngine.UI;

// 화면 HUD (실행 시 코드로 만든다)
// - 좌상단 판: 몇 번째 깨어남 + 상태(중독·횃불), 아이콘이 붙은 HP·MP·허기·횃불 바, 식량·골드
//   바가 줄면 줄어든 만큼 잔상이 잠깐 남았다가 따라 내려간다. 위험하면(체력·허기·횃불 부족) 바가 깜빡인다.
// - 우상단: 턴 수, 우하단: 조작 안내, 좌하단: 진행 로그(뒤에 어두운 배경)
public class HUD : MonoBehaviour
{
    public PlayerStats stats;
    public float logFadeAfter = 6f;

    class Bar
    {
        public RectTransform fill, ghost;
        public Image fillImage, icon;
        public Text value;
        public Color color;
        public float shown = 1f, ghostShown = 1f, ghostHoldUntil;
        public bool warn;
    }

    Bar hp, mp, hunger, torch;
    Text headerText, turnText, foodText, goldText;
    GameObject poisonChip, torchChip;
    Text poisonChipText;
    Text[] logLines;

    void Awake() => Build();

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
        foreach (var b in new[] { hp, mp, hunger, torch }) Animate(b);

        // 로그: 오래된 줄은 서서히 흐려지고, 최신 줄일수록 진하다
        var lines = GameLog.Lines;
        int offset = logLines.Length - lines.Count;
        for (int i = 0; i < lines.Count; i++)
        {
            float age = Time.time - lines[i].time;
            var c = lines[i].color;
            float recency = i == lines.Count - 1 ? 1f : 0.8f;
            c.a = Mathf.Clamp01(1f - (age - logFadeAfter) / 2f) * recency;
            logLines[offset + i].color = c;
        }
    }

    // 잔상 바가 늦게 따라오고, 위험하면 깜빡인다
    void Animate(Bar b)
    {
        if (Time.time > b.ghostHoldUntil) b.ghostShown = Mathf.MoveTowards(b.ghostShown, b.shown, Time.deltaTime * 0.8f);
        b.ghost.anchorMax = new Vector2(Mathf.Max(b.ghostShown, b.shown), 1f);

        float pulse = b.warn ? 0.5f + 0.5f * Mathf.Sin(Time.time * 7f) : 0f;
        b.fillImage.color = Color.Lerp(b.color, new Color(1f, 0.25f, 0.2f), pulse * 0.6f);
        b.icon.rectTransform.localScale = Vector3.one * (1f + 0.12f * pulse);
    }

    void RefreshBars()
    {
        if (stats == null) return;
        var s = stats;
        headerText.text = string.IsNullOrEmpty(s.PlayerName) ? $"{TowerMemory.RunCount}번째 깨어남" : s.PlayerName;

        poisonChip.SetActive(s.PoisonTurns > 0);
        poisonChipText.text = $"중독 {s.PoisonTurns}";
        torchChip.SetActive(s.TorchLit);

        SetBar(hp, s.Hp, s.maxHp, s.Hp < s.maxHp * 0.3f);
        SetBar(mp, s.Mp, s.maxMp, false);
        SetBar(hunger, s.Hunger, s.maxHunger, s.Hunger <= s.hungryWarnAt);
        SetBar(torch, s.Torch, s.maxTorch, s.TorchLit && s.Torch <= s.torchWarnAt);
        if (!s.HasTorch) torch.value.text = "없음";

        foodText.text = $"× {s.Food}";
        int keys = s.Bag.Count(Items.Key);
        goldText.text = keys > 0 ? $"골드 {s.Gold}     열쇠 {keys}" : $"골드 {s.Gold}";
    }

    static void SetBar(Bar b, int value, int max, bool warn)
    {
        float t = max > 0 ? Mathf.Clamp01((float)value / max) : 0f;
        if (t < b.shown) b.ghostHoldUntil = Time.time + 0.45f;   // 줄어든 만큼 잠깐 남겨 둔다
        else b.ghostShown = t;                                    // 늘어나면 잔상 없이 바로
        b.shown = t;
        b.fill.anchorMax = new Vector2(t, 1f);
        b.value.text = $"{value} / {max}";
        b.warn = warn;
    }

    void RefreshTurn(int turn) => turnText.text = $"턴  {turn}";

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

    // ---- UI 생성 ----

    void Build()
    {
        var root = UIKit.Canvas(transform, "HUDCanvas", 0);
        var topLeft = new Vector2(0, 1);

        // 좌상단 판
        var panel = UIKit.Image(root, "StatusPanel", new Color(0.03f, 0.025f, 0.02f, 0.62f));
        UIKit.Place(panel.rectTransform, topLeft, new Vector2(20, -18), new Vector2(470, 262));
        var pol = panel.gameObject.AddComponent<Outline>();
        pol.effectColor = new Color(0.55f, 0.45f, 0.3f, 0.45f);

        headerText = Shadowed(UIKit.Text(root, "", 24, TextAnchor.MiddleLeft, UIKit.Gold));
        UIKit.Place(headerText.rectTransform, topLeft, new Vector2(40, -30), new Vector2(220, 32));
        poisonChip = Chip(root, new Vector2(270, -33), new Color(0.25f, 0.4f, 0.1f, 0.9f), new Color(0.75f, 0.95f, 0.45f), out poisonChipText);
        torchChip = Chip(root, new Vector2(372, -33), new Color(0.45f, 0.24f, 0.06f, 0.9f), new Color(1f, 0.75f, 0.4f), out var torchChipText);
        torchChipText.text = "횃불 듦";

        float y = -72f;
        hp = MakeBar(root, "hp", "HP", new Color(0.78f, 0.2f, 0.17f), ref y);
        mp = MakeBar(root, "mp", "MP", new Color(0.26f, 0.45f, 0.78f), ref y);
        hunger = MakeBar(root, "food", "허기", new Color(0.62f, 0.66f, 0.27f), ref y);
        torch = MakeBar(root, "torch", "횃불", new Color(0.94f, 0.56f, 0.18f), ref y);

        // 식량·골드
        var foodIcon = UIKit.Image(root, "FoodIcon", Color.white);
        foodIcon.sprite = Resources.Load<Sprite>("Icons/food");
        foodIcon.preserveAspect = true;
        UIKit.Place(foodIcon.rectTransform, topLeft, new Vector2(42, y - 2), new Vector2(26, 26));
        foodText = Shadowed(UIKit.Text(root, "", 20, TextAnchor.MiddleLeft, GameLog.Info));
        UIKit.Place(foodText.rectTransform, topLeft, new Vector2(74, y), new Vector2(80, 30));
        goldText = Shadowed(UIKit.Text(root, "", 20, TextAnchor.MiddleLeft, UIKit.Gold));
        UIKit.Place(goldText.rectTransform, topLeft, new Vector2(160, y), new Vector2(160, 30));

        // 우상단 턴
        var turnBg = UIKit.Image(root, "TurnPanel", new Color(0.03f, 0.025f, 0.02f, 0.62f));
        UIKit.Place(turnBg.rectTransform, new Vector2(1, 1), new Vector2(-20, -18), new Vector2(150, 50));
        turnBg.gameObject.AddComponent<Outline>().effectColor = new Color(0.55f, 0.45f, 0.3f, 0.45f);
        turnText = Shadowed(UIKit.Text(turnBg.rectTransform, "", 26, TextAnchor.MiddleCenter, GameLog.Info));
        UIKit.Stretch(turnText.rectTransform);

        // 우하단 조작 안내
        var keys = Shadowed(UIKit.Text(root, "[I] 소지품    [F] 횃불    [E] 먹기    [Esc] 메뉴", 18, TextAnchor.LowerRight, new Color(1, 1, 1, 0.45f)));
        UIKit.Place(keys.rectTransform, new Vector2(1, 0), new Vector2(-28, 22), new Vector2(600, 26));

        // 좌하단 로그 (왼쪽이 진하고 오른쪽으로 사라지는 배경)
        const int lineH = 34;
        var logBg = UIKit.Image(root, "LogBg", Color.white);
        logBg.sprite = FadeRight();
        UIKit.Place(logBg.rectTransform, new Vector2(0, 0), new Vector2(0, 14), new Vector2(900, GameLog.MaxLines * lineH + 24));
        logLines = new Text[GameLog.MaxLines];
        for (int i = 0; i < logLines.Length; i++)
        {
            var t = Shadowed(UIKit.Text(root, "", 24, TextAnchor.LowerLeft, GameLog.Info));
            UIKit.Place(t.rectTransform, new Vector2(0, 0), new Vector2(36, 26 + (logLines.Length - 1 - i) * lineH), new Vector2(860, lineH));
            logLines[i] = t;
        }
    }

    Bar MakeBar(RectTransform root, string iconId, string label, Color color, ref float y)
    {
        var topLeft = new Vector2(0, 1);
        var b = new Bar { color = color };

        b.icon = UIKit.Image(root, iconId + "Icon", Color.white);
        b.icon.sprite = Resources.Load<Sprite>("Icons/" + iconId);
        b.icon.preserveAspect = true;
        UIKit.Place(b.icon.rectTransform, topLeft, new Vector2(38, y + 3), new Vector2(36, 36));
        // 크기 깜빡임이 가운데를 기준으로 되도록
        b.icon.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        b.icon.rectTransform.anchoredPosition += new Vector2(18, -18);

        var bg = UIKit.Image(root, label + "Bar", new Color(0, 0, 0, 0.6f));
        UIKit.Place(bg.rectTransform, topLeft, new Vector2(84, y - 4), new Vector2(380, 26));
        bg.gameObject.AddComponent<Outline>().effectColor = new Color(0.5f, 0.42f, 0.3f, 0.5f);

        var ghost = UIKit.Image(bg.rectTransform, "Ghost", new Color(0.95f, 0.78f, 0.55f, 0.45f));
        b.ghost = ghost.rectTransform;
        b.ghost.anchorMin = Vector2.zero; b.ghost.anchorMax = Vector2.one;
        b.ghost.offsetMin = new Vector2(2, 2); b.ghost.offsetMax = new Vector2(-2, -2);

        b.fillImage = UIKit.Image(bg.rectTransform, "Fill", color);
        b.fill = b.fillImage.rectTransform;
        b.fill.anchorMin = Vector2.zero; b.fill.anchorMax = Vector2.one;
        b.fill.offsetMin = new Vector2(2, 2); b.fill.offsetMax = new Vector2(-2, -2);

        var name = Shadowed(UIKit.Text(bg.rectTransform, label, 16, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.85f)));
        UIKit.Stretch(name.rectTransform);
        name.rectTransform.offsetMin = new Vector2(10, 0);

        b.value = Shadowed(UIKit.Text(bg.rectTransform, "", 17, TextAnchor.MiddleRight, Color.white));
        UIKit.Stretch(b.value.rectTransform);
        b.value.rectTransform.offsetMax = new Vector2(-10, 0);

        y -= 42f;
        return b;
    }

    // 작은 상태 표시 (중독, 횃불)
    GameObject Chip(RectTransform root, Vector2 pos, Color bgColor, Color textColor, out Text label)
    {
        var bg = UIKit.Image(root, "Chip", bgColor);
        UIKit.Place(bg.rectTransform, new Vector2(0, 1), pos, new Vector2(94, 26));
        label = UIKit.Text(bg.rectTransform, "", 16, TextAnchor.MiddleCenter, textColor);
        UIKit.Stretch(label.rectTransform);
        bg.gameObject.SetActive(false);
        return bg.gameObject;
    }

    static Text Shadowed(Text t)
    {
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.9f);
        return t;
    }

    // 왼쪽이 어둡고 오른쪽으로 갈수록 투명해지는 배경
    static Sprite FadeRight()
    {
        const int w = 256;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
            tex.SetPixel(x, 0, new Color(0, 0, 0, 0.55f * (1f - Mathf.SmoothStep(0.3f, 1f, x / (float)(w - 1)))));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f));
    }
}
