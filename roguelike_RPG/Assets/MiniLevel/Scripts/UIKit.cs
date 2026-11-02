using System;
using UnityEngine;
using UnityEngine.UI;

// 코드로 UI를 만들 때 쓰는 공통 도구 (타이틀·일시정지 메뉴 등)
public static class UIKit
{
    public static readonly Color Gold = new Color(0.86f, 0.72f, 0.42f);
    public static readonly Color Dim = new Color(0.62f, 0.58f, 0.5f);
    public static readonly Color ButtonColor = new Color(0.16f, 0.13f, 0.1f, 0.92f);

    static Font font;
    public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

    // 월드 공간 UI(머리 위 체력바·데미지 숫자)가 벽에 가려지지 않도록 항상 위에 그리는 재질
    static Material worldOnTop;
    public static Material WorldOnTop
    {
        get
        {
            if (worldOnTop == null)
            {
                worldOnTop = new Material(Shader.Find("UI/Default"));
                worldOnTop.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            return worldOnTop;
        }
    }

    public static void DrawOnTop(GameObject root)
    {
        foreach (var g in root.GetComponentsInChildren<Graphic>(true)) g.material = WorldOnTop;
    }

    public static RectTransform Canvas(Transform parent, string name, int order)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(parent, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        return (RectTransform)go.transform;
    }

    public static Image Image(RectTransform parent, string name, Color color, bool raycast = false)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        img.transform.SetParent(parent, false);
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    public static Text Text(RectTransform parent, string text, int size, TextAnchor anchor, Color color)
    {
        var t = new GameObject("Text", typeof(Text)).GetComponent<Text>();
        t.transform.SetParent(parent, false);
        t.font = Font; t.fontSize = size; t.alignment = anchor; t.color = color; t.text = text;
        t.raycastTarget = false;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    public static Button Button(RectTransform parent, string label, int size, Action onClick)
    {
        var img = Image(parent, "Button", ButtonColor, true);
        var btn = img.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.9f, 1.6f, 1.2f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick());
        var ol = img.gameObject.AddComponent<Outline>();
        ol.effectColor = new Color(0.55f, 0.45f, 0.3f, 0.7f);
        var t = Text(img.rectTransform, label, size, TextAnchor.MiddleCenter, new Color(0.92f, 0.88f, 0.8f));
        Stretch(t.rectTransform);
        return btn;
    }

    // 기준점(anchor)과 위치·크기를 한 번에
    public static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = r.pivot = anchor;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    public static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }
}
