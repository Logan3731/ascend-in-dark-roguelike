using UnityEngine;

// 이 오브젝트가 놓인 칸으로는 이동할 수 없다 (상자, 고블린 등).
public class GridBlocker : MonoBehaviour
{
    public Vector2Int Cell => GridTile.WorldToCell(transform.position);
}
