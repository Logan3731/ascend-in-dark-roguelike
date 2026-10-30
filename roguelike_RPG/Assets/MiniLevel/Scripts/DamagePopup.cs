using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 머리 위로 떠올랐다가 사라지는 숫자 (피해량, 빗나감, 회복 등). 항상 카메라를 향한다.
public class DamagePopup : MonoBehaviour
{
    public static readonly Color Hit = new Color(1f, 0.92f, 0.6f);        // 내가 준 피해
    public static readonly Color Big = new Color(1f, 0.6f, 0.15f);        // 최대치로 때림
    public static readonly Color Hurt = new Color(1f, 0.3f, 0.25f);       // 내가 받은 피해
    public static readonly Color Miss = new Color(0.7f, 0.7f, 0.7f);
    public static readonly Color Heal = new Color(0.55f, 0.9f, 0.4f);
    public static readonly Color Poison = new Color(0.6f, 0.85f, 0.25f);

    Text text;
    Vector3 start;
    float scale;

    // delay: 공격 연출이 닿는 순간에 맞춰 띄우기 위한 대기 시간
    public static void Spawn(Vector3 worldPos, string msg, Color color, float size = 1f, float delay = 0f)
    {
        var go = new GameObject("Popup", typeof(Canvas));
        var p = go.AddComponent<DamagePopup>();
        p.Build(msg, color, size);
        // 같은 자리에 겹치지 않도록 살짝 흩뿌린다
        p.start = worldPos + new Vector3(Random.Range(-0.4f, 0.4f), 0, Random.Range(-0.4f, 0.4f));
        go.transform.position = p.start;
        p.StartCoroutine(p.Run(delay));
    }

    // 대상의 머리 위
    public static Vector3 Above(Component target, float extra = 0.6f)
    {
        var b = new Bounds(target.transform.position, Vector3.zero);
        foreach (var r in target.GetComponentsInChildren<Renderer>())
            if (r.enabled && !(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
        return new Vector3(target.transform.position.x, b.max.y + extra, target.transform.position.z);
    }

    void Build(string msg, Color color, float size)
    {
        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 8;
        var rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(300, 80);
        scale = 0.018f * size;
        rt.localScale = Vector3.one * scale;

        text = new GameObject("Text", typeof(Text), typeof(Outline)).GetComponent<Text>();
        text.transform.SetParent(rt, false);
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 48;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.color = color;
        text.text = msg;
        var ol = text.GetComponent<Outline>();
        ol.effectColor = new Color(0, 0, 0, 0.9f);
        ol.effectDistance = new Vector2(2, -2);
        var tr = text.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
    }

    IEnumerator Run(float delay)
    {
        text.enabled = false;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        text.enabled = true;
        const float life = 0.9f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / life)
        {
            // 처음엔 툭 튀어나오고(크게) 위로 떠오르며 흐려진다
            float pop = t < 0.15f ? Mathf.Lerp(1.6f, 1f, t / 0.15f) : 1f;
            transform.localScale = Vector3.one * scale * pop;
            transform.position = start + Vector3.up * (1.4f * (1f - (1f - t) * (1f - t)));
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
            var c = text.color; c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f; text.color = c;
            yield return null;
        }
        Destroy(gameObject);
    }
}
