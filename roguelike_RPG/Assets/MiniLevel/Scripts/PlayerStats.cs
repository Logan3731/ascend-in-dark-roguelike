using System;
using UnityEngine;

// 주인공의 HP·MP·허기·횃불 내구도. 한 턴이 끝날 때마다 허기와 횃불이 줄어든다.
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

    public int Hp { get; private set; }
    public int Mp { get; private set; }
    public int Hunger { get; private set; }
    public int Torch { get; private set; }
    public bool TorchLit => Torch > 0;
    public bool Dead => Hp <= 0;

    public event Action OnChanged;

    void Awake()
    {
        Hp = maxHp; Mp = maxMp; Hunger = maxHunger; Torch = maxTorch;

        // 횃불은 손뼈 아래에 있어서 직렬화 참조가 깨지기 쉽다 -> 실행 시 자식에서 찾는다
        if (torchLight == null || torchLight.gameObject.scene != gameObject.scene)
            torchLight = GetComponentInChildren<Light>(true);
        if (torchFlame == null || torchFlame.scene != gameObject.scene)
        {
            torchFlame = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Flame") { torchFlame = t.gameObject; break; }
        }
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

        if (TorchLit)
        {
            int t = Torch;
            Torch = Mathf.Max(0, Torch - torchPerTurn);
            if (t > torchWarnAt && Torch <= torchWarnAt) GameLog.Add("횃불이 곧 꺼질 것 같다.", GameLog.Warn);
            if (Torch == 0)
            {
                GameLog.Add("횃불이 꺼졌다.", GameLog.Bad);
                ApplyTorchVisual();
            }
        }
        OnChanged?.Invoke();
    }

    public void Damage(int amount)
    {
        if (Dead) return;
        Hp = Mathf.Max(0, Hp - amount);
        if (Dead) GameLog.Add("당신은 쓰러졌다. 경험이 탑에 스며든다…", GameLog.Bad);
        OnChanged?.Invoke();
    }

    public void Eat(int amount)
    {
        Hunger = Mathf.Min(maxHunger, Hunger + amount);
        GameLog.Add("배를 채웠다.", GameLog.Good);
        OnChanged?.Invoke();
    }

    public void RefillTorch(int amount)
    {
        bool wasOut = !TorchLit;
        Torch = Mathf.Min(maxTorch, Torch + amount);
        if (wasOut) ApplyTorchVisual();
        OnChanged?.Invoke();
    }

    // 횃불이 꺼져도 완전히 안 보이진 않게: 주변만 희미하게 보이는 차가운 빛으로 바꾼다
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
    }
}
