using System;
using UnityEngine;

// 주인공의 HP·MP·허기·횃불 내구도와 소지품, 선/악. 한 턴이 끝날 때마다 허기와 (들고 있는) 횃불이 줄어든다.
public class PlayerStats : MonoBehaviour, LightSources.ISource
{
    [Header("HP / MP")]
    public int maxHp = 100;
    public int maxMp = 30;

    [Header("허기")]
    public int maxHunger = 100;
    public int hungerPerTurn = 1;
    public int starveDamage = 3;          // 허기 0일 때 턴마다 HP 감소
    public int hungryWarnAt = 30;

    [Header("횃불")]
    public int maxTorch = 120;
    public int torchPerTurn = 1;
    public int torchWarnAt = 20;
    public Light torchLight;
    public GameObject torchFlame;
    public GameObject torchStick;

    [Header("소지품")]
    public int startFood = 2;
    public int foodRestore = 30;

    public string PlayerName { get; set; } = "";
    public int Hp { get; private set; }
    public int Mp { get; private set; }
    public int Hunger { get; private set; }
    public int Torch { get; private set; }            // 지금 쓰는 횃불 한 자루의 남은 내구도
    public bool HasTorch => Bag.Count(Items.Torch) > 0;
    public bool TorchHeld { get; private set; }
    public bool TorchLit => HasTorch && TorchHeld && Torch > 0;
    public int Food => Bag.Count(Items.Food);
    public int Gold { get; private set; }
    public bool HasTools => Bag.Count(Items.Axe) > 0 && Bag.Count(Items.Pickaxe) > 0;
    public readonly Inventory Bag = new Inventory();   // 가방 + 장비
    public ItemDef Weapon => Bag.Equipped(EquipSlot.Weapon);
    public int Karma => TowerMemory.Karma;   // + 선 / - 악, 복제가 바뀌어도 쌓인다 (플레이어에게는 숨김)
    public int Exp { get; private set; }     // 이번 생에서 쌓은 전투 경험 -> 죽으면 경험의 정수가 된다
    public bool Dead => Hp <= 0;
    public int PoisonTurns { get; private set; }

    [Header("힘: 같은 턴에 서로 때리면 높은 쪽이 먼저")]
    public int strength = 5;
    public int Strength => strength;

    [Header("공격력 (최소~최대, 매번 무작위)")]
    public int fistMin = 1, fistMax = 2;
    // 무기를 장착하면 맨손 + 무기 (예: 낡은 도끼 2~7 -> 3~9)
    public int AttackMin => fistMin + (Weapon != null ? Weapon.attackMin : 0);
    public int AttackMax => fistMax + (Weapon != null ? Weapon.attackMax : 0);

    public int RollAttack()
    {
        int dmg = UnityEngine.Random.Range(fistMin, fistMax + 1);
        if (Weapon != null) dmg += UnityEngine.Random.Range(Weapon.attackMin, Weapon.attackMax + 1);
        return dmg;
    }

    public void Poison(int turns)
    {
        if (Dead) return;
        bool fresh = PoisonTurns == 0;
        PoisonTurns = Mathf.Max(PoisonTurns, turns);
        if (fresh) GameLog.Add("독에 중독되었다!", GameLog.Bad);
        OnChanged?.Invoke();
    }

    public event Action OnChanged;
    public event Action OnDied;

    void Awake()
    {
        ApplyUpgrades();
        Hp = maxHp; Mp = maxMp; Hunger = maxHunger;
        Bag.EquipDirect(Items.Tunic);          // 남자 주인공: 누더기 튜닉 하나로 시작
        Bag.Add(Items.Food, startFood);
        Bag.OnChanged += () => OnChanged?.Invoke();

        // 횃불은 손뼈 아래에 있어서 직렬화 참조가 깨지기 쉽다 -> 실행 시 자식에서 찾는다
        if (torchLight == null || torchLight.gameObject.scene != gameObject.scene)
            torchLight = GetComponentInChildren<Light>(true);
        if (torchFlame == null || torchFlame.scene != gameObject.scene)
            torchFlame = FindChild("Flame");
        if (torchStick == null || torchStick.scene != gameObject.scene)
            torchStick = FindChild("TorchStick");
    }

