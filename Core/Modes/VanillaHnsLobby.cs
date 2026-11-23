using HarmonyLib;
using InnerNet;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼの間も、ホストのロビーに要る物だけを当てておく。
/// 通常モードのロビーの処理 (GameStartManagerPatch・OnPlayerJoinedPatch) は休んでいる間は外れている。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsLobby
{
    // かくれんぼの部屋を作った時や試合後に作り直されたロビーでは、本編の最低人数 (4 人) に戻ってしまう
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    private static class StartPatch
    {
        public static void Postfix(GameStartManager __instance)
        {
            if (__instance) __instance.MinPlayers = 1;
        }
    }

    // 入ってきた人の確認 (BAN・禁止名・フレンドコード無し) を、かくれんぼの部屋でも通常と同じに行う
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    private static class JoinPatch
    {
        public static void Postfix([HarmonyArgument(0)] ClientData client)
        {
            if (client == null || !AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
            try { OnPlayerJoinedPatch.CheckJoiner(client); }
            catch (System.Exception ex) { Logger.Warn($"入ってきた人の確認に失敗: {ex.Message}", nameof(VanillaHnsLobby)); }
        }
    }
}
