using UnityEngine;

// 쿼터뷰 카메라가 플레이어를 따라간다.
// (예전엔 가리는 벽을 반투명하게 했지만, 이제 카메라 쪽 벽은 아예 세우지 않으므로 가릴 것이 없다 — FloorGenerator.IsFrontWall)
public class CameraFollowOcclusion : MonoBehaviour
{
    public Transform target;
    public float distance = 40f;
    public float followSpeed = 8f;

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
        var focus = target.position + Vector3.up * 1f;
        var wanted = focus - transform.forward * distance;
        if (!started) { followPos = wanted; started = true; }   // 첫 프레임엔 바로 플레이어 위로 (시작 위치가 매번 달라서)
        followPos = Vector3.Lerp(followPos, wanted, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));

        var offset = Vector3.zero;
        if (Time.time < shakeUntil)
        {
            float k = (shakeUntil - Time.time) / shakeLength;   // 점점 잦아든다
            offset = (transform.right * (Mathf.PerlinNoise(Time.time * 40f, 0f) - 0.5f) +
                      transform.up * (Mathf.PerlinNoise(0f, Time.time * 40f) - 0.5f)) * 2f * shakeAmount * k;
        }
        transform.position = followPos + offset;
    }
}
