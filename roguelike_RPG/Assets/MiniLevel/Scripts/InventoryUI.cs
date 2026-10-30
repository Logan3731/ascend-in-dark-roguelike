using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// [I] 상태창·소지품. 왼쪽: 능력치와 장비 칸, 오른쪽: 가방과 아이템 설명.
// 가방 아이템을 클릭하면 사용(먹기·횃불 들기)하거나 장착하고, 장비 칸을 클릭하면 벗는다.
// 열려 있는 동안 플레이어는 움직이지 않는다 (턴도 흐르지 않음).
public class InventoryUI : MonoBehaviour
{
    public PlayerStats player;

    public static bool IsOpen => instance != null && instance.root.activeSelf;
    static InventoryUI instance;

    public static readonly Color SlotColor = new Color(0.13f, 0.11f, 0.09f, 1f);
    public static readonly Color SlotHover = new Color(0.27f, 0.22f, 0.16f, 1f);
    static readonly Color Gold = new Color(0.86f, 0.72f, 0.42f);
    static readonly Color Dim = new Color(0.62f, 0.58f, 0.5f);

    Font font;
    GameObject root;
    Text statsText, detailName, detailDesc, detailHint, goldText, bagCountText;
    readonly List<InventorySlotView> bagSlots = new List<InventorySlotView>();
    readonly Dictionary<EquipSlot, InventorySlotView> equipSlots = new Dictionary<EquipSlot, InventorySlotView>();

    void Awake()
    {
        instance = this;
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
        root.SetActive(false);
    }

