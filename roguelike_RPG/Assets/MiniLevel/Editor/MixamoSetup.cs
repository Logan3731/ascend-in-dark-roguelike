using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Mixamo 모션을 주인공에게 붙인다 (Tools ▸ Mixamo 모션 적용). 처음 한 번은 에디터가 열릴 때 저절로 돈다.
// 1) 주인공 모델(PlayerAnimated.fbx)을 Humanoid로 바꿔 아바타를 만든다 (뼈 이름 Hips·Spine·Thigh.L…로 자동 연결)
// 2) Models/Mixamo의 모션들도 Humanoid로 — 같은 사람형 뼈대라면 어떤 모델의 모션이든 옮겨 붙는다(리타깃)
// 3) 애니메이터: 대기·걷기를 횃불 모션으로 바꾸고, 공격·피격·쓰러짐·던지기·마시기·줍기·횃불 붙이기를 트리거로 더한다
// 4) 주인공 프리팹의 Animator에 아바타를 넣는다
// 결과는 Logs/MixamoSetup.log 에 남긴다. 아바타가 사람형으로 잡히지 않으면 원래대로 되돌리고 멈춘다.
[InitializeOnLoad]
public static class MixamoSetup
{
    const string MixamoDir = "Assets/MiniLevel/Models/Mixamo";
    const string PlayerFbx = "Assets/MiniLevel/Models/PlayerAnimated/PlayerAnimated.fbx";
    const string ControllerPath = "Assets/MiniLevel/Models/PlayerAnimated/Player.controller";
    const string PlayerPrefab = "Assets/MiniLevel/Prefabs/P_Player.prefab";
    const string DoneMarker = "Assets/MiniLevel/Models/Mixamo/.setup_done";
    static readonly string LogPath = Path.Combine("Logs", "MixamoSetup.log");

    // (파일, 애니메이터 상태 이름, 반복, 트리거 이름 — null이면 기존 상태의 모션만 바꾼다, 횃불 든 손 모션인가,
    //  게임 속도에 맞춘 길이(초) — 0이면 원래 속도. 턴이 빨라서 긴 동작은 빨리 감는다)
    static readonly (string file, string state, bool loop, string trigger, bool torchHand, float seconds)[] Clips =
    {
        ("Torch_Idle", "Idle", true, null, true, 0f),
        ("Torch_Walk", "Walk", true, null, true, 0f),
        ("Torch_Attack", "Attack", false, "Attack", true, 0.85f),
        ("Hit_Reaction", "Hit", false, "Hit", false, 0.4f),
        ("Dying", "Die", false, "Die", false, 1.6f),
        ("Throw", "Throw", false, "Throw", false, 0.8f),
        ("Drinking", "Drink", false, "Drink", false, 1.4f),
        ("Picking_Up", "PickUp", false, "PickUp", false, 1.0f),
        ("Torch_Light_Wall", "LightTorch", false, "LightTorch", true, 1.4f),
        // 아래는 아직 파일이 없다 (없으면 "없음"만 남기고 넘어간다). Models/Mixamo에 이 이름으로 넣고 메뉴를 다시 돌린다.
        // 공격 모션 여럿: GridClickMover가 Attack1→2→3 차례로 쓰고, 하나도 없으면 Attack(Torch_Attack)
        ("Attack1", "Attack1", false, "Attack1", false, 0.85f),
        ("Attack2", "Attack2", false, "Attack2", false, 0.85f),
        ("Attack3", "Attack3", false, "Attack3", false, 0.85f),
        // 스킬 모션
        ("Triple", "Triple", false, "Triple", true, 0f),             // 3연격 콤보 (블렌더에서 Torch_Attack 자세로 엮음, 원래 속도: 타격 0.27·0.62·0.97초)
        ("JumpSlam", "JumpSlam", false, "JumpSlam", false, 0.6f),    // 점프 찍기
        ("ShieldBash", "ShieldBash", false, "ShieldBash", false, 0.5f),
        ("ThrowDart", "ThrowDart", false, "ThrowDart", false, 0.5f),
    };

    static MixamoSetup()
    {
        if (File.Exists(DoneMarker)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            Run();
        };
    }

