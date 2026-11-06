using UnityEngine;

// 책장: 뒤지면(한 턴) 물약에 대한 기록을 찾을 때가 있다 — 아직 모르는 색 하나의 정체가 드러난다.
public class Bookshelf : Prop
{
    public const float StrengthBookChance = 0.1f;   // 힘의 책 (읽으면 힘 +1)

    public override void Interact(PlayerStats player)
    {
        if (Used) { GameLog.Add("이미 다 뒤져 본 책장이다.", GameLog.Info); return; }
        Used = true;
        SpendTurn(player, transform.position);
        float r = Random.value;
        string found = r < 0.6f ? Potions.RevealOne() : null;
        if (found != null)
        {
            GameLog.Add($"낡은 연금술 기록을 찾았다: {found}!", GameLog.Good);
            DamagePopup.Spawn(DamagePopup.Above(player), "기록 발견!", DamagePopup.Big, 0.9f);
        }
        else if (r < 0.8f)
        {
            int g = Random.Range(5, 13);
            player.AddGold(g);
            GameLog.Add($"책 사이에 끼워진 동전을 찾았다. 골드 +{g}", GameLog.Good);
        }
        else if (r < 0.8f + StrengthBookChance)
        {
            GameLog.Add("낡은 무술서를 찾아 읽었다.", GameLog.Good);
            player.AddStrength(1);
        }
        else GameLog.Add("책장을 뒤졌지만 바스러진 종이뿐이다.", GameLog.Info);
        Dim();
    }
}
