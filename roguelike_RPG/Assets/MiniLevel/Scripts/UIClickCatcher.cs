using System;
using UnityEngine;
using UnityEngine.EventSystems;

// UI 빈 곳(배경) 클릭을 받아서 알려 준다. 들고 있던 아이템을 되돌리거나 창을 닫을 때 쓴다.
public class UIClickCatcher : MonoBehaviour, IPointerClickHandler
{
    public Action onClick;
    public void OnPointerClick(PointerEventData e) { onClick?.Invoke(); }
}
