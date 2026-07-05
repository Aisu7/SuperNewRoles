using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using InnerNet;
using SuperNewRoles.Modules;
using SuperNewRoles.CustomOptions.Categories;
using UnityEngine;

namespace SuperNewRoles.Patches;

public static class PlayerKickHelper
{
    private static bool IsPcPlatform(Platforms platform) =>
        platform == Platforms.StandaloneSteamPC ||
        platform == Platforms.StandaloneEpicPC ||
        platform == Platforms.StandaloneWin10;

    private static bool IsAndroidPlatform(Platforms platform) =>
        platform == Platforms.Android ||
        (int)platform == 112 || // Starlight（Android版AmongUsランチャー）が送るプラットフォーム値
        platform.ToString().IndexOf("Android", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsUnclassifiedPlatform(Platforms platform)
    {
        string platformName = platform.ToString();
        return string.IsNullOrWhiteSpace(platformName) ||
               int.TryParse(platformName, out _) ||
               platformName.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0 ||
               platformName.IndexOf("Invalid", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsOtherKnownPlatform(Platforms platform) =>
        !IsPcPlatform(platform) &&
        !IsAndroidPlatform(platform) &&
        !IsUnclassifiedPlatform(platform);

    public static bool KickPlayerIfNeeded(ClientData client, bool kickPC, bool kickAndroid, bool kickOther)
    {
        if (client == null) return false;
        if (AmongUsClient.Instance.ClientId == client.Id) return false;

        // PlatformData が null かどうかも含めてログに記録する
        // Starlight など特殊な環境では PlatformData 自体が null で届く場合がある
        if (client.PlatformData == null)
        {
            SuperNewRoles.Logger.Info($"プレイヤー {client.PlayerName} のPlatformData: null（プラットフォーム判定不可）");
            return false;
        }

        var pf = client.PlatformData.Platform;

        // プラットフォーム値をログに出力する
        // Starlight など未知のプラットフォームが何の値を送ってくるかを把握するために使用する
        // ログを見てAndroidと判定されるべき値が抜けていたらコードに追加する
        SuperNewRoles.Logger.Info($"プレイヤー {client.PlayerName} のプラットフォーム: {pf} (数値: {(int)pf})");

        if (kickPC && IsPcPlatform(pf))
        {
            AmongUsClient.Instance.KickPlayer(client.Id, false);
            SuperNewRoles.Logger.Info($"PCプレイヤー {client.PlayerName} をキックしました");
            return true;
        }
        if (kickAndroid && IsAndroidPlatform(pf))
        {
            AmongUsClient.Instance.KickPlayer(client.Id, false);
            SuperNewRoles.Logger.Info($"Androidプレイヤー {client.PlayerName} をキックしました");
            return true;
        }
        if (kickOther && IsOtherKnownPlatform(pf))
        {
            AmongUsClient.Instance.KickPlayer(client.Id, false);
            SuperNewRoles.Logger.Info($"その他プラットフォームのプレイヤー {client.PlayerName} をキックしました");
            return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
class BanParticularPlayerPatch
{
    // 二重実行防止：現在チェック待機中のクライアントIDを管理する
    private static readonly HashSet<int> _pendingCheckClientIds = new();

    public static void Postfix([HarmonyArgument(0)] ClientData client)
    {
        SuperNewRoles.Logger.Info($"{client.PlayerName}(ClientID:{client.Id})が参加");

        if (!AmongUsClient.Instance.AmHost)
            return;

        // LocalPlayer が null の間はロビーがまだ初期化されていない
        // （「ホストを待っています」表示中など）この段階でのキック・BANは誤動作の原因になる
        if (PlayerControl.LocalPlayer == null)
            return;

        // 自分自身（ホスト）は処理しない
        if (AmongUsClient.Instance.ClientId == client.Id)
            return;

        // ロビー（GameStates.Joined）以外のタイミングで呼ばれた場合はスキップ
        // ゲーム終了→ロビー復帰時などの遷移中は FriendCode 等がまだ揃っていないため
        if (AmongUsClient.Instance.GameState != InnerNetClient.GameStates.Joined)
            return;

        // プラットフォームチェック
        if (GeneralSettingOptions.KickPlatformPlayers && PlayerKickHelper.KickPlayerIfNeeded(client,
                                                   GeneralSettingOptions.KickPCPlayers,
                                                   GeneralSettingOptions.KickAndroidPlayers,
                                                   GeneralSettingOptions.KickOtherPlayers))
            return;

        if (!GeneralSettingOptions.BanNoFriendCodePlayers)
            return;

        // SNR カスタムサーバーでは EOS 認証が通らず FriendCode が全員空になるため、
        // この設定は機能しない（全員BANされてしまう）。カスタムサーバー使用中はスキップする。
        if (ModHelpers.IsCustomServer())
            return;

        // FriendCode が既に届いている場合は即座に判定する（コルーチン不要）
        if (!string.IsNullOrEmpty(client.FriendCode))
        {
            if (!client.FriendCode.Contains("#"))
            {
                AmongUsClient.Instance.KickPlayer(client.Id, true);
                SuperNewRoles.Logger.Info($"フレンドコードを持っていないプレイヤー {client.PlayerName} をBANしました");
            }
            return;
        }

        // FriendCode がまだ届いていない場合のみコルーチンで待機してから判定する
        // 同じクライアントに対して既に待機中の場合は追加起動しない（二重実行防止）
        if (!_pendingCheckClientIds.Contains(client.Id))
        {
            AmongUsClient.Instance.StartCoroutine(
                CheckFriendCodeDelayed(client).WrapToIl2Cpp()
            );
        }
    }

    // FriendCode チェックを最大0.5秒待機してから行う（時間ベースのリトライ戦略）
    //
    // 【なぜ Time.deltaTime を使うか】
    //   yield return null は「1フレーム待つ」命令のため、
    //   60fps 環境では 1フレーム = 約16ms、120fps 環境では 約8ms と変わる。
    //   フレーム数で待機すると fps によってタイムアウト時間がズレるため、
    //   Time.deltaTime（前フレームの経過秒数）を積算し、実時間で計測する。
    //
    // 【なぜ 0.5 秒か】
    //   FriendCode は参加直後の UDP パケットで届くが、
    //   国内サーバーで 50〜100ms、海外サーバーで 150〜300ms 程度の遅延がある。
    //   安全マージンを含め 500ms あれば、不安定な接続でも正規プレイヤーを誤BANしない。
    private static System.Collections.IEnumerator CheckFriendCodeDelayed(ClientData client)
    {
        int clientId = client.Id;
        _pendingCheckClientIds.Add(clientId);

        // FriendCode が届くまで最大 0.5 秒待機する（Time.deltaTime による実時間計測）
        const float timeoutSeconds = 0.5f;
        float elapsed = 0f;
        while (elapsed < timeoutSeconds && string.IsNullOrEmpty(client.FriendCode))
        {
            yield return null;
            elapsed += Time.deltaTime;
        }

        // 待機終了後の状態再チェック
        // ・ホスト権限を失っていたらスキップ
        // ・ゲームが開始されていたらスキップ（ロビー以外は判定しない）
        // ・自分自身（念のため再確認）はスキップ
        if (client == null ||
            !AmongUsClient.Instance.AmHost ||
            AmongUsClient.Instance.ClientId == clientId ||
            AmongUsClient.Instance.GameState != InnerNetClient.GameStates.Joined)
        {
            _pendingCheckClientIds.Remove(clientId);
            yield break;
        }

        // BAN判定：0.5秒待っても FriendCode が空、または "#" を含まない場合はBAN
        if (string.IsNullOrEmpty(client.FriendCode) || !client.FriendCode.Contains("#"))
        {
            AmongUsClient.Instance.KickPlayer(clientId, true);
            SuperNewRoles.Logger.Info($"フレンドコードを持っていないプレイヤー {client.PlayerName} をBANしました");
        }

        _pendingCheckClientIds.Remove(clientId);
    }
}

[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
class BanPlatformPlayersOnGameStartPatch
{
    private static bool lastKickPlatformPlayersEnabled;
    private static bool lastKickPCPlayers;
    private static bool lastKickAndroidPlayers;
    private static bool lastKickOtherPlayers;

    public static void Postfix(GameStartManager __instance)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        bool currentKickPlatformPlayersEnabled = GeneralSettingOptions.KickPlatformPlayers;
        bool currentKickPCPlayers = GeneralSettingOptions.KickPCPlayers;
        bool currentKickAndroidPlayers = GeneralSettingOptions.KickAndroidPlayers;
        bool currentKickOtherPlayers = GeneralSettingOptions.KickOtherPlayers;

        if (currentKickPlatformPlayersEnabled != lastKickPlatformPlayersEnabled ||
            currentKickPCPlayers != lastKickPCPlayers ||
            currentKickAndroidPlayers != lastKickAndroidPlayers ||
            currentKickOtherPlayers != lastKickOtherPlayers)
        {
            lastKickPlatformPlayersEnabled = currentKickPlatformPlayersEnabled;
            lastKickPCPlayers = currentKickPCPlayers;
            lastKickAndroidPlayers = currentKickAndroidPlayers;
            lastKickOtherPlayers = currentKickOtherPlayers;

            if (!currentKickPlatformPlayersEnabled) return;

            var clients = AmongUsClient.Instance.allClients;

            foreach (var client in clients)
            {
                PlayerKickHelper.KickPlayerIfNeeded(client, currentKickPCPlayers, currentKickAndroidPlayers, currentKickOtherPlayers);
            }
        }
    }
}
