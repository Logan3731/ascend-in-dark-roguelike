using System.Collections.Generic;
using UnityEngine;

// VARCO로 만든 모델(Resources/Models/<이름>/<이름>.fbx + M_<이름>.mat)을 실행 중에 불러와 칸에 맞게 놓는다.
// 프리팹을 만들려면 에디터가 필요해서, 이렇게 불러오는 쪽을 쓴다. 모델이 없으면 null (부르는 쪽이 기본 도형으로 대신한다).
public static class ResourceModel
{
    static readonly Dictionary<string, (GameObject model, Material mat)> cache = new Dictionary<string, (GameObject, Material)>();

    // footprint: 바닥에서 본 가로·세로 중 긴 쪽이 이 길이가 되게 줄인다 (높이는 비율대로). 바닥에 붙여 가운데에 놓는다.
    public static GameObject Spawn(string name, Transform parent, float footprint)
    {
        if (!cache.TryGetValue(name, out var e))
        {
            e = (Resources.Load<GameObject>($"Models/{name}/{name}"), Resources.Load<Material>($"Models/{name}/M_{name}"));
            cache[name] = e;
        }
        if (e.model == null) return null;

        var go = Object.Instantiate(e.model, parent, false);
        go.name = name;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Object.Destroy(go); return null; }
        if (e.mat != null) foreach (var r in renderers) r.sharedMaterial = e.mat;

        var b = Bounds(renderers);
        float longest = Mathf.Max(b.size.x, b.size.z);
        if (longest > 0.0001f) go.transform.localScale *= footprint / longest;
        b = Bounds(renderers);
        var at = parent != null ? parent.position : Vector3.zero;
        go.transform.position += new Vector3(at.x - b.center.x, at.y + 0.02f - b.min.y, at.z - b.center.z);
        return go;
    }

    // 동작이 든 캐릭터 모델 (블렌더에서 부품을 뼈대에 조립한 것). 키가 height가 되게 줄이고 발을 바닥에 둔다.
    // 모델은 +Z를 보고 있어야 한다 (몬스터가 바라보는 방향). FBX는 레거시 Animation으로 가져온다.
    // 동작이 모델 최상위의 위치·회전까지 덮어쓰므로, 크기·위치·방향은 한 겹 감싼 상자(holder)에 준다.
    // yaw: 블렌더에서 -Y를 보고 만든 모델은 Unity에서 -Z를 본다 → 180°로 돌려 +Z(몬스터 앞)를 보게.
    public static GameObject SpawnCharacter(string name, Transform parent, float height, float yaw = 180f)
    {
        var model = Resources.Load<GameObject>($"Models/{name}/{name}");
        if (model == null) return null;
        var holder = new GameObject(name).transform;
        holder.SetParent(parent, false);
        holder.localRotation = Quaternion.Euler(0, yaw, 0);
        var go = Object.Instantiate(model, holder, false);
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Object.Destroy(holder.gameObject); return null; }
        // 가져온 그대로는 누운 자세(축 변환 전)라, 대기 동작 첫 자세로 맞춘 뒤 잰다
        var anim = go.GetComponentInChildren<Animation>();
        if (anim != null)
            foreach (AnimationState s in anim)
                if (s.name.EndsWith("|Idle") || s.name == "Idle") { s.clip.SampleAnimation(go, 0f); break; }
        var b = Bounds(renderers);
        if (b.size.y > 0.0001f) holder.localScale *= height / b.size.y;
        b = Bounds(renderers);
        var at = parent != null ? parent.position : Vector3.zero;
        holder.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        return holder.gameObject;
    }

    // 메시의 경계를 월드 좌표로 (층을 만드는 동안처럼 꺼져 있는 오브젝트는 Renderer.bounds가 비어 있어서)
    static Bounds Bounds(Renderer[] rs)
    {
        bool any = false;
        var b = new Bounds();
        foreach (var r in rs)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var m = r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(corner);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                else b.Encapsulate(p);
            }
        }
        return b;
    }
}
