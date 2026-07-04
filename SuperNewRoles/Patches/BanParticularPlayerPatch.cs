using System;
using HarmonyLib;
using InnerNet;
using SuperNewRoles.Modules;
using SuperNewRoles.CustomOptions.Categories;

namespace SuperNewRoles.Patches;

public static class PlayerKickHelper
{
    private static bool IsPcPlatform(Platforms platform) =>
        platform == Platforms.StandaloneSteamPC ||
        platform == Platforms.StandaloneEpicPC ||
        platform == Platforms.StandaloneWin10;

    private static bool IsAndroidPlatform(Platforms platform) =>
        platform == Platforms.Android ||
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
        if (client == null || client.PlatformData == null) return false;
        if (AmongUsClient.Instance.ClientId == client.Id) return false;

        var pf = client.PlatformData.Platform;

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
    private static readonly System.Collections.Generic.HashSet<int> _pendingCheckClientIds = new();

    public static void Postfix([HarmonyArgument(0)] ClientData client)
    {
        SuperNewRoles.Logger.Info($"{client.PlayerName}(ClientID:{client.Id})が参加");

        if (!AmongUsClient.Instance.AmHost)
            return;

        // 自分自身（ホスト）は処理しない
        if (AmongUsClient.Instance.ClientId == client.Id)
            return;

        // ロビー（GameStates.Joined）以外のタイミングで呼ばれた場合はスキップ
        if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined)
            return;

        // プラットフォームチェック
        if (GeneralSettingOptions.KickPlatformPlayers && PlayerKickHelper.KickPlayerIfNeeded(client,
                                                   GeneralSettingOptions.KickPCPlayers,
                                                   GeneralSettingOptions.KickAndroidPlayers,
                                                   GeneralSettingOptions.KickOtherPlayers))
            return;

        // フレンドコードのチェック
        // コルーチン（WrapToIl2Cpp）はSNRコードベースでは使用しないため LateTask で代替する。
        // FriendCode は参加直後には届いていない場合があるため 0.5 秒後に判定する。
        if (GeneralSettingOptions.BanNoFriendCodePlayers && !_pendingCheckClientIds.Contains(client.Id))
        {
            int clientId = client.Id;
            _pendingCheckClientIds.Add(clientId);

            new LateTask(() =>
            {
                _pendingCheckClientIds.Remove(clientId);

                // 待機後の状態再チェック
                if (!AmongUsClient.Instance.AmHost) return;
                if (AmongUsClient.Instance.ClientId == clientId) return;
                if (AmongUsClient.Instance.GameState != InnerNet.InnerNetClient.GameStates.Joined) return;

                // client オブジェクトが既に無効になっている可能性があるため
                // allClients から再取得して最新の FriendCode を確認する
                ClientData latestClient = null;
                foreach (var c in AmongUsClient.Instance.allClients)
                {
                    if (c.Id == clientId)
                    {
                        latestClient = c;
                        break;
                    }
                }

                if (latestClient == null) return;

                if (string.IsNullOrEmpty(latestClient.FriendCode) || !latestClient.FriendCode.Contains("#"))
                {
                    AmongUsClient.Instance.KickPlayer(clientId, true);
                    SuperNewRoles.Logger.Info($"フレンドコードを持っていないプレイヤー {latestClient.PlayerName} をBANしました");
                }
            }, 0.5f, "BanNoFriendCodeCheck");
        }
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
