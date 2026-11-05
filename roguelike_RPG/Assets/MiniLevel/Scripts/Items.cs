using System;
using System.Collections.Generic;
using UnityEngine;

public enum ItemKind { Food, Torch, Tool, Equipment, Material, Potion }

// 장비 칸: 모자 / 상의 / 하의 / 신발 / 무기. 상하의(일체형)는 상의·하의 두 칸을 함께 차지한다.
public enum EquipSlot { None, Hat, Top, Bottom, TopBottom, Shoes, Weapon }

public class ItemDef
{
    public string id, name, desc;
    public ItemKind kind;
    public EquipSlot slot;
    public bool stackable;
    public int attackMin, attackMax;     // 무기일 때 더해지는 공격력
    public int weight;                   // 무기 무게: 힘보다 무거우면 빗나가고 늦고, 힘이 남으면 연속 공격 (PlayerStats.WeaponNote)

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
        id = "axe", name = "낡은 도끼", kind = ItemKind.Equipment, slot = EquipSlot.Weapon, attackMin = 2, attackMax = 7, weight = 5,
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
        id = "club", name = "나무 몽둥이", kind = ItemKind.Equipment, slot = EquipSlot.Weapon, attackMin = 2, attackMax = 5, weight = 2,
        desc = "굵은 가지를 깎아 만든 몽둥이.\n가벼워서 휘두르기 쉽다.",
    };
    public static readonly ItemDef StoneAxe = new ItemDef
    {
        id = "stoneaxe", name = "돌도끼", kind = ItemKind.Equipment, slot = EquipSlot.Weapon, attackMin = 3, attackMax = 8, weight = 7,
        desc = "날카롭게 깨뜨린 돌날을 나뭇가지에 묶은 도끼.\n묵직하게 들어가지만, 힘이 모자라면 휘청인다.",
    };
    public static readonly ItemDef Key = new ItemDef
    {
        id = "key", name = "녹슨 열쇠", kind = ItemKind.Tool, stackable = true,
        desc = "상자 바닥에서 나온 묵직한 열쇠.\n잠긴 철문 하나를 열 수 있다. 한 번 쓰면 자물쇠에 박혀 빠지지 않는다.",
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
    public bool Equip(ItemDef def)
    {
        if (def.slot == EquipSlot.None || !bag.Exists(s => s.def == def)) return false;
        var freed = new List<EquipSlot> { def.slot };
        if (def.slot == EquipSlot.TopBottom) { freed.Add(EquipSlot.Top); freed.Add(EquipSlot.Bottom); }
        if (def.slot == EquipSlot.Top || def.slot == EquipSlot.Bottom) freed.Add(EquipSlot.TopBottom);

        var back = new List<ItemDef>();
        foreach (var s in freed)
            if (equipped.TryGetValue(s, out var old)) { back.Add(old); equipped.Remove(s); }
        if (bag.Count - 1 + back.Count > Capacity) { foreach (var o in back) equipped[o.slot] = o; return false; }

        var stack = bag.Find(s => s.def == def);
        if (--stack.count <= 0) bag.Remove(stack);
        equipped[def.slot] = def;
        foreach (var o in back) bag.Add(new Stack { def = o, count = 1 });
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
    public List<ItemDef> ExportEquipped() => new List<ItemDef>(equipped.Values);

    public void Import(List<(ItemDef def, int count)> items, List<ItemDef> worn)
    {
        bag.Clear();
        equipped.Clear();
        foreach (var (def, count) in items) bag.Add(new Stack { def = def, count = count });
        foreach (var def in worn) equipped[def.slot] = def;
        OnChanged?.Invoke();
    }
}
