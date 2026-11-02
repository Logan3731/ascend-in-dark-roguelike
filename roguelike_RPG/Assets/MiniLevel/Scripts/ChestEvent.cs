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
        int food = 0, gold = 0;
        bool torch = givesTorch;
        switch (tier)
        {
            case Tier.Small:
                if (Random.value < 0.6f) food = 1; else gold = Random.Range(5, 11);
                break;
            case Tier.Big:
                food = Random.Range(1, 3);
                gold = Random.Range(10, 21);
                break;
            case Tier.Elite:
                food = 3;
                gold = Random.Range(40, 61);
                torch = true;
                break;
        }

        if (torch) { player.GiveTorch(); got.Add("낡은 횃불"); }
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
