using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 풀숲 (설계 A6). 밟고 지나가면 한 번 뒤진다: 골드·아이템·이슬(체력 회복)이 나올 때가 있다.
// 불(던진 횃불·불꽃 물약)에 타면 숨어 있던 골드는 바닥에 남고 아이템은 사라진다 (Fire).
public class Grass : Interactable
{
    public const float GoldChance = 0.3f;    // 풀마다 처음에 정해 둔다
    public const int GoldMin = 3, GoldMax = 9;
    public const float ItemChance = 0.2f;
    public const float DewChance = 0.15f;
    public const int DewHeal = 8;
    public const float ModelFootprint = 2.2f;   // 높이 약 2

    // 서 있는 풀은 시야를 가린다 (FogOfWar·GridNav.LineOfSight). 밟거나 타면 사라진다
    static readonly HashSet<Vector2Int> standing = new HashSet<Vector2Int>();
    public static bool BlocksSight(Vector2Int c) => standing.Contains(c);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => standing.Clear();

    protected override void Awake()
    {
        triggerOnEnter = true;
        base.Awake();
        standing.Add(Cell);
    }

    void OnDestroy() => standing.Remove(Cell);

    int gold;
    ItemDef item;
    bool dew;
    bool burned;

    public static Grass Create(Vector2Int cell, Transform parent)
    {
        var go = new GameObject("Grass");
        go.transform.SetParent(parent, false);
        go.transform.position = GridTile.CellToWorld(cell);
        go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
        var g = go.AddComponent<Grass>();
        g.triggerOnEnter = true;
        if (ResourceModel.Spawn("Grass", go.transform, ModelFootprint) == null) g.BuildPrimitive();
        g.Roll();
        return g;
    }

    void Roll()
    {
        if (Random.value < GoldChance) gold = Random.Range(GoldMin, GoldMax + 1);
        if (Random.value < ItemChance)
        {
            float r = Random.value;
            item = r < 0.35f ? Items.Food : r < 0.6f ? Items.Dart : r < 0.8f ? Items.Stone : Potions.RandomPotion();
        }
        dew = Random.value < DewChance;
    }

    void BuildPrimitive()
    {
        for (int i = 0; i < 9; i++)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(transform, false);
            float h = Random.Range(0.6f, 1.3f);
            b.transform.localPosition = new Vector3(Random.Range(-1f, 1f), h / 2, Random.Range(-1f, 1f));
            b.transform.localRotation = Quaternion.Euler(Random.Range(-15f, 15f), Random.Range(0, 360f), Random.Range(-15f, 15f));
            b.transform.localScale = new Vector3(0.08f, h, 0.3f);
            var c = new Color(0.35f + Random.value * 0.1f, 0.5f + Random.value * 0.15f, 0.2f);
            var m = b.GetComponent<Renderer>().material;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }

    // 밟았다: 한 번만 뒤진다
    public override void Interact(PlayerStats player)
    {
        if (Used || burned) return;
        Used = true;
        bool any = false;
        if (gold > 0) { player.AddGold(gold); GameLog.Add($"풀숲에서 골드 {gold}닢을 찾았다.", GameLog.Good); DamagePopup.Spawn(DamagePopup.Above(player), $"+{gold} 골드", DamagePopup.Big, 0.9f); any = true; }
        if (item != null)
        {
            if (player.Bag.Add(item)) GameLog.Add($"풀숲에서 {item.name}{Korean.Josa(item.name, "을", "를")} 찾았다.", GameLog.Good);
            else GameLog.Add("가방이 꽉 찼다.", GameLog.Warn);
            any = true;
        }
        if (dew)
        {
            GameLog.Add("풀잎의 이슬을 마셨다.", GameLog.Good);
            player.Heal(DewHeal);
            any = true;
        }
        if (any) GridClickMover.PlayAction("PickUp", GridClickMover.PickUpSeconds);
        gold = 0; item = null; dew = false;
        standing.Remove(Cell);
        StartCoroutine(Trample());
    }

    // 불에 탄다: 골드는 바닥에 남고 아이템·이슬은 사라진다
    public void Burn()
    {
        if (burned) return;
        burned = true;
        standing.Remove(Cell);
        if (gold > 0 && !Used) Pickup.SpawnGold(Cell, gold, transform.parent);
        StartCoroutine(BurnAway());
    }

    // 밟혀서 꺾이며 사라진다
    IEnumerator Trample()
    {
        var from = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            transform.localScale = new Vector3(from.x * (1f + t * 0.3f), from.y * (1f - t), from.z * (1f + t * 0.3f));
            yield return null;
        }
        Destroy(gameObject);
    }

    IEnumerator BurnAway()
    {
        var block = new MaterialPropertyBlock();
        var rs = GetComponentsInChildren<Renderer>();
        var from = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
        {
            block.SetColor("_BaseColor", Color.Lerp(new Color(1f, 0.45f, 0.1f), new Color(0.08f, 0.06f, 0.05f), t));
            foreach (var r in rs) if (r != null) r.SetPropertyBlock(block);
            transform.localScale = new Vector3(from.x, from.y * (1f - t * 0.8f), from.z);
            yield return null;
        }
        Destroy(gameObject);
    }
}
