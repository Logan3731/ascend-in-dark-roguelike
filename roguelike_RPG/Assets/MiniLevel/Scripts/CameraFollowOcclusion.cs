using System.Collections.Generic;
using UnityEngine;

// 쿼터뷰 카메라가 플레이어를 따라가고, 카메라와 플레이어 사이를 가리는 벽은 반투명으로 바꾼다.
public class CameraFollowOcclusion : MonoBehaviour
{
    public Transform target;
    public float distance = 40f;
    public float followSpeed = 8f;
    public Material fadeMaterial;
    public float clearRadius = 1.2f;

    readonly Dictionary<Renderer, Material> faded = new Dictionary<Renderer, Material>();
    readonly HashSet<Renderer> hitThisFrame = new HashSet<Renderer>();

    void LateUpdate()
    {
        if (target == null) return;

        var focus = target.position + Vector3.up * 1f;
        var wanted = focus - transform.forward * distance;
        transform.position = Vector3.Lerp(transform.position, wanted, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        UpdateOcclusion(focus);
    }

    void UpdateOcclusion(Vector3 focus)
    {
        hitThisFrame.Clear();
        var toTarget = focus - transform.position;
        var hits = Physics.SphereCastAll(transform.position, clearRadius, toTarget.normalized, toTarget.magnitude - 0.5f);

        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<OccludingWall>() == null) continue;
            var r = hit.collider.GetComponent<Renderer>();
            if (r == null) continue;
            hitThisFrame.Add(r);
            if (!faded.ContainsKey(r))
            {
                faded[r] = r.sharedMaterial;
                r.sharedMaterial = fadeMaterial;
            }
        }

        var restore = new List<Renderer>();
        foreach (var pair in faded)
            if (!hitThisFrame.Contains(pair.Key)) restore.Add(pair.Key);
        foreach (var r in restore)
        {
            if (r != null) r.sharedMaterial = faded[r];
            faded.Remove(r);
        }
    }
}
