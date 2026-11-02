using UnityEngine;

// 무너진 벽: 곡괭이가 있으면 파낼 수 있다 (세 번 = 세 턴, 시끄러워서 근처의 잠든 적이 깰 수 있다).
// 너머에는 숨겨진 방이 있다. 입구의 곡괭이를 챙길지 말지가 여기서 의미를 가진다.
public class Rubble : Prop
{
    int hitsLeft = 3;

    public override void Interact(PlayerStats player)
    {
        if (player.Bag.Count(Items.Pickaxe) <= 0)
        {
            GameLog.Add("무너진 돌무더기가 길을 막고 있다. 곡괭이가 있으면 파낼 수 있을 것 같다.", GameLog.Info);
            return;
        }
        hitsLeft--;
        SpendTurn(player, transform.position, 0.35f);
        CameraFollowOcclusion.Shake(0.15f, 0.15f);
        WakeNearby();
        if (hitsLeft > 0)
        {
            GameLog.Add($"곡괭이로 돌무더기를 내리쳤다. (남은 {hitsLeft}번)", GameLog.Info);
            return;
        }
        GameLog.Add("돌무더기가 무너지며 숨겨진 통로가 드러났다!", GameLog.Good);
        Used = true;
        GetComponent<GridBlocker>().enabled = false;
        Destroy(gameObject, 0.1f);
    }

    // 곡괭이 소리에 근처(5칸)의 잠든 적이 깰 수 있다
    void WakeNearby()
    {
        foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
            if (m.Asleep && !m.Dead && GridNav.Distance(m.Cell, Cell) <= 5 && Random.value < 0.25f) m.WakeFromNoise();
    }
}
