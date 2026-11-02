using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 시작 방의 모루 = 대장간. 다가가서 클릭하면 제작 창이 열린다.
// 미로에서 주운 나뭇가지·돌멩이로 횃불과 무기를 만든다.
[RequireComponent(typeof(GridBlocker))]
public class Anvil : Interactable
{
    public class Recipe
    {
        public ItemDef result;
        public (ItemDef item, int count)[] cost;
    }

    public static readonly Recipe[] Recipes =
    {
        new Recipe { result = Items.Torch, cost = new[] { (Items.Branch, 2) } },
        new Recipe { result = Items.Club, cost = new[] { (Items.Branch, 3) } },
        new Recipe { result = Items.StoneAxe, cost = new[] { (Items.Branch, 2), (Items.Stone, 3) } },
    };

    public override void Interact(PlayerStats player) => CraftUI.Open(player);
}

// 제작 창 (코드로 만든다). 재료가 모자란 항목은 버튼이 꺼진다.
public class CraftUI : MonoBehaviour
{
    static CraftUI instance;
    public static bool IsOpen => instance != null && instance.root.activeSelf;
    public static int EscFrame { get; private set; } = -1;

    PlayerStats player;
    GameObject root;
    readonly List<(Anvil.Recipe recipe, Text cost, Button button)> rows = new List<(Anvil.Recipe, Text, Button)>();

    public static void Open(PlayerStats player)
    {
        if (instance == null)
        {
            instance = new GameObject("CraftUI").AddComponent<CraftUI>();
            instance.Build();
        }
        instance.player = player;
        instance.root.SetActive(true);
        instance.Refresh();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (IsOpen && kb != null && kb.escapeKey.wasPressedThisFrame) { EscFrame = Time.frameCount; Close(); }
    }

    void Close() => root.SetActive(false);

    void Refresh()
    {
        foreach (var (recipe, cost, button) in rows)
        {
            bool ok = true;
            var parts = new List<string>();
            foreach (var (item, count) in recipe.cost)
            {
                int have = player.Bag.Count(item);
                ok &= have >= count;
                parts.Add($"<color={(have >= count ? "#a8d46f" : "#e0604a")}>{item.name} {have}/{count}</color>");
            }
            cost.text = string.Join("   ", parts);
            button.interactable = ok;
        }
    }

    void Craft(Anvil.Recipe recipe)
    {
        foreach (var (item, count) in recipe.cost) if (player.Bag.Count(item) < count) return;
        foreach (var (item, count) in recipe.cost) player.Bag.Remove(item, count);
        if (recipe.result == Items.Torch) player.GiveTorch();
        else if (!player.Bag.Add(recipe.result)) { GameLog.Add("가방이 꽉 찼다.", GameLog.Warn); return; }
        GameLog.Add($"모루에서 {recipe.result.name}{Korean.Josa(recipe.result.name, "을", "를")} 만들었다.", GameLog.Good);
        Refresh();
    }

    void Build()
    {
        var canvas = UIKit.Canvas(transform, "CraftCanvas", 18);
        root = canvas.gameObject;
        var dim = UIKit.Image(canvas, "Dim", new Color(0, 0, 0, 0.6f), true);
        UIKit.Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<UIClickCatcher>().onClick = Close;

        var panel = UIKit.Image(canvas, "Panel", new Color(0.06f, 0.05f, 0.04f, 0.97f), true);
        UIKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760, 110 + Anvil.Recipes.Length * 120));
        panel.gameObject.AddComponent<Outline>().effectColor = new Color(0.55f, 0.45f, 0.3f, 0.8f);
        var title = UIKit.Text(panel.rectTransform, "모루 · 대장간", 32, TextAnchor.UpperLeft, UIKit.Gold);
        UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(32, -22), new Vector2(400, 44));
        var sub = UIKit.Text(panel.rectTransform, "미로에서 주운 재료로 만든다.  [Esc] 닫기", 17, TextAnchor.UpperRight, UIKit.Dim);
        UIKit.Place(sub.rectTransform, new Vector2(1, 1), new Vector2(-32, -32), new Vector2(400, 30));

        for (int i = 0; i < Anvil.Recipes.Length; i++)
        {
            var r = Anvil.Recipes[i];
            float y = -90 - i * 120;
            var box = UIKit.Image(panel.rectTransform, "Row", new Color(1, 1, 1, 0.04f));
            UIKit.Place(box.rectTransform, new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(700, 104));
            box.rectTransform.pivot = new Vector2(0.5f, 1);

            var icon = UIKit.Image(box.rectTransform, "Icon", Color.white);
            icon.sprite = r.result.Icon;
            UIKit.Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(80, 80));

            string stat = r.result.attackMin > 0 ? $"   <color=#dcb86b>공격 +{r.result.attackMin}~{r.result.attackMax}</color>" : "";
            var name = UIKit.Text(box.rectTransform, $"{r.result.name}{stat}", 24, TextAnchor.UpperLeft, new Color(0.95f, 0.9f, 0.8f));
            UIKit.Place(name.rectTransform, new Vector2(0, 1), new Vector2(110, -12), new Vector2(420, 32));
            var cost = UIKit.Text(box.rectTransform, "", 19, TextAnchor.UpperLeft, GameLog.Info);
            UIKit.Place(cost.rectTransform, new Vector2(0, 1), new Vector2(110, -56), new Vector2(420, 30));

            var recipe = r;
            var btn = UIKit.Button(box.rectTransform, "만들기", 22, () => Craft(recipe));
            UIKit.Place((RectTransform)btn.transform, new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(140, 56));
            rows.Add((r, cost, btn));
        }
        root.SetActive(false);
    }
}
