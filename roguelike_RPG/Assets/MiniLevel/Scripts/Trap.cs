using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 바닥 함정 (설계 A4): 가시 / 독 / 경보. 평소엔 숨겨져 있고, 밟으면 작동하며 모습을 드러낸다.
// - 가시: 밟으면 피해.  독: 밟으면 중독.  경보: 근처 잠든 적을 깨우고, 아직 나를 못 본 적이 함정 자리로 모여든다.
// - 한 번 작동하면 무력해진다 (드러난 채 남는다). 다트·돌멩이를 던져 맞혀도 작동한다 (Thrower.TrapHook).
// - 밟지 않아도 곁을 지나가면 낮은 확률로 발견한다. 돋보기 모드면 훨씬 잘 찾는다 (SecretSearch).
// 몬스터가 밟는 것은 아직 없다.
public class Trap : Interactable, SecretSearch.IHidden
{
    public enum Kind { Spike, Poison, Alarm }

    // ---- 수치 ----
    public const int SpikeMin = 4, SpikeMax = 7;     // 가시 피해 (층마다 +SpikePerFloor)
    public const float SpikePerFloor = 0.5f;
    public const int PoisonTurns = 5;                // 독 함정 중독 턴
    public const int AlarmRadius = 8;                // 경보가 닿는 거리 (칸)

    public Kind kind;
    public bool Revealed { get; private set; }
    public bool Spent { get; private set; }          // 이미 작동했다
    bool SecretSearch.IHidden.IsHidden => !Revealed;
    string SecretSearch.IHidden.What => $"{KindName(kind)} 함정";

    static readonly List<Trap> all = new List<Trap>();
    public static Trap At(Vector2Int cell)
    {
        foreach (var t in all) if (t != null && t.Cell == cell) return t;
        return null;
    }

    public static string KindName(Kind k) => k switch { Kind.Spike => "가시", Kind.Poison => "독", _ => "경보" };

    Renderer[] renderers;
    Transform spikes;

    // FloorGenerator가 만든다
    public static Trap Create(Vector2Int cell, Kind kind, Transform parent, bool revealed = false)
    {
        var go = new GameObject($"Trap_{kind}");
        go.SetActive(false);   // 모양을 다 만든 뒤 깨운다 (Awake에서 렌더러를 모으므로)
        go.transform.SetParent(parent, false);
        go.transform.position = GridTile.CellToWorld(cell);
        var t = go.AddComponent<Trap>();
        t.kind = kind;
        t.triggerOnEnter = true;
        t.BuildLook();
        go.SetActive(true);
        if (revealed) t.Reveal(false);
        return t;
    }

    protected override void Awake()
    {
        triggerOnEnter = true;
        base.Awake();
        all.Add(this);
        SecretSearch.Register(this);
        renderers = GetComponentsInChildren<Renderer>(true);
        if (!Revealed) foreach (var r in renderers) r.enabled = false;
    }

    void OnDestroy() { all.Remove(this); SecretSearch.Unregister(this); }

    // ---- 모양: VARCO 모델(Resources/Models/Trap*)이 있으면 그것, 없으면 간단한 도형 ----
    public const float ModelFootprint = 2.8f;   // 모델의 가로·세로 중 긴 쪽 (한 칸 = 4)
    static string ModelName(Kind k) => k == Kind.Spike ? "TrapSpike" : k == Kind.Poison ? "TrapPoison" : "TrapAlarm";

    void BuildLook()
    {
        if (ResourceModel.Spawn(ModelName(kind), transform, ModelFootprint) != null)
        {
            // 가시 함정: 작동하면 모델 위로 가시가 솟는다 (평소엔 바닥 아래에 숨어 있다)
            if (kind == Kind.Spike) BuildSpikes(new Color(0.62f, 0.6f, 0.56f));
            return;
        }
        BuildPrimitiveLook();
    }

