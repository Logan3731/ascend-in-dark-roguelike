using UnityEngine;

// [이벤트 2] 굶주린 고블린: 식량을 나눠주면 조력자가 되어 골드를 받고 물건을 판다 (골드를 쓸 곳).
// 처치하면 골드를 바로 얻지만 상인을 잃고, 고블린 종족이 적대한다.
public class GoblinEvent : Interactable
{
    public int goldReward = 25;

    public bool Ally { get; private set; }

    ShopUI.Ware[] wares;   // 고블린 상인의 물건: 가격과 남은 수량 (깨어날 때마다 새로)

    void Start()
    {
        wares = new[]
        {
            new ShopUI.Ware { icon = "food", label = "식량", price = 12, stock = 3, give = p => p.AddFood(1) },
            new ShopUI.Ware { icon = "potion_red", label = "수상한 물약", price = 20, stock = 2, give = p => p.Bag.Add(Potions.RandomPotion()) },
            new ShopUI.Ware { icon = "torch", label = "낡은 횃불", price = 15, stock = 2, give = p => p.GiveTorch() },
            new ShopUI.Ware { icon = "key", label = "녹슨 열쇠", price = 60, stock = 1, give = p => p.Bag.Add(Items.Key) },
            // [임시·테스트용] 보조칸 확인용 방패. 방패·보조무기를 얻는 경로(상자·상인·제작)를 정하면 빼거나 옮긴다.
            new ShopUI.Ware { icon = "shield", label = "나무 방패", price = 25, stock = 1, give = p => { if (!p.Bag.Add(Items.Shield)) GameLog.Add("가방이 가득 찼다.", GameLog.Warn); } },
        };
    }

    public override void Interact(PlayerStats player)
    {
        if (Ally) { OpenShop(player); return; }
        if (Used) return;

        bool hasFood = player.Food > 0;
        DialogUI.Show("굶주린 고블린이 웅크린 채 떨고 있다.\n허리춤에 낡은 보따리가 매달려 있다.",
            new DialogUI.Choice(hasFood ? "식량을 나눠준다" : "식량을 나눠준다 (식량 없음)", () =>
            {
                Used = true;
                Ally = true;
                player.AddFood(-1);
                player.AddKarma(+1);
                GameLog.Add("고블린이 허겁지겁 먹고는 고개를 숙인다.", GameLog.Good);
                DialogUI.Show("\"…은혜는 갚는다. 금화가 있으면 보따리 속 물건을 내주지.\"",
                    new DialogUI.Choice("물건을 본다", () => OpenShop(player)),
                    new DialogUI.Choice("나중에", null));
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

    void OpenShop(PlayerStats player) => ShopUI.Open(player, wares);
}