    // 경험의 정수로 산 강화를 이번 복제에 적용
    void ApplyUpgrades()
    {
        maxHp += 10 * TowerMemory.Level(TowerMemory.Upgrade.MaxHp);
        strength += TowerMemory.Level(TowerMemory.Upgrade.Strength);
        int fist = TowerMemory.Level(TowerMemory.Upgrade.Fist);
        fistMin += fist; fistMax += fist;
        maxHunger += 15 * TowerMemory.Level(TowerMemory.Upgrade.Hunger);
        startFood += TowerMemory.Level(TowerMemory.Upgrade.StartFood);
    }

    GameObject FindChild(string childName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t.gameObject;
        return null;
    }

    void OnEnable() { TurnManager.OnTurnEnded += HandleTurn; LightSources.Register(this); }
    void OnDisable() { TurnManager.OnTurnEnded -= HandleTurn; LightSources.Unregister(this); }

    // 빛 판정: 횃불을 들고 있으면 주변 몇 칸이 빛 안이 된다 (빛 안의 적은 약해진다)
    public float torchLightCells = 3f;
    bool LightSources.ISource.IsOn => TorchLit;
    Vector2Int LightSources.ISource.Cell => GridTile.WorldToCell(transform.position);
    float LightSources.ISource.RadiusCells => torchLightCells;

    void Start()
    {
        ApplyTorchVisual();
        OnChanged?.Invoke();
    }

    // ---- 기력·강타 / 걸으며 회복 / 이번 생의 레벨 (Darkness Survival의 순환에서 따옴) ----

    [Header("기력·강타")]
    public int strikeCost = 10;      // [Q] 강타: 다음 공격 두 배 + 피할 수 없음
    public int stepsPerRegen = 8;    // 이만큼 걸을 때마다 HP +1 (공격받지 않은 걸음, 굶주리면 안 됨)
    public bool StrikeReady { get; private set; }
    public int Level { get; private set; } = 1;
    public int NextLevelExp => (3 * Level * Level + 9 * Level) / 2;   // 6, 15, 27, 42, 60 …
    int steps;

    public void ToggleStrike()
    {
        if (Dead) return;
        if (StrikeReady) { StrikeReady = false; GameLog.Add("모은 힘을 풀었다.", GameLog.Info); }
        else if (Mp >= strikeCost) { StrikeReady = true; GameLog.Add($"힘을 모은다 — 다음 공격은 강타! (두 배, 피할 수 없음, 기력 {strikeCost})", GameLog.Warn); }
        else GameLog.Add($"기력이 모자라다. (강타에 {strikeCost} 필요)", GameLog.Info);
        OnChanged?.Invoke();
    }

    // 공격할 때 부른다: 강타가 준비돼 있으면 기력을 쓰고 true
    public bool ConsumeStrike()
    {
        if (!StrikeReady || Mp < strikeCost) { StrikeReady = false; return false; }
        StrikeReady = false;
        Mp -= strikeCost;
        OnChanged?.Invoke();
        return true;
    }

    // 공격받지 않고 한 칸 걸었을 때
    public void OnStepped()
    {
        if (Dead || Hunger == 0 || Hp >= maxHp) return;
        if (++steps < stepsPerRegen) return;
        steps = 0;
        Hp++;
        OnChanged?.Invoke();
    }

    void CheckLevelUp()
    {
        while (Exp >= NextLevelExp)
        {
            Level++;
            Hp = Mathf.Min(maxHp, Hp + 10);
            Mp = maxMp;
            GameLog.Add($"레벨이 올랐다! (Lv.{Level}) 기력이 가득 찼다.", GameLog.Good);
            DamagePopup.Spawn(DamagePopup.Above(this, 1.2f), "레벨 업!", DamagePopup.Big, 1.4f);
            LevelUpChoice.Enqueue(this);
        }
    }