    [MenuItem("Tools/Mixamo 모션 적용")]
    public static void Run()
    {
        var log = new StringBuilder();
        void L(string s) { log.AppendLine(s); Debug.Log("[MixamoSetup] " + s); }
        try
        {
            // 1) 주인공 모델 → Humanoid
            var pi = (ModelImporter)AssetImporter.GetAtPath(PlayerFbx);
            var oldType = pi.animationType;
            if (pi.animationType != ModelImporterAnimationType.Human)
            {
                pi.animationType = ModelImporterAnimationType.Human;
                pi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                pi.SaveAndReimport();
            }
            var avatar = AssetDatabase.LoadAllAssetsAtPath(PlayerFbx).OfType<Avatar>().FirstOrDefault();
            L($"주인공 아바타: {(avatar == null ? "없음" : $"valid={avatar.isValid} human={avatar.isHuman}")}");
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                pi.animationType = oldType;
                pi.SaveAndReimport();
                L("사람형 아바타를 만들지 못해 원래대로 되돌렸다. (뼈 이름 자동 연결 실패 — Rig 탭에서 Configure로 직접 연결 필요)");
                return;
            }
            var human = pi.humanDescription.human;
            L("뼈 연결: " + string.Join(", ", human.Select(h => $"{h.humanName}={h.boneName}")));

            // 횃불이 어느 손에 붙어 있나 (Mixamo 횃불 모션은 왼손에 횃불) — 오른손이면 횃불 모션을 좌우 반전
            bool torchInRight = false;
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var stick = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "TorchStick");
                var hand = stick;
                while (hand != null && !hand.name.StartsWith("Hand.") && !hand.name.StartsWith("LowerArm.")) hand = hand.parent;
                torchInRight = hand != null && hand.name.EndsWith(".R");
                L($"횃불 위치: {(stick == null ? "TorchStick 없음" : $"{stick.parent?.name} (손: {(hand == null ? "?" : hand.name)})")} → 횃불 모션 반전 {torchInRight}");

                // 4) 프리팹의 Animator에 아바타
                var anim = root.GetComponentInChildren<Animator>(true);
                if (anim != null)
                {
                    anim.avatar = avatar;
                    anim.applyRootMotion = false;
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
                    L($"프리팹 Animator({anim.gameObject.name})에 아바타 연결");
                }
                else L("프리팹에서 Animator를 찾지 못했다");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            // 2) Mixamo 모션 → Humanoid, 반복·제자리 설정
            var clipFor = new Dictionary<string, AnimationClip>();
            foreach (var c in Clips)
            {
                string path = $"{MixamoDir}/{c.file}.fbx";
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { L($"없음: {path}"); continue; }
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                var takes = mi.defaultClipAnimations;
                foreach (var t in takes)
                {
                    t.name = c.state;
                    t.loopTime = c.loop;
                    t.lockRootRotation = true;            // 몸 방향은 게임이 정한다
                    t.lockRootPositionXZ = true;          // 제자리 (이동은 게임이 한다)
                    t.keepOriginalPositionXZ = false;
                    t.lockRootHeightY = c.state != "Die"; // 쓰러짐만 바닥으로 내려간다
                    t.keepOriginalPositionY = false;
                    t.heightFromFeet = true;              // 높이는 발 기준 — 몸이 바닥에 파묻히지 않게
                    t.mirror = c.torchHand && torchInRight;
                }
                mi.clipAnimations = takes;
                mi.SaveAndReimport();
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview__"));
                if (clip != null) clipFor[c.state] = clip;
                L($"모션 {c.file} → {c.state} ({(clip == null ? "클립 없음" : $"{clip.length:0.00}초")})");
            }

            // 3) 애니메이터
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var sm = ctrl.layers[0].stateMachine;
            AnimatorState Find(string n) => sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n);
            var idle = Find("Idle");
            foreach (var c in Clips)
            {
                if (!clipFor.TryGetValue(c.state, out var clip)) continue;
                var st = Find(c.state);
                if (c.trigger == null)
                {
                    if (st != null) st.motion = clip;
                    continue;
                }
                if (!ctrl.parameters.Any(p => p.name == c.trigger)) ctrl.AddParameter(c.trigger, AnimatorControllerParameterType.Trigger);
                if (st == null) st = sm.AddState(c.state, new Vector3(520, 60 * System.Array.IndexOf(Clips, c), 0));
                st.motion = clip;
                st.speed = c.seconds > 0f ? Mathf.Max(1f, clip.length / c.seconds) : 1f;
                // 어느 상태에서든 트리거로 바로 들어가고, 끝나면 대기로 (쓰러짐은 그대로 멈춘다)
                foreach (var tr in sm.anyStateTransitions.Where(t => t.destinationState == st).ToArray()) sm.RemoveAnyStateTransition(tr);
                var into = sm.AddAnyStateTransition(st);
                into.AddCondition(AnimatorConditionMode.If, 0, c.trigger);
                into.hasExitTime = false;
                into.duration = 0.08f;
                into.canTransitionToSelf = c.trigger != "Die";
                foreach (var tr in st.transitions.ToArray()) st.RemoveTransition(tr);
                if (c.trigger != "Die" && idle != null)
                {
                    var back = st.AddTransition(idle);
                    back.hasExitTime = true;
                    back.exitTime = 0.9f;
                    back.duration = 0.15f;
                    // 걷기 시작하면 동작을 끊고 바로 걷는다 (미끄러지지 않게)
                    var walk = Find("Walk");
                    if (walk != null && ctrl.parameters.Any(p => p.name == "Moving"))
                    {
                        var go = st.AddTransition(walk);
                        go.AddCondition(AnimatorConditionMode.If, 0, "Moving");
                        go.hasExitTime = false;
                        go.duration = 0.1f;
                    }
                }
            }
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            File.WriteAllText(DoneMarker, System.DateTime.Now.ToString("s"));
            L("완료");
        }
        catch (System.Exception e)
        {
            L("오류: " + e);
        }
        finally
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(LogPath, log.ToString());
        }
    }
}
