using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// 화면 효과: 맞으면 화면 가장자리가 붉게 번지고 카메라가 흔들린다.
// 체력이 30% 아래면 가장자리가 붉게 맥박친다.
public class ScreenEffects : MonoBehaviour
{
    public PlayerStats player;
    [Range(0f, 1f)] public float lowHpRatio = 0.3f;

    static ScreenEffects instance;
    Image vignette;
    float flash;          // 맞았을 때 번쩍임 (서서히 줄어듦)

    void Awake()
    {
        instance = this;
        Build();
    }

    // 플레이어가 맞았을 때. ratio = 피해량 / 최대 HP (클수록 세게)
    public static void PlayerHit(float ratio, float delay = 0f)
    {
        if (instance != null) instance.StartCoroutine(instance.Hit(ratio, delay));
    }

    IEnumerator Hit(float ratio, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        flash = Mathf.Max(flash, Mathf.Lerp(0.5f, 0.9f, Mathf.Clamp01(ratio * 8f)));
        CameraFollowOcclusion.Shake(Mathf.Lerp(0.15f, 0.45f, Mathf.Clamp01(ratio * 8f)), 0.25f);
    }

    void Update()
    {
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.6f);
        float low = 0f;
        if (player != null && !player.Dead && player.Hp < player.maxHp * lowHpRatio)
        {
            // 체력이 적을수록 더 진하고 빠르게 맥박친다
            float danger = 1f - player.Hp / (player.maxHp * lowHpRatio);
            low = (0.18f + 0.17f * danger) * (0.6f + 0.4f * Mathf.Sin(Time.time * Mathf.Lerp(3f, 7f, danger)));
        }
        var c = vignette.color;
        c.a = Mathf.Max(flash, low);
        vignette.color = c;
        vignette.enabled = c.a > 0.01f;
    }

    void Build()
    {
        var canvasGo = new GameObject("EffectsCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1;   // HUD·창보다 뒤
        vignette = new GameObject("Vignette", typeof(Image)).GetComponent<Image>();
        vignette.transform.SetParent(canvasGo.transform, false);
        vignette.raycastTarget = false;
        vignette.sprite = MakeVignette();
        vignette.color = new Color(0.75f, 0.05f, 0.03f, 0f);
        var r = vignette.rectTransform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
        vignette.enabled = false;
    }

    // 가운데는 투명, 가장자리로 갈수록 진해지는 원형 그라데이션
    static Sprite MakeVignette()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                px[y * size + x] = new Color(1, 1, 1, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
