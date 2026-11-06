using System;
using System.Collections.Generic;
using UnityEngine;

public enum ItemKind { Food, Torch, Tool, Equipment, Material, Potion }

// 장비 칸: 모자 / 상의 / 하의 / 신발 / 무기 / 보조. 상하의(일체형)는 상의·하의 두 칸을 함께 차지한다.
// 보조칸: 방패, 또는 한손 무기 (한손 무기는 무기 칸과 보조칸 어디에나 낄 수 있다). 양손 무기를 들면 보조칸은 빈다.
public enum EquipSlot { None, Hat, Top, Bottom, TopBottom, Shoes, Weapon, Offhand }

public class ItemDef
{
    public string id, name, desc;
    public ItemKind kind;
    public EquipSlot slot;
    public bool stackable;
    public int attackMin, attackMax;     // 무기일 때 더해지는 공격력
    public int weight;                   // 무기 무게: 힘보다 무거우면 빗나가고 늦고, 힘이 남으면 연속 공격 (PlayerStats.WeaponNote)
    public bool oneHanded;               // 한손 무기: 보조칸에도 낄 수 있다 (무기 칸 아이템만 의미가 있다)
    public float stunChance;             // 보조칸 추가 타격이 맞으면 기절시킬 확률 (방패)

    public bool IsWeapon => slot == EquipSlot.Weapon;
    public bool TwoHanded => IsWeapon && !oneHanded;
    // 이 아이템을 낄 수 있는 칸인가 (상하의 일체형은 상의·하의 칸에 놓아도 된다)
    public bool Fits(EquipSlot target) =>
        slot == target || (slot == EquipSlot.TopBottom && (target == EquipSlot.Top || target == EquipSlot.Bottom))
        || (target == EquipSlot.Offhand && IsWeapon && oneHanded);

    Sprite icon;
    public Sprite Icon => icon != null ? icon : (icon = Resources.Load<Sprite>("Icons/" + id));
}

