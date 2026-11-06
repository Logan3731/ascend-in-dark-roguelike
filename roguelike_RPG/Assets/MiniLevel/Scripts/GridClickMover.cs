using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 바닥 칸을 클릭하면 그 칸을 목표로 정하고 한 칸씩 이동한다 (8방향, 대각선도 한 칸 = 한 턴).
// 마우스를 누르고 있으면 커서를 따라 계속 걷는다.
// 한 칸 갈 때마다 목표까지의 길을 다시 찾는다 (그새 몬스터가 움직였을 수 있으므로).
// 상호작용 대상을 클릭하면 그 옆까지 걸어가서 상호작용한다. [F] 횃불 들기/내리기, [E] 식량 먹기.
// 걷는 중에 다른 곳을 누르면 목표만 바뀌고, 지금 걷는 한 칸을 마친 뒤 방향을 튼다.
// 공격·연출이 끝나기 전에는 다음 행동을 하지 않는다.
public class GridClickMover : MonoBehaviour
{
    public float moveSeconds = 0.7f;    // 한 칸 걷는 시간 (0.55초도 빠르다는 피드백). 프리팹 P_Player에도 같은 값이 직렬화돼 있다
    // 걷기 모션(Torch_Walk 한 바퀴 1.1초 = 두 걸음)이 한 칸에 반 바퀴가 되게 속도를 맞춘다 (Start)
    const float WalkCycleSeconds = 1.1f;
    public GameObject targetMarker;
    public Animator animator;   // "Moving" bool: 걷는 동안 true

    static readonly int MovingParam = Animator.StringToHash("Moving");
    static readonly int WalkSpeedParam = Animator.StringToHash("WalkSpeed");   // 걷기 상태의 재생 속도 배수 (Player.controller)

    PlayerStats stats;
    bool stepping;               // 한 칸 이동 중
    Vector2Int stepCell;         // 지금 걷고 있는 한 칸의 도착 칸
    Vector2Int? goalCell;        // 걸어가는 목표 칸
    Interactable goalTarget;     // 다가가서 상호작용할 대상
    Transform model;             // 공격 연출용 모델 (첫 번째 자식)
    Vector3 modelHome;
    float holdTime;              // 마우스를 누르고 있는 시간
    bool holdFromUI;             // UI 위에서 누르기 시작했으면 따라 걷지 않는다
    Vector2Int? lastHoldCell;
    const float HoldDelay = 0.25f;
    const float TurnPortion = 0.3f;   // 한 칸 걷는 시간 중 몸을 돌리는 데 쓰는 비율

    // 이동·공격·연출이 다 끝나야 다음 행동을 받는다
    bool Busy => stepping || Time.time < TurnResolver.BusyUntil || Potions.PlayerAsleep;
    bool HasGoal => goalCell != null || goalTarget != null;
    static bool Blocked => DialogUI.IsOpen || InventoryUI.IsOpen || PauseMenu.IsOpen || TitleScreen.IsOpen || CraftUI.IsOpen || ShopUI.IsOpen;

    static GridClickMover instance;
    void Awake() => instance = this;

    // 걷던 길을 멈춘다 (몬스터가 나를 알아챘을 때 등). 지금 걷는 한 칸은 마저 걷는다.
    public static void StopWalking() { if (instance != null) instance.ClearGoal(); }

