using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using SuperNewRoles.CustomOptions;
using SuperNewRoles.Modules;
using SuperNewRoles.Roles.Ability;
using UnityEngine;
using SuperNewRoles.Events.PCEvents;
using SuperNewRoles.Modules.Events.Bases;

namespace SuperNewRoles.Roles.Neutral;

class MayorFriends : RoleBase<MayorFriends>
{
    public override RoleId Role { get; } = RoleId.MayorFriends;
    public override Color32 RoleColor { get; } = Jackal.Instance.RoleColor;
    public override List<Func<AbilityBase>> Abilities { get; } = [
    () => new JFriendAbility(new(
            CanUseVent: MayorFriendsCanUseVent,
           IsImpostorVision: MayorFriendsImpostorVision,
           CouldKnowJackals: MayorFriendsCouldKnowJackals,
           TaskNeeded: MayorFriendsTaskNeed,
           SpecialTasks: MayorFriendsCustomTaskCount ? MayorFriendsTaskOption : null)
          ),
    () => new AdditionalVoteAbility(() => MayorFriendsVoteAdditionalVote - 1)];


    public override QuoteMod QuoteMod { get; } = QuoteMod.SuperNewRoles;
    public override RoleTypes IntroSoundType { get; } = RoleTypes.Crewmate;
    public override short IntroNum { get; } = 1;

    public override AssignedTeamType AssignedTeam { get; } = AssignedTeamType.Crewmate;
    public override WinnerTeamType WinnerTeam { get; } = WinnerTeamType.Neutral;
    public override TeamTag TeamTag { get; } = TeamTag.Jackal;
    public override RoleTag[] RoleTags { get; } = [];
    public override RoleOptionMenuType OptionTeam { get; } = RoleOptionMenuType.Crewmate;

    [CustomOptionBool("MayorFriendsCanUseVent", true, translationName: "CanUseVent")]
    public static bool MayorFriendsCanUseVent;

    [CustomOptionBool("MayorFriendsImpostorVision", true, translationName: "HasImpostorVision")]
    public static bool MayorFriendsImpostorVision;

    [CustomOptionBool("MayorFriendsDontAssignIfJackalNotAssigned", true)]
    public static bool MayorFriendsDontAssignIfJackalNotAssigned;

    [CustomOptionBool("MayorFriendsCouldKnowJackals", true)]
    public static bool MayorFriendsCouldKnowJackals;

    [CustomOptionInt("MayorFriendsTaskNeed", 0, 10, 1, 0, parentFieldName: nameof(MayorFriendsCouldKnowJackals))]
    public static int MayorFriendsTaskNeed;

    [CustomOptionBool("MayorFriendsCustomTaskCount", false, parentFieldName: nameof(MayorFriendsCouldKnowJackals))]
    public static bool MayorFriendsCustomTaskCount;

    [CustomOptionTask("MayorFriendsTaskOption", 1, 1, 1, parentFieldName: nameof(MayorFriendsCustomTaskCount), translationName: "TaskOption")]
    public static TaskOptionData MayorFriendsTaskOption;

    [CustomOptionInt("MayorFriendsVoteAdditionalVote", 1, 10, 1, 2)]
    public static int MayorFriendsVoteAdditionalVote;

}