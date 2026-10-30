using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 바닥 칸을 클릭하면 그 칸을 목표로 정하고 한 칸씩 이동한다. 한 칸 = 한 턴.
// 한 칸 갈 때마다 목표까지의 길을 다시 찾는다 (그새 몬스터가 움직였을 수 있으므로).
// 상호작용 대상을 클릭하면 그 옆까지 걸어가서 상호작용한다. [F] 횃불 들기/내리기, [E] 식량 먹기.
// 걷는 중에 다른 곳을 누르면 목표만 바뀌고, 지금 걷는 한 칸을 마친 뒤 방향을 튼다.
// 공격·연출이 끝나기 전에는 다음 행동을 하지 않는다.
public class GridClickMover : MonoBehaviour
{
    public float moveSeconds = 0.15f;
    public GameObject targetMarker;
    public Animator animator;   // "Moving" bool: 걷는 동안 true

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
    static readonly int MovingParam = Animator.StringToHash("Moving");

    PlayerStats stats;
    bool stepping;               // 한 칸 이동 중
    Vector2Int stepCell;         // 지금 걷고 있는 한 칸의 도착 칸
    Vector2Int? goalCell;        // 걸어가는 목표 칸
    Interactable goalTarget;     // 다가가서 상호작용할 대상
    Transform model;             // 공격 연출용 모델 (첫 번째 자식)
    Vector3 modelHome;

    // 이동·공격·연출이 다 끝나야 다음 행동을 받는다
    bool Busy => stepping || Time.time < TurnResolver.BusyUntil;
    bool HasGoal => goalCell != null || goalTarget != null;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        if (transform.childCount > 0) { model = transform.GetChild(0); modelHome = model.localPosition; }
        if (targetMarker != null) targetMarker.SetActive(false);
    }

    void Update()
    {
        if (stats != null && stats.Dead) { ClearGoal(); return; }
        if (DialogUI.IsOpen || InventoryUI.IsOpen || PauseMenu.IsOpen || TitleScreen.IsOpen) return;

        // 클릭은 언제든 받아서 목표만 바꿔 둔다. 실제 방향 전환은 지금 걷는 한 칸을 마친 뒤에 한다.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOverUI())
            HandleClick();

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
        if (ground.Raycast(ray, out float enter)) SetDestination(GridTile.WorldToCell(ray.GetPoint(enter)));
    }

    public void SetDestination(Vector2Int goal)
    {
        var here = CurrentCell;
        if (goal == here)
        {
            // 걷는 중에 내 자리를 누르면 이번 칸에서 멈춘다
            if (HasGoal) { goalTarget = null; goalCell = here; ShowMarker(here); }
            return;
        }
        if (FindPath(here, c => c == goal) == null) return;
        goalTarget = null;
        goalCell = goal;
        ShowMarker(goal);
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
            StartCoroutine(Step(route[0]));
            return;
        }

        if (goalCell != null)
        {
            var goal = goalCell.Value;
            if (here == goal) { ClearGoal(); return; }
            var route = FindPath(here, c => c == goal);
            if (route == null || route.Count == 0) { ClearGoal(); return; }
            StartCoroutine(Step(route[0]));
        }
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

    static bool IsNextTo(Vector2Int a, Vector2Int b)
    {
        var d = a - b;
        return Mathf.Abs(d.x) + Mathf.Abs(d.y) <= 1;
    }

    // 4방향 BFS. 시작 칸은 제외한 경로를 돌려준다. 갈 수 없으면 null.
    static List<Vector2Int> FindPath(Vector2Int start, System.Func<Vector2Int, bool> isGoal)
    {
        var walkable = new HashSet<Vector2Int>();
        foreach (var t in FindObjectsByType<GridTile>(FindObjectsSortMode.None)) walkable.Add(t.Cell);
        foreach (var b in FindObjectsByType<GridBlocker>(FindObjectsSortMode.None))
            if (b.enabled) walkable.Remove(b.Cell);

        var prev = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
        var open = new Queue<Vector2Int>();
        open.Enqueue(start);
        Vector2Int? found = null;
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (isGoal(cur) && walkable.Contains(cur)) { found = cur; break; }
            foreach (var d in Dirs)
            {
                var next = cur + d;
                if (!walkable.Contains(next) || prev.ContainsKey(next)) continue;
                prev[next] = cur;
                open.Enqueue(next);
            }
        }
        if (found == null) return null;

        var route = new List<Vector2Int>();
        for (var c = found.Value; c != start; c = prev[c]) route.Add(c);
        route.Reverse();
        return route;
    }

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
        Face(to);

        for (float t = 0f; t < 1f; t += Time.deltaTime / moveSeconds)
        {
            transform.position = Vector3.Lerp(from, to, t);
            // 마지막 칸이면 도착 직전에 미리 멈춤 동작으로 넘긴다 (도착 후 걸음이 늦게 끝나지 않도록)
            if (t > 0.8f && animator != null && IsFinalStep(cell)) animator.SetBool(MovingParam, false);
            yield return null;
        }
        transform.position = to;
        TurnResolver.ResolveAttacks(stats, null);   // 이동한 턴: 붙어 있던 몬스터만 공격

        // 밟으면 발동하는 칸
        foreach (var trig in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
            if (trig.triggerOnEnter && trig.Cell == cell) trig.Interact(stats);

        // 바닥으로 걸어가던 중 공격을 받았으면 멈춰서 상황을 보게 한다 (대상에게 다가가던 중이면 계속)
        if (TurnResolver.AttackedThisTurn && goalTarget == null) ClearGoal();
        if (IsFinalStep(cell) || !HasGoal)
        {
            if (animator != null) animator.SetBool(MovingParam, false);
            if (goalCell != null && GridTile.WorldToCell(transform.position) == goalCell) ClearGoal();
        }
        stepping = false;
    }
}
