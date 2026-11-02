using System.Collections;
using UnityEngine;

// 낡은 항아리. 다가가서 깨면(한 턴) 무엇이 나올지 모른다 — 대개 빈 것, 가끔 식량·물약·골드·돌, 드물게 쥐가 튀어나온다.
// 탐험하는 동안 "깨 볼까?"가 자주 생기게 하는 작은 보상 (Darkness Survival의 항아리·풀숲 뒤지기에서 따옴).
[RequireComponent(typeof(GridBlocker))]
public class Pot : Interactable
{
    public override void Interact(PlayerStats player)
    {
        if (Used) return;
        Used = true;
        var mover = player.GetComponent<GridClickMover>();
        if (mover != null) mover.FaceTo(transform.position);
        var pc = GridTile.WorldToCell(player.transform.position);
        var cell = Cell;
        GetComponent<GridBlocker>().enabled = false;

        // 깨는 데 한 턴
        TurnResolver.BeginTurn(pc, mover != null ? mover.moveSeconds : 0.3f);
        Roll(player, cell);
        TurnResolver.ResolveAttacks(player, null);
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + 0.3f);
        StartCoroutine(Shatter());
    }

    void Roll(PlayerStats player, Vector2Int cell)
    {
        float r = Random.value;
        if (r < 0.35f) GameLog.Add("항아리를 깼다. 텅 비어 있다.", GameLog.Info);
        else if (r < 0.55f) { player.AddFood(1); Found(player, "식량"); }
        else if (r < 0.72f) { var p = Potions.RandomPotion(); player.Bag.Add(p); Found(player, p.name); }
        else if (r < 0.84f) { int g = Random.Range(4, 10); player.AddGold(g); Found(player, $"골드 {g}"); }
        else if (r < 0.94f) { player.Bag.Add(Items.Stone, 2); Found(player, "돌멩이 2"); }
        else
        {
            GameLog.Add("항아리를 깨자 쥐가 튀어나왔다!", GameLog.Bad);
            var gen = FloorGenerator.Instance;
            if (gen != null && gen.rat != null) Instantiate(gen.rat, GridTile.CellToWorld(cell), Quaternion.Euler(0, Random.Range(0, 360f), 0));
        }
    }

    static void Found(PlayerStats player, string what)
    {
        GameLog.Add($"항아리를 깼다. {what}{Korean.Josa(what, "이", "가")} 들어 있었다!", GameLog.Good);
        DamagePopup.Spawn(DamagePopup.Above(player), $"+{what}", DamagePopup.Heal, 0.85f);
    }

    // 깨지는 연출: 조각이 튀고 몸체는 주저앉아 사라진다
    IEnumerator Shatter()
    {
        var mat = GetComponentInChildren<Renderer>() != null ? GetComponentInChildren<Renderer>().sharedMaterial : null;
        for (int i = 0; i < 7; i++)
        {
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(shard.GetComponent<Collider>());
            shard.transform.position = transform.position + Vector3.up * 0.8f;
            shard.transform.localScale = Vector3.one * Random.Range(0.15f, 0.35f);
            shard.GetComponent<Renderer>().material.color = new Color(0.55f, 0.36f, 0.22f);
            var rb = shard.AddComponent<Rigidbody>();
            rb.linearVelocity = new Vector3(Random.Range(-3f, 3f), Random.Range(3f, 6f), Random.Range(-3f, 3f));
            Destroy(shard, 1.2f);
        }
        var start = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.2f)
        {
            transform.localScale = new Vector3(start.x * (1f + t * 0.3f), start.y * (1f - t), start.z * (1f + t * 0.3f));
            yield return null;
        }
        Destroy(gameObject);
    }
}
