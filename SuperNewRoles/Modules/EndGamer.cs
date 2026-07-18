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
        HashSet<string> hijackAddWinners = new();

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
                UpdateHijackers(ref reason, ref winners, ref color, ref upperText, ref winText, ref winType, hijackAddWinners);
            // 独自単独勝利とは同時勝利できない
            UpdateAdditionalWinners(reason, ref winners, out addWinners, ref winText, winType == WinType.SingleNeutral);
            // Hijackers勝利で複数役職が同時成立した場合の & 表示用リストをマージする
            foreach (var text in hijackAddWinners)
                addWinners.Add(text);
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
        EndGameManagerSetUpPatch.RpcEndGameWithCondition((GameOverReason)CustomGameOverReason.Haison, ExPlayerControl.ExPlayerControls.Select(x => x.PlayerId).ToList(), "廃 of the 村", [], Color.white, true);
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
    // 単純生存横取り勝利（神/マグロ/陰陽師/スペランカー）の同時勝利用ヘルパー。
    // hasHijackWon を使って「乗っ取りが今回初めて成立したか」を判定し、
    // 初回はそれまでの upperText（元の勝利理由等）を破棄して置き換える。
    // これをしないと、UpdateHijackers 呼び出し前に upperText に入っていた
    // 元の勝利理由（例: "ImpostorWin"）が「既に乗っ取り済みの役職」と誤認され、
    // & 表示に混入してしまう（例: "ImpostorWin & Spelunker"）。
    private static void AddHijackUpperText(ref string upperText, ref bool hasHijackWon, HashSet<string> hijackAddWinners, string text)
    {
        if (!hasHijackWon)
        {
            upperText = text;
            hasHijackWon = true;
        }
        else if (upperText != text)
        {
            hijackAddWinners.Add(text);
        }
    }

    private static void UpdateHijackers(ref GameOverReason reason, ref HashSet<ExPlayerControl> winners, ref Color32 color, ref string upperText, ref string winText, ref WinType winType, HashSet<string> hijackAddWinners)
    {
        if (GameSettingOptions.DisableHijackTaskWin && reason == GameOverReason.CrewmatesByTask) return;

        // タスカー（RoleId.Tasker）のタスク勝利は固定仕様として常に乗っ取り不可・乗っ取り側にもならない。
        // Tasker.OnTaskComplete 側で CustomGameOverReason.TaskerWin を使って単独で終了させており、
        // ここで検知した場合は UpdateHijackers 自体を完全にスキップする（乗っ取られもしないし、乗っ取りもしない）。
        if (reason == (GameOverReason)CustomGameOverReason.TaskerWin) return;

        // 乗っ取り役職のうち誰か一人でも条件を満たしたら true になる。
        // upperText を初回だけ強制的にリセットするために使用する。
        bool hasHijackWon = false;

        // 三匹の仔豚勝利（優先度: 最高・分岐なし）
        // 旧仕様:
        // - チーム全員が生存していれば勝利
        // - そうでなくても、生存キラー(インポスター/ジャッカル/その他キラー)が全滅していれば勝利
        // - 同時勝利は禁止
        foreach (var team in Roles.Neutral.TheThreeLittlePigs.Teams)
        {
            if (team == null || team.Count != 3) continue;
            var members = team.Select(id => ExPlayerControl.ById(id)).Where(p => p != null && Roles.Neutral.TheThreeLittlePigs.IsLittlePig(p)).ToList();
            if (members.Count != 3) continue;

            bool allAlive = members.All(p => p.IsAlive());
            bool anyAlive = members.Any(p => p.IsAlive());
            if (!anyAlive) continue;

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

        // === 神（優先度: 低。後で他役職に上書きされる対象）===
        foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
        {
            if (player.Role == RoleId.God && player.IsAlive())
            {
                if (God.GodNeededTask && !player.IsTaskComplete()) continue;

                winners = new HashSet<ExPlayerControl> { player };
                reason = (GameOverReason)CustomGameOverReason.GodWin;
                color = God.Instance.RoleColor;
                upperText = "God";
                hasHijackWon = true;
                winText = "GodDescends";
                winType = WinType.Hijackers;
            }
        }

        // === マグロ（優先度: 中。神を上書き）===
        if (Tuna.EnableTunaSoloWin)
        {
            foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
            {
                if (player.Role == RoleId.Tuna && player.IsAlive())
                {
                    winners = new HashSet<ExPlayerControl> { player };
                    reason = (GameOverReason)CustomGameOverReason.TunaWin;
                    AddHijackUpperText(ref upperText, ref hasHijackWon, hijackAddWinners, "Tuna");
                    color = Tuna.Instance.RoleColor;
                    winText = null;
                    winType = WinType.Hijackers;
                }
            }
        }

        // === 陰陽師（優先度: 中。神を上書き）===
        foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
        {
            if (player.Role != RoleId.OrientalShaman || player.IsDead()) continue;
            if (OrientalShaman.OrientalShamanNeededTaskComplete && !player.IsTaskComplete())
                continue;
            if (player.TryGetAbility<OrientalShamanAbility>(out var orientalShamanAbility))
            {
                winners = new HashSet<ExPlayerControl> { player };
                if (orientalShamanAbility._servant?.Player != null)
                    winners.Add(orientalShamanAbility._servant.Player);
                // CustomGameOverReasonにOrientalShamanWinは存在しないため、
                // reasonは変更せず元の勝利理由(誰かがキル/追放された理由)のまま据え置く
                AddHijackUpperText(ref upperText, ref hasHijackWon, hijackAddWinners, "OrientalShaman");
                color = OrientalShaman.Instance.RoleColor;
                winText = null;
                winType = WinType.Hijackers;
                break;
            }
        }

        // === スペランカー（優先度: 中。神を上書き。Lovers優先）===
        if (!Spelunker.SpelunkerIsAdditionalWin)
        {
            foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
            {
                if (player.Role == RoleId.Spelunker && player.IsAlive())
                {
                    winners = new HashSet<ExPlayerControl> { player };
                    reason = (GameOverReason)CustomGameOverReason.SpelunkerWin;
                    AddHijackUpperText(ref upperText, ref hasHijackWon, hijackAddWinners, "Spelunker");
                    color = Spelunker.Instance.RoleColor;
                    winText = null;
                    winType = WinType.Hijackers;
                }
            }
        }

        // === Moira（優先度: 高。神/マグロ/陰陽師/スペランカーを上書き。Frankensteinとは同時勝利可）===
        foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
        {
            if (player.Role != RoleId.Moira || player.IsDead()) continue;
            if (!player.TryGetAbility<MoiraMeetingAbility>(out var moiraAbility) || moiraAbility.HasCount) continue;

            winners = new HashSet<ExPlayerControl> { player };
            reason = (GameOverReason)CustomGameOverReason.MoiraWin;
            color = Moira.Instance.RoleColor;
            upperText = "Moira";
            winText = null;
            winType = WinType.SingleNeutral;
            break; // Moiraが確定したら後続判定へ
        }

        // === Frankenstein（優先度: 高。Moiraが不成立の場合は神系を上書き。Moiraが成立なら同時勝利として&表示に追加）===
        foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
        {
            if (player.Role != RoleId.Frankenstein || player.IsDead()) continue;
            if (!player.TryGetAbility<FrankensteinAbility>(out var frankensteinAbility) || frankensteinAbility.RemainingKillsToWin > 0) continue;

            // Moiraが既に設定されているか判定
            if (reason == (GameOverReason)CustomGameOverReason.MoiraWin)
            {
                // Moiraとの同時勝利。upperTextは"Moira"のまま残し、
                // Frankensteinはhijackと同様に&表示用リストへ追加する。
                // (これをしないと「Moira」としか表示されずFrankensteinが画面上消えるバグになる)
                winners.Add(player);
                hijackAddWinners.Add("Frankenstein");
            }
            else
            {
                // Moiraが不成立 → 神系を上書き
                winners = new HashSet<ExPlayerControl> { player };
                upperText = "Frankenstein";
            }
            reason = (GameOverReason)CustomGameOverReason.FrankensteinWin;
            color = Frankenstein.Instance.RoleColor;
            winText = null;
            winType = WinType.SingleNeutral;
            return;
        }
    }
    private static void UpdateAdditionalWinners(GameOverReason reason, ref HashSet<ExPlayerControl> winners, out HashSet<string> addWinners, ref string winText, bool cantWinSixAdditionalWinners)
    {
        addWinners = new();
        // 三匹の仔豚勝利は同時勝利しない（旧仕様に合わせる）
        if (reason == (GameOverReason)CustomGameOverReason.TheThreeLittlePigsWin)
        {
            return;
        }
        // ラバーズじゃない人がいる場合
        if (Lovers.LoversWinType == LoversWinType.Single && winners.Any(x => !x.IsLovers()))
        {
            winners.RemoveWhere(x => x.IsLovers());
        }
        if (!cantWinSixAdditionalWinners)
        {
            foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
            {
                switch (player.Role)
                {
                    case RoleId.Opportunist:
                        if (player.IsAlive())
                        {
                            winners.Add(player);
                            addWinners.Add(player.Role.ToString());
                        }
                        break;
                    case RoleId.Tuna when !Tuna.EnableTunaSoloWin:
                        if (player.IsAlive())
                        {
                            winners.Add(player);
                            addWinners.Add(player.Role.ToString());
                        }
                        break;
                    case RoleId.Spelunker when Spelunker.SpelunkerIsAdditionalWin:
                        if (player.IsAlive())
                        {
                            winners.Add(player);
                            addWinners.Add(player.Role.ToString());
                        }
                        break;
                }
            }
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
        foreach (ExPlayerControl player in ExPlayerControl.ExPlayerControls)
        {
            if (player.Role == RoleId.PartTimer)
            {
                PartTimerAbility partTimerAbility = player.GetAbility<PartTimerAbility>();
                if (partTimerAbility != null && partTimerAbility._employer != null && winners.Contains(partTimerAbility._employer))
                {
                    // 生存勝利設定がONで死んでいる場合は勝利しない
                    if (partTimerAbility._data.needAliveToWin && player.IsDead()) continue;
                    winners.Add(player);
                    addWinners.Add(player.Role.ToString());
                }
            }
        }
        if (addWinners.Count != 0)
        {
            winText = null;
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
