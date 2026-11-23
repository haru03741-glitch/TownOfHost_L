using HarmonyLib;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼのロビーでも 1 人から開始できるようにする。
/// 通常モードの開始人数の上書き (GameStartManagerPatch) は休んでいる間は外れているので、
/// かくれんぼの部屋を作った時や試合後に作り直されたロビーでは、本編の最低人数 (4 人) に戻ってしまう。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsMinPlayers
{
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    private static class StartPatch
    {
        public static void Postfix(GameStartManager __instance)
        {
            if (__instance) __instance.MinPlayers = 1;
        }
    }
}