    void OnEnable() { if (player != null) player.OnChanged += Refresh; }
    void OnDisable() { if (player != null) player.OnChanged -= Refresh; }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null || player == null) return;
        bool toggle = kb.iKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
        if (IsOpen && (toggle || kb.escapeKey.wasPressedThisFrame)) Close();
        else if (!IsOpen && toggle && !DialogUI.IsOpen && !player.Dead) Open();
    }

    public void Open()
    {
        root.SetActive(true);
        ShowDetail(null);
        Refresh();
    }

    public void Close() => root.SetActive(false);

    // ---- 내용 갱신 ----

    void Refresh()
    {
        if (!IsOpen) return;
        var p = player;
        string poison = p.PoisonTurns > 0 ? $"   <color=#8fd14f>중독 {p.PoisonTurns}턴</color>" : "";
        statsText.text =
            $"<color=#dcb86b>{TowerMemory.RunCount}번째 깨어남</color>{poison}\n\n" +
            $"HP          {p.Hp} / {p.maxHp}\n" +
            $"MP          {p.Mp} / {p.maxMp}\n" +
            $"허기        {p.Hunger} / {p.maxHunger}\n" +
            $"힘          {p.Strength}\n" +
            $"공격        {p.AttackMin} ~ {p.AttackMax}\n" +
            $"경험        {p.Exp}\n" +
            $"<color=#9ed0ff>경험의 정수  {TowerMemory.Essence}</color>";

        foreach (var kv in equipSlots)
        {
            var def = p.Bag.Equipped(kv.Key);
            // 상하의(일체형)를 입으면 상의·하의 칸 모두에 같은 옷을 보여 준다
            if (def == null && (kv.Key == EquipSlot.Top || kv.Key == EquipSlot.Bottom)) def = p.Bag.Equipped(EquipSlot.TopBottom);
            kv.Value.Set(def, 1);
        }

        var bag = p.Bag.bag;
        for (int i = 0; i < bagSlots.Count; i++)
        {
            if (i < bag.Count) bagSlots[i].Set(bag[i].def, bag[i].count);
            else bagSlots[i].Set(null, 0);
        }
        bagCountText.text = $"{bag.Count} / {Inventory.Capacity}";
        goldText.text = $"골드  {p.Gold}";
    }

    void ShowDetail(ItemDef def, bool equipped = false)
    {
        if (def == null)
        {
            detailName.text = "";
            detailDesc.text = "아이템에 마우스를 올리면 설명이 보인다.";
            detailHint.text = "";
            return;
        }
        detailName.text = def.name;
        string desc = def.desc;
        if (def.attackMin > 0) desc += $"\n\n<color=#dcb86b>공격 +{def.attackMin}~{def.attackMax}</color>";
        if (def == Items.Torch && player.HasTorch) desc += $"\n\n<color=#f08a2b>지금 쓰는 횃불 내구도 {player.Torch} / {player.maxTorch}</color>";
        detailDesc.text = desc;
        detailHint.text = equipped ? "클릭: 벗기"
            : def.kind == ItemKind.Food ? "클릭: 먹기"
            : def.kind == ItemKind.Torch ? (player.TorchHeld ? "클릭: 횃불 내리기" : "클릭: 횃불 들기")
            : def.slot != EquipSlot.None ? "클릭: 장착"
            : "";
    }

    void ClickBag(ItemDef def)
    {
        if (def == null) return;
        switch (def.kind)
        {
            case ItemKind.Food: player.EatFood(); break;
            case ItemKind.Torch: player.ToggleTorch(); break;
            default:
                if (def.slot != EquipSlot.None)
                {
                    if (player.Bag.Equip(def)) GameLog.Add($"{def.name}{Korean.Josa(def.name, "을", "를")} 장착했다.", GameLog.Info);
                    else GameLog.Add("가방이 가득 차 장착할 수 없다.", GameLog.Warn);
                }
                break;
        }
        ShowDetail(def);
    }

    void ClickEquip(EquipSlot slot)
    {
        var def = player.Bag.Equipped(slot);
        if (def == null && (slot == EquipSlot.Top || slot == EquipSlot.Bottom)) { slot = EquipSlot.TopBottom; def = player.Bag.Equipped(slot); }
        if (def == null) return;
        if (player.Bag.Unequip(slot)) GameLog.Add($"{def.name}{Korean.Josa(def.name, "을", "를")} 벗었다.", GameLog.Info);
        else GameLog.Add("가방이 가득 찼다.", GameLog.Warn);
        ShowDetail(def);
    }

    // ---- UI 생성 ----

    void Build()
    {
        var canvasGo = new GameObject("InventoryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = canvasGo;
        var cr = (RectTransform)canvasGo.transform;

        // 뒤 배경을 살짝 어둡게
        var dim = MakeImage(cr, "Dim", new Color(0, 0, 0, 0.55f));
        Stretch(dim.rectTransform);

        var panel = MakeImage(cr, "Panel", new Color(0.06f, 0.05f, 0.045f, 0.97f));
        var pr = panel.rectTransform;
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
        pr.sizeDelta = new Vector2(1180, 700);
        var ol = panel.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0.55f, 0.45f, 0.3f, 0.8f); ol.effectDistance = new Vector2(2, -2);

        var title = MakeText(pr, "소지품", 34, TextAnchor.MiddleLeft, Gold);
        Place(title.rectTransform, 36, -22, 300, 44);
        var close = MakeText(pr, "[I] 닫기", 20, TextAnchor.MiddleRight, Dim);
        PlaceRight(close.rectTransform, -36, -26, 200, 36);

        // 왼쪽: 상태
        var leftHead = MakeText(pr, "상태", 24, TextAnchor.MiddleLeft, Gold);
        Place(leftHead.rectTransform, 36, -84, 200, 32);
        statsText = MakeText(pr, "", 22, TextAnchor.UpperLeft, GameLog.Info);
        statsText.supportRichText = true;
        statsText.lineSpacing = 1.15f;
        Place(statsText.rectTransform, 36, -124, 300, 320);

        // 가운데: 장비 (사람 모양 배치)
        var equipHead = MakeText(pr, "장비", 24, TextAnchor.MiddleLeft, Gold);
        Place(equipHead.rectTransform, 370, -84, 200, 32);
        float ex = 470, ey = -130, s = 96, gap = 120;
        AddEquip(pr, EquipSlot.Hat, "모자", ex, ey, s);
        AddEquip(pr, EquipSlot.Weapon, "무기", ex - gap, ey - gap, s);
        AddEquip(pr, EquipSlot.Top, "상의", ex, ey - gap, s);
        AddEquip(pr, EquipSlot.Bottom, "하의", ex, ey - gap * 2, s);
        AddEquip(pr, EquipSlot.Shoes, "신발", ex, ey - gap * 3, s);
        var note = MakeText(pr, "상하의(일체형)는 상의·하의\n두 칸을 함께 차지한다.", 16, TextAnchor.UpperLeft, Dim);
        Place(note.rectTransform, 370, -618, 280, 50);

        // 오른쪽: 가방 5x4
        var bagHead = MakeText(pr, "가방", 24, TextAnchor.MiddleLeft, Gold);
        Place(bagHead.rectTransform, 680, -84, 200, 32);
        bagCountText = MakeText(pr, "", 18, TextAnchor.MiddleLeft, Dim);
        Place(bagCountText.rectTransform, 750, -86, 120, 30);
        goldText = MakeText(pr, "", 22, TextAnchor.MiddleRight, Gold);
        PlaceRight(goldText.rectTransform, -36, -84, 200, 32);
        float bx = 680, by = -124, bs = 86, bg = 94;
        for (int i = 0; i < Inventory.Capacity; i++)
        {
            int r = i / 5, c = i % 5;
            var slot = InventorySlotView.Create(pr, font, bx + c * bg, by - r * bg, bs);
            slot.onClick = () => ClickBag(slot.def);
            slot.onHover = () => ShowDetail(slot.def);
            bagSlots.Add(slot);
        }

        // 설명
        var detailBg = MakeImage(pr, "Detail", new Color(0, 0, 0, 0.35f));
        Place(detailBg.rectTransform, 680, -506, 464, 170);
        detailName = MakeText(pr, "", 24, TextAnchor.UpperLeft, Gold);
        Place(detailName.rectTransform, 696, -516, 430, 32);
        detailDesc = MakeText(pr, "", 18, TextAnchor.UpperLeft, GameLog.Info);
        detailDesc.supportRichText = true;
        detailDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
        Place(detailDesc.rectTransform, 696, -552, 430, 100);
        detailHint = MakeText(pr, "", 18, TextAnchor.LowerRight, new Color(0.62f, 0.82f, 1f));
        Place(detailHint.rectTransform, 696, -644, 430, 26);
    }

    void AddEquip(RectTransform parent, EquipSlot slot, string label, float x, float y, float size)
    {
        var view = InventorySlotView.Create(parent, font, x, y, size);
        var caption = MakeText(parent, label, 15, TextAnchor.UpperCenter, Dim);   // 칸 이름은 아이템이 있어도 보이게
        Place(caption.rectTransform, x, y - size - 1, size, 18);
        view.onClick = () => ClickEquip(slot);
        view.onHover = () => ShowDetail(view.def, true);
        equipSlots[slot] = view;
    }

    Image MakeImage(RectTransform parent, string name, Color color)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        return img;
    }

    Text MakeText(RectTransform parent, string text, int size, TextAnchor anchor, Color color)
    {
        var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size; t.alignment = anchor; t.color = color; t.text = text;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    static void PlaceRight(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(1, 1);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(w, h);
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }
}
