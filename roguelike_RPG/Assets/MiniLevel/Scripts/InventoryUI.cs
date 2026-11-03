using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// [I] 상태창·소지품. 왼쪽: 능력치, 가운데: 장비 칸, 오른쪽: 가방.
// - 아이템에 마우스를 올리면 마우스 옆에 정보가 뜬다.
// - 소모품(식량·횃불)을 클릭하면 "사용할까?" 창에 효과(허기 +5 등)를 미리 보여 주고 고르게 한다.
// - 장비는 클릭하면 마우스를 따라오고(끌어도 됨), 넣을 수 있는 장비 칸이 금색으로 빛난다.
//   맞는 칸에 놓으면 장착, 가방에 놓으면 해제, 다른 곳이면 "사용할 수 없습니다"와 함께 제자리로 돌아간다.
// 열려 있는 동안 플레이어는 움직이지 않는다 (턴도 흐르지 않음).
public class InventoryUI : MonoBehaviour
{
    public PlayerStats player;

    public static bool IsOpen => instance != null && instance.root.activeSelf;
    public static int EscFrame { get; private set; } = -1;   // 이 프레임의 Esc는 소지품이 썼다 (일시정지가 같이 열리지 않게)
    static InventoryUI instance;

    public static readonly Color SlotColor = new Color(0.13f, 0.11f, 0.09f, 1f);
    public static readonly Color SlotHover = new Color(0.27f, 0.22f, 0.16f, 1f);
    static readonly Color Gold = new Color(0.86f, 0.72f, 0.42f);
    static readonly Color Dim = new Color(0.62f, 0.58f, 0.5f);

    Font font;
    GameObject root;
    RectTransform canvasRect, bagArea;
    Canvas canvas;
    Text statsText, goldText, bagCountText;
    readonly List<InventorySlotView> bagSlots = new List<InventorySlotView>();
    readonly Dictionary<EquipSlot, InventorySlotView> equipSlots = new Dictionary<EquipSlot, InventorySlotView>();

    // 마우스 옆 정보창
    RectTransform tooltip;
    Text tooltipText;
    InventorySlotView hovered;

    // 사용 확인 창
    GameObject confirm;
    RectTransform confirmPanel;
    Text confirmText;
    Button confirmUse;
    Button confirmThrow;   // 물약일 때만: 마시지 않고 던진다
    ItemDef confirmItem;

    // 들고 있는 장비
    ItemDef held;
    EquipSlot heldFrom;             // None이면 가방에서 들어 올림
    InventorySlotView heldSource;
    Image heldIcon;
    Coroutine returning;

    Text floatMsg;
    Coroutine floatRoutine;

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

