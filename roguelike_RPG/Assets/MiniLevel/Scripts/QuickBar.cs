using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 화면 아래 단축키 칸 1~4. 칸을 누르거나 숫자 키를 누르면 "어떻게 할까?" 창(사용·던진다·취소)이 뜬다.
// - 쓸 수 있는 물건(식량·횃불·물약·돌멩이·다트)을 주우면 빈 칸에 저절로 들어간다.
// - 소지품에서 아이템에 마우스를 올리고 1~4를 누르면 그 칸에 넣는다 (이미 다른 칸에 있으면 자리를 바꾼다).
// - 다 써서 하나도 없으면 칸이 빈다.
public class QuickBar : MonoBehaviour
{
    public const int Count = 4;
    public PlayerStats player;

    static readonly ItemDef[] slots = new ItemDef[Count];
    static QuickBar instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() { for (int i = 0; i < Count; i++) slots[i] = null; instance = null; }

    class View { public Image bg, icon; public Text count, key; }
    readonly View[] views = new View[Count];

    public static bool CanHold(ItemDef d) =>
        d != null && (d.kind == ItemKind.Food || d.kind == ItemKind.Torch || d.kind == ItemKind.Potion || d == Items.Stone || d == Items.Dart);

    // 이번 프레임에 누른 숫자 키 (1~4 → 0~3), 없으면 -1
    public static int PressedSlot(Keyboard kb)
    {
        if (kb == null) return -1;
        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 0;
        if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 1;
        if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 2;
        if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return 3;
        return -1;
    }

    public static ItemDef Slot(int index) => index >= 0 && index < Count ? slots[index] : null;

    // 그 칸의 "어떻게 할까?" 창을 연다 (숫자 키·칸 클릭과 같다)
    public static void Open(int index) { if (instance != null && instance.CanAsk()) instance.Ask(index); }

    public static void Assign(int index, ItemDef def)
    {
        if (index < 0 || index >= Count || !CanHold(def)) return;
        for (int i = 0; i < Count; i++) if (slots[i] == def) slots[i] = slots[index];   // 다른 칸에 있으면 자리를 바꾼다
        slots[index] = def;
        if (instance != null) instance.Refresh();
    }

    void Awake()
    {
        instance = this;
        Build();
    }

    void Start()
    {
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        if (player != null) player.OnChanged += Refresh;
        Refresh();
    }

    void OnDestroy()
    {
        if (player != null) player.OnChanged -= Refresh;
        if (instance == this) instance = null;
    }

    int Have(ItemDef d) => player == null || d == null ? 0 : player.Bag.Count(d);

    void Refresh()
    {
        if (player == null) return;
        // 다 쓴 것은 비운다 (손에 든 횃불은 남긴다)
        for (int i = 0; i < Count; i++)
            if (slots[i] != null && Have(slots[i]) == 0 && !(slots[i] == Items.Torch && player.HasTorch)) slots[i] = null;
        // 새로 생긴 쓸 수 있는 물건은 빈 칸에
        foreach (var st in player.Bag.bag)
        {
            if (!CanHold(st.def) || System.Array.IndexOf(slots, st.def) >= 0) continue;
            int empty = System.Array.IndexOf(slots, null);
            if (empty < 0) break;
            slots[empty] = st.def;
        }
        for (int i = 0; i < Count; i++)
        {
            var v = views[i];
            var d = slots[i];
            v.icon.enabled = d != null;
            if (d != null)
            {
                v.icon.sprite = d.Icon;
                v.icon.color = d.Icon != null ? Color.white : d.kind == ItemKind.Potion ? Potions.BottleColor(d) : new Color(0.7f, 0.65f, 0.55f);
            }
            int n = Have(d);
            v.count.text = d != null && n > 1 ? n.ToString() : "";
        }
    }

    void Update()
    {
        int n = PressedSlot(Keyboard.current);
        if (n >= 0 && CanAsk()) Ask(n);
    }

    bool CanAsk() =>
        player != null && !player.Dead && !DialogUI.IsOpen && DialogUI.ClosedFrame != Time.frameCount &&!InventoryUI.IsOpen && !PauseMenu.IsOpen && !TitleScreen.IsOpen
        && !CraftUI.IsOpen && !ShopUI.IsOpen && Thrower.Aiming == null && !GridClickMover.InputLocked;

