using System.Collections;
using UnityEngine;

// 1층에서 깨어날 때: 새 회차를 기록하고, 앞서 쓰러진 자들의 뼈를 놓고, "…올라가야 한다."가 떠오른다.
// 타이틀 화면이 떠 있으면 [깨어나기]를 누를 때까지 기다린다. 이름 입력은 지금은 꺼 둔다 (askName).
public class IntroSequence : MonoBehaviour
{
    public PlayerStats player;
    public GameObject bonesPrefab;
    public bool askName = false;
    public string defaultName = "이름 없는 자";

    bool started;

    void Awake()
    {
        if (!TitleScreen.ShouldShow) StartRun();
    }

    public void StartRun()
    {
        if (started) return;
        started = true;
        TowerMemory.BeginRun();
        TurnManager.ResetTurn();
        GameLog.Clear();
        SpawnBones();
        if (player != null) player.AddFood(0);   // HUD에 회차 표시 갱신
        StartCoroutine(Intro());
    }

    void SpawnBones()
    {
        if (bonesPrefab == null) return;
        foreach (var b in TowerMemory.AllBones)
        {
            var cell = new Vector2Int(b.x, b.y);
            if (FloorGenerator.Instance != null) cell = FloorGenerator.Instance.PlaceBones(cell, b.run);   // 미로가 매번 바뀌므로 자리를 맞춘다
            var go = Instantiate(bonesPrefab, GridTile.CellToWorld(cell), Quaternion.Euler(0, Random.Range(0, 360f), 0));
            go.name = $"Bones_{b.run}";
            go.GetComponent<BonesEvent>().record = b;
        }
    }

    IEnumerator Intro()
    {
        yield return new WaitForSeconds(0.5f);
        if (!askName)
        {
            StartCoroutine(Remember());
            yield break;
        }
        DialogUI.AskText("이름이 뭐였지..?", "이름을 입력", name =>
        {
            player.PlayerName = string.IsNullOrEmpty(name) ? defaultName : name;
            player.AddFood(0);   // HUD 갱신
            StartCoroutine(Remember());
        });
    }

    IEnumerator Remember()
    {
        yield return new WaitForSeconds(0.6f);
        if (TowerMemory.RunCount <= 1)
            GameLog.Add("…올라가야 한다.", GameLog.Warn);
        else
        {
            GameLog.Add("…다시, 깨어났다.", GameLog.Info);
            yield return new WaitForSeconds(0.8f);
            GameLog.Add("…올라가야 한다.", GameLog.Warn);
        }
    }
}
