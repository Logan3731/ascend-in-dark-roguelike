using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// [Esc] 일시정지: 계속하기 / 조작법 / 처음 화면으로 / 게임 종료. 열려 있는 동안 시간이 멈춘다.
// 소지품·대화창이 열려 있으면 Esc는 그쪽을 먼저 닫는다.
public class PauseMenu : MonoBehaviour
{
    public PlayerStats player;

    public static bool IsOpen => instance != null && instance.root.gameObject.activeSelf;
    static PauseMenu instance;

    RectTransform root;
    GameObject menu, controls, confirmTitle;

    void Awake()
    {
        instance = this;
        Build();
        root.gameObject.SetActive(false);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
        if (IsOpen)
        {
            // 하위 화면이 떠 있으면 그것부터 닫는다
            if (controls.activeSelf || confirmTitle.activeSelf) ShowMenu();
            else Close();
            return;
        }
        if (TitleScreen.IsOpen || DialogUI.IsOpen || InventoryUI.IsOpen || InventoryUI.EscFrame == Time.frameCount || CraftUI.IsOpen || CraftUI.EscFrame == Time.frameCount || ShopUI.IsOpen || ShopUI.EscFrame == Time.frameCount || Thrower.Aiming != null || Thrower.EscFrame == Time.frameCount) return;
        if (player != null && player.Dead) return;
        Open();
    }

    public void Open()
    {
        root.gameObject.SetActive(true);
        ShowMenu();
        Time.timeScale = 0f;
    }

    public void Close()
    {
        root.gameObject.SetActive(false);
        Time.timeScale = 1f;
    }

    void OnDisable() { if (IsOpen) Time.timeScale = 1f; }

    void ShowMenu()
    {
        menu.SetActive(true);
        controls.SetActive(false);
        confirmTitle.SetActive(false);
    }

    void ShowControls()
    {
        menu.SetActive(false);
        controls.SetActive(true);
    }

    void AskTitle()
    {
        menu.SetActive(false);
        confirmTitle.SetActive(true);
    }

    void Quit()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- UI 생성 ----

    void Build()
    {
        root = UIKit.Canvas(transform, "PauseCanvas", 25);
        var dim = UIKit.Image(root, "Dim", new Color(0, 0, 0, 0.65f), true);
        UIKit.Stretch(dim.rectTransform);

        menu = Panel("Menu", new Vector2(460, 470));
        var mr = (RectTransform)menu.transform;
        var title = UIKit.Text(mr, "일시정지", 40, TextAnchor.UpperCenter, UIKit.Gold);
        UIKit.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(400, 56));
        string[] labels = { "계속하기", "조작법", "처음 화면으로", "게임 종료" };
        System.Action[] actions = { Close, ShowControls, AskTitle, Quit };
        for (int i = 0; i < labels.Length; i++)
        {
            var b = UIKit.Button(mr, labels[i], 26, actions[i]);
            UIKit.Place((RectTransform)b.transform, new Vector2(0.5f, 1), new Vector2(0, -120 - i * 84), new Vector2(320, 62));
        }

        controls = Panel("Controls", new Vector2(640, 630));
        var cr = (RectTransform)controls.transform;
        var ct = UIKit.Text(cr, "조작법", 36, TextAnchor.UpperCenter, UIKit.Gold);
        UIKit.Place(ct.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(560, 50));
        // 키 / 설명 두 칸으로 맞춘다
        (string key, string desc)[] rows =
        {
            ("마우스 왼쪽", "바닥: 그 칸으로 이동 (한 칸 = 한 턴)"),
            ("", "몬스터: 다가가서 공격"),
            ("", "상자·도구·뼈: 다가가서 살펴보기"),
            ("I / Tab", "소지품·상태창"),
            ("Q", "강타 준비 (다음 공격 두 배, 기력 소모)"),
            ("F", "횃불 들기 / 내리기"),
            ("T", "든 횃불 던지기 (떨어진 곳이 빛 구역)"),
            ("E", "식량 먹기"),
            ("Esc", "일시정지"),
        };
        for (int i = 0; i < rows.Length; i++)
        {
            float y = -110 - i * 38 - (i >= 3 ? 16 : 0);
            var k = UIKit.Text(cr, rows[i].key, 22, TextAnchor.UpperLeft, UIKit.Gold);
            UIKit.Place(k.rectTransform, new Vector2(0, 1), new Vector2(60, y), new Vector2(150, 34));
            var d = UIKit.Text(cr, rows[i].desc, 22, TextAnchor.UpperLeft, new Color(0.9f, 0.86f, 0.78f));
            UIKit.Place(d.rectTransform, new Vector2(0, 1), new Vector2(220, y), new Vector2(380, 34));
        }
        var back = UIKit.Button(cr, "돌아가기", 24, ShowMenu);
        UIKit.Place((RectTransform)back.transform, new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(240, 58));

        confirmTitle = Panel("ConfirmTitle", new Vector2(620, 280));
        var tr = (RectTransform)confirmTitle.transform;
        var msg = UIKit.Text(tr,
            "<size=30><color=#dcb86b>처음 화면으로 돌아갈까?</color></size>\n\n이번 생의 진행은 사라진다.\n(경험의 정수와 강화는 남는다)",
            22, TextAnchor.UpperCenter, new Color(0.9f, 0.86f, 0.78f));
        UIKit.Place(msg.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(560, 140));
        var yes = UIKit.Button(tr, "돌아간다", 24, TitleScreen.ReturnToTitle);
        UIKit.Place((RectTransform)yes.transform, new Vector2(0.5f, 0), new Vector2(-110, 30), new Vector2(180, 56));
        var no = UIKit.Button(tr, "취소", 24, ShowMenu);
        UIKit.Place((RectTransform)no.transform, new Vector2(0.5f, 0), new Vector2(110, 30), new Vector2(180, 56));
    }

    GameObject Panel(string name, Vector2 size)
    {
        var img = UIKit.Image(root, name, new Color(0.06f, 0.05f, 0.045f, 0.97f), true);
        UIKit.Place(img.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, size);
        var ol = img.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0.55f, 0.45f, 0.3f, 0.8f); ol.effectDistance = new Vector2(2, -2);
        return img.gameObject;
    }
}
