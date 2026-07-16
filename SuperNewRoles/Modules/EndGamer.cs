using System.Collections.Generic;
using System.Linq;
using SuperNewRoles.Patches;
using SuperNewRoles.Roles;
using SuperNewRoles.Roles.Modifiers;
using SuperNewRoles.Roles.Neutral;
using UnityEngine;
using SuperNewRoles.CustomOptions.Categories;
using SuperNewRoles.Roles.Ability;

namespace SuperNewRoles.Modules;

public enum WinType
{
    // クルーとかの普通のやつ
    Default,
    // 単独勝利
    SingleNeutral,
    // 乗っ取り勝利
    Hijackers,
    // ノー勝者
    NoWinner
}
public static class EndGamer
{/*
    public static void EndGame(GameOverReason reason)
    {
        List<ExPlayerControl> winners = new();
        Color32 color = Color.white;
        string upperText = null;
        switch (reason)
        {
            case GameOverReason.ImpostorsByKill:
            case GameOverReason.ImpostorsByVote:
            case GameOverReason.ImpostorsBySabotage:
                winners = ExPlayerControl.ExPlayerControls.Where(x => x.IsImpostorWinTeam()).ToList();
                color = Palette.ImpostorRed;
                upperText = "ImpostorWin";
                break;
            case GameOverReason.CrewmatesByTask:
            case GameOverReason.CrewmatesByVote:
                winners = ExPlayerControl.ExPlayerControls.Where(x => x.IsCrewmate()).ToList();
                color = Palette.CrewmateBlue;
                upperText = "CrewmateWin";
                break;
        }
        EndGame(reason, winners, color, upperText);
    }*/
    public static void EndGame(GameOverReason reason, WinType winType, HashSet<ExPlayerControl> winners, Color32 color, string upperText, string winText = null)
    {
        if (CustomOptionManager.DebugMode && CustomOptionManager.DebugModeNoGameEnd && reason != (GameOverReason)CustomGameOverReason.Haison)
        {
            Logger.Info("EndGame called but skipped due to DebugModeNoGameEnd. reason: " + reason);
            return;
        }
        HashSet<string> addWinners = new();

        // サボタージュ勝ちの時はインポスター以外死んだ判定で判定していく
        if (reason == GameOverReason.ImpostorsBySabotage)
        {
            foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
            {
                if (!player.IsImpostorWinTeam())
                    player.Data.IsDead = true;
            }
        }

        if (winType != WinType.NoWinner)
        {
            if (winType != WinType.SingleNeutral && reason != (GameOverReason)CustomGameOverReason.LoversWin)
                UpdateHijackers(ref reason, ref winners, ref color, ref upperText, ref winText, ref winType);
            // 独自単独勝利とは同時勝利できない
            UpdateAdditionalWinners(reason, ref winners, out addWinners, ref winText, winType == WinType.SingleNeutral);
        }
        Logger.Info("----------- Finished EndGame Start -----------");
        Logger.Info("reason: " + reason);
        Logger.Info("winners: " + winners.Count);
        Logger.Info("color: " + color);
        Logger.Info("upperText: " + upperText);
        Logger.Info("winText: " + winText);
        Logger.Info("----------- Finished EndGame End -----------");
        RpcSyncAlive(ExPlayerControl.ExPlayerControls.ToDictionary(x => x.PlayerId, x => x.IsDead()));
        string resolvedWinText = winText;
        // 単独勝利の場合、三人称単数になるので「wins」にする
        if (winType == WinType.SingleNeutral
            && reason != (GameOverReason)CustomGameOverReason.LoversWin
            && (string.IsNullOrEmpty(resolvedWinText) || resolvedWinText == "WinText"))
        {
            resolvedWinText = "SingleNeutralWinText";
        }
        resolvedWinText ??= "WinText";
        EndGameManagerSetUpPatch.RpcEndGameWithCondition(reason, winners.Select(x => x.PlayerId).ToList(), upperText ?? reason.ToString(), addWinners.Select(x => x.ToString()).ToHashSet().ToList(), color, false, resolvedWinText);
    }
    public static void RpcHaison()
    {
        EndGameManagerSetUpPatch.RpcEndGameWithCondition((GameOverReason)CustomGameOverReason.Haison, ExPlayerControl.ExPlayerControls.Select(x => x.PlayerId).ToList(), "廃 of the 村", [], Color.white, false, "WinText");
    }
    [CustomRPC]
    public static void RpcSyncAlive(Dictionary<byte, bool> dead)
    {
        foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
        {
            if (dead.TryGetValue(player.PlayerId, out bool isDead))
                player.Data.IsDead = isDead;
        }
    }
    [CustomRPC]
    public static void RpcEndGameWithWinner(CustomGameOverReason reason, WinType winType, ExPlayerControl[] winners, Color32 color, string upperText, string winText = "")
    {
        if (CustomOptionManager.DebugMode && CustomOptionManager.DebugModeNoGameEnd)
            return;
        ShipStatus.Instance.enabled = false;
        if (!AmongUsClient.Instance.AmHost) return;
        EndGame((GameOverReason)reason, winType, winners.ToHashSet(), color, upperText, string.IsNullOrEmpty(winText) || winText == "" ? null : winText);
    }
    [CustomRPC]
    public static void RpcEndGameImpostorWin()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        EndGameImpostorWin();
    }
    public static void EndGameImpostorWin()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        EndGame(GameOverReason.ImpostorsByKill, WinType.Default, ExPlayerControl.ExPlayerControls.Where(x => x.IsImpostorWinTeam()).ToHashSet(), Palette.ImpostorRed, "ImpostorWin");
    }

    private static void UpdateHijackers(ref GameOverReason reason, ref HashSet<ExPlayerControl> winners, ref Color32 color, ref string upperText, ref string winText, ref WinType winType)
    {
        if (GameSettingOptions.DisableHijackTaskWin && reason == GameOverReason.CrewmatesByTask) return;

        // タスカー（RoleId.Tasker）のタスク勝利は固定仕様として常に乗っ取り不可。
        // Tasker.OnTaskComplete 側で CustomGameOverReason.TaskerWin を使って終了させており、
        // ここでそれを検知して UpdateHijackers 自体をスキップする。
        if (reason == (GameOverReason)CustomGameOverReason.TaskerWin) return;

        // 三匹の仔豚勝利（優先度最高: Hijackers）
        // - チーム全員が生存していれば勝利
        // - そうでなくても、生存キラーが全滅していれば勝利
        // - 同時勝利は禁止（成立したら即 return）
        foreach (var team in TheThreeLittlePigs.Teams)
        {
            if (team == null || team.Count != 3) continue;
            var members = team.Select(id => ExPlayerControl.ById(id)).Where(p => p != null && TheThreeLittlePigs.IsLittlePig(p)).ToList();
            if (members.Count != 3) continue;

            bool allAlive = members.All(p => p.IsAlive());
            if (!members.Any(p => p.IsAlive())) continue;

            bool allKillerDead = ExPlayerControl.ExPlayerControls
                .Where(p => p != null && p.IsAlive())
                .All(p => !p.IsNonCrewKiller() && !p.IsJackalTeam());

            if (allAlive || allKillerDead)
            {
                reason = (GameOverReason)CustomGameOverReason.TheThreeLittlePigsWin;
                winners = members.ToHashSet();
                color = TheThreeLittlePigs.Instance.RoleColor;
                upperText = "TheThreeLittlePigs";
                winText = null;
                winType = WinType.Hijackers;
                return;
            }
        }

        // 条件付き生存横取り勝利（モイラ / フランケンシュタイン、優先度高）
        // 互いに同時勝利可能。どちらか成立したら return（単純生存横取りより優先）。
        {
            var conditionalWinners = new HashSet<ExPlayerControl>();
            bool matched = false;

            foreach (var player in ExPlayerControl.ExPlayerControls)
            {
                if (player.Role != RoleId.Moira || player.IsDead()) continue;
                if (!player.TryGetAbility<MoiraMeetingAbility>(out var a) || a.HasCount) continue;
                conditionalWinners.Add(player);
                reason = (GameOverReason)CustomGameOverReason.MoiraWin;
                color = Moira.Instance.RoleColor; upperText = "Moira"; winText = null;
                matched = true;
            }
            foreach (var player in ExPlayerControl.ExPlayerControls)
            {
                if (player.Role != RoleId.Frankenstein || player.IsDead()) continue;
                if (!player.TryGetAbility<FrankensteinAbility>(out var a) || a.RemainingKillsToWin > 0) continue;
                conditionalWinners.Add(player);
                reason = (GameOverReason)CustomGameOverReason.FrankensteinWin;
                color = Frankenstein.Instance.RoleColor; upperText = "Frankenstein"; winText = null;
                matched = true;
            }

            if (matched)
            {
                winners = conditionalWinners;
                winType = WinType.SingleNeutral;
                return;
            }
        }

        // 単純生存横取り勝利（神 / マグロ / 陰陽師 / スペランカー、優先度低）
        // 神を除き同時勝利可能。いずれか成立しても return しない（追加勝利へ続行）。
        {
            bool matched = false;

            // 神：マグロ/陰陽師/スペランカーが全員死亡していないと勝てない
            bool otherAlive = ExPlayerControl.ExPlayerControls.Any(p =>
                p.IsAlive() && (p.Role == RoleId.Tuna || p.Role == RoleId.OrientalShaman || p.Role == RoleId.Spelunker));
            if (!otherAlive)
                matched |= TryAddHijackWinner(winners, RoleId.God, p => p.IsAlive() && (!God.GodNeededTask || p.IsTaskComplete()),
                    CustomGameOverReason.GodWin, God.Instance.RoleColor, "God", "GodDescends", ref reason, ref color, ref upperText, ref winText);

            if (Tuna.EnableTunaSoloWin)
                matched |= TryAddHijackWinner(winners, RoleId.Tuna, p => p.IsAlive(),
                    CustomGameOverReason.TunaWin, Tuna.Instance.RoleColor, "Tuna", null, ref reason, ref color, ref upperText, ref winText);

            // 陰陽師は式神も同時勝利するため専用処理のまま残す
            foreach (var player in ExPlayerControl.ExPlayerControls)
            {
                if (player.Role != RoleId.OrientalShaman || player.IsDead()) continue;
                if (OrientalShaman.OrientalShamanNeededTaskComplete && !player.IsTaskComplete()) continue;
                if (!player.TryGetAbility<OrientalShamanAbility>(out var a)) continue;
                winners.Add(player);
                if (a._servant?.Player != null) winners.Add(a._servant.Player);
                color = OrientalShaman.Instance.RoleColor; upperText = "OrientalShaman"; winText = null;
                matched = true;
                break;
            }

            if (!Spelunker.SpelunkerIsAdditionalWin)
                matched |= TryAddHijackWinner(winners, RoleId.Spelunker, p => p.IsAlive(),
                    CustomGameOverReason.SpelunkerWin, Spelunker.Instance.RoleColor, "Spelunker", null, ref reason, ref color, ref upperText, ref winText);

            if (matched) winType = WinType.Hijackers;
        }
    }

    // 単純生存横取り勝利用の共通ヘルパー。
    // 条件を満たすプレイヤーを winners に「追加」する（上書きしない = 同時勝利対応）。
    // 表示用の reason/color/upperText/winText は複数同時成立時、最後にマッチしたもので上書きされる。
    private static bool TryAddHijackWinner(
        HashSet<ExPlayerControl> winners, RoleId roleId, System.Func<ExPlayerControl, bool> condition,
        CustomGameOverReason customReason, Color32 winColor, string text, string winTextValue,
        ref GameOverReason reason, ref Color32 color, ref string upperText, ref string winText)
    {
        bool matched = false;
        foreach (var player in ExPlayerControl.ExPlayerControls.Where(p => p.Role == roleId && condition(p)))
        {
            winners.Add(player);
            reason = (GameOverReason)customReason;
            color = winColor; upperText = text; winText = winTextValue;
            matched = true;
        }
        return matched;
    }

    private static void UpdateAdditionalWinners(GameOverReason reason, ref HashSet<ExPlayerControl> winners, out HashSet<string> addWinners, ref string winText, bool cantWinSixAdditionalWinners)
    {
        addWinners = new();
        // 三匹の仔豚勝利は同時勝利しない（旧仕様に合わせる）
        if (reason == (GameOverReason)CustomGameOverReason.TheThreeLittlePigsWin)
            return;

        // ラバーズじゃない人がいる場合
        if (Lovers.LoversWinType == LoversWinType.Single && winners.Any(x => !x.IsLovers()))
        {
            winners.RemoveWhere(x => x.IsLovers());
        }
        if (!cantWinSixAdditionalWinners)
        {
            AddAdditionalWinner(winners, addWinners, RoleId.Opportunist, p => p.IsAlive());
            AddAdditionalWinner(winners, addWinners, RoleId.Tuna,
                p => p.IsAlive() && !Tuna.EnableTunaSoloWin);
            AddAdditionalWinner(winners, addWinners, RoleId.Spelunker,
                p => p.IsAlive() && Spelunker.SpelunkerIsAdditionalWin);
        }
        foreach (ExPlayerControl winner in winners.ToArray())
        {
            if (Lovers.LoversWinType == LoversWinType.Shared && winner.IsLovers())
            {
                foreach (LoversAbility lovers in winner.GetAbility<LoversAbility>()?.couple?.lovers)
                {
                    if (lovers.Player.IsDead()) continue;
                    winners.Add(lovers.Player);
                }
                List<ExPlayerControl> creatorCupid = getCreatorCupid(winner);
                foreach (ExPlayerControl cupid in creatorCupid)
                {
                    winners.Add(cupid);
                    addWinners.Add(cupid.Role.ToString());
                }
            }
        }
        if (reason == (GameOverReason)CustomGameOverReason.LoversWin)
        {
            List<ExPlayerControl> creatorCupid = getCreatorCupid(winners.First());
            foreach (ExPlayerControl cupid in creatorCupid)
            {
                winners.Add(cupid);
                addWinners.Add(cupid.Role.ToString());
            }
        }
        // フリーター：就職先が winners に含まれていて、かつ就職先が死亡中（キル死亡）でなければ同時勝利
        foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
        {
            if (player.Role != RoleId.PartTimer) continue;
            PartTimerAbility partTimerAbility = player.GetAbility<PartTimerAbility>();
            if (partTimerAbility == null || partTimerAbility._employer == null) continue;
            if (!winners.Contains(partTimerAbility._employer)) continue;

            if (partTimerAbility._data.needAliveToWin && player.IsDead()) continue;
            winners.Add(player);
            addWinners.Add(player.Role.ToString());
        }
        if (addWinners.Count != 0)
        {
            winText = null;
        }
    }

    private static void AddAdditionalWinner(HashSet<ExPlayerControl> winners, HashSet<string> addWinners,
        RoleId roleId, System.Func<ExPlayerControl, bool> condition)
    {
        foreach (var player in ExPlayerControl.ExPlayerControls.Where(p => p.Role == roleId && condition(p)))
        {
            winners.Add(player);
            addWinners.Add(roleId.ToString());
        }
    }

    // Helper
    private static List<ExPlayerControl> getCreatorCupid(ExPlayerControl winner)
    {
        return ExPlayerControl.ExPlayerControls.Where(x =>
                x.Role == RoleId.Cupid &&
                x.TryGetAbility<CupidAbility>(out var cupidAbility) &&
                (cupidAbility.Lovers1 == winner.PlayerId || cupidAbility.Lovers2 == winner.PlayerId)).ToList();
    }
}