    // 레벨 업 보상 (LevelUpChoice가 셋을 골라 보여 준다)
    public void ApplyPerk(LevelUpChoice.Perk perk)
    {
        switch (perk)
        {
            case LevelUpChoice.Perk.Vitality: maxHp += 15; Hp += 15; break;
            case LevelUpChoice.Perk.Sharp: fistMin++; fistMax++; break;
            case LevelUpChoice.Perk.Focus: maxMp += 10; Mp += 10; break;
            case LevelUpChoice.Perk.Technique: strikeCost = Mathf.Max(4, strikeCost - 3); break;
            case LevelUpChoice.Perk.Stomach: maxHunger += 25; Hunger += 25; break;
            case LevelUpChoice.Perk.Recovery: stepsPerRegen = Mathf.Max(3, stepsPerRegen - 3); break;
            case LevelUpChoice.Perk.Torchbearer:
                if (HasTorch) Torch += 60; else GiveTorch();
                break;
        }
        OnChanged?.Invoke();
    }

    void HandleTurn(int turn)
    {
        if (Dead) return;

        // 배가 고프지 않으면 기력이 한 턴에 1씩 찬다 (배고프면 멈춘다)
        if (Hunger > hungryWarnAt && Mp < maxMp) Mp++;

        int before = Hunger;
        Hunger = Mathf.Max(0, Hunger - hungerPerTurn);
        if (before > hungryWarnAt && Hunger <= hungryWarnAt) GameLog.Add("배가 고프다.", GameLog.Warn);
        if (Hunger == 0)
        {
            if (before > 0) GameLog.Add("굶주림에 몸이 버티지 못한다.", GameLog.Bad);
            DamagePopup.Spawn(DamagePopup.Above(this), $"굶주림 -{starveDamage}", DamagePopup.Starve, 0.9f);
            Damage(starveDamage);
        }

        if (PoisonTurns > 0)
        {
            PoisonTurns--;
            DamagePopup.Spawn(DamagePopup.Above(this), "-1", DamagePopup.Poison, 0.9f);
            Damage(1);
            if (PoisonTurns == 0 && !Dead) GameLog.Add("독이 빠졌다.", GameLog.Good);
        }

        if (TorchLit)
        {
            int t = Torch;
            Torch = Mathf.Max(0, Torch - torchPerTurn);
            if (t > torchWarnAt && Torch <= torchWarnAt) GameLog.Add("횃불이 곧 꺼질 것 같다.", GameLog.Warn);
            if (Torch == 0)
            {
                GameLog.Add("횃불이 다 타서 사라졌다.", GameLog.Bad);
                Bag.Remove(Items.Torch);
                TorchHeld = false;
                if (HasTorch) { Torch = maxTorch; GameLog.Add("[F] 다음 횃불을 들 수 있다.", GameLog.Info); }
                ApplyTorchVisual();
            }
        }
        OnChanged?.Invoke();
    }

    public void Damage(int amount)
    {
        if (Dead) return;
        Hp = Mathf.Max(0, Hp - amount);
        if (Dead)
        {
            GameLog.Add("당신은 쓰러졌다. 경험이 탑에 스며든다…", GameLog.Bad);
            OnDied?.Invoke();
        }
        OnChanged?.Invoke();
    }

    // 식량 하나를 먹는다
    public void EatFood()
    {
        if (Dead) return;
        if (!Bag.Remove(Items.Food)) { GameLog.Add("먹을 것이 없다.", GameLog.Info); return; }
        int before = Hunger;
        Hunger = Mathf.Min(maxHunger, Hunger + foodRestore);
        GameLog.Add("배를 채웠다.", GameLog.Good);
        DamagePopup.Spawn(DamagePopup.Above(this), $"허기 +{Hunger - before}", DamagePopup.Heal, 0.9f);
        OnChanged?.Invoke();
    }

    public void AddFood(int amount)
    {
        if (amount > 0 && !Bag.Add(Items.Food, amount)) GameLog.Add("가방이 가득 찼다.", GameLog.Warn);
        else if (amount < 0) Bag.Remove(Items.Food, Mathf.Min(-amount, Food));
        OnChanged?.Invoke();
    }
    public void AddGold(int amount) { Gold = Mathf.Max(0, Gold + amount); OnChanged?.Invoke(); }
    public void AddKarma(int amount) { TowerMemory.AddKarma(amount); Debug.Log($"[Karma] {Karma}"); }
    public void AddExp(int amount) { Exp += amount; CheckLevelUp(); OnChanged?.Invoke(); }

