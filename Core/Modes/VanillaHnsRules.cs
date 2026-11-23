using System;
using AmongUs.GameOptions;
using HarmonyLib;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼ (TOHL が休んでいる間) の決まりを、本編の仕組みのまま変える。鬼の人数と、キルでの残り時間の増減。
/// どちらも本編の配役と時間の同期に乗るので、本編の客の画面にそのまま出る。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsRules
{
    private static bool IsHns => GameOptionsManager.Instance != null && GameOptionsManager.Instance.currentGameMode == GameModes.HideNSeek;

    // 本編は鬼の数を人数ごとの上限 (かくれんぼは 1 人) で絞り、3 人以下だと 0 にする。
    // 鬼の人数は本編の配役 (PickSeekersRoundRobin) がそのまま選ぶ。隠れる側は 1 人は残す
    [HarmonyPatch(typeof(IGameOptionsExtensions), nameof(IGameOptionsExtensions.GetAdjustedNumImpostors))]
    private static class NumSeekersPatch
    {
        public static void Postfix([HarmonyArgument(1)] int playerCount, ref int __result)
        {
            if (!IsHns || playerCount < 2) return;
            __result = Math.Clamp(Options.VanillaHnsSeekers.GetInt(), 1, playerCount - 1);
        }
    }

    // 隠れる側が死ぬたびに残り時間を変える (正の値で増える = 鬼に有利)。本編の時間は客へも同期される
    [HarmonyPatch(typeof(HideAndSeekManager), nameof(HideAndSeekManager.OnPlayerDeath))]
    private static class TimeOnKillPatch
    {
        public static void Postfix(HideAndSeekManager __instance, [HarmonyArgument(0)] PlayerControl player)
        {
            try
            {
                if (!AmongUsClient.Instance.AmHost || !player || player.Data?.Role == null || player.Data.Role.IsImpostor) return;
                float sec = Options.VanillaHnsTimeOnKill.GetInt();
                var flow = __instance.LogicFlowHnS;
                if (sec == 0f || flow == null) return;
                // バーが満杯を超えないよう、増やす分は満杯までにする
                if (flow.IsFinalCountdown)
                {
                    float d = sec > 0f ? Math.Min(sec, Math.Max(0f, flow.totalFinalHideTime - flow.currentFinalHideTime)) : sec;
                    if (d != 0f) flow.AdjustFinalEscapeTimer(d);
                }
                else
                {
                    // AdjustEscapeTimer は正の値で残りを減らす (タスクで縮む向き)
                    float d = sec > 0f ? Math.Min(sec, Math.Max(0f, flow.totalHideTime - flow.currentHideTime)) : sec;
                    if (d != 0f) flow.AdjustEscapeTimer(-d, false);
                }
                Logger.Info($"{player.Data.PlayerName} が死んだので残り時間を {sec:+0;-0} 秒", nameof(VanillaHnsRules));
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(VanillaHnsRules)); }
        }
    }
}
