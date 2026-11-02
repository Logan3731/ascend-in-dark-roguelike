using System.Collections;
using UnityEngine;

// 바닥에 던져 둔 횃불. 남은 내구도만큼 타면서 주변(반경 2.5칸)을 밝힌다 → 그 안의 적은 약해진다.
// 밟으면 다시 줍는다 (남은 내구도 그대로). 다 타면 꺼져서 사라진다.
// 던지는 쪽은 Thrower가 맡는다.
public class ThrownTorch : Interactable, LightSources.ISource
{
    public int durability;

    public bool IsOn => durability > 0;
    public float RadiusCells => 2.5f;

    Light glow;

    public static ThrownTorch Create(Vector2Int cell, int durability)
    {
        var go = new GameObject("ThrownTorch");
        go.transform.position = GridTile.CellToWorld(cell);
        var t = go.AddComponent<ThrownTorch>();
        t.triggerOnEnter = true;
        t.durability = durability;
        t.Build();
        return t;
    }

    void Build()
    {
        // 바닥에 비스듬히 누운 막대 + 불꽃 + 빛
        var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(stick.GetComponent<Collider>());
        stick.transform.SetParent(transform, false);
        stick.transform.localPosition = new Vector3(0, 0.15f, 0);
        stick.transform.localRotation = Quaternion.Euler(80, Random.Range(0, 360f), 0);
        stick.transform.localScale = new Vector3(0.14f, 0.7f, 0.14f);
        stick.GetComponent<Renderer>().material.color = new Color(0.32f, 0.2f, 0.1f);

        var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(flame.GetComponent<Collider>());
        flame.name = "Flame";
        flame.transform.SetParent(stick.transform, false);
        flame.transform.localPosition = new Vector3(0, 1f, 0);
        flame.transform.localScale = new Vector3(2.6f, 0.55f, 2.6f);
        var m = flame.GetComponent<Renderer>().material;
        m.color = new Color(1f, 0.55f, 0.15f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.1f) * 4f);

        glow = new GameObject("Glow").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.transform.localPosition = new Vector3(0, 2.5f, 0);
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.62f, 0.3f);
        glow.range = 12f;
        glow.intensity = 20f;
        glow.shadows = LightShadows.None;
    }

    void OnEnable()
    {
        LightSources.Register(this);
        TurnManager.OnTurnEnded += Burn;
    }

    void OnDisable()
    {
        LightSources.Unregister(this);
        TurnManager.OnTurnEnded -= Burn;
    }

    void Update()
    {
        if (glow != null) glow.intensity = 18f + Mathf.PerlinNoise(Time.time * 6f, 0f) * 6f;   // 일렁이는 불빛
    }

    void Burn(int turn)
    {
        if (--durability > 0) return;
        GameLog.Add("던져 둔 횃불이 다 타서 꺼졌다.", GameLog.Info);
        Destroy(gameObject);
    }

    public override void Interact(PlayerStats player)
    {
        if (Used || durability <= 0) return;
        Used = true;
        player.PickUpTorch(durability);
        GameLog.Add("던져 둔 횃불을 다시 주웠다.", GameLog.Good);
        Destroy(gameObject);
    }
}
