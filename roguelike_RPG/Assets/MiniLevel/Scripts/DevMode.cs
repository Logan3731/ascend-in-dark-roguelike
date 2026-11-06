using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 개발자 모드 [F1]: 층 이동(1~10층), 보스 방 문 앞·준보스 방 문 앞·계단 앞으로 순간이동.
// 에디터와 개발 빌드에서만 켜진다. 층 이동은 지금 상태(체력·소지품…)를 그대로 들고 간다.
public class DevMode : MonoBehaviour
{
    static DevMode instance;
    GameObject panel;
    Text info;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Debug.isDebugBuild || instance != null) return;
        instance = new GameObject("DevMode").AddComponent<DevMode>();
        DontDestroyOnLoad(instance.gameObject);
    }

    void Awake() => Build();

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.f1Key.wasPressedThisFrame)
        {
            panel.SetActive(!panel.activeSelf);
            if (panel.activeSelf) RefreshInfo();
        }
    }

    void RefreshInfo()
    {
        var g = FloorGenerator.Instance;
        info.text = $"지금 {RunState.Floor}층" +
                    (g != null && g.BossDoorCell != null ? " · 엘리트 층" : "") +
                    (g != null && g.SemiBossDoorCell != null ? " · 준보스 층" : "");
    }

    void GoFloor(int floor)
    {
        var p = FindAnyObjectByType<PlayerStats>();
        if (p == null || p.Dead) return;
        if (DialogUI.IsOpen || InventoryUI.IsOpen) return;
        GameLog.Add($"[개발] {floor}층으로 이동", GameLog.Warn);
        RunState.DevGoTo(p, floor);
    }

    void Teleport(Vector2Int? cell, string what)
    {
        if (cell == null) { GameLog.Add($"[개발] 이 층엔 {what}이 없다.", GameLog.Info); return; }
        if (GridClickMover.DevTeleport(cell.Value)) GameLog.Add($"[개발] {what} 앞으로 이동", GameLog.Warn);
        else GameLog.Add("[개발] 지금은 이동할 수 없다.", GameLog.Info);
    }

    void Build()
    {
        var root = UIKit.Canvas(transform, "DevCanvas", 40);
        var bg = UIKit.Image(root, "DevPanel", new Color(0.05f, 0.08f, 0.1f, 0.92f), true);
        panel = bg.gameObject;
        UIKit.Place(bg.rectTransform, new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(330, 420));
        bg.gameObject.AddComponent<Outline>().effectColor = new Color(0.4f, 0.8f, 1f, 0.6f);
        var r = bg.rectTransform;

        var title = UIKit.Text(r, "개발자 모드  [F1]", 22, TextAnchor.UpperLeft, new Color(0.5f, 0.85f, 1f));
        UIKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(16, -12), new Vector2(300, 30));
        info = UIKit.Text(r, "", 17, TextAnchor.UpperLeft, UIKit.Dim);
        UIKit.Place(info.rectTransform, new Vector2(0, 1), new Vector2(16, -44), new Vector2(300, 24));

        var head = UIKit.Text(r, "층 이동", 18, TextAnchor.UpperLeft, UIKit.Gold);
        UIKit.Place(head.rectTransform, new Vector2(0, 1), new Vector2(16, -78), new Vector2(300, 24));
        for (int i = 0; i < RunState.TopFloor; i++)
        {
            int floor = i + 1;
            var b = UIKit.Button(r, floor.ToString(), 20, () => GoFloor(floor));
            UIKit.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(16 + (i % 5) * 60, -108 - (i / 5) * 54), new Vector2(54, 46));
        }

        var head2 = UIKit.Text(r, "순간이동", 18, TextAnchor.UpperLeft, UIKit.Gold);
        UIKit.Place(head2.rectTransform, new Vector2(0, 1), new Vector2(16, -222), new Vector2(300, 24));
        string[] labels = { "보스 방 문 앞", "준보스 방 문 앞", "계단 앞" };
        System.Action[] acts =
        {
            () => Teleport(FloorGenerator.Instance != null ? FloorGenerator.Instance.BossDoorCell : null, "보스 방"),
            () => Teleport(FloorGenerator.Instance != null ? FloorGenerator.Instance.SemiBossDoorCell : null, "준보스 방"),
            () => Teleport(FloorGenerator.Instance != null ? FloorGenerator.Instance.StairsCell : null, "계단"),
        };
        for (int i = 0; i < labels.Length; i++)
        {
            var b = UIKit.Button(r, labels[i], 20, acts[i]);
            UIKit.Place((RectTransform)b.transform, new Vector2(0, 1), new Vector2(16, -252 - i * 54), new Vector2(296, 46));
        }
        panel.SetActive(false);
    }
}
