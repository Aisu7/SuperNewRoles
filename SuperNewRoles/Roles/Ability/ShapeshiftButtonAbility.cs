using AmongUs.GameOptions;
using UnityEngine;
using SuperNewRoles.Modules;
using SuperNewRoles.Roles.Ability.CustomButton;
using SuperNewRoles.Events;
using SuperNewRoles.Events.PCEvents;
using SuperNewRoles.Modules.Events.Bases;
using System;
using SuperNewRoles.Roles.Impostor;

namespace SuperNewRoles.Roles.Ability;

public class ShapeshiftButtonAbility : CustomButtonBase, IButtonEffect
{
    public float DurationTime;
    public float CoolTime;
    public int MaxUseCount;

    public override Sprite Sprite => SpriteName != null ? AssetManager.GetAsset<Sprite>(SpriteName) : FastDestroyableSingleton<RoleManager>.Instance.GetRole(RoleTypes.Shapeshifter).Ability.Image;
    public override string buttonText => FastDestroyableSingleton<TranslationController>.Instance.GetString(StringNames.ShapeshiftAbility);
    protected override KeyType keytype => KeyType.Ability1;
    public override float DefaultTimer => CoolTime;

    public bool isEffectActive { get; set; }

    public Action OnEffectEnds => () =>
    {
        _shapeTarget = null;
        PlayerControl.LocalPlayer.RpcShapeshift(PlayerControl.LocalPlayer, true); // Revert shape
        ResetTimer(); // Start cooldown
        ResetLocalPlayerScale(); // 変身解除後のスケール戻し
    };

    public float EffectDuration => DurationTime;
    public float EffectTimer { get; set; }
    public bool effectCancellable => true; // Allow cancelling the shapeshift early

    private PlayerControl _shapeTarget;
    public PlayerControl ShapeTarget => _shapeTarget;

    public override ShowTextType showTextType => MaxUseCount > 0 ? ShowTextType.ShowWithCount : ShowTextType.Hidden;
    public string SpriteName { get; }

    public ShapeshiftButtonAbility(float coolTime, float durationTime, int maxUseCount = -1, string spriteName = null)
    {
        DurationTime = durationTime;
        CoolTime = coolTime;
        MaxUseCount = maxUseCount;
        Count = maxUseCount;
        SpriteName = spriteName;
    }

    public override void OnClick()
    {
        if (MaxUseCount > 0 && Count <= 0)
            return;

        RoleTypes baseRole = ExPlayerControl.LocalPlayer.Data.Role.Role;
        float killTimer = PlayerControl.LocalPlayer.killTimer;
        RoleManager.Instance.SetRole(Player, RoleTypes.Shapeshifter);
        ExPlayerControl.LocalPlayer.Data.Role.TryCast<ShapeshifterRole>()?.UseAbility();
        RoleManager.Instance.SetRole(Player, baseRole);
        PlayerControl.LocalPlayer.killTimer = killTimer;

        new LateTask(() =>
        {
            isEffectActive = false;
            Timer = 0.0001f;
            actionButton.cooldownTimerText.color = Palette.EnabledColor;
        }, 2f / 60f, "ShapeshiftButtonAbility");
    }

    public override bool CheckIsAvailable()
    {
        if (!ExPlayerControl.LocalPlayer.IsAlive()) return false;
        if (!PlayerControl.LocalPlayer.CanMove) return false;
        return true;
    }

    public override bool CheckHasButton()
    {
        return ExPlayerControl.LocalPlayer.IsAlive() && (MaxUseCount <= 0 || Count > 0);
    }

    private void OnShapeshift(ShapeshiftEventData data)
    {
        // Only react if this player is the one shapeshifting and not reverting
        if (data.shapeshifter != Player || data.shapeshifter == data.target) return;

        // 変身前のスケールを保存（ジャンボ等のサイズ変更役職に対応）。
        // JumboModifier 等が各イベントで data.instance.Player.transform のように
        // 対象プレイヤーの実体を直接参照しているのと同様、ここでも
        // PlayerControl.LocalPlayer ではなく Player.Player（この Ability の持ち主）を使う。
        if (Player.AmOwner)
            _preShapeshiftScale = Player.Player.transform.localScale;

        _shapeTarget = data.target;
        if (Count > 0)
            Count--;
        if (!Player.AmOwner) return;
        ResetTimer();
        isEffectActive = true;
        EffectTimer = DurationTime;
        actionButton.cooldownTimerText.color = IButtonEffect.color;
    }

