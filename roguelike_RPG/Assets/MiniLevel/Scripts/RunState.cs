using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 한 번의 깨어남(회차) 동안 층을 오르며 이어지는 것: 지금 몇 층인지, 주인공의 상태(체력·소지품·레벨…).
// 층을 오를 때 상태를 잠깐 맡겨 두고 씬을 다시 연다 → 새 층의 PlayerStats가 깨어나며 되돌려 받는다.
// 죽거나 처음 화면으로 돌아가면 1층부터 (회차가 새로 시작된다).
public static class RunState
{
    public const int TopFloor = 10;
    public static readonly int[] BossFloors = { 5, 10 };
    public static readonly int[] MerchantFloors = { 1, 4, 9 };   // 보스 직전 층에 상인

    public static int Floor { get; private set; } = 1;
    public static bool Continuing => carry != null;          // 계단을 올라 이어지는 중 (새 회차가 아님)
    public static bool IsBossFloor => System.Array.IndexOf(BossFloors, Floor) >= 0;
    public static bool IsMerchantFloor => System.Array.IndexOf(MerchantFloors, Floor) >= 0;
    public static bool IsTutorial => Floor == 1;

    class Carry
    {
        public int maxHp, hp, maxMp, mp, maxHunger, hunger, torch, gold, exp, level, strength, fistMin, fistMax, strikeCost, stepsPerRegen, poison;
        public bool torchHeld;
        public string name;
        public List<(ItemDef def, int count)> bag;
        public List<ItemDef> equipped;
    }
    static Carry carry;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() { Floor = 1; carry = null; }

    // 새 회차 (죽은 뒤·처음 화면에서 깨어날 때)
    public static void NewRun() { Floor = 1; carry = null; }

    // 계단을 오른다: 상태를 맡기고 다음 층을 연다
    public static void Ascend(PlayerStats p)
    {
        carry = new Carry
        {
            maxHp = p.maxHp, hp = p.Hp, maxMp = p.maxMp, mp = p.Mp, maxHunger = p.maxHunger, hunger = p.Hunger,
            torch = p.Torch, gold = p.Gold, exp = p.Exp, level = p.Level, strength = p.strength,
            fistMin = p.fistMin, fistMax = p.fistMax, strikeCost = p.strikeCost, stepsPerRegen = p.stepsPerRegen,
            poison = p.PoisonTurns, torchHeld = p.TorchHeld, name = p.PlayerName,
            bag = p.Bag.ExportBag(), equipped = p.Bag.ExportEquipped(),
        };
        Floor = Mathf.Min(TopFloor, Floor + 1);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // 새 층의 주인공이 깨어날 때: 맡겨 둔 상태를 돌려준다 (없으면 아무것도 안 함)
    public static void RestoreInto(PlayerStats p)
    {
        if (carry == null) return;
        var c = carry;
        p.RestoreFrom(c.maxHp, c.hp, c.maxMp, c.mp, c.maxHunger, c.hunger, c.torch, c.torchHeld, c.gold, c.exp, c.level,
                      c.strength, c.fistMin, c.fistMax, c.strikeCost, c.stepsPerRegen, c.poison, c.name);
        p.Bag.Import(c.bag, c.equipped);
    }

    // 새 층에서 한 번 쓰고 비운다 (IntroSequence가 층 도착 연출을 마친 뒤)
    public static void ClearCarry() => carry = null;
}
