using UnityEngine;

// 그 칸을 처음 밟으면 로그에 문구를 띄운다 (튜토리얼 안내용).
public class MessageTrigger : Interactable
{
    [TextArea] public string message;
    public bool onlyWithoutTorch;   // 횃불을 들고 있으면 띄우지 않는다

    void Reset() { triggerOnEnter = true; }

    public override void Interact(PlayerStats player)
    {
        if (Used) return;
        if (onlyWithoutTorch && player.TorchLit) return;
        Used = true;
        GameLog.Add(message, GameLog.Info);
    }
}
