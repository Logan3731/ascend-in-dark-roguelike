using System.Collections;
using UnityEngine;

// 던지기: [T]로 든 횃불을, 소지품에서 돌멩이를 골라 겨눈 뒤 칸을 누르면 던진다 (한 턴).
// 5칸까지, 벽·닫힌 문을 넘지 못하고, 날아가는 길에 적이 있으면 그 적에게 맞는다.
// - 횃불: 맞으면 불 피해 3~5. 떨어진 자리에서 남은 내구도만큼 타며 빛 구역이 된다 (그 안의 적은 약해진다). 밟으면 다시 줍는다.
// - 돌멩이: 피해 2~4. 떨어진 자리에 남아서 다시 주울 수 있다.
// - 물약: 깨진 자리와 주변 한 칸의 적에게 효과가 퍼진다 (Potions.Shatter). 던지면 정체가 드러난다.
// - 다트([R] 또는 소지품): 맞으면 피해 1~3 후 부러져 사라진다. 빈 바닥에 떨어지면 그 자리에 남아 다시 주울 수 있다.
public static class Thrower
{
    public const int Range = 5;
    public const int DartMin = 1, DartMax = 3;   // 다트 피해

    public static ItemDef Aiming { get; private set; }
    public static int EscFrame { get; private set; } = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => Aiming = null;

    public static void Begin(ItemDef what)
    {
        Aiming = what;
        GameLog.Add($"{what.name}{Korean.Josa(what.name, "을", "를")} 어디로 던질까?", GameLog.Warn);
    }

    public static void Cancel(bool byEsc = false)
    {
        if (Aiming == null) return;
        Aiming = null;
        if (byEsc) EscFrame = Time.frameCount;
        GameLog.Add("던지기를 그만두었다.", GameLog.Info);
    }

    // from에서 to로 던졌을 때 떨어지는 칸과 맞는 적. 너무 멀거나 벽에 막히면 false.
    public static bool Trace(Vector2Int from, Vector2Int to, out Vector2Int land, out Monster hit)
    {
        land = from;
        hit = null;
        if (to == from || GridNav.Distance(from, to) > Range) return false;
        var floors = GridNav.Floors();
        int dx = Mathf.Abs(to.x - from.x), dy = Mathf.Abs(to.y - from.y);
        int sx = from.x < to.x ? 1 : -1, sy = from.y < to.y ? 1 : -1;
        int err = dx - dy;
        var c = from;
        while (c != to)
        {
            int e2 = err * 2;
            if (e2 > -dy) { err -= dy; c.x += sx; }
            if (e2 < dx) { err += dx; c.y += sy; }
            var door = Door.At(c);
            if (!floors.Contains(c) || (door != null && !door.IsOpen)) return false;
            foreach (var m in Object.FindObjectsByType<Monster>(FindObjectsSortMode.None))
                if (m.enabled && !m.Dead && m.Cell == c) { hit = m; land = c; return true; }
            land = c;
        }
        return true;
    }

    public static void Throw(PlayerStats p, GridClickMover mover, Vector2Int target)
    {
        var what = Aiming;
        if (what == null) return;
        var from = GridTile.WorldToCell(p.transform.position);
        if (!Trace(from, target, out var land, out var hit))
        {
            GameLog.Add("거기까지는 던질 수 없다.", GameLog.Info);
            return;
        }
        Aiming = null;
        mover.FaceTo(GridTile.CellToWorld(land));
        const float Flight = 0.3f;

        bool torch = what == Items.Torch;
        int left = 0;
        if (torch) left = p.ReleaseTorch();
        else p.Bag.Remove(what);
        GameLog.Add($"{what.name}{Korean.Josa(what.name, "을", "를")} 던졌다.", GameLog.Info);
        Sfx.Play("throw", 0.15f);
        GridClickMover.PlayAction(what == Items.Dart && GridClickMover.HasAnim("ThrowDart") ? "ThrowDart" : "Throw", GridClickMover.ThrowSeconds);

        // 맞힌 적에게 먼저 피해 (쓰러지면 이번 턴에 덤비지 못한다)
        // 물약은 맞힌 적에게 피해를 주지 않고, 깨진 자리 주변에 효과가 퍼진다 (이번 턴 적의 행동보다 먼저)
        bool potion = what.kind == ItemKind.Potion;
        bool dart = what == Items.Dart;
        if (potion) Potions.Shatter(p, what, land, Flight);
        else if (hit != null)
        {
            int dmg = torch ? Random.Range(3, 6) : dart ? Random.Range(DartMin, DartMax + 1) : Random.Range(2, 5);
            hit.HitByThrow(p, dmg, torch ? "횃불" : dart ? "다트" : "돌멩이", Flight, torch);
        }

        mover.StartCoroutine(Fly(p.transform.position, GridTile.CellToWorld(land), what, Flight, () =>
        {
            if (torch) { ThrownTorch.Create(land, Mathf.Max(1, left)); Fire.Ignite(land, Fire.TorchRadius, p); }
            else if (dart)
            {
                if (hit != null) return;                       // 적에게 맞은 다트는 부러져 사라진다
                if (OnLandedOnTrap(land, what)) return;        // 함정을 작동시킨 다트는 사라진다
                DropItem(land, "dart");                        // 빈 바닥: 그 자리에 남는다
            }
            else if (!potion)
            {
                if (hit == null) OnLandedOnTrap(land, what);   // 돌멩이도 함정을 작동시킨다 (돌은 그 자리에 남는다)
                DropItem(land, "stone");
            }
        }));

        // 던지는 데 한 턴: 그사이 다른 적도 움직인다
        TurnResolver.BeginTurn(from, mover.moveSeconds);
        TurnResolver.ResolveAttacks(p, null);
        TurnResolver.BusyUntil = Mathf.Max(TurnResolver.BusyUntil, Time.time + Mathf.Max(Flight + 0.1f, GridClickMover.TurnSeconds));   // 던지는 데 한 턴 = 한 칸 걷는 시간
    }

