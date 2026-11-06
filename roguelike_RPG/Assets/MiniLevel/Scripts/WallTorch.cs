using UnityEngine;

// 미로 벽에 걸린 횃불. 불붙은 횃불을 들고 와서 클릭하면 불이 붙고, 그 회차 동안 계속 주변을 밝힌다.
// 오브젝트의 원점은 횃불 앞 바닥 칸이고, 모양(Visual)은 벽 쪽에 붙어 있다.
public class WallTorch : Interactable, LightSources.ISource
{
    public float radiusCells = 2.5f;
    public bool startLit;
    public Light lightSource;
    public GameObject flame;

    public bool IsOn { get; private set; }
    public float RadiusCells => radiusCells;

    protected override void Awake()
    {
        base.Awake();
        if (lightSource == null) lightSource = GetComponentInChildren<Light>(true);
        if (flame == null)
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "Flame") flame = t.gameObject;
        SetLit(startLit);
    }

    void OnEnable() => LightSources.Register(this);
    void OnDisable() => LightSources.Unregister(this);

    public override void Interact(PlayerStats player)
    {
        if (IsOn) { GameLog.Add("벽 횃불이 타오르고 있다.", GameLog.Info); return; }
        if (!player.TorchLit)
        {
            GameLog.Add("불씨가 없다. 불붙은 횃불을 들고 와야 한다.", GameLog.Info);
            return;
        }
        SetLit(true);
        GridClickMover.PlayAction("LightTorch", GridClickMover.LightTorchSeconds);
        GameLog.Add("벽 횃불에 불을 옮겨 붙였다. 주변이 밝아진다.", GameLog.Good);
    }

    void SetLit(bool on)
    {
        IsOn = on;
        if (lightSource != null) lightSource.enabled = on;
        if (flame != null) flame.SetActive(on);
    }
}
