using UnityEngine;

// 이동 가능한 바닥 한 칸. 플레이어는 이 칸 단위로 움직인다.
public class GridTile : MonoBehaviour
{
    public const float CellSize = 4f;

    public Vector2Int Cell => WorldToCell(transform.position);

    public static Vector2Int WorldToCell(Vector3 world)
    {
        return new Vector2Int(Mathf.RoundToInt(world.x / CellSize), Mathf.RoundToInt(world.z / CellSize));
    }

    public static Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * CellSize, 0f, cell.y * CellSize);
    }
}