    private void OnWrapUp(WrapUpEventData data)
    {
        // Ensure player is reverted at the end of the round
        if (isEffectActive)
        {
            isEffectActive = false;
            _shapeTarget = null;
            EffectTimer = 0;
        }
        PlayerControl.LocalPlayer.RpcShapeshiftModded(ExPlayerControl.LocalPlayer, false);
        ResetTimer(); // Reset cooldown for next round
        ResetLocalPlayerScale(); // 変身解除後のスケール戻し
    }

    private EventListener<ShapeshiftEventData> _shapeshiftEvent;
    private EventListener<WrapUpEventData> _wrapUpEvent;
    private EventListener<CalledMeetingEventData> _calledMeetingEvent;
    // 変身前のスケールを保存（ジャンボ等のサイズ変更役職に対応するため Vector3.one 固定は使わない）
    private Vector3 _preShapeshiftScale = Vector3.one;

    public override void AttachToLocalPlayer()
    {
        base.AttachToLocalPlayer();
        _shapeshiftEvent = ShapeshiftEvent.Instance.AddListener(OnShapeshift);
        _wrapUpEvent = WrapUpEvent.Instance.AddListener(OnWrapUp);
        _calledMeetingEvent = CalledMeetingEvent.Instance.AddListener(OnCalledMeeting);
    }

    public override void DetachToLocalPlayer()
    {
        base.DetachToLocalPlayer();
        _shapeshiftEvent?.RemoveListener();
        _wrapUpEvent?.RemoveListener();
        _calledMeetingEvent?.RemoveListener();

        // Ensure player reverts shape if the ability is detached while active
        if (isEffectActive)
        {
            isEffectActive = false;
            _shapeTarget = null;
            PlayerControl.LocalPlayer.RpcShapeshiftModded(ExPlayerControl.LocalPlayer, false);
        }
    }

    /// <summary>
    /// 会議招集時に変身中だった場合は即時解除する。
    /// 解除しないまま会議に入ると WrapUp 後にバグった状態でターンが始まる。
    /// </summary>
    private void OnCalledMeeting(CalledMeetingEventData data)
    {
        if (!isEffectActive) return;
        isEffectActive = false;
        _shapeTarget = null;
        EffectTimer = 0;
        PlayerControl.LocalPlayer.RpcShapeshiftModded(ExPlayerControl.LocalPlayer, false);
        ResetTimer();
        ResetLocalPlayerScale(); // 変身解除後のスケール戻し
    }

    /// <summary>
    /// 変身解除後に自分視点でプレイヤーのサイズが小さいまま残るバグへの対処。
    /// シェイプシフトアニメーション（縮小 → 拡大）の拡大フェーズが正常に完了しない場合に
    /// localScale が戻らないため、アニメーション完了を待って強制リセットする。
    /// ジャンボ等の変身前サイズが Vector3.one でない役職にも対応するため、
    /// Vector3.one 固定ではなく変身前に保存したスケールを使用する。
    /// JumboModifier が各イベントで data.instance.Player.transform のように対象プレイヤーの
    /// 実体を直接参照しているのに合わせ、PlayerControl.LocalPlayer に決め打ちせず
    /// Player.Player（このAbilityの持ち主）の transform を対象にする。
    /// </summary>
    private void ResetLocalPlayerScale()
    {
        if (!Player.AmOwner) return;

        var targetScale = _preShapeshiftScale != Vector3.zero ? _preShapeshiftScale : Vector3.one;
        PlayerControl targetPlayer = Player.Player;
        new LateTask(() =>
        {
            if (targetPlayer != null && targetPlayer.transform.localScale != targetScale)
                targetPlayer.transform.localScale = targetScale;
        }, 1.2f, "ResetScaleAfterShapeshift");
    }
}
