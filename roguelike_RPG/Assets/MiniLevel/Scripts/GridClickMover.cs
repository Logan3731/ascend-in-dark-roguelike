using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 바닥 칸을 클릭하면 그 칸에 목적지 표시를 띄우고, 최단 경로를 따라 한 칸씩 이동한다. 한 칸 = 한 턴.
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

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            HandleClick();

        if (!moving && path.Count > 0)
            StartCoroutine(Step(path.Dequeue()));
    }

    void HandleClick()
    {
        var cam = Camera.main;
        if (cam == null) return;

        // 벽 콜라이더에 막히지 않도록 바닥 높이의 평면과 교차한 지점으로 칸을 정한다.
        var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        var ground = new Plane(Vector3.up, Vector3.zero);
        if (ground.Raycast(ray, out float enter))
            SetDestination(GridTile.WorldToCell(ray.GetPoint(enter)));
    }

    public void SetDestination(Vector2Int goal)
    {
        var start = GridTile.WorldToCell(transform.position);
        var route = FindPath(start, goal);
        if (route == null) return;

        path.Clear();
        foreach (var cell in route) path.Enqueue(cell);

        if (targetMarker != null)
        {
            targetMarker.transform.position = GridTile.CellToWorld(goal) + Vector3.up * 0.02f;
            targetMarker.SetActive(route.Count > 0);
        }
    }

    // 4방향 BFS. 시작 칸은 제외한 경로를 돌려준다. 갈 수 없으면 null.
    static List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
    {
        var walkable = new HashSet<Vector2Int>();
        foreach (var t in FindObjectsByType<GridTile>(FindObjectsSortMode.None)) walkable.Add(t.Cell);
        foreach (var b in FindObjectsByType<GridBlocker>(FindObjectsSortMode.None)) walkable.Remove(b.Cell);
        if (!walkable.Contains(goal)) return null;

        var prev = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
        var open = new Queue<Vector2Int>();
        open.Enqueue(start);
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (cur == goal) break;
            foreach (var d in Dirs)
            {
                var next = cur + d;
                if (!walkable.Contains(next) || prev.ContainsKey(next)) continue;
                prev[next] = cur;
                open.Enqueue(next);
            }
        }
        if (!prev.ContainsKey(goal)) return null;

        var route = new List<Vector2Int>();
        for (var c = goal; c != start; c = prev[c]) route.Add(c);
        route.Reverse();
        return route;
    }

    IEnumerator Step(Vector2Int cell)
    {
        moving = true;
        if (animator != null) animator.SetBool(MovingParam, true);
        var from = transform.position;
        var to = GridTile.CellToWorld(cell);
        to.y = from.y;

        var dir = to - from;
        if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir);

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

        if (path.Count == 0)
        {
            if (targetMarker != null) targetMarker.SetActive(false);
            if (animator != null) animator.SetBool(MovingParam, false);
        }
    }
}
