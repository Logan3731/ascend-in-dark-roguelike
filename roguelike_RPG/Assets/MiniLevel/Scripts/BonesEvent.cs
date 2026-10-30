using UnityEngine;

// [이벤트 4] 뼈와 유품: 앞서 쓰러진 자(=이전 복제)가 남긴 뼈. 유품을 챙기거나, 뼈를 묻어 준다(선).
// 밟고 지나갈 수 있고, 클릭하면 상호작용한다.
public class BonesEvent : Interactable
{
    public TowerMemory.Bones record;

    public override void Interact(PlayerStats player)
    {
        if (Used || record == null) return;
        string hint = record.gold > 0 ? "\n해진 주머니가 함께 놓여 있다." : "";
        DialogUI.Show($"누군가의 뼈다. 어딘가 낯익은 옷자락이 걸려 있다.{hint}",
            new DialogUI.Choice("유품을 챙긴다", () =>
            {
                Used = true;
                if (record.gold > 0)
                {
                    player.AddGold(record.gold);
                    GameLog.Add($"유품을 챙겼다. 골드 +{record.gold}", GameLog.Good);
                }
                else GameLog.Add("쓸 만한 것은 남아 있지 않았다.", GameLog.Info);
                record.gold = 0;
                TowerMemory.RemoveBones(record);
                FadeOut.Run(this, 1.5f);
            }),
            new DialogUI.Choice("뼈를 묻어 준다", () =>
            {
                Used = true;
                player.AddKarma(+1);
                GameLog.Add("흩어진 뼈를 모아 조용히 묻어 주었다.", GameLog.Info);
                TowerMemory.RemoveBones(record);
                FadeOut.Run(this, 1.5f);
            }),
            new DialogUI.Choice("그냥 둔다", null));
    }
}