    // 함정 칸에 떨어진 던진 물건 — 아직 작동하지 않은 함정(숨은 것 포함)이 있으면 작동시키고 true.
    static bool OnLandedOnTrap(Vector2Int cell, ItemDef what)
    {
        var trap = Trap.At(cell);
        return trap != null && trap.TriggerRemote(what);
    }

    // 떨어진 자리에 다시 주울 수 있는 물건을 남긴다 (돌멩이·다트)
    static void DropItem(Vector2Int cell, string itemId)
    {
        var gen = FloorGenerator.Instance;
        bool stone = itemId == "stone";
        GameObject go;
        if (stone && gen != null && gen.stonePrefab != null)
            go = Object.Instantiate(gen.stonePrefab, GridTile.CellToWorld(cell), Quaternion.Euler(0, Random.Range(0, 360f), 0));
        else if (stone)
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = GridTile.CellToWorld(cell) + Vector3.up * 0.3f;
            go.transform.localScale = Vector3.one * 0.6f;
        }
        else if ((go = MakeDartModel(cell)) != null)
        {
            // 다트 모델은 콜라이더가 없다 → 줍기 판정(Interactable)이 모델 크기로 상자를 만들지 않도록 작은 상자를 단다
            go.AddComponent<BoxCollider>().size = Vector3.one * 0.5f;
        }
        else
        {
            // 다트 모델을 못 불러오면: 바닥에 누운 가는 쇠막대
            go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Dart";
            go.transform.position = GridTile.CellToWorld(cell) + Vector3.up * 0.08f;
            go.transform.rotation = Quaternion.Euler(90f, Random.Range(0, 360f), 0);
            go.transform.localScale = new Vector3(0.08f, 0.35f, 0.08f);
            go.GetComponent<Renderer>().material.color = new Color(0.55f, 0.55f, 0.6f);
        }
        var pk = go.GetComponent<Pickup>() ?? go.AddComponent<Pickup>();
        pk.itemId = itemId;
        pk.triggerOnEnter = true;
    }

    // 바닥에 떨어진 다트 모델 (Resources/Models/Dart). 크기는 불러온 모델의 실제 크기와 상관없이 DartLength로 맞춘다.
    const float DartLength = 1.3f;   // 한 칸 = 4
    static GameObject dartModel;
    static Material dartMat;

    static GameObject MakeDartModel(Vector2Int cell)
    {
        if (dartModel == null) dartModel = Resources.Load<GameObject>("Models/Dart/Dart");
        if (dartModel == null) return null;
        if (dartMat == null) dartMat = Resources.Load<Material>("Models/Dart/M_Dart");

        var root = new GameObject("Dart");
        root.transform.position = GridTile.CellToWorld(cell);
        var model = Object.Instantiate(dartModel, root.transform);
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Object.Destroy(root); return null; }
        if (dartMat != null) foreach (var r in renderers) r.sharedMaterial = dartMat;

        // 가장 긴 변이 DartLength가 되게 줄이고, 바닥 위에 눕힌다 (방향은 무작위)
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest > 0.0001f) model.transform.localScale *= DartLength / longest;
        // 세워진 채 들어온 모델이면 눕힌다 (가장 긴 축이 위아래)
        if (b.size.y >= longest - 0.0001f) model.transform.rotation = Quaternion.Euler(90f, 0, 0) * model.transform.rotation;
        b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        model.transform.position += new Vector3(root.transform.position.x - b.center.x, 0.02f - b.min.y, root.transform.position.z - b.center.z);
        root.transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
        return root;
    }

    // 포물선으로 날아가는 작은 물체 (연출용)
    static IEnumerator Fly(Vector3 from, Vector3 to, ItemDef what, float seconds, System.Action onLand)
    {
        bool torch = what == Items.Torch, potion = what.kind == ItemKind.Potion, dart = what == Items.Dart;
        var go = GameObject.CreatePrimitive(torch || dart ? PrimitiveType.Cylinder : potion ? PrimitiveType.Capsule : PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.localScale = torch ? new Vector3(0.15f, 0.5f, 0.15f) : dart ? new Vector3(0.07f, 0.3f, 0.07f) : potion ? new Vector3(0.3f, 0.25f, 0.3f) : Vector3.one * 0.45f;
        var mat = go.GetComponent<Renderer>().material;
        mat.color = torch ? new Color(1f, 0.55f, 0.15f) : dart ? new Color(0.6f, 0.6f, 0.65f) : potion ? Potions.BottleColor(what) : new Color(0.45f, 0.44f, 0.42f);
        if (torch) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.1f) * 3f); }
        if (potion) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", Potions.BottleColor(what) * 1.5f); }
        from += Vector3.up * 2.2f;
        to += Vector3.up * 0.3f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / seconds)
        {
            go.transform.position = Vector3.Lerp(from, to, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 2.5f;
            go.transform.Rotate(0, 0, 900f * Time.deltaTime);
            yield return null;
        }
        Object.Destroy(go);
        onLand?.Invoke();
    }
}
