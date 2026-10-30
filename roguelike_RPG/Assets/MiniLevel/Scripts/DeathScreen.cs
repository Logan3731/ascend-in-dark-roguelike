using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 사망 화면: 이번 생의 경험이 경험의 정수가 되고, 정수로 강화를 산 뒤 1층에서 다시 깨어난다.
// [Shift+F12] 저장된 기록 전체 초기화 (테스트용)
public class DeathScreen : MonoBehaviour
{
    public PlayerStats player;

    Font font;
    CanvasGroup group;
    Image black;
    Text essenceText, gainText;
    RectTransform list;
    readonly Button[] buyButtons = new Button[TowerMemory.Upgrades.Length];
    readonly Text[] rowTexts = new Text[TowerMemory.Upgrades.Length];

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
        group.gameObject.SetActive(false);
    }

    void OnEnable() { if (player != null) player.OnDied += HandleDeath; }
    void OnDisable() { if (player != null) player.OnDied -= HandleDeath; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.shiftKey.isPressed && kb.f12Key.wasPressedThisFrame)
        {
            TowerMemory.ResetAll();
            Debug.Log("[TowerMemory] 기록 초기화");
            SceneManager.LoadScene(gameObject.scene.name);
        }
    }

    void HandleDeath()
    {
        TowerMemory.RecordDeath(player.Exp, GridTile.WorldToCell(player.transform.position), player.Gold);
        StartCoroutine(Show());
    }

    IEnumerator Show()
    {
        yield return new WaitForSeconds(1.2f);
        gainText.text = player.Exp > 0
            ? $"이번 생에서 쌓은 경험이 탑에 스며든다…\n경험의 정수 +{player.Exp}"
            : "아무것도 남기지 못했다…";
        Refresh();
        group.gameObject.SetActive(true);
        group.alpha = 0f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1.2f)
        {
            group.alpha = t;
            yield return null;
        }
        group.alpha = 1f;
    }

    void Refresh()
    {
        essenceText.text = $"경험의 정수  {TowerMemory.Essence}";
        for (int i = 0; i < TowerMemory.Upgrades.Length; i++)
        {
            var u = (TowerMemory.Upgrade)i;
            var info = TowerMemory.Upgrades[i];
            int lv = TowerMemory.Level(u);
            rowTexts[i].text = $"{info.name}   <color=#b9b09a>{info.effect}</color>   <color=#8c8473>Lv {lv}/{info.maxLevel}</color>";
            bool maxed = TowerMemory.IsMaxed(u);
            int cost = TowerMemory.Cost(u);
            buyButtons[i].interactable = !maxed && TowerMemory.Essence >= cost;
            buyButtons[i].GetComponentInChildren<Text>().text = maxed ? "최대" : $"강화  {cost}";
        }
    }

    void Buy(int i)
    {
        if (TowerMemory.TryBuy((TowerMemory.Upgrade)i)) Refresh();
    }

    void WakeUp() => SceneManager.LoadScene(gameObject.scene.name);

    // ---- UI 생성 ----

    void Build()
    {
        var canvasGo = new GameObject("DeathCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        group = canvasGo.GetComponent<CanvasGroup>();
        var root = (RectTransform)canvasGo.transform;

        black = new GameObject("Black", typeof(Image)).GetComponent<Image>();
        black.transform.SetParent(root, false);
        black.color = new Color(0, 0, 0, 0.92f);
        Stretch(black.rectTransform);

        var title = MakeText(root, "Title", 64, TextAnchor.MiddleCenter, new Color(0.8f, 0.2f, 0.16f));
        title.text = "당신은 쓰러졌다";
        Place(title.rectTransform, 0, -150, 1000, 90);

        gainText = MakeText(root, "Gain", 28, TextAnchor.MiddleCenter, GameLog.Info);
        Place(gainText.rectTransform, 0, -250, 1000, 90);

        essenceText = MakeText(root, "Essence", 34, TextAnchor.MiddleCenter, new Color(0.62f, 0.82f, 1f));
        Place(essenceText.rectTransform, 0, -335, 1000, 50);

        list = new GameObject("Upgrades", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
        list.SetParent(root, false);
        Place(list, 0, -395, 900, 5 * 64);
        var vl = list.GetComponent<VerticalLayoutGroup>();
        vl.spacing = 8; vl.childControlHeight = vl.childControlWidth = true; vl.childForceExpandHeight = false;

        for (int i = 0; i < TowerMemory.Upgrades.Length; i++)
        {
            var row = new GameObject("Row" + i, typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            row.transform.SetParent(list, false);
            row.color = new Color(1, 1, 1, 0.05f);
            row.GetComponent<LayoutElement>().preferredHeight = 56;
            var rr = row.rectTransform;

            var label = MakeText(rr, "Label", 26, TextAnchor.MiddleLeft, GameLog.Info);
            label.supportRichText = true;
            var lr = label.rectTransform;
            lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(1, 1);
            lr.offsetMin = new Vector2(24, 0); lr.offsetMax = new Vector2(-200, 0);
            rowTexts[i] = label;

            int idx = i;
            buyButtons[i] = MakeButton(rr, "강화", () => Buy(idx));
            var br = (RectTransform)buyButtons[i].transform;
            br.anchorMin = br.anchorMax = br.pivot = new Vector2(1, 0.5f);
            br.anchoredPosition = new Vector2(-10, 0);
            br.sizeDelta = new Vector2(170, 44);
        }

        var wake = MakeButton(root, "다시 깨어나기", WakeUp);
        var wr = (RectTransform)wake.transform;
        wr.anchorMin = wr.anchorMax = wr.pivot = new Vector2(0.5f, 0f);
        wr.anchoredPosition = new Vector2(0, 90);
        wr.sizeDelta = new Vector2(360, 70);
        wake.GetComponentInChildren<Text>().fontSize = 32;
    }

    Button MakeButton(RectTransform parent, string label, System.Action onClick)
    {
        var img = new GameObject("Button", typeof(Image), typeof(Button)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = new Color(0.22f, 0.18f, 0.14f, 1f);
        var btn = img.GetComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.6f, 1.4f, 1.1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());
        var t = MakeText((RectTransform)img.transform, "Label", 24, TextAnchor.MiddleCenter, GameLog.Info);
        t.text = label;
        Stretch(t.rectTransform);
        return btn;
    }

    Text MakeText(RectTransform parent, string name, int size, TextAnchor anchor, Color color)
    {
        var t = new GameObject(name, typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size; t.alignment = anchor; t.color = color;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }
}