    // 밀려난다 (거인 해골의 포효): 지금 걷는 한 칸을 마친 뒤 dir 쪽으로 한 칸. 벽·닫힌 문·몬스터에 막히면 그대로.
    public const float KnockbackSeconds = 0.2f;
    public static void Knockback(Vector2Int dir)
    {
        if (instance == null || dir == Vector2Int.zero) return;
        instance.ClearGoal();
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + instance.moveSeconds + KnockbackSeconds);
        instance.StartCoroutine(instance.KnockbackRoutine(dir));
    }

    IEnumerator KnockbackRoutine(Vector2Int dir)
    {
        while (stepping) yield return null;
        var here = GridTile.WorldToCell(transform.position);
        var to = here + dir;
        var floors = GridNav.Floors();
        var door = Door.At(to);
        if (!GridNav.Walkable(floors).Contains(to) || !GridNav.CornerFree(here, dir, floors) || (door != null && !door.IsOpen))
            yield break;   // 벽에 막혔다
        var from = transform.position;
        var target = GridTile.CellToWorld(to); target.y = from.y;
        for (float t = 0f; t < 1f; t += Time.deltaTime / KnockbackSeconds)
        {
            transform.position = Vector3.Lerp(from, target, 1f - (1f - t) * (1f - t));
            yield return null;
        }
        transform.position = target;
        GameLog.Add("뒤로 밀려났다.", GameLog.Bad);
        // 밀려난 칸에서 밟으면 발동하는 것 (함정·물·줍기)
        foreach (var trig in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
            if (trig.triggerOnEnter && trig.Cell == to) trig.Interact(stats);
    }

    // ---- 입력 잠금 (설계 A3) ----
    // 상호작용·공격·적 발견 연출이 진행되는 동안(BusyUntil)에는 이동 클릭·누르고 걷기·스킬·던지기·먹기·소지품을 모두 받지 않는다.
    // 예약도 받지 않는다: 그동안 누른 클릭은 버린다. (걷는 한 칸 도중의 클릭은 지금처럼 목표만 바꾼다)
    public static bool InputLocked => Time.time < TurnResolver.BusyUntil || Potions.PlayerAsleep;

    bool alertShown;   // 내 머리 위 `!`가 떠 있는 동안 (층이 바뀌면 새 인스턴스라 저절로 풀린다)
    IEnumerator ClearAlertFlag(float after) { yield return new WaitForSeconds(after); alertShown = false; }

    // 한 턴짜리 행동(상호작용·던지기·돋보기로 살피기)에 걸리는 시간 = 한 칸 걷는 시간. 걷기와 같은 박자로 맞춘다.
    public static float TurnSeconds => instance != null ? instance.moveSeconds : 0.55f;

    public const float AlertPause = 0.5f;   // 적 발견 `!` 뒤 멈춰 있는 시간(초). 걷던 한 칸이 끝난 뒤부터 센다.

    // 적을 발견했다: 걷던 길을 끊고, 지금 걷는 한 칸이 끝난 뒤 AlertPause만큼 입력을 막는다.
    // 누르고 있던 마우스도 한 번 떼야 다시 걷는다 (적 쪽으로 그대로 걸어 들어가지 않게).
    public static void Alert()
    {
        bool fresh = instance != null && !instance.alertShown;   // 같은 순간 여러 마리가 알아채도 내 `!`는 하나
        float wait = AlertPause + (instance != null && instance.stepping ? instance.moveSeconds : 0f);
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + wait);
        if (instance == null) return;
        if (fresh)
        {
            instance.alertShown = true;
            Sfx.Play("alert");
            DamagePopup.Spawn(DamagePopup.Above(instance, 1.2f), "!", DamagePopup.Big, 1.8f);
            instance.StartCoroutine(instance.ClearAlertFlag(wait));
        }
        instance.ClearGoal();
        instance.holdSuppressed = true;
    }

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        if (transform.childCount > 0) { model = transform.GetChild(0); modelHome = model.localPosition; }
        if (targetMarker != null) targetMarker.SetActive(false);
        if (animator != null && animator.runtimeAnimatorController != null)
            foreach (var p in animator.parameters)
                if (p.nameHash == WalkSpeedParam) animator.SetFloat(WalkSpeedParam, (WalkCycleSeconds * 0.5f) / moveSeconds);
    }

    void Update()
    {
        if (stats != null && stats.Dead) { ClearGoal(); return; }
        if (Blocked) return;

        // 던질 곳을 겨누는 중: 마우스 아래 칸을 표시하고, 왼쪽 클릭이면 던진다
        if (Thrower.Aiming != null) { UpdateAim(); return; }

        // 클릭은 걷는 중에도 받아서 목표만 바꿔 둔다. 실제 방향 전환은 지금 걷는 한 칸을 마친 뒤에 한다.
        // 연출 중(InputLocked)에는 받지 않는다 — 예약도 하지 않는다.
        bool locked = InputLocked;
        if (!locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOverUI())
            HandleClick();
        HandleHold(locked);

        // [Q] 3연격 준비/해제: 자세만 바꾸는 것이라 걷는 중에도 받는다 (연출 중에는 막는다)
        var keys = Keyboard.current;
        if (!locked && keys != null && keys.qKey.wasPressedThisFrame) TryTriple();
        if (!locked && keys != null && keys.sKey.wasPressedThisFrame) TryMagnifier();
        if (!locked && keys != null && keys.eKey.wasPressedThisFrame) TryEat();          // [E] 식량 (걷는 중에도)   // [S] 돋보기 (걷는 중에도 켜고 끈다)

        if (Busy) return;

        // 목표가 있으면 한 칸 진행
        if (HasGoal)
        {
            Advance();
            return;
        }

        var kb = Keyboard.current;
        if (kb != null && stats != null)
        {
            if (kb.fKey.wasPressedThisFrame) stats.ToggleTorch();
            if (kb.wKey.wasPressedThisFrame) TrySlam();
            if (kb.tKey.wasPressedThisFrame)
            {
                if (stats.TorchLit) { ClearGoal(); Thrower.Begin(Items.Torch); }
                else GameLog.Add("들고 있는 횃불이 없다.", GameLog.Info);
            }
            if (kb.rKey.wasPressedThisFrame)
            {
                if (stats.Bag.Count(Items.Dart) > 0) { ClearGoal(); Thrower.Begin(Items.Dart); }
                else GameLog.Add("던질 다트가 없다.", GameLog.Info);
            }
        }
    }

    // ---- 스킬 (HUD 버튼도 여기로) ----
    const float SlamImpact = 0.3f;   // 뛰어올랐다가 바닥에 닿는 순간
    const float SlamTime = 0.55f;    // 점프 찍기 연출이 끝날 때까지

    // ---- 돋보기 (설계 A4) ----
    // 켜 두면 한 칸 움직이는 데 2턴(제자리에서 살피는 턴 + 걷는 턴)이 들지만, 숨겨진 함정·무너진 벽을 훨씬 잘 찾는다 (SecretSearch).
    // 제자리에서 살피는 턴의 시간은 한 칸 걷는 시간과 같다 (TurnSeconds)
    public static bool Magnifying { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetMagnifier() => Magnifying = false;

    public static void UseMagnifier() { if (instance != null) instance.TryMagnifier(); }

    void TryMagnifier()
    {
        if (stats == null || stats.Dead || Blocked || Thrower.Aiming != null || InputLocked) return;
        Magnifying = !Magnifying;
        GameLog.Add(Magnifying ? "돋보기를 꺼냈다." : "돋보기를 넣었다.", GameLog.Info);
        stats.NotifyChanged();
    }

    public static void UseFood() { if (instance != null) instance.TryEat(); }

    // 단축키 칸에서 던지기: 가던 길을 멈추고 겨누기 시작
    public static void BeginThrow(ItemDef def)
    {
        if (instance == null || def == null) return;
        instance.ClearGoal();
        Thrower.Begin(def);
    }

    void TryEat()
    {
        if (stats == null || stats.Dead || Blocked || Thrower.Aiming != null || InputLocked) return;
        stats.EatFood();
    }

    public static void UseTriple() { if (instance != null) instance.TryTriple(); }
    public static void UseSlam() { if (instance != null) instance.TrySlam(); }

    void TryTriple()
    {
        if (stats == null || stats.Dead || Blocked || Thrower.Aiming != null || InputLocked) return;
        stats.ToggleTriple();
    }

    // [W] 점프 찍기: 제자리에서 한 턴. 주변 8칸의 적에게 공격력만큼 피해(피하거나 막지 못함) + 기절.
    void TrySlam()
    {
        if (stats == null || stats.Dead || Blocked || Thrower.Aiming != null || Busy) return;
        if (!stats.CanSlam()) return;
        ClearGoal();
        stats.SpendSlam();
        var cell = GridTile.WorldToCell(transform.position);
        GameLog.Add("뛰어올라 바닥을 내리찍는다!", GameLog.Good);
        if (HasAnim("JumpSlam")) PlayAnim("JumpSlam"); else PlayAttackAnim();
        StartCoroutine(SlamHop());

        int hit = 0;
        foreach (var m in FindObjectsByType<Monster>(FindObjectsSortMode.None))
        {
            if (!m.enabled || m.Dead || m.Cell == cell || GridNav.Distance(m.Cell, cell) != 1) continue;
            m.HitBySlam(stats, stats.RollAttack(), SlamImpact);   // 기절이 먼저 걸려서 이번 턴에 덤비지 못한다
            hit++;
        }
        if (hit == 0) GameLog.Add("…주변에 맞을 적이 없었다.", GameLog.Info);

        // 한 턴을 쓴다: 그사이 다른 적도 움직인다
        TurnResolver.BeginTurn(cell, moveSeconds);
        TurnResolver.ResolveAttacks(stats, null);
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + SlamTime);
    }

    // 모델만 뛰어올랐다가 내려찍는다
    IEnumerator SlamHop()
    {
        if (model != null)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / SlamImpact)
            {
                model.localPosition = modelHome + Vector3.up * (1.6f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }
            model.localPosition = modelHome;
        }
        else yield return new WaitForSeconds(SlamImpact);
        CameraFollowOcclusion.Shake(0.35f, 0.25f);
        Sfx.Play("skill_slam");
    }

    void UpdateAim()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (mouse != null && mouse.rightButton.wasPressedThisFrame))
        {
            Thrower.Cancel(true);
            if (targetMarker != null) targetMarker.SetActive(false);
            return;
        }
        if (mouse == null || Camera.main == null) return;
        var ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter)) return;
        var cell = GridTile.WorldToCell(ray.GetPoint(enter));
        bool ok = Thrower.Trace(GridTile.WorldToCell(transform.position), cell, out var land, out _);
        if (ok) ShowMarker(land);
        else if (targetMarker != null) targetMarker.SetActive(false);
        if (ok && !Busy && mouse.leftButton.wasPressedThisFrame && !PointerOverUI())
        {
            if (targetMarker != null) targetMarker.SetActive(false);
            Thrower.Throw(stats, this, cell);
        }
    }

    // 지금 서 있는 칸 (걷는 중이면 도착할 칸)
    Vector2Int CurrentCell => stepping ? stepCell : GridTile.WorldToCell(transform.position);

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    void HandleClick()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        // 상호작용 대상을 눌렀는지 먼저 본다 (벽에 가려져도 대상만 골라낸다)
        foreach (var hit in Physics.RaycastAll(ray, 500f))
        {
            var target = hit.collider.GetComponentInParent<Interactable>();
            if (target != null && !target.triggerOnEnter && target.isActiveAndEnabled)
            {
                goalCell = null;
                goalTarget = target;
                ShowMarker(target.Cell);
                return;
            }
        }

        // 벽을 눌렀으면 벽 너머로 돌아가지 않는다: 그 자리에 빨간 표시만
        if (Physics.Raycast(ray, out var first, 500f) && first.collider.GetComponentInParent<WallTile>() != null)
        {
            ShowBlocked(GridTile.WorldToCell(first.collider.transform.position));
            return;
        }

        // 바닥 높이의 평면과 교차한 지점으로 칸을 정한다.
        var ground = new Plane(Vector3.up, Vector3.zero);
        if (!ground.Raycast(ray, out float enter)) return;
        var cell = GridTile.WorldToCell(ray.GetPoint(enter));
        // 그 칸에 막는 사물(상자·고블린·몬스터·닫힌 문)이 있으면 그 옆까지 가서 상호작용한다
        var thing = InteractableAt(cell);
        if (thing != null && thing.GetComponent<GridBlocker>() is GridBlocker b && b.enabled)
        {
            goalCell = null;
            goalTarget = thing;
            ShowMarker(cell);
            return;
        }
        // 밟을 수 있는 칸의 사물(뼈·벽 횃불·계단 등)은 도착하면 상호작용. 갈 수 없는 칸(어둠·벽·막힌 곳)이면 빨간 표시
        if (!SetDestination(cell)) ShowBlocked(cell);
    }

    // 그 칸에 있는 상호작용 대상 (밟으면 저절로 발동하는 것·열린 문은 빼고)
    static Interactable InteractableAt(Vector2Int cell)
    {
        foreach (var it in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
        {
            if (it.triggerOnEnter || !it.isActiveAndEnabled || it.Cell != cell) continue;
            if (it is Door d && d.IsOpen) continue;
            return it;
        }
        return null;
    }

    // 누르고 있으면 커서를 따라 계속 걷는다 (목표 칸만 계속 바꿔 준다).
    // 커서가 한 칸에 머물러 있어도, 목표에 닿았거나 멈춰 섰는데 커서 칸이 아직 내 칸이 아니면 그쪽으로 다시 걷는다.
    // (공격받아 멈춘 뒤에도 계속 누르고 있으면 이어 간다. 적 발견 `!`로 멈췄거나 연출 중에 누른 것은 한 번 떼야 한다)
    bool holdSuppressed;
    float nextHoldRetry;
    const float HoldRetryEvery = 0.2f;   // 갈 수 없는 칸을 계속 누르고 있을 때 길 찾기를 다시 해 보는 간격

    void HandleHold(bool locked)
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.isPressed) { holdTime = 0f; holdSuppressed = false; return; }
        if (mouse.leftButton.wasPressedThisFrame)
        {
            holdTime = 0f; lastHoldCell = null; holdFromUI = PointerOverUI();
            holdSuppressed = locked;   // 연출 중에 누르기 시작한 것은 끝나도 따라 걷지 않는다 (예약 없음)
            return;
        }
        holdTime += Time.deltaTime;
        if (locked || holdFromUI || holdSuppressed || holdTime < HoldDelay || goalTarget != null) return;
        var cam = Camera.main;
        if (cam == null) return;
        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter)) return;
        var cell = GridTile.WorldToCell(ray.GetPoint(enter));
        bool moved = cell != lastHoldCell;
        if (!moved)
        {
            // 같은 칸: 걷고 있거나 이미 그 칸에 서 있으면 할 일이 없다
            if (HasGoal || stepping || cell == CurrentCell || Time.time < nextHoldRetry) return;
            nextHoldRetry = Time.time + HoldRetryEvery;
        }
        lastHoldCell = cell;
        if (!SetDestination(cell) && moved) ShowBlocked(cell);   // 빨간 표시는 커서가 새 칸으로 갔을 때만
    }

    // ---- 갈 수 없는 곳을 눌렀을 때: 그 칸에 빨간 테두리가 잠깐 떴다 사라진다 ----

    GameObject blockedMarker;
    Material blockedMat;
    Renderer[] blockedRenderers;
    float[] blockedAlpha;        // 처음 투명도 (사라지는 도중에 다시 눌러도 원래대로 시작하게)
    Coroutine blockedFade;

    void ShowBlocked(Vector2Int cell)
    {
        if (blockedMarker == null) BuildBlockedMarker();
        blockedMarker.transform.position = GridTile.CellToWorld(cell) + Vector3.up * 0.05f;
        blockedMarker.SetActive(true);
        if (blockedFade != null) StopCoroutine(blockedFade);
        blockedFade = StartCoroutine(FadeBlocked());
    }

    void BuildBlockedMarker()
    {
        blockedMarker = new GameObject("BlockedMarker");
        blockedMat = new Material(Shader.Find("Sprites/Default"));
        float s = GridTile.CellSize * 0.92f, t = 0.28f;   // 칸 크기, 테두리 두께
        void Quad(Vector3 pos, Vector3 scale, float alpha)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(blockedMarker.transform, false);
            q.transform.localPosition = pos;
            q.transform.localRotation = Quaternion.Euler(90, 0, 0);
            q.transform.localScale = scale;
            var m = new Material(blockedMat) { color = new Color(1f, 0.12f, 0.08f, alpha) };
            q.GetComponent<Renderer>().material = m;
        }
        Quad(Vector3.zero, new Vector3(s, s, 1), 0.18f);                          // 옅은 채움
        Quad(new Vector3(0, 0.01f, (s - t) / 2), new Vector3(s, t, 1), 0.85f);    // 테두리 네 변
        Quad(new Vector3(0, 0.01f, -(s - t) / 2), new Vector3(s, t, 1), 0.85f);
        Quad(new Vector3((s - t) / 2, 0.01f, 0), new Vector3(t, s, 1), 0.85f);
        Quad(new Vector3(-(s - t) / 2, 0.01f, 0), new Vector3(t, s, 1), 0.85f);
        blockedRenderers = blockedMarker.GetComponentsInChildren<Renderer>();
        blockedAlpha = new float[blockedRenderers.Length];
        for (int i = 0; i < blockedRenderers.Length; i++) blockedAlpha[i] = blockedRenderers[i].material.color.a;
    }

    void SetBlockedAlpha(float k)
    {
        for (int i = 0; i < blockedRenderers.Length; i++)
        {
            var c = blockedRenderers[i].material.color;
            c.a = blockedAlpha[i] * k;
            blockedRenderers[i].material.color = c;
        }
    }

    IEnumerator FadeBlocked()
    {
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.6f)
        {
            SetBlockedAlpha(t < 0.5f ? 1f : 1f - (t - 0.5f) * 2f);   // 잠깐 그대로 있다가 사라진다
            yield return null;
        }
        blockedMarker.SetActive(false);
        blockedFade = null;
    }

    public bool SetDestination(Vector2Int goal)
    {
        var here = CurrentCell;
        if (goal == here)
        {
            // 걷는 중에 내 자리를 누르면 이번 칸에서 멈춘다
            if (HasGoal) { goalTarget = null; goalCell = here; ShowMarker(here); }
            return true;
        }
        if (FindPath(here, c => c == goal) == null) return false;
        goalTarget = null;
        goalCell = goal;
        ShowMarker(goal);
        return true;
    }

    // 목표를 다시 확인하고 한 칸만 움직이거나, 도착했으면 상호작용한다
    void Advance()
    {
        var here = GridTile.WorldToCell(transform.position);

        if (goalTarget != null)
        {
            var target = goalTarget;
            if (!target.isActiveAndEnabled) { ClearGoal(); return; }
            if (IsNextTo(here, target.Cell))
            {
                ClearGoal();
                Face(target.transform.position);
                target.Interact(stats);
                return;
            }
            var route = FindPath(here, c => IsNextTo(c, target.Cell));
            if (route == null || route.Count == 0) { ClearGoal(); return; }
            ShowMarker(target.Cell);
            StepOrOpen(route[0]);
            return;
        }

        if (goalCell != null)
        {
            var goal = goalCell.Value;
            if (here == goal) { ClearGoal(); return; }
            var route = FindPath(here, c => c == goal);
            if (route == null || route.Count == 0) { ClearGoal(); return; }
            StepOrOpen(route[0]);
        }
    }

    // 다음 칸이 닫힌 문이면 이번 턴은 문을 민다 (목표는 그대로라 열리면 이어서 걷는다)
    void StepOrOpen(Vector2Int next)
    {
        var door = Door.At(next);
        if (door != null && !door.IsOpen) { if (animator != null) animator.SetBool(MovingParam, false); door.Push(stats, false); return; }
        StartCoroutine(Step(next));
    }

    void ShowMarker(Vector2Int cell)
    {
        if (targetMarker == null) return;
        targetMarker.transform.position = GridTile.CellToWorld(cell) + Vector3.up * 0.02f;
        targetMarker.SetActive(true);
    }

    // 개발자 모드: 그 칸(막혀 있으면 가장 가까운 빈 칸)으로 순간이동
    public static bool DevTeleport(Vector2Int target)
    {
        if (instance == null || instance.stepping || instance.stats == null || instance.stats.Dead) return false;
        var free = GridNav.Walkable(GridNav.Floors());
        Vector2Int? best = null;
        for (int r = 0; r <= 4 && best == null; r++)
            for (int dx = -r; dx <= r && best == null; dx++)
                for (int dy = -r; dy <= r && best == null; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var c = target + new Vector2Int(dx, dy);
                    if (free.Contains(c)) best = c;
                }
        if (best == null) return false;
        instance.ClearGoal();
        var p = GridTile.CellToWorld(best.Value);
        p.y = instance.transform.position.y;
        instance.transform.position = p;
        return true;
    }

    void ClearGoal()
    {
        goalCell = null;
        goalTarget = null;
        if (targetMarker != null) targetMarker.SetActive(false);
        if (!stepping && animator != null) animator.SetBool(MovingParam, false);
    }

    static bool IsNextTo(Vector2Int a, Vector2Int b) => a == b || GridNav.Adjacent(a, b);

    // 8방향 최단 경로 (GridNav). 시작 칸은 제외. 갈 수 없으면 null.
    static List<Vector2Int> FindPath(Vector2Int start, System.Func<Vector2Int, bool> isGoal)
    {
        var floors = GridNav.Floors();
        var walkable = GridNav.Walkable(floors);
        foreach (var d in Door.All) if (!d.IsOpen && !d.locked) walkable.Add(d.Cell);   // 닫힌 문은 가다가 연다
        return GridNav.FindPath(start, isGoal, walkable, floors);
    }

    public void FaceTo(Vector3 worldPos) => Face(worldPos);

    // 맞을 때 때린 쪽을 본다 (걷는 중이 아닐 때만)
    public static void FaceAttacker(Vector3 worldPos) { if (instance != null && !instance.stepping) instance.Face(worldPos); }

    // 대상 쪽으로 몸을 돌린다. 한 번에 꺾지 않고 FaceSeconds에 걸쳐 돈다 (걷는 중이면 Step이 방향을 맡는다)
    const float FaceSeconds = 0.12f;
    Coroutine turning;
    void Face(Vector3 worldPos)
    {
        var dir = worldPos - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude <= 0.001f) return;
        var to = Quaternion.LookRotation(dir);
        if (turning != null) StopCoroutine(turning);
        if (stepping || Quaternion.Angle(transform.rotation, to) < 5f) { transform.rotation = to; return; }
        turning = StartCoroutine(TurnTo(to));
    }

    IEnumerator TurnTo(Quaternion to)
    {
        var from = transform.rotation;
        for (float t = 0f; t < 1f; t += Time.deltaTime / FaceSeconds)
        {
            transform.rotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        transform.rotation = to;
        turning = null;
    }

    // 공격할 때 모델만 대상 쪽으로 살짝 내질렀다가 돌아온다
    // Mixamo 모션 트리거 (Attack·Hit·Die·Throw·Drink·PickUp·LightTorch). 애니메이터에 그 이름이 있을 때만 — MixamoSetup이 만든다.
    // delay초 뒤에 (공격·피격이 차례로 이어질 때 동작이 그 순간에 맞게)
    public static void PlayAnim(string trigger, float delay)
    {
        if (delay <= 0f || instance == null) { PlayAnim(trigger); return; }
        instance.StartCoroutine(instance.AnimLater(trigger, delay));
    }
    IEnumerator AnimLater(string trigger, float delay) { yield return new WaitForSeconds(delay); PlayAnim(trigger); }

    // ---- 동작 (줍기·마시기·횃불 붙이기·던지기) ----
    // 동작이 끝날 때까지 걷기 신호를 끄고 입력을 막는다. 예전엔 바로 다른 곳을 누르면 애니메이터가
    // "걷기 시작하면 동작을 끊고 걷는다" 전환으로 넘어가 모션이 사라졌다. 길은 동작이 끝난 뒤 이어 걷는다.
    // 시간 = 애니메이터에서 그 동작이 대기로 돌아가기 시작하는 때 (MixamoSetup의 길이 × 0.9) × 느린 걸음 비율
    public const float PickUpSeconds = 0.9f, DrinkSeconds = 1.25f, LightTorchSeconds = 1.25f, ThrowSeconds = 0.75f;

    public static void PlayAction(string trigger, float seconds)
    {
        if (!HasAnim(trigger)) return;
        PlayAnim(trigger);
        if (instance != null && instance.animator != null) instance.animator.SetBool(MovingParam, false);
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + seconds);
    }

    public static void PlayAnim(string trigger)
    {
        var a = instance != null ? instance.animator : null;
        if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) return;
        foreach (var p in a.parameters)
            if (p.name == trigger) { a.SetTrigger(trigger); return; }
    }

    // 공격: 몸은 제자리에서 대상 쪽으로 돌고, 공격 모션만 재생한다 (예전의 앞으로 밀기 = 박치기는 없앴다)
    public void PlayLunge(Vector3 targetPos, float delay = 0f)
    {
        StartCoroutine(AttackAt(targetPos, delay, null));
    }

    // 스킬 모션 (Triple·JumpSlam·ShieldBash·ThrowDart). 모션이 없으면 아무것도 안 한다 (PlayAnim과 같다)
    public void PlaySkill(string trigger, Vector3 targetPos, float delay = 0f)
    {
        StartCoroutine(AttackAt(targetPos, delay, trigger));
    }

    IEnumerator AttackAt(Vector3 targetPos, float delay, string skill)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Face(targetPos);
        if (skill != null) PlayAnim(skill);
        else PlayAttackAnim();
        Sfx.PlayWeapon("swing", skill == "ShieldBash" ? Items.Shield : stats != null ? stats.Weapon : null, 0.1f);
    }

    // 공격 모션이 여러 개면(Attack1·Attack2·Attack3…) 차례로 돌려 쓴다. 하나도 없으면 Attack.
    public static readonly string[] AttackTriggers = { "Attack1", "Attack2", "Attack3" };
    int attackIndex;
    void PlayAttackAnim()
    {
        for (int k = 0; k < AttackTriggers.Length; k++)
        {
            var trig = AttackTriggers[(attackIndex + k) % AttackTriggers.Length];
            if (!HasAnim(trig)) continue;
            attackIndex = (attackIndex + k + 1) % AttackTriggers.Length;
            PlayAnim(trig);
            return;
        }
        PlayAnim("Attack");
    }

    // 애니메이터에 이 트리거가 있는가 (모션이 들어왔는가)
    public static bool HasAnim(string trigger)
    {
        var a = instance != null ? instance.animator : null;
        if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) return false;
        foreach (var p in a.parameters) if (p.name == trigger) return true;
        return false;
    }

    // 이 칸에 도착하면 걷기를 끝내는가 (걷는 도중 목표가 바뀔 수 있어서 매번 지금 목표로 판단)
    bool IsFinalStep(Vector2Int cell)
    {
        if (goalTarget != null) return IsNextTo(cell, goalTarget.Cell);
        return goalCell == null || goalCell.Value == cell;
    }

    IEnumerator Step(Vector2Int cell)
    {
        if (turning != null) { StopCoroutine(turning); turning = null; }   // 걷는 동안 방향은 Step이 맡는다
        stepping = true;
        if (Magnifying)
        {
            // 돋보기: 먼저 제자리에서 한 턴 살핀다 (그사이 몬스터도 한 번 움직이고, 붙어 있던 녀석은 공격한다)
            var here = GridTile.WorldToCell(transform.position);
            stepCell = here;
            if (animator != null) animator.SetBool(MovingParam, false);
            TurnResolver.BeginTurn(here, moveSeconds);
            yield return new WaitForSeconds(TurnSeconds);
            TurnResolver.ResolveAttacks(stats, null);   // 턴 끝: 이때 숨은 것 찾기를 굴린다 (SecretSearch)
            bool attacked = TurnResolver.AttackedThisTurn;
            while (Time.time < TurnResolver.BusyUntil && !stats.Dead) yield return null;   // 공격·발견 연출이 끝날 때까지
            // 공격받았거나, 적을 발견했거나(Alert), 무언가 찾아 멈췄거나, 가려던 칸이 막혔으면 걷지 않는다
            bool blocked = !GridNav.Walkable(GridNav.Floors()).Contains(cell);
            if (stats.Dead || !HasGoal || (attacked && goalTarget == null) || blocked)
            {
                if (attacked && goalTarget == null) ClearGoal();
                stepping = false;
                if (HasGoal && !Busy && !Blocked && !stats.Dead) Advance();   // 막혔을 뿐이면 길을 다시 찾는다
                yield break;
            }
        }
        stepCell = cell;
        TurnResolver.BeginTurn(cell, moveSeconds);   // 몬스터도 같은 시간에 움직인다
        Sfx.Play("step", moveSeconds * 0.35f, 0.6f);
        if (animator != null) animator.SetBool(MovingParam, true);
        var from = transform.position;
        var to = GridTile.CellToWorld(cell);
        to.y = from.y;
        // 몸은 걸음 초반에 부드럽게 돌린다 (뚝 꺾이지 않게)
        var fromRot = transform.rotation;
        var dir = to - from; dir.y = 0;
        var toRot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : fromRot;

        for (float t = 0f; t < 1f; t += Time.deltaTime / moveSeconds)
        {
            transform.position = Vector3.Lerp(from, to, t);
            transform.rotation = Quaternion.Slerp(fromRot, toRot, Mathf.SmoothStep(0f, 1f, t / TurnPortion));
            // 마지막 칸이면 도착 직전에 미리 멈춤 동작으로 넘긴다 (도착 후 걸음이 늦게 끝나지 않도록)
            if (t > 0.8f && animator != null && IsFinalStep(cell)) animator.SetBool(MovingParam, false);
            yield return null;
        }
        transform.position = to;
        transform.rotation = toRot;
        TurnResolver.ResolveAttacks(stats, null);   // 이동한 턴: 붙어 있던 몬스터만 공격
        if (!TurnResolver.AttackedThisTurn) stats.OnStepped();   // 공격받지 않고 걸으면 천천히 회복

        // 밟으면 발동하는 칸
        foreach (var trig in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
            if (trig.triggerOnEnter && trig.Cell == cell) trig.Interact(stats);

        // 바닥으로 걸어가던 중 공격을 받았으면 멈춰서 상황을 보게 한다 (대상에게 다가가던 중이면 계속)
        if (TurnResolver.AttackedThisTurn && goalTarget == null) ClearGoal();
        if (IsFinalStep(cell) || !HasGoal)
        {
            if (animator != null) animator.SetBool(MovingParam, false);
            if (goalCell != null && GridTile.WorldToCell(transform.position) == goalCell)
            {
                ClearGoal();
                // 목표 칸에 도착: 그 칸에 있는 것(뼈·벽 횃불·계단·도구)과 바로 상호작용 (클릭하지 않아도)
                var thing = InteractableAt(cell);
                if (thing != null && !(thing is Monster) && !(thing is Door)) thing.Interact(stats);
            }
        }
        stepping = false;
        // 다음 칸으로 바로 이어 걷는다 (한 프레임 멈칫하지 않게)
        if (HasGoal && !Busy && !Blocked && !stats.Dead) Advance();
    }
}