    void BuildSpikes(Color color)
    {
        spikes = new GameObject("Spikes").transform;
        spikes.SetParent(transform, false);
        spikes.localPosition = new Vector3(0, -0.7f, 0);
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                var s = Part(PrimitiveType.Cube, spikes, new Vector3(x * 0.75f, 0.3f, z * 0.75f), new Vector3(0.14f, 0.8f, 0.14f), color);
                s.transform.localRotation = Quaternion.Euler(0, 45, 0);
            }
    }

    void BuildPrimitiveLook()
    {
        Color plate = kind == Kind.Spike ? new Color(0.35f, 0.33f, 0.32f) : kind == Kind.Poison ? new Color(0.22f, 0.4f, 0.12f) : new Color(0.5f, 0.38f, 0.12f);
        var p = Part(PrimitiveType.Cube, transform, new Vector3(0, 0.04f, 0), new Vector3(2.6f, 0.08f, 2.6f), plate);
        p.name = "Plate";
        switch (kind)
        {
            case Kind.Spike:
                BuildSpikes(new Color(0.6f, 0.58f, 0.55f));   // 작동하면 위로 솟는다
                break;
            case Kind.Poison:
                for (int i = 0; i < 4; i++)
                {
                    float a = i * Mathf.PI / 2 + 0.6f;
                    Part(PrimitiveType.Cylinder, transform, new Vector3(Mathf.Cos(a) * 0.7f, 0.1f, Mathf.Sin(a) * 0.7f), new Vector3(0.3f, 0.04f, 0.3f), new Color(0.05f, 0.08f, 0.04f));
                }
                Part(PrimitiveType.Sphere, transform, new Vector3(0, 0.12f, 0), new Vector3(0.6f, 0.18f, 0.6f), new Color(0.45f, 0.85f, 0.2f));
                break;
            default:
                Part(PrimitiveType.Cylinder, transform, new Vector3(0, 0.12f, 0), new Vector3(0.9f, 0.05f, 0.9f), new Color(0.8f, 0.62f, 0.2f));
                Part(PrimitiveType.Sphere, transform, new Vector3(0, 0.35f, 0), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.85f, 0.68f, 0.25f));
                break;
        }
    }

    static GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Color color)
    {
        var g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        var m = g.GetComponent<Renderer>().material;
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        return g;
    }

    // ---- 드러나기 ----
    public void Reveal(bool announce)
    {
        if (Revealed) return;
        Revealed = true;
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers) r.enabled = true;
        if (announce)
        {
            GameLog.Add($"바닥에 숨겨진 {KindName(kind)} 함정을 발견했다!", GameLog.Warn);
            DamagePopup.Spawn(transform.position + Vector3.up * 1.5f, "함정!", DamagePopup.Big, 1.2f);
        }
    }

    void SecretSearch.IHidden.Discover() => Reveal(true);

    // ---- 작동 ----
    // 플레이어가 밟았다
    public override void Interact(PlayerStats player)
    {
        if (Spent || player.Dead) return;
        bool known = Revealed;
        Reveal(false);
        Spent = true;
        GridClickMover.StopWalking();
        switch (kind)
        {
            case Kind.Spike:
                int dmg = Random.Range(SpikeMin, SpikeMax + 1) + Mathf.FloorToInt(SpikePerFloor * (RunState.Floor - 1));
                GameLog.Add(known ? $"가시 함정을 밟았다! {dmg}의 피해." : $"딸깍— 바닥에서 가시가 솟았다! {dmg}의 피해.", GameLog.Bad);
                DamagePopup.Spawn(DamagePopup.Above(player), $"-{dmg}", DamagePopup.Hurt, 1.3f);
                ScreenEffects.PlayerHit((float)dmg / player.maxHp, 0f);
                player.Damage(dmg);
                if (!player.Dead) GridClickMover.PlayAnim("Hit");
                StartCoroutine(SpikesUp());
                break;
            case Kind.Poison:
                GameLog.Add(known ? "독 함정을 밟았다!" : "딸깍— 발밑에서 독 안개가 뿜어져 나왔다!", GameLog.Bad);
                player.Poison(PoisonTurns);
                StartCoroutine(Puff(new Color(0.45f, 0.85f, 0.2f, 0.55f)));
                break;
            default:
                GameLog.Add(known ? "경보 함정을 밟았다!" : "딸깍— 날카로운 종소리가 울려 퍼진다!", GameLog.Bad);
                SoundAlarm();
                break;
        }
        Dim();
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + GridClickMover.TurnSeconds);
    }

    // 던진 다트·돌멩이에 맞아 작동한다 (그 자리에 아무도 없으니 가시·독은 헛되이 터지고, 경보는 적을 이리로 부른다)
    public bool TriggerRemote(ItemDef by)
    {
        if (Spent) return false;
        Reveal(false);
        Spent = true;
        string what = by != null ? by.name : "던진 것";
        switch (kind)
        {
            case Kind.Spike:
                GameLog.Add($"{what}{Korean.Josa(what, "이", "가")} 함정을 건드려 가시가 솟았다.", GameLog.Good);
                StartCoroutine(SpikesUp());
                break;
            case Kind.Poison:
                GameLog.Add($"{what}{Korean.Josa(what, "이", "가")} 함정을 건드려 독 안개가 흩어졌다.", GameLog.Good);
                StartCoroutine(Puff(new Color(0.45f, 0.85f, 0.2f, 0.55f)));
                break;
            default:
                GameLog.Add($"{what}{Korean.Josa(what, "이", "가")} 경보 함정을 울렸다!", GameLog.Warn);
                SoundAlarm();
                break;
        }
        Dim();
        return true;
    }

    void SoundAlarm()
    {
        Sfx.Play("trap_alarm");
        CameraFollowOcclusion.Shake(0.12f, 0.3f);
        DamagePopup.Spawn(transform.position + Vector3.up * 1.5f, "땡그랑!", DamagePopup.Big, 1.3f);
        int n = Monster.HearAlarm(Cell, AlarmRadius);
        if (n > 0) GameLog.Add($"근처의 적 {n}마리가 소리를 들었다…", GameLog.Bad);
        StartCoroutine(Puff(new Color(1f, 0.85f, 0.35f, 0.45f)));
    }

    void Dim()
    {
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", new Color(0.3f, 0.28f, 0.26f));
        foreach (var r in renderers) if (r != null) r.SetPropertyBlock(block);
    }

    IEnumerator SpikesUp()
    {
        Sfx.Play("trap_spike");
        if (spikes == null) yield break;
        var from = spikes.localPosition;
        var to = new Vector3(from.x, 0.35f, from.z);   // 모델 판(두께 약 0.5) 위로 가시 끝이 확 드러나게
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.08f)
        {
            spikes.localPosition = Vector3.Lerp(from, to, t);
            yield return null;
        }
        spikes.localPosition = to;
    }

    // 퍼지며 사라지는 둥근 연기 (독·경보)
    IEnumerator Puff(Color color)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(g.GetComponent<Collider>());
        g.transform.position = transform.position + Vector3.up * 0.5f;
        var mat = new Material(Shader.Find("Sprites/Default")) { color = color };
        g.GetComponent<Renderer>().material = mat;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.8f)
        {
            g.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 4f, t);
            var c = color; c.a *= 1f - t; mat.color = c;
            yield return null;
        }
        Destroy(g);
    }
}