// 게임에 나오는 아이템 목록
public static class Items
{
    public static readonly ItemDef Food = new ItemDef
    {
        id = "food", name = "식량", kind = ItemKind.Food, stackable = true,
        desc = "말린 고기 몇 조각.\n먹으면 허기를 30 채운다.",
    };
    public static readonly ItemDef Torch = new ItemDef
    {
        id = "torch", name = "낡은 횃불", kind = ItemKind.Torch, stackable = true,
        desc = "나뭇가지에 천을 감아 만든 불씨.\n들고 있으면 시야가 넓어진다. 타는 동안 내구도가 줄어들고, 다 타면 사라진다.",
    };
    public static readonly ItemDef Axe = new ItemDef
    {
        id = "axe", name = "낡은 도끼", kind = ItemKind.Equipment, slot = EquipSlot.Weapon, attackMin = 2, attackMax = 7, weight = 5, oneHanded = true,
        desc = "입구에 놓여 있던 도끼.\n나무를 벨 수 있고, 무기로도 쓸 만하다.",
    };
    public static readonly ItemDef Pickaxe = new ItemDef
    {
        id = "pickaxe", name = "낡은 곡괭이", kind = ItemKind.Tool,
        desc = "입구에 놓여 있던 곡괭이.\n광석을 캘 수 있다.",
    };
    public static readonly ItemDef Tunic = new ItemDef
    {
        id = "tunic", name = "누더기 튜닉", kind = ItemKind.Equipment, slot = EquipSlot.Top,
        desc = "해지고 기운 천 튜닉에 밧줄 허리띠.\n눈을 떴을 때 입고 있던 유일한 옷.",
    };
    public static readonly ItemDef Branch = new ItemDef
    {
        id = "branch", name = "나뭇가지", kind = ItemKind.Material, stackable = true,
        desc = "미로 바닥에 떨어져 있던 마른 나뭇가지.\n모루에서 횃불이나 무기를 만드는 재료.",
    };
    public static readonly ItemDef Stone = new ItemDef
    {
        id = "stone", name = "돌멩이", kind = ItemKind.Material, stackable = true,
        desc = "모서리가 날카로운 주먹만 한 돌.\n모루에서 돌도끼를 만드는 재료.",
    };
    public static readonly ItemDef Club = new ItemDef
    {
        id = "club", name = "나무 몽둥이", kind = ItemKind.Equipment, slot = EquipSlot.Weapon, attackMin = 2, attackMax = 5, weight = 2, oneHanded = true,
        desc = "굵은 가지를 깎아 만든 몽둥이.\n가벼워서 휘두르기 쉽다.",
    };
    public static readonly ItemDef StoneAxe = new ItemDef
    {
        id = "stoneaxe", name = "돌도끼", kind = ItemKind.Equipment, slot = EquipSlot.Weapon, attackMin = 3, attackMax = 8, weight = 7, oneHanded = false,   // 양손
        desc = "날카롭게 깨뜨린 돌날을 나뭇가지에 묶은 도끼.\n묵직하게 들어가지만, 힘이 모자라면 휘청인다.",
    };
    public static readonly ItemDef Key = new ItemDef
    {
        id = "key", name = "녹슨 열쇠", kind = ItemKind.Tool, stackable = true,
        desc = "상자 바닥에서 나온 묵직한 열쇠.\n잠긴 철문 하나를 열 수 있다. 한 번 쓰면 자물쇠에 박혀 빠지지 않는다.",
    };
    // 다트: 던져서 피해 (Thrower). 적에게 맞으면 부러져 사라지고, 빈 바닥에 떨어지면 다시 주울 수 있다.
    public static readonly ItemDef Dart = new ItemDef
    {
        id = "dart", name = "다트", kind = ItemKind.Material, stackable = true,
        desc = "손바닥만 한 쇠 다트.\n던져서 적을 맞힌다. 빗나가 바닥에 떨어진 것은 다시 주울 수 있다.",
    };
    // 나무 방패: 보조칸. 가끔 방패로 후려쳐 적을 기절시킨다 (보조칸 추가 타격, PlayerStats.Offhand*)
    public static readonly ItemDef Shield = new ItemDef
    {
        id = "shield", name = "나무 방패", kind = ItemKind.Equipment, slot = EquipSlot.Offhand,
        attackMin = 1, attackMax = 2, stunChance = 0.35f,
        desc = "널빤지를 덧대 만든 둥근 방패.\n보조칸에 낀다. 공격할 때 가끔 방패로 후려쳐 적을 기절시킨다.",
    };
}

// 가방(칸 수 제한) + 장착 중인 장비
public class Inventory
{
    public class Stack
    {
        public ItemDef def;
        public int count;
    }

    public const int Capacity = 20;
    public readonly List<Stack> bag = new List<Stack>();
    readonly Dictionary<EquipSlot, ItemDef> equipped = new Dictionary<EquipSlot, ItemDef>();

    public event Action OnChanged;
    public Action<ItemDef> onAdded;   // 가방에 새로 들어왔을 때 (처음 얻은 아이템 설명 — PlayerStats)

    public ItemDef Equipped(EquipSlot slot)
    {
        equipped.TryGetValue(slot, out var def);
        return def;
    }

    public int Count(ItemDef def)
    {
        int n = 0;
        foreach (var s in bag) if (s.def == def) n += s.count;
        foreach (var kv in equipped) if (kv.Value == def) n++;
        return n;
    }

