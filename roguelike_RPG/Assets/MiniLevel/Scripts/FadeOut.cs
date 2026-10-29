using System.Collections;
using UnityEngine;

// 오브젝트를 서서히 투명하게 만든 뒤 없앤다 (열어 본 상자, 쓰러진 몬스터).
public static class FadeOut
{
    public static void Run(MonoBehaviour owner, float seconds, float delay = 0f)
    {
        owner.StartCoroutine(Fade(owner.gameObject, seconds, delay));
    }

    static IEnumerator Fade(GameObject go, float seconds, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // 막고 있던 칸과 클릭 판정을 먼저 풀어 준다
        foreach (var b in go.GetComponentsInChildren<GridBlocker>()) b.enabled = false;
        foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;

        var renderers = go.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var m in r.materials) MakeTransparent(m);
        }

        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            foreach (var r in renderers)
                foreach (var m in r.materials) SetAlpha(m, 1f - t);
            yield return null;
        }
        Object.Destroy(go);
    }

    // URP Lit 재질을 실행 중에 반투명으로 바꾼다
    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static void SetAlpha(Material m, float a)
    {
        if (m.HasProperty("_BaseColor")) { var c = m.GetColor("_BaseColor"); c.a = a; m.SetColor("_BaseColor", c); }
        else if (m.HasProperty("_Color")) { var c = m.color; c.a = a; m.color = c; }
    }
}
