using UnityEngine;

// 클릭하면 플레이어가 그 옆 칸(또는 그 칸)까지 걸어가서 Interact를 부른다.
// triggerOnEnter면 클릭 없이 그 칸을 밟는 순간 발동한다.
public abstract class Interactable : MonoBehaviour
{
    public bool triggerOnEnter;
    public bool Used { get; protected set; }

    public Vector2Int Cell => GridTile.WorldToCell(transform.position);

    protected virtual void Awake()
    {
        if (triggerOnEnter || GetComponentInChildren<Collider>() != null) return;

        // 클릭 판정용 콜라이더: 모델 전체를 감싼다
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(b.center);
        var s = transform.lossyScale;
        box.size = new Vector3(b.size.x / Mathf.Abs(s.x), b.size.y / Mathf.Abs(s.y), b.size.z / Mathf.Abs(s.z));
    }

    public abstract void Interact(PlayerStats player);
}
