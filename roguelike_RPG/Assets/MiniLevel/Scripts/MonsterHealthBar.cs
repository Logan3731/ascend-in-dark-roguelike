using UnityEngine;
using UnityEngine.UI;

// 몬스터 머리 위 체력바 (레벨·이름 + 남은 체력). 몬스터 자식이 아닌 별도 오브젝트로 따라다닌다
// (분열 복제나 피격 연출에 같이 흔들리지 않도록).
public class MonsterHealthBar : MonoBehaviour
{
    const float Width = 160f, Height = 14f, Scale = 0.012f;

    Monster target;
    RectTransform fill;
    Text label;
    float headHeight;
    bool lastLit, litKnown;

    public static MonsterHealthBar Create(Monster m)
    {
        var go = new GameObject("HPBar_" + m.name, typeof(Canvas));
        var bar = go.AddComponent<MonsterHealthBar>();
        bar.target = m;
        bar.Build();
        bar.Refresh();
        return bar;
    }

    void Build()
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 5;
        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(Width, Height + 26f);
        root.localScale = Vector3.one * Scale;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label = new GameObject("Label", typeof(Text), typeof(Outline)).GetComponent<Text>();
        label.transform.SetParent(root, false);
        label.font = font; label.fontSize = 20; label.alignment = TextAnchor.LowerCenter;
        label.color = new Color(0.95f, 0.9f, 0.8f);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.GetComponent<Outline>().effectColor = Color.black;
        var lr = label.rectTransform;
        lr.anchorMin = new Vector2(0, 1); lr.anchorMax = new Vector2(1, 1); lr.pivot = new Vector2(0.5f, 1);
        lr.sizeDelta = new Vector2(0, 26); lr.anchoredPosition = Vector2.zero;

        var bg = new GameObject("Bg", typeof(Image)).GetComponent<Image>();
        bg.transform.SetParent(root, false);
        bg.color = new Color(0, 0, 0, 0.75f);
        var br = bg.rectTransform;
        br.anchorMin = new Vector2(0, 0); br.anchorMax = new Vector2(1, 0); br.pivot = new Vector2(0.5f, 0);
        br.sizeDelta = new Vector2(0, Height); br.anchoredPosition = Vector2.zero;

        var f = new GameObject("Fill", typeof(Image)).GetComponent<Image>();
        f.transform.SetParent(bg.transform, false);
        f.color = new Color(0.78f, 0.16f, 0.12f);
        fill = f.rectTransform;
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(2, 2); fill.offsetMax = new Vector2(-2, -2);

        UIKit.DrawOnTop(gameObject);   // 벽 뒤에 있어도 이름·체력이 보이게

        // 모델 꼭대기 높이
        var b = new Bounds(target.transform.position, Vector3.zero);
        foreach (var r in target.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        headHeight = b.max.y - target.transform.position.y + 0.4f;
    }

    public void Refresh()
    {
        if (target == null) return;
        float t = target.MaxHp > 0 ? (float)target.Hp / target.MaxHp : 0f;
        fill.anchorMax = new Vector2(t, 1f);
        // 빛 안이면 이름 흰색(약해짐), 어둠 속이면 빨간색(사나워짐)
        lastLit = target.InLight; litKnown = true;
        string nameColor = lastLit ? "#f2eee6" : "#ff4a3d";
        label.text = $"<color={nameColor}>{target.Label}</color>  {target.Hp}/{target.MaxHp}";
    }

    void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        if (!litKnown || target.InLight != lastLit) Refresh();
        transform.position = target.transform.position + Vector3.up * headHeight;
        var cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;   // 항상 카메라를 향한다
    }
}
