using UnityEngine;

// 신비한 우물: 동전(골드 10)을 던지면 무작위로 보답한다 (체력·기력·허기 또는 아무 일 없음). 세 번 던지면 말라 버린다.
public class Well : Prop
{
    const int Price = 10;
    int coinsLeft = 3;

    public override void Interact(PlayerStats player)
    {
        if (coinsLeft <= 0) { GameLog.Add("우물이 말라 바닥이 드러났다.", GameLog.Info); return; }
        bool can = player.Gold >= Price;
        DialogUI.Show($"희미하게 푸른빛이 도는 우물이다. 물 위로 동전 몇 닢이 비친다.\n<color=#9e9480>(가진 골드 {player.Gold}, 우물에 남은 기운 {coinsLeft})</color>",
            new DialogUI.Choice(can ? $"동전을 던진다 ({Price} 골드)" : $"동전을 던진다 (골드 부족)", () => Toss(player), can),
            new DialogUI.Choice("그만둔다", null));
    }

    void Toss(PlayerStats player)
    {
        player.AddGold(-Price);
        coinsLeft--;
        SpendTurn(player, transform.position);
        float r = Random.value;
        if (r < 0.25f) { GameLog.Add("물이 따뜻하게 빛난다. 상처가 아문다.", GameLog.Good); player.Heal(25); }
        else if (r < 0.45f) { GameLog.Add("맑은 기운이 몸을 채운다.", GameLog.Good); player.RestoreMp(); }
        else if (r < 0.65f) { GameLog.Add("물 한 모금에 배가 든든해진다.", GameLog.Good); player.Feed(30); }
        else if (r < 0.75f) { var p = Potions.RandomPotion(); player.Bag.Add(p); GameLog.Add($"물속에서 {p.name}{Korean.Josa(p.name, "이", "가")} 떠올랐다!", GameLog.Good); }
        else GameLog.Add("퐁당. 동전이 가라앉았다… 아무 일도 일어나지 않았다.", GameLog.Info);
        if (coinsLeft <= 0) { GameLog.Add("우물의 빛이 꺼졌다.", GameLog.Info); Dim(); }
    }
}