    // ---- 어떻게 할까? ----

    void Ask(int index)
    {
        var d = slots[index];
        if (d == null) { GameLog.Add($"{index + 1}번 칸이 비어 있다.", GameLog.Info); return; }
        var p = player;
        bool torch = d.kind == ItemKind.Torch;
        string useLabel = d.kind == ItemKind.Food ? "먹는다" : d.kind == ItemKind.Potion ? "마신다" : torch ? (p.TorchHeld ? "내린다" : "든다") : "사용";
        bool canUse = d.kind == ItemKind.Food || d.kind == ItemKind.Potion || torch;
        bool canThrow = d.kind == ItemKind.Potion || d == Items.Stone || d == Items.Dart || (torch && p.TorchLit);

        string info = d.kind == ItemKind.Food ? $"허기 {p.Hunger} → {Mathf.Min(p.maxHunger, p.Hunger + p.foodRestore)}"
            : d.kind == ItemKind.Potion ? (Potions.IsKnown(d) ? Potions.Preview(d) : "무슨 일이 일어날지 모른다…")
            : d == Items.Stone ? "맞으면 피해 2~4"
            : d == Items.Dart ? $"맞으면 피해 {Thrower.DartMin}~{Thrower.DartMax}"
            : torch && !p.TorchLit ? $"남은 내구도 {p.Torch}" : "맞으면 불 피해 3~5";
        DialogUI.Show($"{d.name}{Korean.Josa(d.name, "을", "를")} 어떻게 할까?   <color=#9e9480>({Have(d)}개)</color>\n<size=24><color=#9ed0ff>{info}</color></size>",
            new DialogUI.Choice($"[1] {useLabel}", () => Use(d), canUse),
            new DialogUI.Choice("[2] 던진다", () => GridClickMover.BeginThrow(d), canThrow),
            new DialogUI.Choice("[3] 취소", null));
    }

    void Use(ItemDef d)
    {
        if (d.kind == ItemKind.Food) player.EatFood();
        else if (d.kind == ItemKind.Torch) player.ToggleTorch();
        else if (d.kind == ItemKind.Potion) Potions.Drink(player, d);
    }

    // ---- UI ----

    void Build()
    {
        var root = UIKit.Canvas(transform, "QuickBarCanvas", 1);
        const float size = 70, gap = 10;
        // 로그(왼쪽 아래)와 스킬 버튼(오른쪽 아래) 사이
        float x0 = 86 - (Count * size + (Count - 1) * gap) / 2f;
        for (int i = 0; i < Count; i++)
        {
            int idx = i;
            var v = new View();
            var btn = UIKit.Button(root, "", 16, () => { if (CanAsk()) Ask(idx); });
            v.bg = btn.GetComponent<Image>();
            UIKit.Place(v.bg.rectTransform, new Vector2(0.5f, 0), new Vector2(x0 + i * (size + gap) + size / 2f, 60), new Vector2(size, size));
            v.bg.rectTransform.pivot = new Vector2(0.5f, 0);

            v.icon = UIKit.Image(v.bg.rectTransform, "Icon", Color.white);
            v.icon.preserveAspect = true;
            UIKit.Stretch(v.icon.rectTransform);
            v.icon.rectTransform.offsetMin = new Vector2(8, 8);
            v.icon.rectTransform.offsetMax = new Vector2(-8, -8);
            v.icon.enabled = false;

            v.key = UIKit.Text(v.bg.rectTransform, (i + 1).ToString(), 16, TextAnchor.UpperLeft, UIKit.Gold);
            UIKit.Place(v.key.rectTransform, new Vector2(0, 1), new Vector2(5, -2), new Vector2(20, 20));
            v.key.gameObject.AddComponent<Shadow>().effectColor = Color.black;

            v.count = UIKit.Text(v.bg.rectTransform, "", 17, TextAnchor.LowerRight, Color.white);
            UIKit.Place(v.count.rectTransform, new Vector2(1, 0), new Vector2(-5, 2), new Vector2(40, 22));
            v.count.gameObject.AddComponent<Shadow>().effectColor = Color.black;
            views[i] = v;
        }
    }
}
