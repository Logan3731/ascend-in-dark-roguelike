using System.Collections.Generic;
using UnityEngine;

// 효과음·배경음. Resources/Sounds/<이름> 파일을 찾아 재생한다 (wav·ogg·mp3). 파일이 없으면 아무 소리도 안 낸다.
// 같은 소리를 여러 개(이름_1, 이름_2 …) 넣어 두면 그중 하나를 골라 매번 조금 다르게 들린다.
// 소리 목록은 Sfx.Names (파일 이름 = 이 목록의 이름).
public static class Sfx
{
    public static readonly string[] Names =
    {
        "step",          // 한 칸 걸을 때 (돌바닥 발소리)
        "swing_fist", "swing_blunt", "swing_blade",   // 휘두르기 (맨손·둔기·날붙이)
        "hit_fist", "hit_blunt", "hit_blade",         // 때렸을 때 (맨손·둔기·날붙이)
        "hit_bone",      // 해골류를 때렸을 때 겹치는 뼈 소리
        "hit_slime",     // 슬라임을 때렸을 때 겹치는 끈적한 소리
        // 몬스터별 공격·죽음: attack_<종>, die_<종> (slime·rat·bat·wolf·zombie·goblin·skeleton)
        "block",         // 막기 (나무·금속에 막힘)
        "dodge",         // 회피 (휙 바람 소리)
        "player_hurt",   // 플레이어가 맞았을 때 (신음)
        "monster_die",   // 몬스터가 쓰러질 때 (종별 소리가 없을 때)
        "pickup",        // 물건 줍기
        "coins",         // 골드
        "chest_open",    // 상자 뚜껑
        "door_open",     // 문 열기 (나무·철)
        "pot_break",     // 항아리 깨기
        "drink",         // 물약 마시기
        "eat",           // 식량 먹기
        "throw",         // 던지기
        "trap_spike",    // 가시 함정
        "trap_alarm",    // 경보 함정 종소리
        "alert",         // 적 발견 (!)
        "level_up",      // 레벨 업
        "giant_roar",    // 거인 해골 포효
        "giant_slam",    // 거인 해골 내려찍기·휘두르기
        "skill_slam",    // 점프 찍기 착지
        "ambience",      // 배경 (던전 공기, 반복)
    };

    public const float Volume = 0.8f;
    public const float AmbienceVolume = 0.35f;

    static readonly Dictionary<string, AudioClip[]> cache = new Dictionary<string, AudioClip[]>();
    static AudioSource[] pool;
    static AudioSource ambience;
    static int next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() { cache.Clear(); pool = null; ambience = null; }

    static AudioClip[] Clips(string name)
    {
        if (cache.TryGetValue(name, out var c)) return c;
        var list = new List<AudioClip>();
        var one = Resources.Load<AudioClip>("Sounds/" + name);
        if (one != null) list.Add(one);
        for (int i = 1; i <= 6; i++)
        {
            var v = Resources.Load<AudioClip>($"Sounds/{name}_{i}");
            if (v != null) list.Add(v);
        }
        c = list.ToArray();
        cache[name] = c;
        return c;
    }

    static void EnsurePool()
    {
        if (pool != null && pool[0] != null) return;
        var go = new GameObject("Sfx");
        Object.DontDestroyOnLoad(go);
        // 씬에 귀(AudioListener)가 없으면 아무 소리도 안 들린다. 효과음은 모두 2D라 위치와 상관없이 여기에 단다 (층을 옮겨도 남는다)
        if (Object.FindAnyObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
        pool = new AudioSource[12];
        for (int i = 0; i < pool.Length; i++) { pool[i] = go.AddComponent<AudioSource>(); pool[i].playOnAwake = false; }
        ambience = go.AddComponent<AudioSource>();
        ambience.loop = true; ambience.playOnAwake = false; ambience.volume = AmbienceVolume;
    }

    // 효과음 하나. delay초 뒤에 (공격이 닿는 순간 등), 음높이를 조금씩 흔들어 반복이 덜 거슬리게
    public static void Play(string name, float delay = 0f, float volume = 1f)
    {
        var clips = Clips(name);
        if (clips.Length == 0) return;
        EnsurePool();
        var src = pool[next++ % pool.Length];
        src.clip = clips[Random.Range(0, clips.Length)];
        src.volume = Volume * volume;
        src.pitch = Random.Range(0.93f, 1.07f);
        if (delay > 0f) src.PlayDelayed(delay); else src.Play();
    }

    // 무기 종류별 소리: 맨손 fist, 둔기 blunt (도끼·몽둥이·방패), 날붙이 blade (그 밖의 무기)
    public static string WeaponKind(ItemDef w)
    {
        if (w == null) return "fist";
        if (w == Items.Axe || w == Items.StoneAxe || w == Items.Club || w == Items.Shield) return "blunt";
        return "blade";
    }

    public static void PlayWeapon(string what, ItemDef weapon, float delay = 0f, float volume = 1f) =>
        PlayKind(what, WeaponKind(weapon), delay, volume);

    // what_kind 소리 (swing_fist·die_slime 등). 없으면 기본 소리 (die → monster_die, attack → 없음)
    public static void PlayKind(string what, string kind, float delay = 0f, float volume = 1f)
    {
        if (kind != null && Clips($"{what}_{kind}").Length > 0) { Play($"{what}_{kind}", delay, volume); return; }
        if (what == "die") Play("monster_die", delay, volume);
    }

    // 층마다 깔리는 배경 소리 (파일이 있을 때만)
    public static void StartAmbience()
    {
        var clips = Clips("ambience");
        if (clips.Length == 0) return;
        EnsurePool();
        if (ambience.isPlaying && ambience.clip == clips[0]) return;
        ambience.clip = clips[0];
        ambience.Play();
    }
}