    // 물약 등으로 체력·허기를 채운다 / 독을 뺀다
    public void Heal(int amount)
    {
        if (Dead) return;
        int before = Hp;
        Hp = Mathf.Min(maxHp, Hp + amount);
        DamagePopup.Spawn(DamagePopup.Above(this), $"+{Hp - before}", DamagePopup.Poison, 1.1f);
        OnChanged?.Invoke();
    }

    public void Feed(int amount)
    {
        int before = Hunger;
        Hunger = Mathf.Min(maxHunger, Hunger + amount);
        DamagePopup.Spawn(DamagePopup.Above(this), $"허기 +{Hunger - before}", DamagePopup.Heal, 0.9f);
        OnChanged?.Invoke();
    }

    public void RestoreMp() { Mp = maxMp; OnChanged?.Invoke(); }

    public void CurePoison()
    {
        if (PoisonTurns == 0) return;
        PoisonTurns = 0;
        GameLog.Add("독이 빠졌다.", GameLog.Good);
        OnChanged?.Invoke();
    }

    // 들고 있던 횃불을 던진다: 남은 내구도를 돌려준다. 가방에 횃불이 더 있으면 새 것을 든다.
    public int ReleaseTorch()
    {
        int left = Torch;
        Bag.Remove(Items.Torch);
        if (HasTorch) Torch = maxTorch;
        else TorchHeld = false;
        ApplyTorchVisual();
        OnChanged?.Invoke();
        return left;
    }

    // 던져 둔 횃불을 다시 줍는다 (들고 있던 게 없으면 남은 내구도 그대로)
    public void PickUpTorch(int durability)
    {
        bool had = HasTorch;
        Bag.Add(Items.Torch);
        if (!had) Torch = durability;
        ApplyTorchVisual();
        OnChanged?.Invoke();
    }
    // 입구의 곡괭이와 도끼: 도끼는 무기 칸에 바로 장착
    public void GiveTools()
    {
        Bag.Add(Items.Pickaxe);
        if (Weapon == null) Bag.EquipDirect(Items.Axe); else Bag.Add(Items.Axe);
        OnChanged?.Invoke();
    }

    // 새 횃불을 얻는다 (아직 들지는 않은 상태)
    public void GiveTorch()
    {
        if (!HasTorch) Torch = maxTorch;
        Bag.Add(Items.Torch);
        ApplyTorchVisual();
        OnChanged?.Invoke();
    }

    public void ToggleTorch()
    {
        if (Dead) return;
        if (!HasTorch) { GameLog.Add("횃불이 없다.", GameLog.Info); return; }
        TorchHeld = !TorchHeld;
        GameLog.Add(TorchHeld ? "횃불을 들었다." : "횃불을 내렸다.", GameLog.Info);
        ApplyTorchVisual();
        OnChanged?.Invoke();
    }

    [Header("횃불 빛 (들었을 때)")]
    public float torchRange = 34f;        // 손 횃불이 닿는 거리 (한 칸 = 4)
    public float torchIntensity = 38f;
    public float glowRange = 32f;         // 머리 위 보조 빛: 주변 바닥을 고르게 밝힌다 (그림자 없음)
    public float glowIntensity = 20f;

    // 횃불이 없거나 꺼져도 완전히 안 보이진 않게: 주변만 희미하게 보이는 차가운 빛으로 바꾼다
    Color litColor;
    bool litCached;
    Light glow;

    void ApplyTorchVisual()
    {
        if (torchLight != null)
        {
            if (!litCached) { litColor = torchLight.color; litCached = true; }
            torchLight.range = TorchLit ? torchRange : 6f;
            torchLight.intensity = TorchLit ? torchIntensity : 3f;
            torchLight.color = TorchLit ? litColor : new Color(0.55f, 0.6f, 0.75f);
            torchLight.shadows = TorchLit ? LightShadows.Soft : LightShadows.None;
        }
        if (glow == null)
        {
            var go = new GameObject("TorchGlow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 5f, 0);
            glow = go.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.shadows = LightShadows.None;
            glow.color = new Color(1f, 0.68f, 0.38f);
            glow.range = glowRange;
            glow.intensity = glowIntensity;
        }
        glow.enabled = TorchLit;
        if (torchFlame != null) torchFlame.SetActive(TorchLit);
        if (torchStick != null) torchStick.SetActive(HasTorch && TorchHeld);
    }
}
