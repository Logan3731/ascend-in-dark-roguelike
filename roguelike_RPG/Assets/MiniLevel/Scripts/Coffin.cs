using UnityEngine;

// 관: 열면(악) 부장품을 얻지만 가끔 독기가 새어 나온다. 기도하면(선) 마음이 놓여 체력이 조금 찬다.
public class Coffin : Prop
{
    public override void Interact(PlayerStats player)
    {
        if (Used) { GameLog.Add("고요히 잠든 관이다.", GameLog.Info); return; }
        DialogUI.Show("오래전에 안장된 관이다. 무거운 뚜껑에 먼지가 쌓여 있다.",
            new DialogUI.Choice("뚜껑을 연다", () => Open(player)),
            new DialogUI.Choice("기도한다", () => Pray(player)),
            new DialogUI.Choice("지나친다", null));
    }

    void Open(PlayerStats player)
    {
        Used = true;
        player.AddKarma(-1);
        SpendTurn(player, transform.position, 0.5f);
        GameLog.Add("망자의 안식을 깨고 뚜껑을 밀어냈다…", GameLog.Warn);
        if (Random.value < 0.3f)
        {
            GameLog.Add("관 안에서 썩은 독기가 피어올랐다!", GameLog.Bad);
            player.Poison(5);
        }
        int g = Random.Range(12, 26);
        player.AddGold(g);
        string got = $"골드 {g}";
        if (Random.value < 0.6f) { var p = Potions.RandomPotion(); player.Bag.Add(p); got += $", {p.name}"; }
        GameLog.Add($"부장품을 챙겼다: {got}", GameLog.Good);
        Dim();
    }

    void Pray(PlayerStats player)
    {
        Used = true;
        player.AddKarma(+1);
        SpendTurn(player, transform.position);
        GameLog.Add("잠시 눈을 감고 이름 모를 망자를 위해 기도했다. 마음이 차분해진다.", GameLog.Good);
        player.Heal(10);
        Dim();
    }
}
