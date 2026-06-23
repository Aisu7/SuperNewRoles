using System;
using System.Collections.Generic;
using UnityEngine;
using AmongUs.GameOptions;
using SuperNewRoles.CustomOptions;
using SuperNewRoles.Modules;
using SuperNewRoles.Roles.Ability;
using SuperNewRoles.Roles.Ability.CustomButton;
using SuperNewRoles.Events;
using SuperNewRoles.Events.PCEvents;
using SuperNewRoles.Modules.Events.Bases;
using HarmonyLib;

namespace SuperNewRoles.Roles.Impostor;

// 提案者：gamerkun さん
class Jammer : RoleBase<Jammer>
{
    public override RoleId Role => RoleId.Jammer;
    public override Color32 RoleColor => Palette.ImpostorRed;
    public override List<Func<AbilityBase>> Abilities => [
        () => new JammerAbility(
            JammerCoolTime,
            JammerDurationTime,
            JammerAbilityCount,
            JammerCanUseAbilitiesAgainstImposter
        )
    ];

    public override QuoteMod QuoteMod => QuoteMod.SuperNewRoles;
    public override RoleTypes IntroSoundType => RoleTypes.Shapeshifter;
    public override short IntroNum => 1;

    public override AssignedTeamType AssignedTeam => AssignedTeamType.Impostor;
    public override WinnerTeamType WinnerTeam => WinnerTeamType.Impostor;
    public override TeamTag TeamTag => TeamTag.Impostor;
    public override RoleTag[] RoleTags => [RoleTag.Information];
    public override RoleOptionMenuType OptionTeam => RoleOptionMenuType.Impostor;

    [CustomOptionFloat("JammerCoolTime", 2.5f, 60f, 2.5f, 25f, translationName: "CoolTime")]
    public static float JammerCoolTime;
    [CustomOptionFloat("JammerDurationTime", 2.5f, 120f, 2.5f, 10f, translationName: "DurationTime")]
    public static float JammerDurationTime;
    [CustomOptionInt("JammerAbilityCount", 1, 15, 1, 3, translationName: "UseLimit")]
    public static int JammerAbilityCount;
    [CustomOptionBool("JammerCanUseAbilitiesAgainstImposter", false, translationName: "JammerCanUseAbilitiesAgainstImposter")]
    public static bool JammerCanUseAbilitiesAgainstImposter;
}

public class JammerAbility : TargetCustomButtonBase, IButtonEffect
{
    private float _coolTime;
    private float _durationTime;
    private int _abilityCount;
    private bool _canUseAgainstImpostors;
    private int _usedCount;
    private ExPlayerControl _invisibleTarget;
    private EventListener<MeetingStartEventData> _onMeetingStart;
    private EventListener<DieEventData> _onDie;
    private EventListener _onFixedUpdate;
    private readonly OpacityFadeController _opacityFader = new();

    public bool isEffectActive { get; set; }
    public float EffectTimer { get; set; }
    public float EffectDuration => _durationTime;
    public Action OnEffectEnds => () =>
    {
        if (_invisibleTarget != null)
        {
            RpcSetInvisible(_invisibleTarget, false);
            _invisibleTarget = null;
        }
    };
    public bool effectCancellable => true;

    // バニラの PlayerControl.SetHatAndVisorAlpha が任意のタイミングで呼ばれ、
    // バイザー(とハット)のアルファ値を上書きしてしまう問題への対策。
    // 現在ジャマーで非表示中の対象を登録しておき、上書きされた直後に再適用する。
    private static readonly Dictionary<byte, JammerAbility> _jammedTargets = new();

    public override Color32 OutlineColor => Color.red;
    public override Sprite Sprite => AssetManager.GetAsset<Sprite>("JammerButton.png");
    public override string buttonText => ModTranslation.GetString("JammerButtonName");
    protected override KeyType keytype => KeyType.Ability1;
    public override float DefaultTimer => _coolTime;
    public override bool OnlyCrewmates => !_canUseAgainstImpostors;
    public override bool TargetPlayersInVents => false;

    public JammerAbility(float coolTime, float durationTime, int abilityCount, bool canUseAgainstImpostors)
    {
        _coolTime = coolTime;
        _durationTime = durationTime;
        _abilityCount = abilityCount;
        _canUseAgainstImpostors = canUseAgainstImpostors;
        _usedCount = 0;
    }

