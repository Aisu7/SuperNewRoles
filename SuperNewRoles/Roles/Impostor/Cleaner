using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using SuperNewRoles.CustomOptions;
using SuperNewRoles.Modules;
using SuperNewRoles.Roles.Ability;
using UnityEngine;

namespace SuperNewRoles.Roles.Impostor;

class Cleaner : RoleBase<Cleaner>
{
    public override RoleId Role { get; } = RoleId.Cleaner;
    public override Color32 RoleColor { get; } = Palette.ImpostorRed;

    public override List<Func<AbilityBase>> Abilities { get; } = [
        () => new CleanerAbility(CleanerCoolTime)
    ];

    public override QuoteMod QuoteMod => QuoteMod.SuperNewRoles;
    public override AssignedTeamType AssignedTeam => AssignedTeamType.Impostor;
    public override WinnerTeamType WinnerTeam => WinnerTeamType.Impostor;
    public override TeamTag TeamTag => TeamTag.Impostor;
    public override RoleTag[] RoleTags => [];
    public override short IntroNum => 1;
    public override RoleOptionMenuType OptionTeam => RoleOptionMenuType.Impostor;

    [CustomOptionFloat("CleanerCoolTime", 40f, 70f, 2.5f, 60f, translationName: "CoolTime")]
    public static float CleanerCoolTime;
}
