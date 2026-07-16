using System;
using System.Collections.Generic;
using UnityEngine;
using AmongUs.GameOptions;
using SuperNewRoles.CustomOptions;
using SuperNewRoles.Modules;
using SuperNewRoles.Roles.Ability;

namespace SuperNewRoles.Roles.Madmates;

class MadCleaner : RoleBase<MadCleaner>
{
    public override RoleId Role { get; } = RoleId.MadCleaner;
    public override Color32 RoleColor { get; } = Palette.ImpostorRed;

    public override List<Func<AbilityBase>> Abilities { get; } = [
        () => new MadmateAbility(new(
            MadCleanerHasImpostorVision,
            MadCleanerCouldUseVent,
            MadCleanerCanKnowImpostors,
            MadCleanerNeededTaskCount,
            MadCleanerIsSpecialTasks ? MadCleanerSpecialTasks : null)),
        () => new CleanerAbility(
            MadCleanerCoolTime)
    ];

    public override QuoteMod QuoteMod { get; } = QuoteMod.SuperNewRoles;
    public override RoleTypes IntroSoundType { get; } = RoleTypes.Shapeshifter;
    public override short IntroNum { get; } = 1;

    public override AssignedTeamType AssignedTeam { get; } = AssignedTeamType.Crewmate;
    public override WinnerTeamType WinnerTeam { get; } = WinnerTeamType.Impostor;
    public override TeamTag TeamTag { get; } = TeamTag.Madmate;
    public override RoleTag[] RoleTags { get; } = [];
    public override RoleOptionMenuType OptionTeam { get; } = RoleOptionMenuType.Crewmate;

    // --- Cleaner Ability ---
    [CustomOptionFloat("MadCleanerCoolTime", 40f, 70f, 2.5f, 60f, translationName: "CoolTime")]
    public static float MadCleanerCoolTime;

    

    // --- Madmate Custom Options ---
    [CustomOptionBool("MadCleanerCouldUseVent", false, translationName: "CanUseVent")]
    public static bool MadCleanerCouldUseVent;

    [CustomOptionBool("MadCleanerHasImpostorVision", false, translationName: "HasImpostorVision")]
    public static bool MadCleanerHasImpostorVision;

    [CustomOptionBool("MadCleanerCanKnowImpostors", false, translationName: "MadmateCanKnowImpostors")]
    public static bool MadCleanerCanKnowImpostors;

    [CustomOptionInt("MadCleanerNeededTaskCount", 0, 30, 1, 6, parentFieldName: nameof(MadCleanerCanKnowImpostors), translationName: "MadmateNeededTaskCount")]
    public static int MadCleanerNeededTaskCount;

    [CustomOptionBool("MadCleanerIsSpecialTasks", false, translationName: "MadmateIsSpecialTasks")]
    public static bool MadCleanerIsSpecialTasks;
    [CustomOptionTask("MadCleanerSpecialTasks", 1, 1, 1, translationName: "MadmateSpecialTasks", parentFieldName: nameof(MadCleanerIsSpecialTasks))]
    public static TaskOptionData MadCleanerSpecialTasks;
}