using System;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;

using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Translator;
using TownOfHostL.Core;

namespace TownOfHostL
{
    /// <summary>
    /// TOHL の独自の電文。版の知らせの番号はほかの TOHL 系の Mod と同じ (版の違う TOHL と知らせ合えるように)。
    /// 90 から先は、ホストと同じ版の TOHL を入れた客 (<see cref="Modules.Net.ModClientSync"/>) にだけ宛てて送る。本編の客には届かない
    /// </summary>
    public enum CustomRPC
    {
        VersionCheck = 80,
        RequestRetryVersionCheck = 81,
        ModClientOptions = 90,
        ModClientRoles = 91,
        ModClientEnd = 92,
    }
    public enum Sounds
    {
        KillSound,
        TaskComplete,
        SabotageAlarm
    }
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    class RPCHandlerPatch
    {
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] byte callId, [HarmonyArgument(1)] MessageReader reader)
        {
            var rpcType = (RpcCalls)callId;
            // 本編の客なら送らない電文の受け口 (ホストのみ)
            if (RpcGuard.ShouldDrop(__instance, rpcType, reader)) return false;
            if (Logger.IsTagEnabled("ReceiveRPC")) Log.Info("ReceiveRPC", $"{__instance?.Data?.PlayerId}({__instance?.Data?.PlayerName}):{callId}({RPC.GetRpcName(callId)})");
            switch (rpcType)
            {
                case RpcCalls.SetName:
                case RpcCalls.SetRole:
                case RpcCalls.SendChat:
                case RpcCalls.StartMeeting:
                    break;
                case RpcCalls.CheckVanish:
                case RpcCalls.MurderPlayer:
                    return HandleWithoutRead(__instance, rpcType);
                default:
                    return true;
            }
            // 読む電文だけ写しを作り、読み終えたら返す (返さないと電文ごとに写しの入れ物が増える)
            MessageReader subReader = MessageReader.Get(reader);
            try { return HandleRead(__instance, rpcType, subReader); }
            finally { subReader.Recycle(); }
        }
        static bool HandleRead(PlayerControl __instance, RpcCalls rpcType, MessageReader subReader)
        {
            switch (rpcType)
            {
                case RpcCalls.SetName: //SetNameRPC
                    subReader.ReadUInt32();
                    string name = subReader.ReadString();
                    if (subReader.BytesRemaining > 0 && subReader.ReadBoolean()) return false;
                    Logger.Info("名前変更:" + __instance.GetNameWithRole() + " => " + name, "SetName");
                    // 長すぎる名前は頭の 350 字だけを手元に当てる
                    if (name.Length > 350 && AmongUsClient.Instance.AmHost)
                    {
                        __instance.SetName(name.Substring(0, 350));
                        return false;
                    }
                    break;
                case RpcCalls.SetRole: //SetNameRPC
                    var role = (RoleTypes)subReader.ReadUInt16();
                    bool canOverride = subReader.ReadBoolean();
                    Logger.Info("役職:" + __instance.GetRealName(Options.GetNameChangeModes() == NameChange.Crew) + " => " + role, "SetRole");
                    break;
                case RpcCalls.SendChat:
                    var text = subReader.ReadString();
                    Logger.Info($"{__instance.GetNameWithRole()}:{text}", "SendChat");
                    // 発言の処理の例外で、本編の発言の処理 (他の客の画面に出す) まで止めない
                    try { ChatCommands.OnReceiveChat(__instance, text); }
                    catch (Exception ex) { Logger.Exception(ex, "OnReceiveChat"); }
                    break;
                case RpcCalls.StartMeeting:
                    var p = Utils.GetPlayerById(subReader.ReadByte());
                    Logger.Info($"{__instance.GetNameWithRole()} => {p?.GetNameWithRole() ?? "null"}", "StartMeeting");
                    break;
            }
            return true;
        }
        static bool HandleWithoutRead(PlayerControl __instance, RpcCalls rpcType)
        {
            switch (rpcType)
            {
                case RpcCalls.CheckVanish:
                    // 本人の画面だけファントムの役職 (デシンク) は、ホストの手元ではファントムでないので、本編が確認を捨てる。
                    // 本人の基底がファントムで生きている人の確認だけ、ホストの判定 (PlayerControlCheckVanishPatch) へ直接回す。
                    // それ以外 (基底がファントムでない・死んでいる・一時的な幽霊) は本編と同じく捨てる
                    // (受け取った確認を判定へ回す形は End K not の Modules/RPC.cs を参考にした)
                    if (AmongUsClient.Instance.AmHost && __instance.Data?.Role?.Role != RoleTypes.Phantom)
                    {
                        if (GameStates.IsInTask && !ExileController.Instance && !AntiBlackout.IsCached
                            && __instance.IsAlive() && !TempGhost.IsGhost(__instance)
                            && RoleView.SelfRole(__instance) == RoleTypes.Phantom)
                            __instance.CheckVanish();
                        return false;
                    }
                    break;
                case RpcCalls.MurderPlayer: //MurderPlayerRPC
                    if (GameStates.IsLobby && Options.CheatLobbyKill.GetBool())
                    {
                        //ロビーキルチート
                        var client = __instance.GetClient();
                        if (client == null) return false;
                        if (Options.CheaterAutoBan.GetBool()) BanManager.AddBanPlayer(client, true);
                        AmongUsClient.Instance.KickPlayer(client.Id, false);
                        Logger.Info($"LobbyKillCancel {client.PlayerName}, FriendCode={client.FriendCode}, ProductUserId={client.ProductUserId}", "RPCHandlerPatch");
                        return false;
                    }
                    break;
            }
            return true;
        }
        static float lastRetryVersionCheck = float.NegativeInfinity;
        public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] byte callId, [HarmonyArgument(1)] MessageReader reader)
        {
            switch ((RpcCalls)callId)
            {
                case RpcCalls.SetHatStr:
                case RpcCalls.SetSkinStr:
                case RpcCalls.SetPetStr:
                case RpcCalls.SetVisorStr:
                    SkinControle.Check(__instance);
                    break;
            }

            //CustomRPC以外は処理しない
            if (callId < (byte)CustomRPC.VersionCheck) return;

            var rpcType = (CustomRPC)callId;
            switch (rpcType)
            {
                case CustomRPC.VersionCheck:
                    try
                    {
                        Version version = Version.Parse(reader.ReadString());
                        string tag = reader.ReadString();
                        // ForkId は本家の古い版 (3 より前) だけが載せない。版の数字ではなく、残りがあるかで読む (TOHL の版は 0 から始まる)
                        string forkId = reader.BytesRemaining > 0 ? reader.ReadString() : Main.OriginalForkId;
                        var pv = new PlayerVersion(version, tag, forkId);
                        Main.playerVersion[__instance.PlayerId] = pv;
                        Modules.Net.ModClientSync.OnVersionFromClient(__instance, pv);
                    }
                    catch
                    {
                        Logger.Warn($"{__instance?.Data?.PlayerName}({__instance.PlayerId}): バージョン情報が無効です", "RpcVersionCheck");
                        _ = new LateTask(() =>
                        {
                            if (__instance.LacksClient("RequestRetryVersionCheck")) return;
                            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.RequestRetryVersionCheck, SendOption.Reliable, __instance.GetClientId());
                            AmongUsClient.Instance.FinishRpcImmediately(writer);
                        }, 1f, "Retry Version Check Task");
                    }
                    break;
                case CustomRPC.RequestRetryVersionCheck:
                    // 頼むのはホストだけ。1 通で全員宛ての Reliable を出し直すので、ホスト以外の頼みと連打は受けない
                    if (__instance.OwnerId != AmongUsClient.Instance.HostId) break;
                    if (UnityEngine.Time.realtimeSinceStartup - lastRetryVersionCheck < 1f) break;
                    lastRetryVersionCheck = UnityEngine.Time.realtimeSinceStartup;
                    RPC.RpcVersionCheck();
                    break;
            }
        }
    }
    static partial class RPC
    {
        public static void ExileAsync(PlayerControl player)
        {
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(player.NetId, (byte)RpcCalls.Exiled, Hazel.SendOption.Reliable, -1);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            player.Exiled();
        }
        public static void RpcVersionCheck()
        {
            // Task.Delay で待つと続きがメインスレッド外で動くので、LateTask で待ち直す
            if (PlayerControl.LocalPlayer == null)
            {
                if (AmongUsClient.Instance != null && !GameStates.IsNotJoined)
                    _ = new LateTask(RpcVersionCheck, 0.5f, "");
                return;
            }
            MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId,
                (byte)CustomRPC.VersionCheck,
                Hazel.SendOption.Reliable,
                -1
            );
            writer.Write(Main.PluginVersion);
            writer.Write($"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})");
            writer.Write(Main.ForkId);
            AmongUsClient.Instance.FinishRpcImmediately(writer);

            Main.playerVersion[PlayerControl.LocalPlayer.PlayerId] = new PlayerVersion(
                Main.PluginVersion,
                $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})",
                Main.ForkId
            );
        }

        public static void PlaySound(byte playerID, Sounds sound)
        {
            if (PlayerControl.LocalPlayer.PlayerId == playerID)
            {
                switch (sound)
                {
                    case Sounds.KillSound:
                        SoundManager.Instance.PlaySound(PlayerControl.LocalPlayer.KillSfx, false, 0.8f);
                        break;
                    case Sounds.SabotageAlarm:
                        if (ShipStatus.Instance != null && ShipStatus.Instance.SabotageSound != null)
                            SoundManager.Instance.PlaySound(ShipStatus.Instance.SabotageSound, false, 0.8f);
                        break;
                    case Sounds.TaskComplete:
                        SoundManager.Instance.PlaySound(DestroyableSingleton<HudManager>.Instance.TaskCompleteSound, false, 0.8f);
                        break;
                }
            }
        }
        public static string GetRpcName(byte callId)
        {
            string rpcName;
            if ((rpcName = Enum.GetName(typeof(RpcCalls), callId)) != null) { }
            else if ((rpcName = Enum.GetName(typeof(CustomRPC), callId)) != null) { }
            else rpcName = callId.ToString();
            return rpcName;
        }
        public static void SetRealKiller(byte targetId, byte killerId)
        {
            var state = PlayerState.GetByPlayerId(targetId);
            state.RealKiller.Item1 = DateTime.Now;
            state.RealKiller.Item2 = killerId;
        }
        public static void ReportDeadBodyForced(this PlayerControl player, NetworkedPlayerInfo target)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            Logger.Info($"会議を開く: {player?.GetNameWithRole()} (dead={player?.Data?.IsDead}) => {target?.PlayerName ?? "button"}", "ReportDeadBody");
            MeetingRoomManager.Instance.AssignSelf(player, target);
            if (AmongUsClient.Instance.AmHost)
            {
                DestroyableSingleton<HudManager>.Instance.OpenMeetingRoom(player);
                player.RpcStartMeeting(target);
            }
        }
    }

    [HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.StartRpcImmediately))]
    class StartRpcImmediatelyPatch
    {
        public static void Prefix(
            InnerNet.InnerNetClient __instance,
            [HarmonyArgument(0)] uint targetNetId,
            byte callId,
            [HarmonyArgument(3)] int targetClientId = -1)
        {
            if ((RpcCalls)callId == RpcCalls.SnapTo) Modules.Net.SnapToBudget.Record();

            // 送るたびに通る。記録が切られている時は、名前を引く処理 (全員の走査など) をしない
            if (!Logger.IsTagEnabled("SendRPC")) return;
            string rpcName = RPC.GetRpcName(callId);
            string target = targetClientId < 0 ? "All" : AmongUsClient.Instance.GetClient(targetClientId)?.PlayerName ?? targetClientId.ToString();
            string from = Main.AllPlayerControls.FirstOrDefault(c => c.NetId == targetNetId)?.Data?.PlayerName ?? targetNetId.ToString();

            Log.Info("SendRPC", $"送信RPC From:{from} Target:{target} CallID:{callId}({rpcName})");
        }
    }
}