    public override bool CheckIsAvailable()
    {
        if (!Player.IsAlive()) return false;
        if (!Player.Player.CanMove) return false;
        if (_usedCount >= _abilityCount) return false;
        if (!TargetIsExist) return false;
        return true;
    }

    public override void OnClick()
    {
        if (Target == null) return;

        if (_invisibleTarget != null && _invisibleTarget != Target)
        {
            RpcSetInvisible(_invisibleTarget, false);
        }

        _invisibleTarget = Target;
        RpcSetInvisible(Target, true);
        _usedCount++;
        ResetTimer();
    }

    public override void AttachToAlls()
    {
        base.AttachToAlls();
        _onFixedUpdate = FixedUpdateEvent.Instance.AddListener(() => OnFixedUpdate());
    }

    public override void DetachToAlls()
    {
        base.DetachToAlls();
        _onFixedUpdate?.RemoveListener();
        _opacityFader.StopAll();
    }

    public override void AttachToLocalPlayer()
    {
        base.AttachToLocalPlayer();
        _onMeetingStart = MeetingStartEvent.Instance.AddListener(OnMeetingStart);
        _onDie = DieEvent.Instance.AddListener(OnDie);
    }

    public override void DetachToLocalPlayer()
    {
        base.DetachToLocalPlayer();
        // クリーンアップ：透明効果を確実に解除
        if (_invisibleTarget != null)
        {
            RpcSetInvisible(_invisibleTarget, false);
            _invisibleTarget = null;
        }
        _onMeetingStart?.RemoveListener();
        _onDie?.RemoveListener();
    }

    private void OnDie(DieEventData data)
    {
        // ジャマー対象が死亡した場合もレジストリから外す
        // (バニラの SetHatAndVisorAlpha が死亡後の表示処理で呼ばれても干渉しないようにする)
        if (_invisibleTarget != null && data.player == _invisibleTarget)
        {
            _jammedTargets.Remove(_invisibleTarget.PlayerId);
        }
    }

    private void OnMeetingStart(MeetingStartEventData data)
    {
        if (_invisibleTarget != null)
        {
            RpcSetInvisible(_invisibleTarget, false);
            _invisibleTarget = null;
        }
    }

    private void OnFixedUpdate()
    {
        if (_invisibleTarget != null && !_invisibleTarget.IsDead())
        {
            _opacityFader.Apply(_invisibleTarget, CanSeeTranslucentState(_invisibleTarget, out var opacity) ? opacity : 0f, forceSnap: true);
        }
    }

    [CustomRPC]
    public void RpcSetInvisible(ExPlayerControl target, bool isInvisible)
    {
        SetInvisible(target, isInvisible);
    }

    private void SetInvisible(ExPlayerControl target, bool isInvisible)
    {
        if (isInvisible)
        {
            _jammedTargets[target.PlayerId] = this;
            _opacityFader.Apply(target, CanSeeTranslucentState(target, out var opacity) ? opacity : 0f);
        }
        else
        {
            _jammedTargets.Remove(target.PlayerId);
            _opacityFader.Apply(target, 1f);
        }
    }

    private bool CanSeeTranslucentState(ExPlayerControl invisibleTarget, out float opacity)
    {
        if (invisibleTarget == ExPlayerControl.LocalPlayer)
        {
            opacity = 1f;
            return true;
        }
        if (ExPlayerControl.LocalPlayer.IsImpostor())
        {
            opacity = 0.4f;
            return true;
        }
        opacity = 0f;
        return false;
    }

    // バニラの PlayerControl.SetHatAndVisorAlpha は、近くの障害物や暗所などの判定により
    // 任意のタイミングで呼び出され、バイザー(及びハット)のアルファ値を直接上書きしてしまう。
    // このため OpacityFadeController で一度フェードさせても、直後にバニラ側の処理で
    // 不透明(アルファ1)へ戻され、「バイザーだけ消えずに残る」症状が発生していた。
    // 対象がジャマーで非表示中であれば、上書きされた直後に再度ジャマーの不透明度を
    // 強制適用することで対処する。
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetHatAndVisorAlpha))]
    public static class SetHatAndVisorAlphaPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            if (__instance == null) return;
            if (!_jammedTargets.TryGetValue(__instance.PlayerId, out var ability)) return;

            ExPlayerControl target = __instance;
            if (target == null || target.IsDead())
            {
                _jammedTargets.Remove(__instance.PlayerId);
                return;
            }

            float opacity = ability.CanSeeTranslucentState(target, out var op) ? op : 0f;
            ModHelpers.SetOpacity(__instance, opacity);
        }
    }
}
