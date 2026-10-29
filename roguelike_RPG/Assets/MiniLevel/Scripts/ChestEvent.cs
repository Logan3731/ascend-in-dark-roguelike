using UnityEngine;

// 미로 입구 근처 상자: 첫 횃불. 얻는 순간 횃불 튜토리얼 문구가 뜬다.
public class ChestEvent : Interactable
{
    public int food = 1;

    public override void Interact(PlayerStats player)
    {
        if (Used)
        {
            GameLog.Add("빈 상자다.", GameLog.Info);
            return;
        }
        Used = true;
        GameLog.Add("상자를 열었다. 낡은 횃불이 들어 있다.", GameLog.Good);
        player.GiveTorch();
        if (food > 0)
        {
            player.AddFood(food);
            GameLog.Add($"식량 {food}개를 얻었다.", GameLog.Good);
        }
        GameLog.Add("[F] 키로 횃불을 들면 주변이 밝아진다.", GameLog.Warn);
    }
}
