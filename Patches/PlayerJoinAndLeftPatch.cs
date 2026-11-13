using System;
using System.Collections.Generic;
using AmongUs.Data;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;

using TownOfHostL.Modules;
using TownOfHostL.Roles;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Neutral;
using static TownOfHostL.Translator;

namespace TownOfHostL
{
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    class OnGameJoinedPatch
    {
        public static void Postfix(AmongUsClient __instance)
        {
            try
            {
                // 設定は起動時にメインスレッドで読み込み終わっている。ここで待っても (メインスレッドを止めるだけで)
                // 読み込みは進まないので、終わっていなければ記録だけ残す
                if (!Options.IsLoaded) Logger.Warn("設定の読み込みが完了していない状態で部屋に入りました", "OnGameJoined");

                Logger.Info($"{__instance?.GameId}に参加 (公式サーバー: {Modules.Net.OfficialServer.IsActive})", "OnGameJoined");
                Main.playerVersion = new Dictionary<byte, PlayerVersion>();
                try { AutoRehost.OnGameJoined(); } catch (Exception ex) { Logger.Warn($"AutoRehost.OnGameJoined failed: {ex.Message}", "OnGameJoined"); }
                OnPlayerLeftPatch.ResetHandled();
                try { RPC.RpcVersionCheck(); } catch { }
                try { if (SoundManager.Instance != null) SoundManager.Instance.ChangeAmbienceVolume(DataManager.Settings.Audio.AmbienceVolume); } catch { }

                ChatUpdatePatch.DoBlockChat = false;
                GameStates.InGame = false;
                Modules.ChatGuard.Reset();
                Modules.ChatPushOut.Reset();
                try { ErrorText.Instance.Clear(); } catch { }
                if (AmongUsClient.Instance.AmHost) //以下、ホストのみ実行
                {
                    if (Main.NormalOptions != null && Main.NormalOptions.KillCooldown == 0f)
                        Main.NormalOptions.KillCooldown = Main.LastKillCooldown.Value;

                    try { AURoleOptions.SetOpt(Main.NormalOptions.Cast<IGameOptions>()); } catch { }
                    if (AURoleOptions.ShapeshifterCooldown == 0f)
                        AURoleOptions.ShapeshifterCooldown = Main.LastShapeshifterCooldown.Value;

                    _ = new LateTask(() =>
                    {
                        try
                        {
                            if (Main.KillFlashAfterDeadByHost.Value && PlayerControl.LocalPlayer != null)
                            {
                                Main.KillFlashAfterDead.Add(PlayerControl.LocalPlayer.Data.PlayerName);
                            }
                        }
                        catch { }
                    }, 3f, "KillFlashAfterDeadByHost");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"OnGameJoinedPatch.Postfix で例外: {ex.Message}", "OnGameJoinedPatch");
                Logger.Exception(ex, "OnGameJoinedPatch");
            }
        }
    }
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.DisconnectInternal))]
    class DisconnectInternalPatch
    {
        public static void Prefix(InnerNetClient __instance, DisconnectReasons reason, string stringReason)
        {
            try
            {
                Logger.Info($"切断(理由:{reason}:{stringReason}, ping:{__instance?.Ping})", "Session");
                try { AutoRehost.OnDisconnect(__instance, reason); }
                catch (Exception ex) { Logger.Warn($"AutoRehost.OnDisconnect failed: {ex.Message}", "DisconnectInternalPatch"); }

                // 終了の送信が失敗しても役職の後始末は必ず行う
                try
                {
                    if (AmongUsClient.Instance.AmHost && GameStates.InGame)
                        GameManager.Instance.RpcEndGame(GameOverReason.ImpostorDisconnect, false);
                }
                catch (Exception ex)
                {
                    Logger.Warn($"RpcEndGame on disconnect failed: {ex.Message}", "DisconnectInternalPatch");
                }

                CustomRoleManager.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn($"DisconnectInternalPatch.Prefix failed: {ex.Message}", "DisconnectInternalPatch");
            }
        }
    }
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    class OnPlayerJoinedPatch
    {
        public static void Postfix(AmongUsClient __instance, [HarmonyArgument(0)] ClientData client)
        {
            try
            {
                if (client == null) return;
                Logger.Info($"{client.PlayerName}(ClientID:{client.Id}(HashedPUID:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(client.ProductUserId))).ToLowerInvariant()}))が参加", "Session");
                if (AmongUsClient.Instance.AmHost && client.FriendCode == "" && Options.KickPlayerFriendCodeNotExist.GetBool())
                {
                    AmongUsClient.Instance.KickPlayer(client.Id, false);
                    Logger.SendInGame(string.Format(GetString("Message.KickedByNoFriendCode"), client.PlayerName));
                    Logger.Info($"フレンドコードがないプレイヤー{client?.PlayerName}({client.ProductUserId})をキックしました。", "Kick");
                }
                if (DestroyableSingleton<FriendsListManager>.Instance.IsPlayerBlockedUsername(client.FriendCode) && AmongUsClient.Instance.AmHost)
                {
                    AmongUsClient.Instance.KickPlayer(client.Id, true);
                    Logger.Info($"ブロック済みのプレイヤー{client?.PlayerName}({client.FriendCode})({client.ProductUserId})をBANしました。", "BAN");
                }
                BanManager.CheckBanPlayer(client);
                BanManager.CheckDenyNamePlayer(client);
                JoinGuard.OnPlayerJoined(client);
                try { RPC.RpcVersionCheck(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warn($"OnPlayerJoinedPatch.Postfix で例外: {ex.Message}", "OnPlayerJoinedPatch");
                Logger.Exception(ex, "OnPlayerJoinedPatch");
            }
        }
    }
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    class OnPlayerLeftPatch
    {
        // 試合開始の読み込み待ちで本編が客を ClientTimeout で切ると、同じ接続の OnPlayerLeft が 2 回来る。
        // 2 回目は Character が外れた後なので、処理済みの接続は飛ばす。接続 ID は部屋の中で使い回されない。
        static readonly HashSet<int> handledClientIds = new();

        public static void ResetHandled() => handledClientIds.Clear();

        /// <summary>抜けた接続の PlayerId。Character が外れていても GameData の ClientId から引く。見つからなければ null。</summary>
        static byte? ResolvePlayerId(ClientData data)
        {
            if (data.Character != null) return data.Character.PlayerId;
            var gameData = GameData.Instance;
            if (gameData == null) return null;
            foreach (var info in gameData.AllPlayers)
            {
                if (info != null && info.ClientId == data.Id) return info.PlayerId;
            }
            return null;
        }

        static void Prefix([HarmonyArgument(0)] ClientData data)
        {
            try
            {
                if (data == null || data.Character == null) return;

                VentEnterTask.TaskRemove(data.Character.PlayerId);
            }
            catch (Exception ex)
            {
                Logger.Warn($"OnPlayerLeftPatch.Prefix failed: {ex.Message}", "OnPlayerLeftPatch");
            }
        }
        public static void Postfix(AmongUsClient __instance, [HarmonyArgument(0)] ClientData data, [HarmonyArgument(1)] DisconnectReasons reason)
        {
            bool isFailure = false;
            try
            {
                if (data == null)
                {
                    isFailure = true;
                    Logger.Warn("退出者の接続情報がnull", nameof(OnPlayerLeftPatch));
                }
                else if (!handledClientIds.Add(data.Id))
                {
                    Logger.Info($"{data.PlayerName}(ClientID:{data.Id})の切断は処理済み(理由:{reason})", "Session");
                }
                else if (ResolvePlayerId(data) is not byte playerId)
                {
                    // ロビーで、キャラクターが割り当たる前に抜けた接続。記録する状態が無い
                    if (AmongUsClient.Instance.IsGameStarted) isFailure = true;
                    Logger.Warn($"退出者のプレイヤーが見つからない(ClientID:{data.Id})", nameof(OnPlayerLeftPatch));
                }
                else
                {
                    // 試合の開始からイントロまでの間 (IsInGame がまだ false) に抜けた人も、試合中の離脱として扱う
                    if ((GameStates.IsInGame || AmongUsClient.Instance.IsGameStarted)
                        && PlayerState.GetByPlayerId(playerId) is PlayerState state)
                    {
                        var wasAlive = !state.IsDead;
                        if (wasAlive && state.SubRoles.Contains(CustomRoles.Lovers))
                        {
                            foreach (var lovers in Lovers.playersList.ToArray())
                            {
                                Lovers.playersList.Remove(lovers);
                                PlayerState.GetByPlayerId(lovers.PlayerId)?.RemoveSubRole(CustomRoles.Lovers);
                            }
                        }
                        if (wasAlive) GameLog.OnDisconnected(playerId);
                        if (state.DeathReason == CustomDeathReason.etc) //死因が設定されていなかったら
                        {
                            state.DeathReason = CustomDeathReason.Disconnected;
                            state.SetDead();
                        }
                        if (GameData.Instance != null && GameData.Instance.GetPlayerById(playerId) is NetworkedPlayerInfo info)
                            AntiBlackout.OnDisconnect(info);
                        PlayerGameOptionsSender.RemoveSender(playerId);
                        if (wasAlive) Modules.AutoAbort.OnAliveDisconnected();
                        if (AmongUsClient.Instance.AmHost && GameStates.IsInGame) CustomRoleManager.NotifyPlayerLeft(playerId);
                    }
                    Main.playerVersion.Remove(playerId);
                    AfkDetector.OnPlayerLeft(playerId);
                    Logger.Info($"{data.PlayerName}(ClientID:{data.Id})が切断(理由:{reason}, ping:{AmongUsClient.Instance.Ping})", "Session");
                }
            }
            catch (Exception e)
            {
                Logger.Warn("切断処理中に例外が発生", nameof(OnPlayerLeftPatch));
                Logger.Exception(e, nameof(OnPlayerLeftPatch));
                isFailure = true;
            }

            if (isFailure)
            {
                Logger.Warn($"正常に完了しなかった切断 - 名前:{(data == null || data.PlayerName == null ? "(不明)" : data.PlayerName)}, 理由:{reason}, ping:{AmongUsClient.Instance.Ping}", "Session");
                try { ErrorText.Instance.AddError(AmongUsClient.Instance.GameState is InnerNetClient.GameStates.Started ? ErrorCode.OnPlayerLeftPostfixFailedInGame : ErrorCode.OnPlayerLeftPostfixFailedInLobby); } catch { }
            }
        }
    }
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CreatePlayer))]
    class CreatePlayerPatch
    {
        public static void Postfix(AmongUsClient __instance, [HarmonyArgument(0)] ClientData client)
        {
            try
            {
                if (!AmongUsClient.Instance.AmHost) return;
                if (client == null) return;
                OptionItem.SyncAllOptions();
                _ = new LateTask(() =>
                {
                    try
                    {
                        if (client.Character == null) return;
                        if (client.Character.PlayerId == PlayerControl.LocalPlayer.PlayerId) return;
                        if (AmongUsClient.Instance.IsGamePublic) Utils.SendMessageAutoSplit(string.Format(GetString("Message.AnnounceUsingTOH"), Main.PluginVersion), true, sendTo: client.Character.PlayerId);
                        TemplateManager.SendTemplate("welcome", client.Character.PlayerId, true);
                    }
                    catch { }
                }, 3f, "Welcome Message");
                if (Options.AutoDisplayLastResult.GetBool() && PlayerState.AllPlayerStates.Count != 0 && Main.clientIdList.Contains(client.Id))
                {
                    _ = new LateTask(() =>
                    {
                        try
                        {
                            if (!AmongUsClient.Instance.IsGameStarted && client.Character != null)
                            {
                                Main.isChatCommand = true;
                                Utils.ShowLastResult(client.Character.PlayerId);
                            }
                        }
                        catch { }
                    }, 3f, "DisplayLastRoles");
                }
                if (Options.AutoDisplayKillLog.GetBool() && PlayerState.AllPlayerStates.Count != 0 && Main.clientIdList.Contains(client.Id))
                {
                    _ = new LateTask(() =>
                    {
                        try
                        {
                            if (!GameStates.IsInGame && client.Character != null)
                            {
                                Main.isChatCommand = true;
                                Utils.ShowKillLog(client.Character.PlayerId);
                            }
                        }
                        catch { }
                    }, 3.2f, "DisplayKillLog");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"CreatePlayerPatch.Postfix で例外: {ex.Message}", "CreatePlayerPatch");
                Logger.Exception(ex, "CreatePlayerPatch");
            }
        }
    }
}