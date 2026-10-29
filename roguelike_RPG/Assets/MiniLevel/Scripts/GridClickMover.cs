using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 바닥 칸을 클릭하면 그 칸에 목적지 표시를 띄우고, 최단 경로를 따라 한 칸씩 이동한다. 한 칸 = 한 턴.
// 상호작용 대상을 클릭하면 그 옆까지 걸어가서 상호작용한다. [F] 횃불 들기/내리기, [E] 식량 먹기.
public class GridClickMover : MonoBehaviour
{
    public float moveSeconds = 0.15f;
    public GameObject targetMarker;
    public Animator animator;   // "Moving" bool: 걷는 동안 true

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
    static readonly int MovingParam = Animator.StringToHash("Moving");

    readonly Queue<Vector2Int> path = new Queue<Vector2Int>();
    bool moving;
    PlayerStats stats;
    Interactable pendingTarget;   // 도착하면 상호작용할 대상

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        if (targetMarker != null) targetMarker.SetActive(false);
    }

    void Update()
    {
        if (stats != null && stats.Dead)
        {
            path.Clear();
            return;
        }
        if (DialogUI.IsOpen) return;

        var kb = Keyboard.current;
        if (kb != null && stats != null)
        {
            if (kb.fKey.wasPressedThisFrame) stats.ToggleTorch();
            if (kb.eKey.wasPressedThisFrame) stats.EatFood();
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOverUI())
            HandleClick();

        if (!moving && path.Count > 0)
            StartCoroutine(Step(path.Dequeue()));
    }

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
                ApproachAndInteract(target);
                return;
            }
        }

        // 벽 콜라이더에 막히지 않도록 바닥 높이의 평면과 교차한 지점으로 칸을 정한다.
        var ground = new Plane(Vector3.up, Vector3.zero);
        if (ground.Raycast(ray, out float enter))
        {
            pendingTarget = null;
            SetDestination(GridTile.WorldToCell(ray.GetPoint(enter)));
        }
    }

    void ApproachAndInteract(Interactable target)
    {
        var here = GridTile.WorldToCell(transform.position);
        if (!moving && IsNextTo(here, target.Cell))
        {
            pendingTarget = null;
            path.Clear();
            Face(target.transform.position);
            target.Interact(stats);
            return;
        }

        var route = FindPath(here, c => IsNextTo(c, target.Cell));
        if (route == null) return;
        pendingTarget = target;
        SetRoute(route);
    }

    static bool IsNextTo(Vector2Int a, Vector2Int b)
    {
        var d = a - b;
        return Mathf.Abs(d.x) + Mathf.Abs(d.y) <= 1;
    }

    public void SetDestination(Vector2Int goal)
    {
        var route = FindPath(GridTile.WorldToCell(transform.position), c => c == goal);
        if (route == null) return;
        SetRoute(route);
    }

    void SetRoute(List<Vector2Int> route)
    {
        path.Clear();
        foreach (var cell in route) path.Enqueue(cell);

        if (targetMarker != null)
        {
            if (route.Count > 0) targetMarker.transform.position = GridTile.CellToWorld(route[route.Count - 1]) + Vector3.up * 0.02f;
            targetMarker.SetActive(route.Count > 0);
        }
    }

    // 4방향 BFS. 시작 칸은 제외한 경로를 돌려준다. 갈 수 없으면 null.
    static List<Vector2Int> FindPath(Vector2Int start, System.Func<Vector2Int, bool> isGoal)
    {
        var walkable = new HashSet<Vector2Int>();
        foreach (var t in FindObjectsByType<GridTile>(FindObjectsSortMode.None)) walkable.Add(t.Cell);
        foreach (var b in FindObjectsByType<GridBlocker>(FindObjectsSortMode.None)) walkable.Remove(b.Cell);

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

    IEnumerator Step(Vector2Int cell)
    {
        moving = true;
        if (animator != null) animator.SetBool(MovingParam, true);
        var from = transform.position;
        var to = GridTile.CellToWorld(cell);
        to.y = from.y;
        Face(to);

        bool lastStep = path.Count == 0;
        for (float t = 0f; t < 1f; t += Time.deltaTime / moveSeconds)
        {
            transform.position = Vector3.Lerp(from, to, t);
            // 마지막 칸이면 도착 직전에 미리 멈춤 동작으로 넘긴다 (도착 후 걸음이 늦게 끝나지 않도록)
            if (lastStep && t > 0.8f && animator != null) animator.SetBool(MovingParam, false);
            yield return null;
        }
        transform.position = to;
        moving = false;
        TurnManager.EndTurn();

        // 밟으면 발동하는 칸
        foreach (var trig in FindObjectsByType<Interactable>(FindObjectsSortMode.None))
            if (trig.triggerOnEnter && trig.Cell == cell) trig.Interact(stats);

        if (path.Count == 0)
        {
            if (targetMarker != null) targetMarker.SetActive(false);
            if (animator != null) animator.SetBool(MovingParam, false);
            if (pendingTarget != null)
            {
                var target = pendingTarget;
                pendingTarget = null;
                if (target != null && IsNextTo(cell, target.Cell))
                {
                    Face(target.transform.position);
                    target.Interact(stats);
                }
            }
        }
    }
}
