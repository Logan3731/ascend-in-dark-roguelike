using System.Collections.Generic;
using UnityEngine;

// 무너진 벽: 곡괭이가 있으면 파낼 수 있다 (세 번 = 세 턴, 시끄러워서 근처의 잠든 적이 깰 수 있다).
// 너머에는 숨겨진 방이 있다. 입구의 곡괭이를 챙길지 말지가 여기서 의미를 가진다.
// 처음엔 숨겨져 있다 (설계 A4): 평범한 벽처럼 보이고 시야도 막는다. 곁을 지나가다(돋보기면 훨씬 잘) 발견하면 돌무더기가 드러난다.
public class Rubble : Prop, SecretSearch.IHidden
{
    int hitsLeft = 3;

    // ---- 숨김 ----
    public bool Hidden { get; private set; }
    GameObject disguise;          // 숨어 있는 동안 씌워 둔 가짜 벽
    Renderer[] rocks;
    static readonly HashSet<Vector2Int> hiddenCells = new HashSet<Vector2Int>();
    public static bool HiddenAt(Vector2Int c) => hiddenCells.Contains(c);   // 숨은 동안 시야를 막는다 (FogOfWar)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => hiddenCells.Clear();

    bool SecretSearch.IHidden.IsHidden => Hidden;
    string SecretSearch.IHidden.What => "무너진 벽";

    // FloorGenerator가 돌무더기를 쌓은 뒤 부른다: 돌은 감추고 가짜 벽을 씌운다 (클릭해도 벽처럼 빨간 표시만)
    public void Hide(GameObject fakeWall)
    {
        Hidden = true;
        hiddenCells.Add(Cell);
        disguise = fakeWall;
        disguise.transform.SetParent(transform, true);
        var mine = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (!r.transform.IsChildOf(disguise.transform)) { r.enabled = false; mine.Add(r); }
        rocks = mine.ToArray();
        enabled = false;   // 숨은 동안엔 눌러도 반응하지 않는다 (GridClickMover는 isActiveAndEnabled만 본다)
    }

    void SecretSearch.IHidden.Discover()
    {
        if (!Hidden) return;
        Hidden = false;
        hiddenCells.Remove(Cell);
        if (disguise != null) Destroy(disguise);
        if (rocks != null) foreach (var r in rocks) if (r != null) r.enabled = true;
        enabled = true;
        GameLog.Add("벽 한쪽이 무너져 내린 흔적을 발견했다!", GameLog.Warn);
        DamagePopup.Spawn(transform.position + Vector3.up * 2.5f, "발견!", DamagePopup.Big, 1.2f);
    }

    protected override void Awake()
    {
        base.Awake();
        SecretSearch.Register(this);
    }

    void OnDestroy()
    {
        SecretSearch.Unregister(this);
        hiddenCells.Remove(Cell);
    }

    public override void Interact(PlayerStats player)
    {
        if (Hidden) return;
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
            GameLog.Add("곡괭이로 돌무더기를 내리쳤다.", GameLog.Info);
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
            if (m.Asleep && !m.Dead && GridNav.Distance(m.Cell, Cell) <= 5 && GridNav.LineOfSight(m.Cell, Cell) && Random.value < 0.25f) m.WakeFromNoise();
    }
}
