using System.Collections.Generic;
using UnityEngine;

// 불과 물의 규칙을 한 곳에 모은다 (설계 A6).
// 불씨: 던진 횃불(떨어진 칸), 불꽃 물약(깨진 칸과 주변 한 칸), 마신 불꽃 물약(나).
// - 풀: 탄다. 숨어 있던 골드는 남고 아이템은 사라진다 (Grass.Burn).
// - 사람·몬스터: 불이 붙어 몇 턴 동안 턴마다 화상 피해 (Burning).
// - 물: 물 칸엔 불이 붙지 않고, 불붙은 채 물에 들어가면 꺼진다 (WaterTile).
public static class Fire
{
    public const int TorchRadius = 0;      // 던진 횃불: 떨어진 칸만
    public const int PotionRadius = 1;     // 불꽃 물약: 주변 한 칸까지
    public const int BurnTurns = 3;        // 불이 붙으면 타는 턴
    public const int DrinkBurnTurns = 4;   // 불꽃 물약을 마시면
    public const int BurnDamage = 2;       // 턴마다 화상 피해

    // center에서 radius(칸) 안에 불이 붙는다. by: 불을 낸 사람 (몬스터를 쓰러뜨리면 경험이 간다)
    public static void Ignite(Vector2Int center, int radius, PlayerStats by = null)
    {
        foreach (var g in Object.FindObjectsByType<Grass>(FindObjectsSortMode.None))
            if (GridNav.Distance(g.Cell, center) <= radius && !WaterTile.At(g.Cell)) g.Burn();
        foreach (var m in Object.FindObjectsByType<Monster>(FindObjectsSortMode.None))
            if (m.enabled && !m.Dead && GridNav.Distance(m.Cell, center) <= radius) SetOnFire(m, BurnTurns, by);
        var p = Object.FindAnyObjectByType<PlayerStats>();
        if (p != null && !p.Dead && GridNav.Distance(GridTile.WorldToCell(p.transform.position), center) <= radius) SetOnFire(p, BurnTurns, p);
    }

    // 불을 붙인다 (물 위면 붙지 않는다)
    public static void SetOnFire(Component target, int turns, PlayerStats by)
    {
        if (target == null || WaterTile.At(GridTile.WorldToCell(target.transform.position))) return;
        var b = target.GetComponent<Burning>();
        bool fresh = b == null;
        if (fresh) b = target.gameObject.AddComponent<Burning>();
        b.turns = Mathf.Max(b.turns, turns);
        if (by != null) b.by = by;
        if (!fresh) return;
        if (target is PlayerStats ps) { GameLog.Add("몸에 불이 붙었다!", GameLog.Bad); ps.NotifyChanged(); }
        else if (target is Monster m) GameLog.Add($"{m.displayName}{Korean.Josa(m.displayName, "이", "가")} 불타오른다!", GameLog.Good);
    }

    public static int BurnTurnsOf(Component c) { var b = c != null ? c.GetComponent<Burning>() : null; return b != null ? b.turns : 0; }

    // 칸에 들어섰다: 물이면 불이 꺼진다 (플레이어는 WaterTile, 몬스터는 Monster.MoveTo가 부른다)
    public static void Entered(Component who, Vector2Int cell)
    {
        if (!WaterTile.At(cell)) return;
        var b = who != null ? who.GetComponent<Burning>() : null;
        if (b != null) b.Extinguish(true);
    }
}

// 불붙은 상태: 턴마다 화상 피해, 다 타거나 물에 들어가면 꺼진다. 몸 위에 작은 불꽃이 너울거린다.
public class Burning : MonoBehaviour
{
    public int turns;
    public PlayerStats by;
    GameObject flame;
    Light glow;

    void Start()
    {
        flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(flame.GetComponent<Collider>());
        flame.name = "BurnFlame";
        flame.transform.SetParent(transform, false);
        flame.transform.localPosition = new Vector3(0, 2.2f, 0);
        var m = flame.GetComponent<Renderer>().material;
        m.color = new Color(1f, 0.5f, 0.1f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.05f) * 4f);
        glow = new GameObject("BurnGlow").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = new Vector3(0, 2.5f, 0);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.55f, 0.2f);
        glow.range = 8f;
        glow.shadows = LightShadows.None;
    }

    void Update()
    {
        if (flame == null) return;
        float k = 0.8f + 0.25f * Mathf.PerlinNoise(Time.time * 6f, 0f);
        flame.transform.localScale = new Vector3(0.9f, 1.3f, 0.9f) * k;
        glow.intensity = 6f * k;
    }

    void OnEnable() => TurnManager.OnTurnEnded += Tick;
    void OnDisable() => TurnManager.OnTurnEnded -= Tick;

    void Tick(int turn)
    {
        var cell = GridTile.WorldToCell(transform.position);
        if (WaterTile.At(cell)) { Extinguish(true); return; }
        var p = GetComponent<PlayerStats>();
        var m = GetComponent<Monster>();
        if (p != null)
        {
            if (p.Dead) { Extinguish(false); return; }
            DamagePopup.Spawn(DamagePopup.Above(p), $"화상 -{Fire.BurnDamage}", DamagePopup.Starve, 0.9f);
            p.Damage(Fire.BurnDamage);
        }
        else if (m != null)
        {
            if (m.Dead || !m.enabled) { Extinguish(false); return; }
            m.TakeBurnDamage(Fire.BurnDamage, by);
        }
        if (--turns <= 0) Extinguish(false);
    }

    public void Extinguish(bool byWater)
    {
        var p = GetComponent<PlayerStats>();
        if (byWater && p != null) GameLog.Add("물에 들어가 불이 꺼졌다.", GameLog.Good);
        else if (p != null && !p.Dead) GameLog.Add("불이 꺼졌다.", GameLog.Info);
        if (flame != null) Destroy(flame);
        if (glow != null) Destroy(glow.gameObject);
        turns = 0;
        Destroy(this);
        if (p != null) p.NotifyChanged();
    }
}
