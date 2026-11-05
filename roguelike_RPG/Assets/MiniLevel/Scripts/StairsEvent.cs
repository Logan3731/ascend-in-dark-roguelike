using UnityEngine;

// 위로 올라가는 계단. 다가가서 클릭하면 오를지 묻는다. 오르면 다음 층 (RunState.Ascend — 상태를 이어받는다).
// 보스 층은 우두머리를 쓰러뜨려야 오를 수 있다. 10층 꼭대기에서 오르면 탑을 벗어난다 (엔딩).
public class StairsEvent : Interactable
{
    public bool down;   // 내려가는 계단 (지금은 쓰지 않음)

    public override void Interact(PlayerStats player)
    {
        foreach (var boss in FindObjectsByType<Boss>(FindObjectsSortMode.None))
            if (boss.Alive)
            {
                GameLog.Add($"{boss.Title}{Korean.Josa(boss.Title, "이", "가")} 계단을 막고 있다. 쓰러뜨려야 오를 수 있다.", GameLog.Bad);
                return;
            }

        bool top = RunState.Floor >= RunState.TopFloor;
        DialogUI.Show(top ? "탑 꼭대기로 이어지는 마지막 계단이다. 오를까?" : $"위로 올라가는 돌계단이다. {RunState.Floor + 1}층으로 오를까?",
            new DialogUI.Choice("오른다", () =>
            {
                if (!top)
                {
                    GameLog.Add("계단을 올랐다.", GameLog.Good);
                    RunState.Ascend(player);
                    return;
                }
                DialogUI.Show($"마지막 계단 끝에서 희미한 빛이 새어 든다.\n\n탑을 벗어났다! ({TurnManager.Turn}턴 · Lv.{player.Level})\n어둠을 끝까지 올랐다.",
                    new DialogUI.Choice("처음 화면으로", TitleScreen.ReturnToTitle));
            }),
            new DialogUI.Choice("아직은", null));
    }
}
