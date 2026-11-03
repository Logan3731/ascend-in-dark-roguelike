using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 죽어도 남는 것: 경험의 정수, 정수로 산 강화, 선/악, 복제 횟수, 이전 복제들의 뼈.
// 파일(persistentDataPath/tower_memory.json)에 저장해서 게임을 껐다 켜도 이어진다.
public static class TowerMemory
{
    [Serializable]
    public class Bones
    {
        public int x, y;
        public int gold;     // 그 복제가 지니고 있던 골드 (유품)
        public int run;      // 몇 번째 복제였는지
    }

    [Serializable]
    class Data
    {
        public int essence;
        public int runCount;
        public int karma;
        public List<int> upgradeLevels = new List<int>();
        public List<Bones> bones = new List<Bones>();
        public List<string> hints = new List<string>();   // 이미 본 튜토리얼 안내
    }

    // ---- 강화 목록 ----

    public enum Upgrade { MaxHp, Strength, Fist, Hunger, StartFood }

    public struct UpgradeInfo
    {
        public string name, effect;
        public int baseCost, maxLevel;
    }

    public static readonly UpgradeInfo[] Upgrades =
    {
        new UpgradeInfo { name = "강인한 몸",   effect = "최대 HP +10",       baseCost = 4, maxLevel = 10 },
        new UpgradeInfo { name = "단련된 근육", effect = "힘 +1",             baseCost = 5, maxLevel = 10 },
        new UpgradeInfo { name = "주먹의 기억", effect = "맨손 공격 +1",      baseCost = 6, maxLevel = 5 },
        new UpgradeInfo { name = "굶주림 내성", effect = "최대 허기 +15",     baseCost = 3, maxLevel = 5 },
        new UpgradeInfo { name = "주머니",      effect = "시작 식량 +1",      baseCost = 4, maxLevel = 3 },
    };

    public const int MaxBones = 5;

    static Data data;
    static string FilePath => Path.Combine(Application.persistentDataPath, "tower_memory.json");

    static Data D
    {
        get
        {
            if (data == null) Load();
            return data;
        }
    }

    public static int Essence => D.essence;
    public static int RunCount => D.runCount;
    public static int Karma => D.karma;
    public static IReadOnlyList<Bones> AllBones => D.bones;

    public static int Level(Upgrade u)
    {
        int i = (int)u;
        while (D.upgradeLevels.Count <= i) D.upgradeLevels.Add(0);
        return D.upgradeLevels[i];
    }

    public static int Cost(Upgrade u) => Upgrades[(int)u].baseCost * (Level(u) + 1);
    public static bool IsMaxed(Upgrade u) => Level(u) >= Upgrades[(int)u].maxLevel;

    public static bool TryBuy(Upgrade u)
    {
        if (IsMaxed(u) || D.essence < Cost(u)) return false;
        D.essence -= Cost(u);
        D.upgradeLevels[(int)u]++;
        Save();
        return true;
    }

    // 새 복제가 깨어날 때
    public static void BeginRun()
    {
        D.runCount++;
        Save();
    }

    // 죽었을 때: 이번 생의 경험이 정수가 되고, 그 자리에 뼈가 남는다
    public static void RecordDeath(int exp, Vector2Int cell, int gold)
    {
        D.essence += exp;
        D.bones.Add(new Bones { x = cell.x, y = cell.y, gold = gold, run = D.runCount });
        while (D.bones.Count > MaxBones) D.bones.RemoveAt(0);
        Save();
    }

    public static void RemoveBones(Bones b)
    {
        D.bones.Remove(b);
        Save();
    }

    // 튜토리얼 안내: 한 번 본 것은 다음 회차에 다시 띄우지 않는다
    public static bool HintSeen(string id) => D.hints != null && D.hints.Contains(id);

    public static void MarkHint(string id)
    {
        if (D.hints == null) D.hints = new List<string>();
        if (D.hints.Contains(id)) return;
        D.hints.Add(id);
        Save();
    }

    public static void AddKarma(int amount)
    {
        D.karma += amount;
        Save();
    }

    // 기록을 모두 지운다 (테스트용)
    public static void ResetAll()
    {
        data = new Data();
        Save();
    }

    static void Load()
    {
        try
        {
            data = File.Exists(FilePath) ? JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) : new Data();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TowerMemory] 불러오기 실패, 새로 시작: {e.Message}");
            data = new Data();
        }
        if (data == null) data = new Data();
    }

    static void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(data, true)); }
        catch (Exception e) { Debug.LogWarning($"[TowerMemory] 저장 실패: {e.Message}"); }
    }
}
