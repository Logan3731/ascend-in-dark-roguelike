using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 상점 창 (고블린 상인). 물건마다 그림·이름·가격·남은 수량과 [사기] 버튼. 골드가 모자라거나 다 팔리면 버튼이 꺼진다.
public class ShopUI : MonoBehaviour
{
    public class Ware
    {
        public string icon;      // Resources/Icons 안의 그림 이름
        public string label;
        public int price, stock;
        public System.Action<PlayerStats> give;
    }

    static ShopUI instance;
    public static bool IsOpen => instance != null && instance.root != null && instance.root.activeSelf;
    public static int EscFrame { get; private set; } = -1;

    PlayerStats player;
    Ware[] wares;
    GameObject root;
    RectTransform panel;
    Text goldText;
    readonly List<GameObject> rows = new List<GameObject>();

    public static void Open(PlayerStats player, Ware[] wares)
    {
        if (instance == null) instance = new GameObject("ShopUI").AddComponent<ShopUI>();
        instance.player = player;
        instance.wares = wares;
        instance.Build();
        instance.root.SetActive(true);
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (IsOpen && kb != null && kb.escapeKey.wasPressedThisFrame) { EscFrame = Time.frameCount; Close(); }
    }

    void Close() => root.SetActive(false);

    void Buy(Ware w)
    {
        if (w.stock <= 0 || player.Gold < w.price) return;
        player.AddGold(-w.price);
        w.stock--;
        w.give(player);
        GameLog.Add($"고블린에게서 {w.label}{Korean.Josa(w.label, "을", "를")} 샀다.", GameLog.Good);
        Build();   // 수량·골드 다시 그리기
    }

    void Build()
    {
        if (root == null)
        {
            var canvas = UIKit.Canvas(transform, "ShopCanvas", 18);
            root = canvas.gameObject;
            var dim = UIKit.Image(canvas, "Dim", new Color(0, 0, 0, 0.6f), true);
            UIKit.Stretch(dim.rectTransform);
            dim.gameObject.AddComponent<UIClickCatcher>().onClick = Close;
            var p = UIKit.Image(canvas, "Panel", new Color(0.06f, 0.05f, 0.04f, 0.97f), true);
            panel = p.rectTransform;
            p.gameObject.AddComponent<Outline>().effectColor = new Color(0.55f, 0.45f, 0.3f, 0.8f);
            var title = UIKit.Text(panel, "고블린의 보따리", 32, TextAnchor.UpperLeft, UIKit.Gold);
            UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(32, -22), new Vector2(400, 44));
            goldText = UIKit.Text(panel, "", 22, TextAnchor.UpperRight, UIKit.Gold);
            UIKit.Place(goldText.rectTransform, new Vector2(1, 1), new Vector2(-32, -30), new Vector2(300, 30));
            var hint = UIKit.Text(panel, "\"골라 봐. 깎아 주진 않아.\"   [Esc] 닫기", 17, TextAnchor.LowerLeft, UIKit.Dim);
            UIKit.Place(hint.rectTransform, new Vector2(0, 0), new Vector2(32, 16), new Vector2(600, 26));
        }
        foreach (var r in rows) Destroy(r);
        rows.Clear();
        UIKit.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 140 + wares.Length * 96));
        goldText.text = $"가진 골드 {player.Gold}";

        for (int i = 0; i < wares.Length; i++)
        {
            var w = wares[i];
            var box = UIKit.Image(panel, "Row", new Color(1, 1, 1, 0.04f));
            box.rectTransform.anchorMin = box.rectTransform.anchorMax = box.rectTransform.pivot = new Vector2(0.5f, 1);
            box.rectTransform.anchoredPosition = new Vector2(0, -84 - i * 96);
            box.rectTransform.sizeDelta = new Vector2(660, 84);
            rows.Add(box.gameObject);

            var icon = UIKit.Image(box.rectTransform, "Icon", Color.white);
            icon.sprite = Resources.Load<Sprite>("Icons/" + w.icon);
            UIKit.Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(64, 64));

            var name = UIKit.Text(box.rectTransform, w.label, 24, TextAnchor.MiddleLeft, new Color(0.95f, 0.9f, 0.8f));
            UIKit.Place(name.rectTransform, new Vector2(0, 0.5f), new Vector2(92, 0), new Vector2(260, 40));
            bool soldOut = w.stock <= 0;
            var info = UIKit.Text(box.rectTransform, soldOut ? "다 팔림" : $"<color=#dcb86b>{w.price} 골드</color>   남은 {w.stock}", 20, TextAnchor.MiddleLeft, UIKit.Dim);
            UIKit.Place(info.rectTransform, new Vector2(0, 0.5f), new Vector2(330, 0), new Vector2(180, 40));

            var ware = w;
            var btn = UIKit.Button(box.rectTransform, "사기", 22, () => Buy(ware));
            UIKit.Place((RectTransform)btn.transform, new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(120, 52));
            btn.interactable = !soldOut && player.Gold >= w.price;
        }
    }
}
