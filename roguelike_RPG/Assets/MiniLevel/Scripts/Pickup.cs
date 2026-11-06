using UnityEngine;

// 바닥에 떨어진 것 (나뭇가지·돌멩이·다트·골드). 밟으면 저절로 줍는다.
public class Pickup : Interactable
{
    public string itemId = "branch";
    public int count = 1;

    ItemDef Def => itemId switch { "stone" => Items.Stone, "dart" => Items.Dart, "food" => Items.Food, _ => Items.Branch };

    void Reset() { triggerOnEnter = true; }

    public override void Interact(PlayerStats player)
    {
        if (Used) return;
        if (itemId == "gold")
        {
            Used = true;
            player.AddGold(count);
            Sfx.Play("coins");
            GridClickMover.PlayAction("PickUp", GridClickMover.PickUpSeconds);
            GameLog.Add($"골드 {count}닢을 주웠다.", GameLog.Good);
            DamagePopup.Spawn(DamagePopup.Above(player), $"+{count} 골드", DamagePopup.Big, 0.9f);
            Destroy(gameObject);
            return;
        }
        if (!player.Bag.Add(Def, count)) { GameLog.Add("가방이 꽉 찼다.", GameLog.Warn); return; }
        Used = true;
        Sfx.Play("pickup");
        GridClickMover.PlayAction("PickUp", GridClickMover.PickUpSeconds);
        GameLog.Add(count > 1 ? $"{Def.name} {count}개를 주웠다." : $"{Def.name}{Korean.Josa(Def.name, "을", "를")} 주웠다.", GameLog.Good);
        DamagePopup.Spawn(DamagePopup.Above(player), $"+{Def.name}", DamagePopup.Heal, 0.8f);
        Destroy(gameObject);
    }

    // 바닥에 떨어진 물건 하나 (준보스의 식량 등): 작은 꾸러미 모양
    public static Pickup SpawnItem(Vector2Int cell, string id, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Drop_" + id;
        go.transform.SetParent(parent, false);
        go.transform.position = GridTile.CellToWorld(cell) + Vector3.up * 0.25f;
        go.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
        go.transform.localScale = new Vector3(0.7f, 0.45f, 0.55f);
        var m = go.GetComponent<Renderer>().material;
        m.color = new Color(0.55f, 0.35f, 0.2f);
        var p = go.AddComponent<Pickup>();
        p.itemId = id;
        p.triggerOnEnter = true;
        return p;
    }

    // 바닥의 골드 더미 (불탄 풀·보너스 방)
    public static Pickup SpawnGold(Vector2Int cell, int amount, Transform parent = null)
    {
        var go = new GameObject("Gold");
        go.transform.SetParent(parent, false);
        go.transform.position = GridTile.CellToWorld(cell);
        for (int i = 0; i < 4; i++)
        {
            var coin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(coin.GetComponent<Collider>());
            coin.transform.SetParent(go.transform, false);
            coin.transform.localPosition = new Vector3(Random.Range(-0.4f, 0.4f), 0.04f + i * 0.06f, Random.Range(-0.4f, 0.4f));
            coin.transform.localScale = new Vector3(0.45f, 0.03f, 0.45f);
            var m = coin.GetComponent<Renderer>().material;
            m.color = new Color(1f, 0.78f, 0.25f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(0.6f, 0.4f, 0.05f));
        }
        var p = go.AddComponent<Pickup>();
        p.itemId = "gold";
        p.count = amount;
        p.triggerOnEnter = true;
        return p;
    }
}
