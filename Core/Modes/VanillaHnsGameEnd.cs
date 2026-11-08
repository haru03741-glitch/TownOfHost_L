using HarmonyLib;
using UnityEngine;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼで「ゲームを終了しない」を効かせる。本編の終了判定 (時間切れ・全員キル) を止め、
/// 代わりにホストの Shift+Enter+L で廃村にする (通常モードの廃村のキーは休んでいるため)。
/// 終了判定の止め方は SuperNewRoles のかくれんぼの CheckEndCriteria の前置きを参考にした。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsGameEnd
{
    /// <summary>ホストが廃村にした試合か。結果の知らせで勝者の代わりに廃村と出す</summary>
    public static bool Aborted { get; private set; }

    [HarmonyPatch(typeof(LogicGameFlowHnS), nameof(LogicGameFlowHnS.CheckEndCriteria))]
    private static class CheckEndPatch
    {
        public static bool Prefix() => !(AmongUsClient.Instance.AmHost && Options.NoGameEnd.GetBool());
    }

    [HarmonyPatch(typeof(ControllerManager), nameof(ControllerManager.Update))]
    private static class AbortKeyPatch
    {
        public static void Postfix()
        {
            // GameStates.IsInGame は通常モードのイントロのパッチが立てるので、本編のかくれんぼの間は立たない。本編の状態で見る
            if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !AmongUsClient.Instance.IsGameStarted || !ShipStatus.Instance) return;
            if (HudManager.InstanceExists && HudManager.Instance.Chat && HudManager.Instance.Chat.IsOpenOrOpening) return;
            if (!Input.GetKeyDown(KeyCode.L) && !Input.GetKeyDown(KeyCode.Return)) return;
            if (!Input.GetKey(KeyCode.L) || !Input.GetKey(KeyCode.Return) || !Input.GetKey(KeyCode.LeftShift)) return;
            if (Aborted) return;
            Aborted = true;
            Logger.Info("ホストが廃村にした", "VanillaHnsGameEnd");
            GameManager.Instance.RpcEndGame(GameOverReason.ImpostorDisconnect, false);
        }
    }

    /// <summary>ロビーに戻った時に呼ぶ。</summary>
    public static void Reset() => Aborted = false;
}