    public bool Add(ItemDef def, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            var stack = def.stackable ? bag.Find(s => s.def == def) : null;
            if (stack != null) stack.count++;
            else if (bag.Count < Capacity) bag.Add(new Stack { def = def, count = 1 });
            else { OnChanged?.Invoke(); return false; }   // 가방이 꽉 참
        }
        OnChanged?.Invoke();
        onAdded?.Invoke(def);
        return true;
    }

    public bool Remove(ItemDef def, int count = 1)
    {
        if (Count(def) < count) return false;
        for (int i = 0; i < count; i++)
        {
            var stack = bag.Find(s => s.def == def);
            if (stack != null)
            {
                if (--stack.count <= 0) bag.Remove(stack);
                continue;
            }
            // 가방에 없으면 장착 중인 것을 뺀다
            foreach (var kv in equipped)
                if (kv.Value == def) { equipped.Remove(kv.Key); break; }
        }
        OnChanged?.Invoke();
        return true;
    }

    // 가방의 장비를 장착한다. 그 칸에 있던 장비는 가방으로 돌아간다.
    // target: 놓을 칸 (한손 무기를 보조칸에 낄 때 Offhand). None이면 아이템의 기본 칸.
    public bool Equip(ItemDef def, EquipSlot target = EquipSlot.None)
    {
        if (def.slot == EquipSlot.None || !bag.Exists(s => s.def == def)) return false;
        var at = target == EquipSlot.Offhand && def.Fits(EquipSlot.Offhand) ? EquipSlot.Offhand : def.slot;
        var freed = new List<EquipSlot> { at };
        if (at == EquipSlot.TopBottom) { freed.Add(EquipSlot.Top); freed.Add(EquipSlot.Bottom); }
        if (at == EquipSlot.Top || at == EquipSlot.Bottom) freed.Add(EquipSlot.TopBottom);
        // 양손 무기를 들면 보조칸이 비고, 양손 무기를 든 채 보조칸에 끼면 양손 무기를 내려놓는다
        var main = Equipped(EquipSlot.Weapon);
        if (at == EquipSlot.Offhand && main != null && main.TwoHanded) return false;   // 양손 무기를 든 채로는 보조칸에 못 낀다
        if (at == EquipSlot.Weapon && def.TwoHanded) freed.Add(EquipSlot.Offhand);       // 양손 무기를 들면 보조칸은 가방으로

        var back = new List<(EquipSlot slot, ItemDef def)>();
        foreach (var s in freed)
            if (equipped.TryGetValue(s, out var old)) { back.Add((s, old)); equipped.Remove(s); }
        if (bag.Count - 1 + back.Count > Capacity) { foreach (var o in back) equipped[o.slot] = o.def; return false; }

        var stack = bag.Find(s => s.def == def);
        if (--stack.count <= 0) bag.Remove(stack);
        equipped[at] = def;
        foreach (var o in back) bag.Add(new Stack { def = o.def, count = 1 });
        OnChanged?.Invoke();
        return true;
    }

    public bool Unequip(EquipSlot slot)
    {
        if (!equipped.TryGetValue(slot, out var def) || bag.Count >= Capacity) return false;
        equipped.Remove(slot);
        bag.Add(new Stack { def = def, count = 1 });
        OnChanged?.Invoke();
        return true;
    }

    // 처음부터 장착한 채로 시작 (가방을 거치지 않음)
    public void EquipDirect(ItemDef def) => equipped[def.slot] = def;

    // 층을 오를 때 맡겨 두고 되돌려 받기 (RunState)
    public List<(ItemDef def, int count)> ExportBag() => bag.ConvertAll(s => (s.def, s.count));
    // 칸 정보도 함께 맡긴다 (보조칸에 낀 한손 무기가 무기 칸으로 돌아가지 않게)
    public List<(EquipSlot slot, ItemDef def)> ExportEquipped()
    {
        var list = new List<(EquipSlot slot, ItemDef def)>();
        foreach (var kv in equipped) list.Add((kv.Key, kv.Value));
        return list;
    }

    public void Import(List<(ItemDef def, int count)> items, List<(EquipSlot slot, ItemDef def)> worn)
    {
        bag.Clear();
        equipped.Clear();
        foreach (var (def, count) in items) bag.Add(new Stack { def = def, count = count });
        foreach (var (slot, def) in worn) equipped[slot] = def;
        OnChanged?.Invoke();
    }
}
