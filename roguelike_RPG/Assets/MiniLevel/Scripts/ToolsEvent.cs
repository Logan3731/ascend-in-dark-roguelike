using UnityEngine;

// [이벤트 1] 입구의 도구: 첫 선/악 시험. 놔두면 선, 가져가면 악.
public class ToolsEvent : Interactable
{
    public override void Interact(PlayerStats player)
    {
        if (Used) return;
        DialogUI.Show("입구에 곡괭이와 도끼가 있다. 가져갈까?",
            new DialogUI.Choice("누군가의 것일 거야", () =>
            {
                Used = true;
                player.AddKarma(+1);
                GameLog.Add("곡괭이와 도끼를 그대로 두었다.", GameLog.Info);
            }),
            new DialogUI.Choice("가져가자", () =>
            {
                Used = true;
                player.AddKarma(-1);
                player.GiveTools();
                GameLog.Add("곡괭이와 도끼를 챙겼다.", GameLog.Good);
                gameObject.SetActive(false);
            }));
    }
}
