using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 상태창·소지품의 아이템 칸 하나: 아이콘 + 개수, 마우스 올림/클릭
public class InventorySlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public ItemDef def;
    public Action onClick, onHover;
    Image bg, icon;
    Text count, nameText, emptyLabel;

    public static InventorySlotView Create(RectTransform parent, Font font, float x, float y, float size)
    {
        var go = new GameObject("Slot", typeof(Image));
        go.transform.SetParent(parent, false);
        var r = (RectTransform)go.transform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, y);
        r.sizeDelta = new Vector2(size, size);
        var v = go.AddComponent<InventorySlotView>();
        v.bg = go.GetComponent<Image>();
        v.bg.color = InventoryUI.SlotColor;
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(0.4f, 0.33f, 0.24f, 0.7f);

        v.icon = new GameObject("Icon", typeof(Image)).GetComponent<Image>();
        v.icon.transform.SetParent(r, false);
        v.icon.raycastTarget = false;
        v.icon.preserveAspect = true;
        var ir = v.icon.rectTransform;
        ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
        ir.offsetMin = new Vector2(6, 6); ir.offsetMax = new Vector2(-6, -6);

        v.nameText = MakeLabel(r, font, 14, TextAnchor.MiddleCenter, GameLog.Info);
        v.emptyLabel = MakeLabel(r, font, 16, TextAnchor.MiddleCenter, new Color(0.45f, 0.4f, 0.33f));
        v.count = MakeLabel(r, font, 18, TextAnchor.LowerRight, Color.white);
        var cr = v.count.rectTransform;
        cr.offsetMin = new Vector2(4, 2); cr.offsetMax = new Vector2(-6, -2);
        v.count.gameObject.AddComponent<Outline>().effectColor = Color.black;
        return v;
    }

    static Text MakeLabel(RectTransform parent, Font font, int size, TextAnchor anchor, Color color)
    {
        var t = new GameObject("Label", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = font; t.fontSize = size; t.alignment = anchor; t.color = color;
        t.raycastTarget = false;
        var r = t.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        return t;
    }

    public void SetEmptyLabel(string label) => emptyLabel.text = label;

    public void Set(ItemDef item, int n)
    {
        def = item;
        var sprite = item != null ? item.Icon : null;
        icon.enabled = sprite != null;
        icon.sprite = sprite;
        // 아이콘이 없으면 이름으로 대신 보여 준다
        nameText.text = item != null && sprite == null ? item.name : "";
        emptyLabel.enabled = item == null;
        count.text = item != null && n > 1 ? n.ToString() : "";
    }

    public void OnPointerEnter(PointerEventData e) { bg.color = InventoryUI.SlotHover; onHover?.Invoke(); }
    public void OnPointerExit(PointerEventData e) { bg.color = InventoryUI.SlotColor; }
    public void OnPointerClick(PointerEventData e) { onClick?.Invoke(); }
}
