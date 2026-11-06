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
    GameObject poisonChip, torchChip, burnChip;
    Text poisonChipText, burnChipText;
    Text[] logLines;

    class Skill
    {
        public Button button;
        public Image bg;
        public Text label;
        public Image[] dots;
    }
    Skill triple, slam, magnifier, food;
    static readonly Color SkillReadyColor = new Color(0.55f, 0.32f, 0.08f, 0.95f);

    void Awake() => Build();

    void RefreshSkills(PlayerStats s)
    {
        SetSkill(triple, "[Q] 3연격", s.TripleCostNow, s.TripleCd, s.Mp >= s.TripleCostNow, s.TripleReady);
        SetSkill(slam, "[W] 점프 찍기", s.SlamCostNow, s.SlamCd, s.Mp >= s.SlamCostNow, false);
        // 돋보기: 기력·쿨타임 없음, 켜져 있으면 밝게
        magnifier.label.text = GridClickMover.Magnifying ? "<size=19>[S] 돋보기</size>\n<size=14><color=#dcb86b>켜짐</color></size>" : "<size=19>[S] 돋보기</size>\n<size=14><color=#9e9480>꺼짐</color></size>";
        magnifier.bg.color = GridClickMover.Magnifying ? SkillReadyColor : UIKit.ButtonColor;
        food.label.text = $"<size=19>[E] 식량</size>\n<size=14><color={(s.Food > 0 ? "#a8d46f" : "#e0704a")}>× {s.Food}</color></size>";
        food.bg.color = s.Food > 0 ? UIKit.ButtonColor : new Color(0.08f, 0.07f, 0.06f, 0.85f);
    }

    static void SetSkill(Skill k, string name, int cost, int cd, bool enoughMp, bool ready)
    {
        bool usable = cd == 0 && enoughMp;
        k.label.text = $"<size=19>{name}</size>\n<size=14><color={(enoughMp ? "#9ed0ff" : "#e0704a")}>기력 {cost}</color></size>";
        k.bg.color = ready ? SkillReadyColor : usable ? UIKit.ButtonColor : new Color(0.08f, 0.07f, 0.06f, 0.85f);
        k.label.color = usable || ready ? new Color(0.92f, 0.88f, 0.8f) : new Color(0.5f, 0.47f, 0.42f);
        int n = PlayerStats.Dots(cd);
        for (int i = 0; i < k.dots.Length; i++) k.dots[i].enabled = i < n;
    }

    // right: 버튼 오른쪽 끝의 x (화면 오른쪽 기준). 버튼 아래에 쿨타임 점 자리를 만든다.
    Skill SkillButton(RectTransform root, float right, int cooldown, System.Action onClick)
    {
        const float w = 160, h = 58;
        var k = new Skill();
        k.button = UIKit.Button(root, "", 19, () => onClick());
        k.bg = k.button.GetComponent<Image>();
        k.label = k.button.GetComponentInChildren<Text>();
        k.label.lineSpacing = 0.9f;
        UIKit.Place((RectTransform)k.button.transform, new Vector2(1, 0), new Vector2(right, 76), new Vector2(w, h));

        int max = PlayerStats.Dots(cooldown + 1);   // 쓴 직후 잠깐 하나 더 많을 수 있다
        k.dots = new Image[max];
        const float gap = 16;
        for (int i = 0; i < max; i++)
        {
            var d = UIKit.Image(root, "CooldownDot", new Color(0.95f, 0.75f, 0.35f, 0.95f));
            float x = right - w / 2 + (i - (max - 1) / 2f) * gap;   // 버튼 가운데 아래에 나란히
            UIKit.Place(d.rectTransform, new Vector2(1, 0), Vector2.zero, new Vector2(10, 10));
            d.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            d.rectTransform.anchoredPosition = new Vector2(x, 62);
            d.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            d.enabled = false;
            k.dots[i] = d;
        }
        return k;
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
        gameObject.AddComponent<QuickBar>().player = stats;   // 화면 아래 단축키 칸 1~4
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
        string who = string.IsNullOrEmpty(s.PlayerName) ? $"{TowerMemory.RunCount}번째" : s.PlayerName;
        headerText.text = $"Lv.{s.Level}  <size=16><color=#dcb86b>{RunState.Floor}층</color><color=#9e9480> · {who} · 경험 {s.Exp}/{s.NextLevelExp}</color></size>";

        poisonChip.SetActive(s.PoisonTurns > 0);
        poisonChipText.text = $"중독 {s.PoisonTurns}";
        torchChip.SetActive(s.TorchLit);
        int burn = Fire.BurnTurnsOf(s);
        burnChip.SetActive(burn > 0);
        burnChipText.text = $"화상 {burn}";
        // 켜진 칩만 왼쪽부터 나란히
        float cx = 270;
        foreach (var chip in new[] { burnChip, poisonChip, torchChip })
            if (chip.activeSelf) { ((RectTransform)chip.transform).anchoredPosition = new Vector2(cx, -33); cx += 102; }

        SetBar(hp, s.Hp, s.maxHp, s.Hp < s.maxHp * 0.3f);
        SetBar(mp, s.Mp, s.maxMp, s.TripleReady);
        if (s.TripleReady) mp.value.text = "3연격 준비!";
        RefreshSkills(s);
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
        burnChip = Chip(root, new Vector2(270, -33), new Color(0.55f, 0.15f, 0.05f, 0.9f), new Color(1f, 0.7f, 0.4f), out burnChipText);

        float y = -72f;
        hp = MakeBar(root, "hp", "HP", new Color(0.78f, 0.2f, 0.17f), ref y);
        mp = MakeBar(root, "mp", "기력", new Color(0.26f, 0.45f, 0.78f), ref y);
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
        var keys = Shadowed(UIKit.Text(root, "[I] 소지품    [1~4] 단축키    [Space] 확인    [Q] 3연격    [W] 점프 찍기    [S] 돋보기    [R] 다트    [F] 횃불    [T] 횃불 던지기    [E] 먹기    [Esc] 메뉴", 18, TextAnchor.LowerRight, new Color(1, 1, 1, 0.45f)));
        UIKit.Place(keys.rectTransform, new Vector2(1, 0), new Vector2(-28, 22), new Vector2(900, 26));

        // 우하단 스킬 버튼 두 개 (클릭해도 된다) + 버튼 아래 쿨타임 점 (점 하나 = 2턴)
        triple = SkillButton(root, -208, PlayerStats.TripleCooldown, GridClickMover.UseTriple);
        slam = SkillButton(root, -28, PlayerStats.SlamCooldown, GridClickMover.UseSlam);
        magnifier = SkillButton(root, -388, 0, GridClickMover.UseMagnifier);
        food = SkillButton(root, -568, 0, GridClickMover.UseFood);   // 쿨타임 없음 (점 자리만 빈다)

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
