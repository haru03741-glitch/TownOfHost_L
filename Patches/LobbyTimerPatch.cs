using HarmonyLib;

namespace TownOfHostL.Patches;

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.HandleLobbyTimerExtensionRequest))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
internal static class LobbyTimerPatch
{
    private static int lastExtensionId = int.MinValue;

    public static void Postfix(LobbyBehaviour __instance, int timeRemainingSeconds, bool isExtensionAvailable, int extensionId, int extendedTimeSeconds)
    {
        GameStartManagerPatch.SyncTimer(timeRemainingSeconds);
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || !isExtensionAvailable || __instance == null) return;
        if (extensionId == lastExtensionId) return;
        lastExtensionId = extensionId;
        Logger.Info($"ロビーの時間切れが近い ({timeRemainingSeconds}秒) ので延長する", "LobbyTimer");
        __instance.RpcExtendLobbyTimer();
        GameStartManagerPatch.SyncTimer(timeRemainingSeconds + extendedTimeSeconds);
    }
}