        if (IsOpen)
        {
            if (kb.escapeKey.wasPressedThisFrame)
            {
                EscFrame = Time.frameCount;
                if (held != null) ReturnHeld(false);
                else if (confirm.activeSelf) CloseConfirm();
                else Close();
            }
            else if (kb.iKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) Close();

            var mouse = MouseLocal();
            if (held != null && returning == null) heldIcon.rectTransform.anchoredPosition = mouse;
            if (tooltip.gameObject.activeSelf) PlaceNearMouse(tooltip, mouse);
        }
        else if ((kb.iKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) && !DialogUI.IsOpen && !PauseMenu.IsOpen && !TitleScreen.IsOpen && !CraftUI.IsOpen && !ShopUI.IsOpen && !player.Dead) Open();
    }

    public void Open()
    {
        root.SetActive(true);
        CloseConfirm();
        HideTooltip();
        Refresh();
    }

    public void Close()
    {
        if (held != null) { if (returning != null) StopCoroutine(returning); returning = null; ClearHeld(); }
        CloseConfirm();
        HideTooltip();
        root.SetActive(false);
    }

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
        if (heldSource != null) heldSource.SetLifted(true);
    }

    // ---- 마우스 옆 정보 ----

    void ShowTooltip(InventorySlotView slot, bool equipped)
    {
        hovered = slot;
        var def = slot.def;
        if (def == null || held != null || confirm.activeSelf) { HideTooltip(); return; }

        string kind = def.kind == ItemKind.Food || def.kind == ItemKind.Torch || def.kind == ItemKind.Potion ? $"소모품 · {player.Bag.Count(def)}개"
            : def.slot != EquipSlot.None ? $"장비 · {SlotName(def.slot)} 칸"
            : def.kind == ItemKind.Material ? $"재료 · {player.Bag.Count(def)}개"
            : "도구";
        var s = $"<size=24><color=#dcb86b>{def.name}</color></size>\n<color=#9e9480>{kind}</color>\n\n{def.desc}";
        if (def.attackMin > 0) s += $"\n\n<color=#dcb86b>공격 +{def.attackMin}~{def.attackMax}</color>";
        if (def.kind == ItemKind.Food) s += $"\n\n<color=#a8d46f>허기 +{player.foodRestore}</color>";
        if (def.kind == ItemKind.Torch && player.HasTorch) s += $"\n\n<color=#f08a2b>지금 쓰는 횃불 내구도 {player.Torch} / {player.maxTorch}</color>";
        string hint = equipped ? "클릭하거나 끌어서 가방에 넣으면 벗는다"
            : def == Items.Stone ? "클릭: 던지기 (재료로도 쓴다)"
            : def.kind == ItemKind.Potion ? "클릭: 마시거나 던진다"
            : Usable(def) ? "클릭: 사용"
            : def.slot != EquipSlot.None ? "클릭하거나 끌어서 장비 칸에 넣는다"
            : def.kind == ItemKind.Material ? "시작한 방의 모루에서 쓸 수 있다"
            : "아직 쓸 곳이 없다";
        s += $"\n\n<color=#9ed0ff>{hint}</color>";

        tooltipText.text = s;
        tooltip.sizeDelta = new Vector2(360, tooltip.sizeDelta.y);           // 너비를 먼저 정해야 줄바꿈 높이가 맞는다
        tooltip.sizeDelta = new Vector2(360, tooltipText.preferredHeight + 28);
        tooltip.gameObject.SetActive(true);
        tooltip.SetAsLastSibling();
        PlaceNearMouse(tooltip, MouseLocal());
    }

    void HideTooltip()
    {
        hovered = null;
        if (tooltip != null) tooltip.gameObject.SetActive(false);
    }

    static string SlotName(EquipSlot s) => s switch
    {
        EquipSlot.Hat => "모자", EquipSlot.Top => "상의", EquipSlot.Bottom => "하의",
        EquipSlot.TopBottom => "상의·하의", EquipSlot.Shoes => "신발", EquipSlot.Weapon => "무기",
        _ => "",
    };

    // ---- 소모품: 사용 확인 ----

    void OpenConfirm(InventorySlotView slot)
    {
        var def = slot.def;
        confirmItem = def;
        HideTooltip();
        var p = player;
        string body;
        if (def.kind == ItemKind.Food)
        {
            int after = Mathf.Min(p.maxHunger, p.Hunger + p.foodRestore);
            int gain = after - p.Hunger;
            body = $"<size=26><color=#dcb86b>{def.name}{Korean.Josa(def.name, "을", "를")} 먹을까?</color></size>\n\n" +
                   (gain > 0 ? $"허기  {p.Hunger} → {after}   <color=#a8d46f>(+{gain})</color>"
                             : $"허기  {p.Hunger} → {after}   <color=#9e9480>(이미 배부르다)</color>") +
                   $"\n<color=#9e9480>{def.name} {p.Food} → {p.Food - 1}</color>";
        }
        else if (def.kind == ItemKind.Potion)
            body = $"<size=26><color=#dcb86b>{def.name}{Korean.Josa(def.name, "을", "를")} 어떻게 할까?</color></size>\n\n" +
                   (Potions.IsKnown(def) ? $"<color=#a8d46f>{Potions.Preview(def)}</color>" : "<color=#e0a04a>무슨 일이 일어날지 모른다…</color>") +
                   $"\n<color=#9ed0ff>{Potions.ThrowPreview(def)}</color>";
        else if (def == Items.Stone)
            body = "<size=26><color=#dcb86b>돌멩이를 던질까?</color></size>\n\n" +
                   $"던질 칸을 고른다 ({Thrower.Range}칸까지)\n<color=#dcb86b>맞으면 피해 2~4</color>\n<color=#9e9480>떨어진 돌은 다시 주울 수 있다</color>";
        else if (!p.TorchHeld)
            body = $"<size=26><color=#dcb86b>{def.name}{Korean.Josa(def.name, "을", "를")} 들까?</color></size>\n\n" +
                   "<color=#f08a2b>주변이 밝아진다</color>\n" +
                   $"한 턴마다 내구도 -1   <color=#9e9480>(남은 {p.Torch})</color>";
        else
            body = $"<size=26><color=#dcb86b>{def.name}{Korean.Josa(def.name, "을", "를")} 내릴까?</color></size>\n\n" +
                   "<color=#9e9480>주변이 어두워진다</color>\n" +
                   "내구도가 줄지 않는다";
        confirmText.text = body;
        confirmUse.GetComponentInChildren<Text>().text = def.kind == ItemKind.Food ? "먹는다" : def.kind == ItemKind.Potion ? "마신다" : def == Items.Stone ? "던진다" : p.TorchHeld ? "내린다" : "든다";
        confirmThrow.gameObject.SetActive(def.kind == ItemKind.Potion);

        confirm.SetActive(true);
        confirm.transform.SetAsLastSibling();
        // 아이템 칸 바로 옆에 띄운다
        var local = (Vector2)canvasRect.InverseTransformPoint(slot.Rect.TransformPoint(slot.Rect.rect.center));
        confirmPanel.anchoredPosition = ClampInCanvas(new Vector2(local.x - 190, local.y - 60), confirmPanel.sizeDelta);
    }

    void CloseConfirm()
    {
        confirmItem = null;
        if (confirm != null) confirm.SetActive(false);
    }

    void UseConfirmed()
    {
        var def = confirmItem;
        CloseConfirm();
        if (def == null) return;
        if (def.kind == ItemKind.Food) player.EatFood();
        else if (def.kind == ItemKind.Torch) player.ToggleTorch();
        else if (def.kind == ItemKind.Potion) Potions.Drink(player, def);
        else if (def == Items.Stone) { Close(); Thrower.Begin(Items.Stone); }
    }

    void ThrowConfirmed()
    {
        var def = confirmItem;
        CloseConfirm();
        if (def == null) return;
        Close();
        Thrower.Begin(def);
    }

    // ---- 장비 들기·놓기 ----

    static bool CanPlace(ItemDef def, EquipSlot target) =>
        def.slot == target || (def.slot == EquipSlot.TopBottom && (target == EquipSlot.Top || target == EquipSlot.Bottom));

    EquipSlot SlotOf(InventorySlotView view)
    {
        foreach (var kv in equipSlots) if (kv.Value == view) return kv.Key;
        return EquipSlot.None;
    }

    EquipSlot EquippedKey(EquipSlot shown)
    {
        // 상의·하의 칸에 보이는 일체형 옷은 실제로는 TopBottom 칸에 있다
        if (player.Bag.Equipped(shown) == null && (shown == EquipSlot.Top || shown == EquipSlot.Bottom)) return EquipSlot.TopBottom;
        return shown;
    }

    void PickUp(InventorySlotView slot, EquipSlot from)
    {
        if (slot.def == null || returning != null) return;
        held = slot.def;
        heldFrom = from;
        heldSource = slot;
        slot.SetLifted(true);
        HideTooltip();
        heldIcon.sprite = held.Icon;
        heldIcon.gameObject.SetActive(true);
        heldIcon.transform.SetAsLastSibling();
        heldIcon.rectTransform.anchoredPosition = MouseLocal();
        foreach (var kv in equipSlots) kv.Value.SetHighlight(CanPlace(held, kv.Key));
    }

    void DropOnEquip(EquipSlot target)
    {
        if (!CanPlace(held, target)) { ReturnHeld(true); return; }
        if (heldFrom != EquipSlot.None) { ReturnHeld(false); return; }   // 이미 입고 있는 장비를 같은 자리에
        var def = held;
        ClearHeld();
        if (player.Bag.Equip(def)) GameLog.Add($"{def.name}{Korean.Josa(def.name, "을", "를")} 장착했다.", GameLog.Info);
        else ShowFloat("가방이 가득 찼습니다");
        Refresh();
    }

    void DropOnBag()
    {
        if (heldFrom == EquipSlot.None) { ReturnHeld(false); return; }   // 가방 → 가방: 그대로
        var def = held;
        var from = heldFrom;
        ClearHeld();
        if (player.Bag.Unequip(from)) GameLog.Add($"{def.name}{Korean.Josa(def.name, "을", "를")} 벗었다.", GameLog.Info);
        else ShowFloat("가방이 가득 찼습니다");
        Refresh();
    }

    // 놓을 수 없는 곳: 메시지를 띄우고 원래 칸으로 날아가서 돌아간다
    void ReturnHeld(bool rejected)
    {
        if (held == null) return;
        if (rejected) ShowFloat("사용할 수 없습니다");
        foreach (var kv in equipSlots) kv.Value.SetHighlight(false);
        if (returning != null) StopCoroutine(returning);
        returning = StartCoroutine(FlyBack());
    }

    IEnumerator FlyBack()
    {
        var from = heldIcon.rectTransform.anchoredPosition;
        var src = heldSource;
        Vector2 to = src != null ? (Vector2)canvasRect.InverseTransformPoint(src.Rect.TransformPoint(src.Rect.rect.center)) : from;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.2f)
        {
            heldIcon.rectTransform.anchoredPosition = Vector2.Lerp(from, to, 1f - (1f - t) * (1f - t));
            yield return null;
        }
        returning = null;
        ClearHeld();
        Refresh();
    }

    void ClearHeld()
    {
        if (heldSource != null) heldSource.SetLifted(false);
        held = null;
        heldSource = null;
        heldFrom = EquipSlot.None;
        heldIcon.gameObject.SetActive(false);
        foreach (var kv in equipSlots) kv.Value.SetHighlight(false);
    }

    // ---- 칸 클릭·끌기 ----

    // 클릭하면 사용 확인 창이 뜨는 것 (먹기·횃불·물약·돌 던지기)
    static bool Usable(ItemDef d) => d.kind == ItemKind.Food || d.kind == ItemKind.Torch || d.kind == ItemKind.Potion || d == Items.Stone;

    void ClickBagSlot(InventorySlotView slot)
    {
        if (returning != null) return;
        if (held != null) { DropOnBag(); return; }
        if (slot.def == null) return;
        if (Usable(slot.def)) OpenConfirm(slot);
        else PickUp(slot, EquipSlot.None);
    }

    void ClickEquipSlot(InventorySlotView slot)
    {
        if (returning != null) return;
        var key = SlotOf(slot);
        if (held != null) { DropOnEquip(key); return; }
        if (slot.def != null) PickUp(slot, EquippedKey(key));
    }

    void BeginDrag(InventorySlotView slot, bool isEquip)
    {
        if (held != null || slot.def == null || confirm.activeSelf) return;
        if (isEquip) PickUp(slot, EquippedKey(SlotOf(slot)));
        else if (!Usable(slot.def)) PickUp(slot, EquipSlot.None);
    }

    void EndDrag(PointerEventData e)
    {
        if (held == null || returning != null) return;
        var go = e.pointerCurrentRaycast.gameObject;
        var slot = go != null ? go.GetComponentInParent<InventorySlotView>() : null;
        if (slot != null && bagSlots.Contains(slot)) { DropOnBag(); return; }
        if (slot != null) { DropOnEquip(SlotOf(slot)); return; }
        if (go != null && go.transform.IsChildOf(bagArea)) { DropOnBag(); return; }
        ReturnHeld(true);
    }

    void ClickBackground()
    {
        if (held != null && returning == null) ReturnHeld(true);
    }

    // ---- 떠오르는 안내 문구 ----

    void ShowFloat(string msg)
    {
        GameLog.Add(msg, GameLog.Warn);
        if (floatRoutine != null) StopCoroutine(floatRoutine);
        floatRoutine = StartCoroutine(FloatRoutine(msg));
    }

    IEnumerator FloatRoutine(string msg)
    {
        floatMsg.text = msg;
        floatMsg.gameObject.SetActive(true);
        floatMsg.transform.SetAsLastSibling();
        var start = MouseLocal() + new Vector2(0, 40);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.1f)
        {
            floatMsg.rectTransform.anchoredPosition = start + new Vector2(0, 30 * t);
            var c = floatMsg.color; c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f; floatMsg.color = c;
            yield return null;
        }
        floatMsg.gameObject.SetActive(false);
        floatRoutine = null;
    }

    // ---- 좌표 ----

    Vector2 MouseLocal()
    {
        var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var screen = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out var local);
        return local;
    }

    Vector2 ClampInCanvas(Vector2 pos, Vector2 size)
    {
        var half = canvasRect.rect.size * 0.5f;
        pos.x = Mathf.Clamp(pos.x, -half.x + 10, half.x - size.x - 10);
        pos.y = Mathf.Clamp(pos.y, -half.y + size.y + 10, half.y - 10);
        return pos;
    }

    void PlaceNearMouse(RectTransform r, Vector2 mouse)
    {
        var pos = mouse + new Vector2(28, -28);
        // 화면 오른쪽 끝이면 마우스 왼쪽에 띄운다
        if (pos.x + r.sizeDelta.x > canvasRect.rect.width * 0.5f - 10) pos.x = mouse.x - r.sizeDelta.x - 28;
        r.anchoredPosition = ClampInCanvas(pos, r.sizeDelta);
    }

    // ---- UI 생성 ----

    void Build()
    {
        var canvasGo = new GameObject("InventoryCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 15;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        root = canvasGo;
        canvasRect = (RectTransform)canvasGo.transform;
        var cr = canvasRect;

        // 뒤 배경을 살짝 어둡게 (빈 곳 클릭 = 들고 있던 장비 되돌리기)
        var dim = MakeImage(cr, "Dim", new Color(0, 0, 0, 0.55f));
        dim.raycastTarget = true;
        Stretch(dim.rectTransform);
        dim.gameObject.AddComponent<UIClickCatcher>().onClick = ClickBackground;

        var panel = MakeImage(cr, "Panel", new Color(0.06f, 0.05f, 0.045f, 0.97f));
        panel.raycastTarget = true;
        panel.gameObject.AddComponent<UIClickCatcher>().onClick = ClickBackground;
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
        Place(note.rectTransform, 370, -628, 280, 50);

        // 오른쪽: 가방 5x4 (가방 영역 아무 데나 놓으면 가방으로)
        var bagHead = MakeText(pr, "가방", 24, TextAnchor.MiddleLeft, Gold);
        Place(bagHead.rectTransform, 680, -84, 200, 32);
        bagCountText = MakeText(pr, "", 18, TextAnchor.MiddleLeft, Dim);
        Place(bagCountText.rectTransform, 750, -86, 120, 30);
        goldText = MakeText(pr, "", 22, TextAnchor.MiddleRight, Gold);
        PlaceRight(goldText.rectTransform, -36, -84, 200, 32);
        float bx = 680, by = -124, bs = 86, bg = 94;
        var area = MakeImage(pr, "BagArea", new Color(1, 1, 1, 0.02f));
        area.raycastTarget = true;
        Place(area.rectTransform, bx - 8, by + 8, bg * 5 + 8, bg * 4 + 8);
        bagArea = area.rectTransform;
        area.gameObject.AddComponent<UIClickCatcher>().onClick = () => { if (held != null && returning == null) DropOnBag(); };
        for (int i = 0; i < Inventory.Capacity; i++)
        {
            int r = i / 5, c = i % 5;
            var slot = InventorySlotView.Create(pr, font, bx + c * bg, by - r * bg, bs);
            slot.onClick = () => ClickBagSlot(slot);
            slot.onEnter = () => ShowTooltip(slot, false);
            slot.onExit = () => { if (hovered == slot) HideTooltip(); };
            slot.onBeginDrag = () => BeginDrag(slot, false);
            slot.onEndDrag = EndDrag;
            bagSlots.Add(slot);
        }
        var help = MakeText(pr, "마우스를 올리면 정보 · 클릭하면 사용 · 장비는 끌어서 장비 칸에", 17, TextAnchor.UpperLeft, Dim);
        Place(help.rectTransform, 680, -510, 470, 26);

        BuildOverlays(cr);
    }

    void BuildOverlays(RectTransform cr)
    {
        // 마우스 옆 정보창
        var tipBg = MakeImage(cr, "Tooltip", new Color(0.03f, 0.025f, 0.02f, 0.96f));
        tooltip = tipBg.rectTransform;
        tooltip.anchorMin = tooltip.anchorMax = new Vector2(0.5f, 0.5f);
        tooltip.pivot = new Vector2(0, 1);
        var tol = tipBg.gameObject.AddComponent<Outline>();
        tol.effectColor = new Color(0.55f, 0.45f, 0.3f, 0.9f);
        tooltipText = MakeText(tooltip, "", 18, TextAnchor.UpperLeft, GameLog.Info);
        tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
        var tr = tooltipText.rectTransform;
        tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1);
        tr.anchoredPosition = new Vector2(0, -14);
        tr.sizeDelta = new Vector2(-28, 600);
        tooltip.gameObject.SetActive(false);

        // 사용 확인 창 (뒤를 누르면 취소)
        confirm = new GameObject("Confirm", typeof(RectTransform));
        confirm.transform.SetParent(cr, false);
        Stretch((RectTransform)confirm.transform);
        var blocker = MakeImage((RectTransform)confirm.transform, "Blocker", new Color(0, 0, 0, 0.25f));
        blocker.raycastTarget = true;
        Stretch(blocker.rectTransform);
        blocker.gameObject.AddComponent<UIClickCatcher>().onClick = CloseConfirm;
        var cp = MakeImage((RectTransform)confirm.transform, "Panel", new Color(0.07f, 0.06f, 0.05f, 0.98f));
        cp.raycastTarget = true;
        confirmPanel = cp.rectTransform;
        confirmPanel.anchorMin = confirmPanel.anchorMax = new Vector2(0.5f, 0.5f);
        confirmPanel.pivot = new Vector2(0, 1);
        confirmPanel.sizeDelta = new Vector2(420, 250);
        var col = cp.gameObject.AddComponent<Outline>();
        col.effectColor = Gold; col.effectDistance = new Vector2(2, -2);
        confirmText = MakeText(confirmPanel, "", 20, TextAnchor.UpperLeft, GameLog.Info);
        Place(confirmText.rectTransform, 22, -18, 380, 160);
        confirmUse = MakeButton(confirmPanel, "사용", UseConfirmed, new Vector2(-144, 18));
        confirmThrow = MakeButton(confirmPanel, "던진다", ThrowConfirmed, new Vector2(-270, 18));
        MakeButton(confirmPanel, "취소", CloseConfirm, new Vector2(-18, 18));
        confirm.SetActive(false);

        // 들고 있는 장비 아이콘 (마우스를 따라다님)
        heldIcon = MakeImage(cr, "Held", Color.white);
        heldIcon.preserveAspect = true;
        heldIcon.rectTransform.anchorMin = heldIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        heldIcon.rectTransform.sizeDelta = new Vector2(80, 80);
        heldIcon.gameObject.SetActive(false);

        floatMsg = MakeText(cr, "", 26, TextAnchor.MiddleCenter, new Color(1f, 0.45f, 0.35f));
        floatMsg.rectTransform.anchorMin = floatMsg.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        floatMsg.rectTransform.sizeDelta = new Vector2(400, 40);
        floatMsg.gameObject.AddComponent<Outline>().effectColor = Color.black;
        floatMsg.gameObject.SetActive(false);
    }

    void AddEquip(RectTransform parent, EquipSlot slot, string label, float x, float y, float size)
    {
        var view = InventorySlotView.Create(parent, font, x, y, size);
        var caption = MakeText(parent, label, 15, TextAnchor.UpperCenter, Dim);   // 칸 이름은 아이템이 있어도 보이게
        Place(caption.rectTransform, x, y - size - 1, size, 18);
        view.onClick = () => ClickEquipSlot(view);
        view.onEnter = () => ShowTooltip(view, true);
        view.onExit = () => { if (hovered == view) HideTooltip(); };
        view.onBeginDrag = () => BeginDrag(view, true);
        view.onEndDrag = EndDrag;
        equipSlots[slot] = view;
    }

    Button MakeButton(RectTransform parent, string label, System.Action onClick, Vector2 bottomRight)
    {
        var img = MakeImage(parent, "Button", new Color(0.22f, 0.18f, 0.14f, 1f));
        img.raycastTarget = true;
        var btn = img.gameObject.AddComponent<Button>();
        var colors = btn.colors; colors.highlightedColor = new Color(1.6f, 1.4f, 1.1f); btn.colors = colors;
        btn.onClick.AddListener(() => onClick());
        var r = img.rectTransform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(1, 0);
        r.anchoredPosition = bottomRight;
        r.sizeDelta = new Vector2(120, 46);
        var t = MakeText(r, label, 22, TextAnchor.MiddleCenter, GameLog.Info);
        Stretch(t.rectTransform);
        return btn;
    }

    Image MakeImage(RectTransform parent, string name, Color color)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text MakeText(RectTransform parent, string text, int size, TextAnchor anchor, Color color)
    {
        var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size; t.alignment = anchor; t.color = color; t.text = text;
        t.raycastTarget = false;
        t.supportRichText = true;
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
