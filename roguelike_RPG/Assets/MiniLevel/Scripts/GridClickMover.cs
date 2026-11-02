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
    public float moveSeconds = 0.15f;
    public GameObject targetMarker;
    public Animator animator;   // "Moving" bool: 걷는 동안 true

    static readonly int MovingParam = Animator.StringToHash("Moving");

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
    bool Busy => stepping || Time.time < TurnResolver.BusyUntil;
    bool HasGoal => goalCell != null || goalTarget != null;
    static bool Blocked => DialogUI.IsOpen || InventoryUI.IsOpen || PauseMenu.IsOpen || TitleScreen.IsOpen || Minimap.FullOpen || CraftUI.IsOpen || ShopUI.IsOpen;

    static GridClickMover instance;
    void Awake() => instance = this;

    // 걷던 길을 멈춘다 (몬스터가 나를 알아챘을 때 등). 지금 걷는 한 칸은 마저 걷는다.
    public static void StopWalking() { if (instance != null) instance.ClearGoal(); }

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        if (transform.childCount > 0) { model = transform.GetChild(0); modelHome = model.localPosition; }
        if (targetMarker != null) targetMarker.SetActive(false);
    }

    void Update()
    {
        if (stats != null && stats.Dead) { ClearGoal(); return; }
        if (Blocked) return;

        // 던질 곳을 겨누는 중: 마우스 아래 칸을 표시하고, 왼쪽 클릭이면 던진다
        if (Thrower.Aiming != null) { UpdateAim(); return; }

        // 클릭은 언제든 받아서 목표만 바꿔 둔다. 실제 방향 전환은 지금 걷는 한 칸을 마친 뒤에 한다.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOverUI())
            HandleClick();
        HandleHold();

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
            if (kb.eKey.wasPressedThisFrame) stats.EatFood();
            if (kb.qKey.wasPressedThisFrame) stats.ToggleStrike();
            if (kb.tKey.wasPressedThisFrame)
            {
                if (stats.TorchLit) { ClearGoal(); Thrower.Begin(Items.Torch); }
                else GameLog.Add("들고 있는 횃불이 없다. ([F]로 든다)", GameLog.Info);
            }
        }
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

        // 벽 콜라이더에 막히지 않도록 바닥 높이의 평면과 교차한 지점으로 칸을 정한다.
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
        SetDestination(cell);   // 밟을 수 있는 칸의 사물(뼈·벽 횃불·계단 등)은 도착하면 상호작용
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

    // 누르고 있으면 커서를 따라 계속 걷는다 (목표 칸만 계속 바꿔 준다)
    void HandleHold()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.isPressed) { holdTime = 0f; return; }
        if (mouse.leftButton.wasPressedThisFrame) { holdTime = 0f; lastHoldCell = null; holdFromUI = PointerOverUI(); return; }
        holdTime += Time.deltaTime;
        if (holdFromUI || holdTime < HoldDelay || goalTarget != null) return;
        var cam = Camera.main;
        if (cam == null) return;
        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter)) return;
        var cell = GridTile.WorldToCell(ray.GetPoint(enter));
        if (cell == lastHoldCell) return;   // 커서가 다른 칸으로 옮겨 갔을 때만 길을 다시 찾는다
        lastHoldCell = cell;
        SetDestination(cell);
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

    void Face(Vector3 worldPos)
    {
        var dir = worldPos - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir);
    }

    // 공격할 때 모델만 대상 쪽으로 살짝 내질렀다가 돌아온다
    public void PlayLunge(Vector3 targetPos, float delay = 0f)
    {
        if (model != null) StartCoroutine(Lunge(targetPos, delay));
    }

    IEnumerator Lunge(Vector3 targetPos, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Face(targetPos);
        var dir = targetPos - transform.position; dir.y = 0;
        var push = dir.normalized * 1.2f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            model.position = transform.TransformPoint(modelHome) + push * Mathf.Sin(t * Mathf.PI);
            yield return null;
        }
        model.localPosition = modelHome;
    }

    // 이 칸에 도착하면 걷기를 끝내는가 (걷는 도중 목표가 바뀔 수 있어서 매번 지금 목표로 판단)
    bool IsFinalStep(Vector2Int cell)
    {
        if (goalTarget != null) return IsNextTo(cell, goalTarget.Cell);
        return goalCell == null || goalCell.Value == cell;
    }

    IEnumerator Step(Vector2Int cell)
    {
        stepping = true;
        stepCell = cell;
        TurnResolver.BeginTurn(cell, moveSeconds);   // 몬스터도 같은 시간에 움직인다
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
