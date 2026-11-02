using UnityEngine;

// 위로 올라가는 계단. 다가가서 클릭하면 오를지 묻는다. (2층은 아직 없어서, 오르면 1층 클리어로 처음 화면에 돌아간다)
public class StairsEvent : Interactable
{
    public override void Interact(PlayerStats player)
    {
        DialogUI.Show("위로 올라가는 돌계단이다. 오를까?",
            new DialogUI.Choice("오른다", () =>
            {
                GameLog.Add("계단을 올랐다.", GameLog.Good);
                DialogUI.Show($"계단 끝은 아직 짙은 어둠에 잠겨 있다.\n\n1층을 벗어났다! ({TurnManager.Turn}턴)\n(2층은 아직 만들어지지 않았다)",
                    new DialogUI.Choice("처음 화면으로", TitleScreen.ReturnToTitle));
            }),
            new DialogUI.Choice("아직은", null));
    }
}
