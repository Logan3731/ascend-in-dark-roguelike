using UnityEngine;

// 물약이 만드는 잠깐의 빛. 정해진 턴 동안 빛 구역이 된다 → 그 안의 적은 약해지고 박쥐는 피한다.
// - 던진 밝은 눈·불꽃 물약: 터진 자리에 남는다.
// - 마신 밝은 눈의 물약: 마신 사람을 따라다닌다 (follow).
// 던진 횃불과 달리 주울 수 없고, 시간이 지나면 사라진다.
public class PotionGlow : MonoBehaviour, LightSources.ISource
{
    int turnsLeft;
    float radius;
    Transform follow;
    Light glow;
    float baseIntensity;

    public bool IsOn => turnsLeft > 0;
    public Vector2Int Cell => GridTile.WorldToCell(transform.position);
    public float RadiusCells => radius;

    public static PotionGlow Create(Vector2Int cell, int turns, Color color, Transform follow = null, float radius = 2.5f)
    {
        var go = new GameObject("PotionGlow");
        go.transform.position = GridTile.CellToWorld(cell);
        var g = go.AddComponent<PotionGlow>();
        g.turnsLeft = turns;
        g.radius = radius;
        g.follow = follow;
        g.glow = new GameObject("Glow").AddComponent<Light>();
        g.glow.transform.SetParent(go.transform, false);
        g.glow.transform.localPosition = new Vector3(0, 2.5f, 0);
        g.glow.type = LightType.Point;
        g.glow.color = color;
        g.glow.range = radius * 5f;
        g.glow.shadows = LightShadows.None;
        g.baseIntensity = 22f;
        return g;
    }

    void OnEnable()
    {
        LightSources.Register(this);
        TurnManager.OnTurnEnded += Tick;
    }

    void OnDisable()
    {
        LightSources.Unregister(this);
        TurnManager.OnTurnEnded -= Tick;
    }

    void Update()
    {
        if (follow != null) transform.position = follow.position;
        if (glow != null) glow.intensity = baseIntensity * (0.8f + Mathf.PerlinNoise(Time.time * 5f, 0f) * 0.4f);
    }

    void Tick(int turn)
    {
        if (--turnsLeft > 0) return;
        if (follow != null) GameLog.Add("눈앞의 빛이 사그라들었다.", GameLog.Info);
        Destroy(gameObject);
    }
}
