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

    // 피격 흔들림: 따라가는 위치 위에 잠깐 흔들림을 더한다
    static float shakeAmount, shakeUntil, shakeLength;
    Vector3 followPos;
    bool started;

    public static void Shake(float amount, float seconds)
    {
        shakeAmount = Mathf.Max(amount, Time.time < shakeUntil ? shakeAmount : 0f);
        shakeLength = seconds;
        shakeUntil = Time.time + seconds;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => shakeUntil = 0f;

    void LateUpdate()
    {
        if (target == null) return;
        if (!started) { followPos = transform.position; started = true; }

        var focus = target.position + Vector3.up * 1f;
        var wanted = focus - transform.forward * distance;
        followPos = Vector3.Lerp(followPos, wanted, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        var offset = Vector3.zero;
        if (Time.time < shakeUntil)
        {
            float k = (shakeUntil - Time.time) / shakeLength;   // 점점 잦아든다
            offset = (transform.right * (Mathf.PerlinNoise(Time.time * 40f, 0f) - 0.5f) +
                      transform.up * (Mathf.PerlinNoise(0f, Time.time * 40f) - 0.5f)) * 2f * shakeAmount * k;
        }
        transform.position = followPos + offset;

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
