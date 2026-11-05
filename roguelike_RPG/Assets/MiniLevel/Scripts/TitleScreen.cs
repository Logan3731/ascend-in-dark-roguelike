using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 게임을 켰을 때 한 번 뜨는 타이틀 화면. 대표 키아트 위에 제목과 [깨어나기 / 기록 지우기 / 종료].
// 죽고 다시 깨어날 때(씬을 다시 열 때)는 뜨지 않는다. 일시정지 메뉴의 "처음 화면으로"를 누르면 다시 뜬다.
public class TitleScreen : MonoBehaviour
{
    public string gameTitle = "Ascend in Dark";       // 가제 (제목 미정)
    public string tagline = "모든 걸 잃어도, 경험은 남는다";
    public IntroSequence intro;

    static bool shownThisSession;
    public static bool ShouldShow => !shownThisSession;
    public static bool IsOpen => instance != null && instance.root != null && instance.root.gameObject.activeSelf;
    static TitleScreen instance;

    RectTransform root;
    CanvasGroup group;
    Text recordText;
    GameObject confirmReset;

    // 일시정지 메뉴에서 "처음 화면으로"
    public static void ReturnToTitle()
    {
        shownThisSession = false;
        Time.timeScale = 1f;
        RunState.NewRun();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => shownThisSession = false;

    void Awake()
    {
        instance = this;
        if (!ShouldShow) return;
        Build();
        RefreshRecord();
    }

    void RefreshRecord()
    {
        recordText.text = TowerMemory.RunCount > 0
            ? $"지금까지 {TowerMemory.RunCount}번 깨어났다   ·   <color=#9ed0ff>경험의 정수 {TowerMemory.Essence}</color>"
            : "";
    }

    void WakeUp()
    {
        shownThisSession = true;
        StartCoroutine(FadeAndStart());
    }

    IEnumerator FadeAndStart()
    {
        group.interactable = false;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.9f)
        {
            group.alpha = 1f - t;
            yield return null;
        }
        root.gameObject.SetActive(false);
        if (intro != null) intro.StartRun();
    }

    void AskReset() => confirmReset.SetActive(true);

    void DoReset()
    {
        TowerMemory.ResetAll();
        confirmReset.SetActive(false);
        RefreshRecord();
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- UI 생성 ----

    void Build()
    {
        root = UIKit.Canvas(transform, "TitleCanvas", 30);
        group = root.gameObject.AddComponent<CanvasGroup>();

        // 배경: 대표 키아트 (화면을 꽉 채움)
        var bg = UIKit.Image(root, "Background", Color.white, true);
        var sprite = Resources.Load<Sprite>("UI/title_bg");
        bg.sprite = sprite;
        UIKit.Stretch(bg.rectTransform);
        if (sprite != null)
        {
            var fit = bg.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = sprite.rect.width / sprite.rect.height;
        }
        // 왼쪽을 어둡게 해서 글자가 잘 보이게 (오른쪽으로 갈수록 부드럽게 사라짐)
        var shade = UIKit.Image(root, "Shade", Color.white);
        shade.sprite = LeftShade();
        UIKit.Place(shade.rectTransform, new Vector2(0, 0.5f), Vector2.zero, new Vector2(1200, 1080));

        var title = UIKit.Text(root, gameTitle, 104, TextAnchor.UpperLeft, new Color(0.93f, 0.82f, 0.6f));
        title.fontStyle = FontStyle.Bold;
        var tol = title.gameObject.AddComponent<Outline>();
        tol.effectColor = new Color(0, 0, 0, 0.9f); tol.effectDistance = new Vector2(3, -3);
        UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(110, -110), new Vector2(900, 130));

        var tag = UIKit.Text(root, tagline, 30, TextAnchor.UpperLeft, new Color(0.85f, 0.8f, 0.72f));
        tag.fontStyle = FontStyle.Italic;
        UIKit.Place(tag.rectTransform, new Vector2(0, 1), new Vector2(116, -250), new Vector2(800, 44));

        var line = UIKit.Image(root, "Line", new Color(0.86f, 0.72f, 0.42f, 0.6f));
        UIKit.Place(line.rectTransform, new Vector2(0, 1), new Vector2(116, -310), new Vector2(420, 2));

        // 메뉴
        string[] labels = { "깨어나기", "기록 지우기", "종료" };
        System.Action[] actions = { WakeUp, AskReset, Quit };
        for (int i = 0; i < labels.Length; i++)
        {
            var b = UIKit.Button(root, labels[i], 30, actions[i]);
            UIKit.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(116, -400 - i * 86), new Vector2(340, 66));
        }

        recordText = UIKit.Text(root, "", 22, TextAnchor.UpperLeft, UIKit.Dim);
        UIKit.Place(recordText.rectTransform, new Vector2(0, 1), new Vector2(118, -680), new Vector2(700, 30));

        var foot = UIKit.Text(root, "가제 · 프로토타입", 18, TextAnchor.LowerLeft, new Color(0.6f, 0.56f, 0.5f, 0.8f));
        UIKit.Place(foot.rectTransform, new Vector2(0, 0), new Vector2(118, 40), new Vector2(400, 26));

        BuildResetConfirm();
    }

    // 왼쪽은 진하고 오른쪽 끝은 투명한 검은 그라데이션
    static Sprite LeftShade()
    {
        const int w = 256;
        var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)(w - 1);
            tex.SetPixel(x, 0, new Color(0, 0, 0, 0.7f * (1f - Mathf.SmoothStep(0.35f, 1f, t))));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f));
    }

    void BuildResetConfirm()
    {
        var holder = new GameObject("ConfirmReset", typeof(RectTransform));
        holder.transform.SetParent(root, false);
        var hr = (RectTransform)holder.transform;
        UIKit.Stretch(hr);
        var block = UIKit.Image(hr, "Blocker", new Color(0, 0, 0, 0.6f), true);
        UIKit.Stretch(block.rectTransform);
        var panel = UIKit.Image(hr, "Panel", new Color(0.07f, 0.06f, 0.05f, 0.98f), true);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 260));
        panel.gameObject.AddComponent<Outline>().effectColor = UIKit.Gold;
        var msg = UIKit.Text(panel.rectTransform,
            "<size=30><color=#dcb86b>기록을 모두 지울까?</color></size>\n\n경험의 정수, 강화, 남겨진 뼈가 모두 사라진다.",
            22, TextAnchor.UpperCenter, new Color(0.9f, 0.86f, 0.78f));
        UIKit.Place(msg.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(560, 120));
        var yes = UIKit.Button(panel.rectTransform, "지운다", 24, DoReset);
        UIKit.Place((RectTransform)yes.transform, new Vector2(0.5f, 0), new Vector2(-110, 30), new Vector2(180, 56));
        var no = UIKit.Button(panel.rectTransform, "취소", 24, () => confirmReset.SetActive(false));
        UIKit.Place((RectTransform)no.transform, new Vector2(0.5f, 0), new Vector2(110, 30), new Vector2(180, 56));
        confirmReset = holder;
        confirmReset.SetActive(false);
    }
}
