using System;
using AmongUs.GameOptions;
using HarmonyLib;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼ (TOHL が休んでいる間) の決まりを、本編の仕組みのまま変える。
/// 鬼の人数・キルでの残り時間の増減・開始時の鬼の役職・死んだ隠れる側の役職。
/// どれも本編の配役と時間の同期に乗るので、本編の客の画面にそのまま出る。能力の長さと待ち時間は VanillaHnsAbilities が数える。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsRules
{
    private static bool IsHns => GameOptionsManager.Instance != null && GameOptionsManager.Instance.currentGameMode == GameModes.HideNSeek;

    // 開始時の配役の間だけ立てる。配役の外の RpcSetRole (死んだ時の幽霊など) では鬼の役職を差し替えない
    private static bool assigning;
    /// <summary>開始時の配役の最中か (ホストだけ)。</summary>
    internal static bool Assigning => assigning;

    private static RoleTypes SeekerRole => Options.VanillaHnsSeekerRole.GetValue() switch
    {
        1 => RoleTypes.Shapeshifter,
        2 => RoleTypes.Phantom,
        _ => RoleTypes.Impostor,
    };

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

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    private static class SelectRolesPatch
    {
        public static void Prefix()
        {
            try
            {
                assigning = AmongUsClient.Instance && AmongUsClient.Instance.AmHost && IsHns;
                if (assigning)
                {
                    VanillaHnsAbilities.Reset();
                    VanillaHnsHiddenSeeker.Reset();
                }
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(VanillaHnsRules)); }
        }
        public static Exception Finalizer(Exception __exception)
        {
            assigning = false;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
    private static class SetRolePatch
    {
        public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] ref RoleTypes roleType)
        {
            if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !IsHns) return;
            var from = roleType;
            // 本編のかくれんぼは鬼を Impostor で配る
            if (assigning && roleType == RoleTypes.Impostor) roleType = SeekerRole;
            // 死んだ時の最初の幽霊の役職で替える (2 回目の幽霊の SetRole だと、客の能力のボタンが出ないことがある)
            else if (!assigning && roleType == RoleTypes.CrewmateGhost && Options.VanillaHnsGhostGuard.GetBool()) roleType = RoleTypes.GuardianAngel;
            if (roleType != from) Logger.Info($"{__instance.Data?.PlayerName}: {from} → {roleType}", nameof(VanillaHnsRules));
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
                // バーが満杯を超えず 0 を下回らないよう、増やす分は満杯まで・減らす分は残りまでにする
                if (flow.IsFinalCountdown)
                {
                    float d = sec > 0f ? Math.Min(sec, Math.Max(0f, flow.totalFinalHideTime - flow.currentFinalHideTime)) : -Math.Min(-sec, Math.Max(0f, flow.currentFinalHideTime));
                    if (d != 0f) flow.AdjustFinalEscapeTimer(d);
                }
                else
                {
                    // AdjustEscapeTimer は正の値で残りを減らす (タスクで縮む向き)
                    float d = sec > 0f ? Math.Min(sec, Math.Max(0f, flow.totalHideTime - flow.currentHideTime)) : -Math.Min(-sec, Math.Max(0f, flow.currentHideTime));
                    if (d != 0f) flow.AdjustEscapeTimer(-d, false);
                }
                Logger.Info($"{player.Data.PlayerName} が死んだので残り時間を {sec:+0;-0} 秒", nameof(VanillaHnsRules));
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(VanillaHnsRules)); }
        }
    }
}
