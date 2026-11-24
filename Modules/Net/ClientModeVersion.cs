using System;
using HarmonyLib;
using Hazel;
using TownOfHostL.Core.Modes;

namespace TownOfHostL.Modules.Net;

/// <summary>
/// 客の間 (<see cref="TownOfHostL.Core.VanillaHideAndSeek.IsClientMode"/>) は電文の受け口 (Standard) を外しているので、
/// ホストが TOHL かどうかを左上に出すためのホストの版だけをここで受ける。ホスト以外から来た物は読まない。
/// 客の側から送るのは、ホストが同じ版の TOHL だった時に、ホストにだけ宛てて返す版の 1 本だけ (<see cref="ModClientSync"/>)。
/// 版の違う TOHL・ほかの Mod・本編のホストには何も送らない (ホストは入った人に版を送り直す)。
/// 同じ版のホストから届く TOHL の画面の情報 (設定・役職・結果) もここで受ける。
/// 独自の番号の電文は本編へ渡さない。本編は知らない番号を役職の電文として読み、ロビーでは役職が無いので例外になる
/// (例外になると、その後ろの Postfix も走らない)。
/// </summary>
[HarmonyPatch, PatchBundle(PatchBundles.Core)]
public static class ClientModeVersion
{
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc)), HarmonyPrefix]
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] byte callId, [HarmonyArgument(1)] MessageReader reader)
    {
        if (!TownOfHostL.Core.VanillaHideAndSeek.IsClientMode) return true;
        if (callId == (byte)CustomRPC.RequestRetryVersionCheck) return false;
        var client = AmongUsClient.Instance;
        if (callId is (byte)CustomRPC.ModClientOptions or (byte)CustomRPC.ModClientRoles or (byte)CustomRPC.ModClientEnd or (byte)CustomRPC.ModClientHud)
        {
            if (client != null && __instance != null && __instance.OwnerId == client.HostId)
                ModClientSync.Receive((CustomRPC)callId, reader);
            return false;
        }
        if (callId != (byte)CustomRPC.VersionCheck) return true;
        if (client == null || __instance == null || __instance.OwnerId != client.HostId) return false;
        try
        {
            var sub = MessageReader.Get(reader);
            var version = Version.Parse(sub.ReadString());
            var tag = sub.ReadString();
            // ForkId は本家の古い版 (3 より前) だけが載せない。版の数字ではなく、残りがあるかで読む (TOHL の版は 0 から始まる)
            var forkId = sub.BytesRemaining > 0 ? sub.ReadString() : Main.OriginalForkId;
            sub.Recycle();
            var pv = new PlayerVersion(version, tag, forkId);
            Main.playerVersion[__instance.PlayerId] = pv;
            ModClientSync.OnHostVersion(pv);
        }
        catch (Exception ex)
        {
            Logger.Warn($"ホストの版を読めませんでした: {ex.Message}", "ClientMode");
        }
        return false;
    }
}
