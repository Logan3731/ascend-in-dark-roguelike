using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 상자 3종(작은/큰/엘리트). 열면 뚜껑이 젖혀지고 내용물을 보여 준 뒤, [확인]을 누르면 서서히 사라진다.
public class ChestEvent : Interactable
{
    public enum Tier { Small, Big, Elite }

    public Tier tier = Tier.Big;
    public bool givesTorch;          // 미로 입구의 첫 상자: 낡은 횃불 + 튜토리얼 문구
    public int keys;                 // 잠긴 문 열쇠 (미로를 만들 때 정해진다)
    public Transform lid;            // 경첩(뒤쪽 위 모서리)이 원점인 뚜껑
    public float openAngle = 110f;

    public override void Interact(PlayerStats player)
    {
        if (Used) return;
        Used = true;
        GridClickMover.PlayAnim("PickUp");
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + 0.7f);   // 뚜껑이 열리고 창이 뜰 때까지 대기
        StartCoroutine(Open(player));
    }

    IEnumerator Open(PlayerStats player)
    {
        if (lid == null) lid = FindDeep(transform, "Lid");
        if (lid != null)
        {
            var closed = lid.localRotation;
            var open = closed * Quaternion.AngleAxis(-openAngle, Vector3.right);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
            {
                float e = 1f - (1f - t) * (1f - t) * (1f - t);   // 처음엔 빠르게, 끝에서 천천히
                lid.localRotation = Quaternion.SlerpUnclamped(closed, open, e);
                yield return null;
            }
            lid.localRotation = open;
        }
        yield return new WaitForSeconds(0.15f);

        var loot = RollLoot(player);
        string title = tier == Tier.Small ? "작은 상자" : tier == Tier.Big ? "큰 상자" : "엘리트 상자";
        DialogUI.Show($"{title}를 열었다.\n{string.Join(",  ", loot)}",
            new DialogUI.Choice("확인", () =>
            {
                if (givesTorch) GameLog.Add("[F] 키로 횃불을 들면 주변이 밝아진다.", GameLog.Warn);
                FadeOut.Run(this, 1.2f);
            }));
    }

    // 내용물을 정하고 바로 지급한다. 화면에 보여 줄 문구 목록을 돌려준다.
    List<string> RollLoot(PlayerStats player)
    {
        var got = new List<string>();
        int food = 0, gold = 0, potions = 0;
        bool torch = givesTorch;
        // 골드는 고블린 상인에게, 물약은 마셔 봐야 아는 도박 — 상자마다 고를 거리가 생기게
        switch (tier)
        {
            case Tier.Small:
                float r = Random.value;
                if (r < 0.4f) food = 1; else if (r < 0.75f) potions = 1; else gold = Random.Range(8, 15);
                break;
            case Tier.Big:
                food = Random.Range(0, 2);
                gold = Random.Range(10, 21);
                potions = 1;
                break;
            case Tier.Elite:
                food = 2;
                gold = Random.Range(40, 61);
                potions = 2;
                torch = true;
                break;
        }

        if (torch) { player.GiveTorch(); got.Add("낡은 횃불"); }
        for (int i = 0; i < potions; i++)
        {
            var pot = Potions.RandomPotion();
            player.Bag.Add(pot);
            got.Add(pot.name);
        }
        if (food > 0) { player.AddFood(food); got.Add($"식량 {food}"); }
        if (gold > 0) { player.AddGold(gold); got.Add($"골드 {gold}"); }
        if (keys > 0) { player.Bag.Add(Items.Key, keys); got.Add(keys > 1 ? $"녹슨 열쇠 {keys}" : "녹슨 열쇠"); }
        GameLog.Add($"얻었다: {string.Join(", ", got)}", GameLog.Good);
        return got;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name || t.name.StartsWith(name + ".")) return t;   // Blender 중복 이름(Lid.001)도 허용
        return null;
    }
}
