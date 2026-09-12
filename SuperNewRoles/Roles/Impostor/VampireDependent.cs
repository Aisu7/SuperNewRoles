using System;
using System.Collections.Generic;
using UnityEngine;
using AmongUs.GameOptions;
using SuperNewRoles.CustomOptions;
using SuperNewRoles.Modules;
using SuperNewRoles.Roles.Ability;
using SuperNewRoles.Events.PCEvents;
using SuperNewRoles.Events;
using SuperNewRoles.CustomOptions.Categories;

namespace SuperNewRoles.Roles.Impostor;

class VampireDependent : RoleBase<VampireDependent>
{
    public override RoleId Role => RoleId.VampireDependent;
    public override Color32 RoleColor => Palette.ImpostorRed;
    public override List<Func<AbilityBase>> Abilities { get; } = [() => new VampireDependentAbility(
        data: new VampireDependentData(
            killCooldown: Vampire.VampireDependentKillCooldown,
            canUseVent: Vampire.VampireDependentCanUseVent
        ),
        vampire: new VampireData(
            vampireInvisibleOnAdmin: Vampire.VampireInvisibleOnAdmin,
            vampireCannotFixSabotage: Vampire.VampireCannotFixSabotage,
            vampireCannotUseDevice: Vampire.VampireCannotUseDevice,
            vampireDependentHasReverseVision: Vampire.VampireDependentHasReverseVision,
            vampireDependentHasImpostorVisionInLightsoff: Vampire.VampireDependentHasImpostorVisionInLightsoff,
            vampireNoDeathOnVitals: Vampire.VampireNoDeathOnVitals
        )
    )];

    public override QuoteMod QuoteMod => QuoteMod.SuperNewRoles;
    public override RoleTypes IntroSoundType => RoleTypes.Shapeshifter;
    public override short IntroNum => 1;

    public override AssignedTeamType AssignedTeam => AssignedTeamType.Impostor;
    public override WinnerTeamType WinnerTeam => WinnerTeamType.Impostor;
    public override TeamTag TeamTag => TeamTag.Impostor;
    public override RoleTag[] RoleTags => [RoleTag.ImpostorTeam];
    public override RoleOptionMenuType OptionTeam => RoleOptionMenuType.Hidden;
    public override bool HideInRoleDictionary => true; // 役職辞典で非表示にする
}
public record VampireDependentData(float killCooldown, bool canUseVent);
public class VampireDependentAbility : AbilityBase
{
    public VampireAbility vampire { get; private set; }
    public VampireData VampireData { get; }
    public CustomVentAbility ventAbility;
    private CustomKillButtonAbility killButtonAbility;
    public VampireDependentData Data { get; }
    private SabotageCanUseAbility sabotageCanUseAbility;
    private DeviceCanUseAbility deviceCanUseAbility;
    private HideInAdminAbility hideInAdminAbility;
    private ReverseVisionAbility reverseVisionAbility;
    private bool _hasVampire;

    /// <summary>
    /// 眷属の能力設定と、親ヴァンパイアから引き継ぐ設定を保持します。
    /// </summary>
    /// <param name="data">眷属固有の能力設定。</param>
    /// <param name="vampire">親ヴァンパイアから引き継ぐ能力設定。</param>
    public VampireDependentAbility(VampireDependentData data, VampireData vampire)
    {
        this.Data = data;
        this.VampireData = vampire;
    }

    /// <summary>
    /// 全プレイヤーで使用する能力と、親ヴァンパイアの死亡監視を登録します。
    /// </summary>
    public override void AttachToAlls()
    {
        base.AttachToAlls();
        SubscribeWithAbility(FixedUpdateEvent.Instance, OnFixedUpdate);

        killButtonAbility = new CustomKillButtonAbility(
            canKill: () => true,
            killCooldown: () => Data.killCooldown,
            onlyCrewmates: () => true,
            isTargetable: (player) => player != vampire?.Player
        );
        ventAbility = new CustomVentAbility(() => Data.canUseVent);
        sabotageCanUseAbility = new SabotageCanUseAbility(
            () => VampireData.vampireCannotFixSabotage ? SabotageType.Lights : SabotageType.None
        );
        deviceCanUseAbility = new DeviceCanUseAbility(
            () => VampireData.vampireCannotUseDevice ? DeviceTypeFlag.All : DeviceTypeFlag.None
        );
        hideInAdminAbility = new HideInAdminAbility(
            () => VampireData.vampireInvisibleOnAdmin
        );
        reverseVisionAbility = new ReverseVisionAbility(
            () => VampireData.vampireDependentHasReverseVision,
            () => VampireData.vampireDependentHasImpostorVisionInLightsoff
        );

        Player.AttachAbility(killButtonAbility, new AbilityParentAbility(this));
        Player.AttachAbility(ventAbility, new AbilityParentAbility(this));
        Player.AttachAbility(sabotageCanUseAbility, new AbilityParentAbility(this));
        Player.AttachAbility(deviceCanUseAbility, new AbilityParentAbility(this));
        Player.AttachAbility(hideInAdminAbility, new AbilityParentAbility(this));
        Player.AttachAbility(reverseVisionAbility, new AbilityParentAbility(this));
        Player.AttachAbility(new KnowOtherAbility((player) => player.Player == vampire?.Player, () => true), new AbilityParentAbility(this));
    }

    /// <summary>
    /// ローカルプレイヤー向けに、親ヴァンパイアの名前表示更新を登録します。
    /// </summary>
    public override void AttachToLocalPlayer()
    {
        base.AttachToLocalPlayer();
        SubscribeWithAbility(NameTextUpdateEvent.Instance, OnNameTextUpdate);
    }

    /// <summary>
    /// 親ヴァンパイアの名前をインポスター陣営の色で表示します。
    /// </summary>
    /// <param name="data">名前表示を更新するプレイヤーの情報。</param>
    private void OnNameTextUpdate(NameTextUpdateEventData data)
    {
        // 親ヴァンパイアの名前に印を付ける
        if (vampire?.Player == data.Player)
        {
            NameText.SetNameTextColor(data.Player, Palette.ImpostorRed, true);
        }
    }

    /// <summary>
    /// ホスト上で親ヴァンパイアの死亡を監視し、生存中の眷属を自殺させます。
    /// </summary>
    private void OnFixedUpdate()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!_hasVampire) return;
        if (Player.IsDead()) return;

        if (vampire?.Player != null && vampire.Player.IsAlive()) return;

        Player.RpcCustomDeath(CustomDeathType.Suicide);
    }

    /// <summary>
    /// 親ヴァンパイアを設定し、死亡監視を有効にします。
    /// </summary>
    /// <param name="vampire">この眷属を生成した親ヴァンパイア。</param>
    public void SetVampire(VampireAbility vampire)
    {
        this.vampire = vampire;
        _hasVampire = true;
    }
}
