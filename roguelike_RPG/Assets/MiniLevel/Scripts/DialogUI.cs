using System;
using UnityEngine;
using UnityEngine.UI;

// 화면 아래쪽 대화창: 질문 + 선택지 버튼, 또는 질문 + 글자 입력. 열려 있는 동안 플레이어는 움직이지 않는다.
public class DialogUI : MonoBehaviour
{
    public struct Choice
    {
        public string label;
        public bool enabled;
        public Action onPick;
        public Choice(string label, Action onPick, bool enabled = true) { this.label = label; this.onPick = onPick; this.enabled = enabled; }
    }

    public static bool IsOpen => instance != null && instance.panel.activeSelf;

    static DialogUI instance;

    Font font;
    GameObject panel;
    Text promptText;
    RectTransform buttonRow;
    InputField input;

    void Awake()
    {
        instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
        panel.SetActive(false);
    }

    public static void Show(string prompt, params Choice[] choices)
    {
        instance.Open(prompt);
        foreach (var c in choices)
        {
            var pick = c.onPick;
            instance.MakeButton(c.label, c.enabled, () => { instance.Close(); pick?.Invoke(); });
        }
    }

    public static void AskText(string prompt, string placeholder, Action<string> onSubmit)
    {
        instance.Open(prompt);
        var inp = instance.input;
        inp.gameObject.SetActive(true);
        inp.text = "";
        ((Text)inp.placeholder).text = placeholder;
        inp.ActivateInputField();
        void Submit()
        {
            string value = inp.text.Trim();
            instance.Close();
            onSubmit?.Invoke(value);
        }
        inp.onSubmit.RemoveAllListeners();
        inp.onSubmit.AddListener(_ => Submit());
        instance.buttonRow.anchoredPosition = new Vector2(200, 26);   // 입력칸 오른쪽에 확인 버튼
        instance.MakeButton("확인", true, Submit);
    }

    void Open(string prompt)
    {
        foreach (Transform b in buttonRow) Destroy(b.gameObject);
        input.gameObject.SetActive(false);
        buttonRow.anchoredPosition = new Vector2(0, 26);
        promptText.text = prompt;
        panel.SetActive(true);
    }

    void Close()
    {
        input.onSubmit.RemoveAllListeners();
        panel.SetActive(false);
    }

    // ---- UI 생성 ----

    void Build()
    {
        var canvasGo = new GameObject("DialogCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var bg = new GameObject("Panel", typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(canvasGo.transform, false);
        bg.color = new Color(0.05f, 0.045f, 0.04f, 0.92f);
        var pr = bg.rectTransform;
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0f);
        pr.anchoredPosition = new Vector2(0, 60);
        pr.sizeDelta = new Vector2(900, 210);
        panel = bg.gameObject;

        var outline = bg.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.45f, 0.3f, 0.8f);
        outline.effectDistance = new Vector2(2, -2);

        promptText = MakeText(pr, "Prompt", 30, TextAnchor.MiddleCenter);
        var tr = promptText.rectTransform;
        tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1);
        tr.anchoredPosition = new Vector2(0, -24);
        tr.sizeDelta = new Vector2(-60, 80);

        // 이름 입력칸
        var field = new GameObject("Input", typeof(Image), typeof(InputField)).GetComponent<InputField>();
        field.transform.SetParent(pr, false);
        field.GetComponent<Image>().color = new Color(0, 0, 0, 0.6f);
        var fr = (RectTransform)field.transform;
        fr.anchorMin = fr.anchorMax = fr.pivot = new Vector2(0.5f, 0);
        fr.anchoredPosition = new Vector2(-110, 26);
        fr.sizeDelta = new Vector2(380, 56);
        var ft = MakeText(fr, "Text", 28, TextAnchor.MiddleLeft);
        ft.supportRichText = false;
        Stretch(ft.rectTransform, 16);
        var ph = MakeText(fr, "Placeholder", 28, TextAnchor.MiddleLeft);
        ph.color = new Color(1, 1, 1, 0.35f);
        ph.fontStyle = FontStyle.Italic;
        Stretch(ph.rectTransform, 16);
        field.textComponent = ft;
        field.placeholder = ph;
        field.characterLimit = 12;
        input = field;

        buttonRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
        buttonRow.SetParent(pr, false);
        buttonRow.anchorMin = new Vector2(0, 0); buttonRow.anchorMax = new Vector2(1, 0); buttonRow.pivot = new Vector2(0.5f, 0);
        buttonRow.anchoredPosition = new Vector2(0, 26);
        buttonRow.sizeDelta = new Vector2(-60, 60);
        var layout = buttonRow.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 24;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
    }

    void MakeButton(string label, bool enabled, Action onClick)
    {
        var img = new GameObject("Choice", typeof(Image), typeof(Button), typeof(LayoutElement)).GetComponent<Image>();
        img.transform.SetParent(buttonRow, false);
        img.color = new Color(0.22f, 0.18f, 0.14f, 1f);
        var le = img.GetComponent<LayoutElement>();
        le.preferredWidth = Mathf.Max(200, label.Length * 30 + 60);
        le.preferredHeight = 56;

        var btn = img.GetComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.6f, 1.4f, 1.1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        btn.colors = colors;
        btn.interactable = enabled;
        btn.onClick.AddListener(() => onClick());

        var t = MakeText((RectTransform)img.transform, "Label", 26, TextAnchor.MiddleCenter);
        t.text = label;
        if (!enabled) t.color = new Color(0.6f, 0.58f, 0.55f);
        Stretch(t.rectTransform, 0);
    }

    static void Stretch(RectTransform r, float pad)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(pad, 0); r.offsetMax = new Vector2(-pad, 0);
    }

    Text MakeText(RectTransform parent, string name, int size, TextAnchor anchor)
    {
        var t = new GameObject(name, typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = GameLog.Info;
        t.raycastTarget = false;
        return t;
    }
}
