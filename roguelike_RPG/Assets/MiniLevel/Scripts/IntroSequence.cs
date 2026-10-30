using System.Collections;
using UnityEngine;

// 1층에서 깨어날 때: 새 회차를 기록하고, 앞서 쓰러진 자들의 뼈를 놓고, "…올라가야 한다."가 떠오른다.
// 이름 입력은 지금은 꺼 둔다 (askName).
public class IntroSequence : MonoBehaviour
{
    public PlayerStats player;
    public GameObject bonesPrefab;
    public bool askName = false;
    public string defaultName = "이름 없는 자";

    void Awake()
    {
        TowerMemory.BeginRun();
        TurnManager.ResetTurn();
        GameLog.Clear();
        SpawnBones();
    }

    void SpawnBones()
    {
        if (bonesPrefab == null) return;
        foreach (var b in TowerMemory.AllBones)
        {
            var cell = new Vector2Int(b.x, b.y);
            var go = Instantiate(bonesPrefab, GridTile.CellToWorld(cell), Quaternion.Euler(0, Random.Range(0, 360f), 0));
            go.name = $"Bones_{b.run}";
            go.GetComponent<BonesEvent>().record = b;
        }
    }

    IEnumerator Start()
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
