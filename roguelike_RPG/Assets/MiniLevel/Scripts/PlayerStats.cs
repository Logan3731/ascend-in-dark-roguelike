using System;
using UnityEngine;

// 주인공의 HP·MP·허기·횃불 내구도와 소지품, 선/악. 한 턴이 끝날 때마다 허기와 (들고 있는) 횃불이 줄어든다.
public class PlayerStats : MonoBehaviour
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
    public int Torch { get; private set; }
    public bool HasTorch { get; private set; }
    public bool TorchHeld { get; private set; }
    public bool TorchLit => HasTorch && TorchHeld && Torch > 0;
    public int Food { get; private set; }
    public int Gold { get; private set; }
    public bool HasTools { get; private set; }
    public int Karma { get; private set; }   // + 선 / - 악 (플레이어에게는 숨김)
    public bool Dead => Hp <= 0;
    public int PoisonTurns { get; private set; }

    [Header("공격력 (최소~최대, 매번 무작위)")]
    public int fistMin = 1, fistMax = 2;
    public int axeMin = 2, axeMax = 7;           // 도끼를 들면 맨손 + 도끼 = 3~9

    public int AttackMin => fistMin + (HasTools ? axeMin : 0);
    public int AttackMax => fistMax + (HasTools ? axeMax : 0);

    public int RollAttack()
    {
        int dmg = UnityEngine.Random.Range(fistMin, fistMax + 1);
        if (HasTools) dmg += UnityEngine.Random.Range(axeMin, axeMax + 1);
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

    void Awake()
    {
        Hp = maxHp; Mp = maxMp; Hunger = maxHunger; Food = startFood;

        // 횃불은 손뼈 아래에 있어서 직렬화 참조가 깨지기 쉽다 -> 실행 시 자식에서 찾는다
        if (torchLight == null || torchLight.gameObject.scene != gameObject.scene)
            torchLight = GetComponentInChildren<Light>(true);
        if (torchFlame == null || torchFlame.scene != gameObject.scene)
            torchFlame = FindChild("Flame");
        if (torchStick == null || torchStick.scene != gameObject.scene)
            torchStick = FindChild("TorchStick");
    }

    GameObject FindChild(string childName)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t.gameObject;
        return null;
    }

    void OnEnable() { TurnManager.OnTurnEnded += HandleTurn; }
    void OnDisable() { TurnManager.OnTurnEnded -= HandleTurn; }

    void Start()
    {
        ApplyTorchVisual();
        OnChanged?.Invoke();
    }

    void HandleTurn(int turn)
    {
        if (Dead) return;

        int before = Hunger;
        Hunger = Mathf.Max(0, Hunger - hungerPerTurn);
        if (before > hungryWarnAt && Hunger <= hungryWarnAt) GameLog.Add("배가 고프다.", GameLog.Warn);
        if (Hunger == 0)
        {
            if (before > 0) GameLog.Add("굶주림에 몸이 버티지 못한다.", GameLog.Bad);
            Damage(starveDamage);
        }

        if (PoisonTurns > 0)
        {
            PoisonTurns--;
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
                GameLog.Add("횃불이 꺼졌다.", GameLog.Bad);
                HasTorch = false;
                TorchHeld = false;
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
            GameLog.Add("[R] 다시 시작", GameLog.Info);
        }
        OnChanged?.Invoke();
    }

    // 식량 하나를 먹는다
    public void EatFood()
    {
        if (Dead) return;
        if (Food <= 0) { GameLog.Add("먹을 것이 없다.", GameLog.Info); return; }
        Food--;
        Hunger = Mathf.Min(maxHunger, Hunger + foodRestore);
        GameLog.Add("배를 채웠다.", GameLog.Good);
        OnChanged?.Invoke();
    }

    public void AddFood(int amount) { Food = Mathf.Max(0, Food + amount); OnChanged?.Invoke(); }
    public void AddGold(int amount) { Gold = Mathf.Max(0, Gold + amount); OnChanged?.Invoke(); }
    public void AddKarma(int amount) { Karma += amount; Debug.Log($"[Karma] {Karma}"); }
    public void GiveTools() { HasTools = true; OnChanged?.Invoke(); }

    // 새 횃불을 얻는다 (아직 들지는 않은 상태)
    public void GiveTorch()
    {
        HasTorch = true;
        Torch = maxTorch;
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

    // 횃불이 없거나 꺼져도 완전히 안 보이진 않게: 주변만 희미하게 보이는 차가운 빛으로 바꾼다
    float litRange, litIntensity;
    Color litColor;
    bool litCached;

    void ApplyTorchVisual()
    {
        if (torchLight != null)
        {
            if (!litCached) { litRange = torchLight.range; litIntensity = torchLight.intensity; litColor = torchLight.color; litCached = true; }
            torchLight.range = TorchLit ? litRange : 6f;
            torchLight.intensity = TorchLit ? litIntensity : 3f;
            torchLight.color = TorchLit ? litColor : new Color(0.55f, 0.6f, 0.75f);
            torchLight.shadows = TorchLit ? LightShadows.Soft : LightShadows.None;
        }
        if (torchFlame != null) torchFlame.SetActive(TorchLit);
        if (torchStick != null) torchStick.SetActive(HasTorch && TorchHeld);
    }
}
