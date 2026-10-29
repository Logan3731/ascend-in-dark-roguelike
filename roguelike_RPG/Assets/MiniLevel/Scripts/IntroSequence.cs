using System.Collections;
using UnityEngine;

// 1층 시작: (이름을 떠올리고) 신들이 심어 둔 "…올라가야 한다."가 떠오른다.
// 이름 입력은 지금은 꺼 둔다 (askName).
public class IntroSequence : MonoBehaviour
{
    public PlayerStats player;
    public bool askName = false;
    public string defaultName = "이름 없는 자";

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
        GameLog.Add("…올라가야 한다.", GameLog.Warn);
    }
}