// 숨겨진 것(함정·숨겨진 무너진 벽) 찾기. 매 턴 끝에 플레이어 주변의 숨겨진 것을 확률로 발견한다.
// 돋보기 모드(GridClickMover.Magnifying)면 반경과 확률이 커진다. FloorGenerator가 붙인다.
public class SecretSearch : MonoBehaviour
{
    public interface IHidden
    {
        bool IsHidden { get; }
        Vector2Int Cell { get; }
        string What { get; }
        void Discover();
    }

    public const int Radius = 2;                 // 평소: 이 거리(칸) 안의 숨겨진 것을
    public const float Chance = 0.04f;           //       턴마다 이 확률로 발견
    public const int MagnifyRadius = 3;          // 돋보기: 더 넓게
    public const float MagnifyChance = 0.4f;     //         훨씬 잘 찾는다 (한 칸에 2턴이라 두 번 굴린다)

    static readonly List<IHidden> hidden = new List<IHidden>();
    public static void Register(IHidden h) { if (!hidden.Contains(h)) hidden.Add(h); }
    public static void Unregister(IHidden h) => hidden.Remove(h);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => hidden.Clear();

    PlayerStats player;

    void OnEnable() => TurnManager.OnTurnEnded += OnTurn;
    void OnDisable() => TurnManager.OnTurnEnded -= OnTurn;

    void OnTurn(int turn)
    {
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        if (player == null || player.Dead) return;
        var here = GridTile.WorldToCell(player.transform.position);
        bool mag = GridClickMover.Magnifying;
        int radius = mag ? MagnifyRadius : Radius;
        float chance = mag ? MagnifyChance : Chance;
        bool found = false;
        foreach (var h in hidden.ToArray())
        {
            if (h == null || (h is Object o && o == null) || !h.IsHidden) continue;
            if (GridNav.Distance(h.Cell, here) > radius || Random.value >= chance) continue;
            h.Discover();
            found = true;
        }
        if (found) GridClickMover.StopWalking();   // 찾았으면 멈춰서 보게 (그대로 밟지 않게)
    }
}
