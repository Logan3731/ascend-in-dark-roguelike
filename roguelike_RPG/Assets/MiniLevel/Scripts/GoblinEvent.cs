using UnityEngine;

// [이벤트 2] 굶주린 고블린: 식량을 나눠주면 조력자, 처치하면 골드를 얻지만 고블린 종족이 적대한다.
public class GoblinEvent : Interactable
{
    public int goldReward = 25;

    public bool Ally { get; private set; }

    public override void Interact(PlayerStats player)
    {
        if (Ally)
        {
            GameLog.Add("고블린이 반가운 듯 끄덕인다.", GameLog.Info);
            return;
        }
        if (Used) return;

        bool hasFood = player.Food > 0;
        DialogUI.Show("굶주린 고블린이 웅크린 채 떨고 있다.",
            new DialogUI.Choice(hasFood ? "식량을 나눠준다" : "식량을 나눠준다 (식량 없음)", () =>
            {
                Used = true;
                Ally = true;
                player.AddFood(-1);
                player.AddKarma(+1);
                GameLog.Add("고블린이 허겁지겁 먹고는 고개를 숙인다.", GameLog.Good);
            }, hasFood),
            new DialogUI.Choice("처치한다", () =>
            {
                Used = true;
                player.AddKarma(-1);
                player.AddGold(goldReward);
                GameLog.Add($"고블린을 처치했다. 골드 {goldReward}을 얻었다.", GameLog.Bad);
                GameLog.Add("어딘가에서 고블린들이 이를 갈고 있다…", GameLog.Warn);
                Destroy(gameObject);
            }),
            new DialogUI.Choice("지나친다", null));
    }
}
