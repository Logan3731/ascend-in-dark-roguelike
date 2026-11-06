using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 레벨이 오르면 보상 셋 중 하나를 고른다. 다른 대화창이 떠 있으면 닫힌 뒤에 띄운다 (여러 번 오르면 차례로).
public class LevelUpChoice : MonoBehaviour
{
    public enum Perk { Vitality, Sharp, Focus, Technique, Stomach, Recovery, Torchbearer, Might }

    static readonly Dictionary<Perk, (string label, string desc)> info = new Dictionary<Perk, (string, string)>
    {
        [Perk.Vitality] = ("체력 +15", "최대 체력 +15"),
        [Perk.Sharp] = ("공격 +1", "공격력 +1"),
        [Perk.Focus] = ("기력 +10", "최대 기력 +10"),
        [Perk.Technique] = ("스킬 기력 -2", "3연격·점프 찍기 기력 비용 -2"),
        [Perk.Stomach] = ("허기 +25", "최대 허기 +25"),
        [Perk.Recovery] = ("걸음 회복 ↑", "걸을 때 체력 회복 빠르게"),
        [Perk.Torchbearer] = ("횃불 +60", "횃불 내구도 +60"),
        [Perk.Might] = ("힘 +1", "힘 +1 (무거운 무기를 다룬다)"),
    };

    static LevelUpChoice runner;
    static int pending;
    static PlayerStats player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() { pending = 0; runner = null; }

    public static void Enqueue(PlayerStats p)
    {
        player = p;
        pending++;
        if (runner == null) runner = new GameObject("LevelUpChoice").AddComponent<LevelUpChoice>();
        runner.StopAllCoroutines();
        runner.StartCoroutine(runner.ShowWhenFree());
    }

    IEnumerator ShowWhenFree()
    {
        while (pending > 0)
        {
            // 다른 창·연출이 끝날 때까지 기다린다
            while (DialogUI.IsOpen || InventoryUI.IsOpen || PauseMenu.IsOpen || Time.time < TurnResolver.BusyUntil) yield return null;
            yield return new WaitForSeconds(0.3f);
            if (player == null || player.Dead) { pending = 0; yield break; }

            pending--;
            var pool = new List<Perk>((Perk[])System.Enum.GetValues(typeof(Perk)));
            pool.Remove(Perk.Might);   // 힘은 힘의 물약·힘의 책으로만
            pool.Remove(Perk.Sharp);   // 공격력은 레벨 업으로 올리지 않는다
            for (int i = pool.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
            var picks = pool.GetRange(0, 3);
            string body = $"<color=#dcb86b>레벨 업! Lv.{player.Level}</color>   하나를 고른다.\n<size=22><color=#9e9480>" +
                          string.Join("   ·   ", picks.ConvertAll(p => info[p].desc)) + "</color></size>";
            bool chosen = false;
            var choices = picks.ConvertAll(p => new DialogUI.Choice(info[p].label, () =>
            {
                player.ApplyPerk(p);
                GameLog.Add($"{info[p].desc}", GameLog.Good);
                chosen = true;
            }));
            DialogUI.Show(body, choices.ToArray());
            while (!chosen && DialogUI.IsOpen) yield return null;
        }
    }
}
