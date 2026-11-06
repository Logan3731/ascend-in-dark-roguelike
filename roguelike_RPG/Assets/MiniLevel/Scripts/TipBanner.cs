using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 화면 상단 가운데에 뜨는 팁 (방 안내·적 대처법 같은 튜토리얼 문구). 로그 대신 여기로 띄운다.
// 여러 개가 한꺼번에 오면 차례로 보여 준다. 글이 길수록 오래 떠 있고, 클릭하면 바로 다음 팁으로 넘어간다.
public class TipBanner : MonoBehaviour
{
    public const float MinSeconds = 4f, MaxSeconds = 10f;   // 떠 있는 시간 (글자 수에 따라 이 사이)
    public const float SecondsPerChar = 0.06f;
    const float Fade = 0.25f;
    const float Width = 860f;   // 좌상단 상태판·우상단 턴 표시와 겹치지 않게 (1920 기준 530~1390)

    static TipBanner instance;
    readonly Queue<string> queue = new Queue<string>();
    string showing;
    bool skip;

    CanvasGroup group;
    RectTransform panel;
    Text text;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => instance = null;

    public static void Show(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (instance == null) instance = new GameObject("TipBanner").AddComponent<TipBanner>();
        if (message == instance.showing || instance.queue.Contains(message)) return;   // 같은 팁은 한 번만
        instance.queue.Enqueue(message);
        if (instance.queue.Count == 1 && instance.showing == null) instance.StartCoroutine(instance.Run());
    }

    void Awake() => Build();

    IEnumerator Run()
    {
        while (queue.Count > 0)
        {
            showing = queue.Dequeue();
            text.text = showing;
            // 너비를 정한 뒤 높이를 글에 맞춘다
            panel.sizeDelta = new Vector2(Width, text.preferredHeight + 30);
            skip = false;
            group.blocksRaycasts = true;   // 떠 있는 동안만 클릭을 받는다 (안 보일 땐 그 자리 클릭이 바닥으로 가게)
            float hold = Mathf.Clamp(showing.Length * SecondsPerChar, MinSeconds, MaxSeconds);
            for (float t = 0f; t < 1f && !skip; t += Time.unscaledDeltaTime / Fade) { group.alpha = t; yield return null; }
            group.alpha = 1f;
            for (float t = 0f; t < hold && !skip; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Fade) { group.alpha = 1f - t; yield return null; }
            group.alpha = 0f;
            group.blocksRaycasts = false;
        }
        showing = null;
    }

    void Build()
    {
        var root = UIKit.Canvas(transform, "TipCanvas", 5);   // HUD(0) 위, 대화창·소지품 아래
        var bg = UIKit.Image(root, "Tip", new Color(0.05f, 0.04f, 0.03f, 0.88f), true);
        panel = bg.rectTransform;
        UIKit.Place(panel, new Vector2(0.5f, 1f), new Vector2(0, -22), new Vector2(Width, 60));
        var ol = bg.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0.86f, 0.72f, 0.42f, 0.6f);
        ol.effectDistance = new Vector2(2, -2);
        bg.gameObject.AddComponent<UIClickCatcher>().onClick = () => skip = true;   // 클릭: 다음 팁으로

        var label = UIKit.Text(panel, "팁", 18, TextAnchor.UpperLeft, UIKit.Gold);
        UIKit.Place(label.rectTransform, new Vector2(0, 1), new Vector2(16, -14), new Vector2(40, 24));

        text = UIKit.Text(panel, "", 21, TextAnchor.UpperLeft, new Color(0.95f, 0.91f, 0.82f));
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.lineSpacing = 1.1f;
        var tr = text.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(56, 14); tr.offsetMax = new Vector2(-20, -14);
        // preferredHeight를 재려면 너비가 정해져 있어야 한다
        panel.sizeDelta = new Vector2(Width, 60);

        group = bg.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        DontDestroyOnLoad(gameObject);   // 층을 올라도 남은 팁을 이어서 보여 준다
    }
}
