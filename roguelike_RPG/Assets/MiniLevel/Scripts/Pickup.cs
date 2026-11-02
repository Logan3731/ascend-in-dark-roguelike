using UnityEngine;

// 바닥에 떨어진 재료 (나뭇가지·돌멩이). 밟으면 저절로 줍는다.
public class Pickup : Interactable
{
    public string itemId = "branch";
    public int count = 1;

    ItemDef Def => itemId == "stone" ? Items.Stone : Items.Branch;

    void Reset() { triggerOnEnter = true; }

    public override void Interact(PlayerStats player)
    {
        if (Used) return;
        if (!player.Bag.Add(Def, count)) { GameLog.Add("가방이 꽉 찼다.", GameLog.Warn); return; }
        Used = true;
        GameLog.Add(count > 1 ? $"{Def.name} {count}개를 주웠다." : $"{Def.name}{Korean.Josa(Def.name, "을", "를")} 주웠다.", GameLog.Good);
        DamagePopup.Spawn(DamagePopup.Above(player), $"+{Def.name}", DamagePopup.Heal, 0.8f);
        Destroy(gameObject);
    }
}
