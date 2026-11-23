using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼで、鬼の変身・透明化と、守護天使の護衛の長さと待ち時間をホストが数える。
/// 本編のかくれんぼでは役職の能力の秒数が客の手元で 1 秒に決まり、ホストの設定では変えられない。
/// そこで客が送ってくる要求をホストの側で止める:
/// 戻す要求 (変身を解く・姿を出す) は設定の長さまで待たせてからホストが通し、待ち時間の間の次の要求は通さない。
/// 護衛は守った人と時刻をホストが覚え、その間のキルを本編の「守られて失敗したキル」にする。
/// 客の画面のボタンは本編の 1 秒のまま動くので、待ち時間の間も押せる見た目だが、押しても何も起きない。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsAbilities
{
    private static readonly Dictionary<byte, float> usedAt = new();
    private static readonly Dictionary<byte, float> readyAt = new();
    private static readonly Dictionary<byte, float> guardReadyAt = new();
    private static readonly Dictionary<byte, float> protectedUntil = new();
    private static readonly Dictionary<byte, float> lastBlockedAt = new();
    // 守りに弾かれたキルの知らせ (全員宛て) を、同じ鬼からは 1 秒に 1 回までにする。連打で同じ電文を続けて送らない
    private const float BlockedNoticeInterval = 1f;
    private static readonly HashSet<byte> pendingRevert = new();
    private static readonly HashSet<byte> pendingAppear = new();
    // ホストが自分で戻す時は、待たせる処理を通さない
    private static bool hostCall;

    private static float Now => Time.realtimeSinceStartup;
    private static float Duration => Options.VanillaHnsAbilityDuration.GetInt();
    private static float Cooldown => Options.VanillaHnsAbilityCooldown.GetInt();

    private static bool Active
        => AmongUsClient.Instance && AmongUsClient.Instance.AmHost && AmongUsClient.Instance.IsGameStarted
           && GameOptionsManager.Instance != null && GameOptionsManager.Instance.currentGameMode == GameModes.HideNSeek;

    /// <summary>試合の始め (配役の前) に呼ぶ。</summary>
    public static void Reset()
    {
        usedAt.Clear();
        readyAt.Clear();
        guardReadyAt.Clear();
        protectedUntil.Clear();
        lastBlockedAt.Clear();
        pendingRevert.Clear();
        pendingAppear.Clear();
    }

    // 能力を使い終えた (戻した) 時。次に使えるのは待ち時間の後
    private static void Finished(byte id)
    {
        usedAt.Remove(id);
        readyAt[id] = Now + Cooldown;
    }

    private static bool CanUse(byte id) => !usedAt.ContainsKey(id) && !(readyAt.TryGetValue(id, out var r) && Now < r);

    // 戻す要求を待たせるか。待たせたら、長さが過ぎた時に Tick で通す
    private static bool Hold(byte id, HashSet<byte> pending)
    {
        if (!usedAt.TryGetValue(id, out var t) || Now - t >= Duration) return false;
        if (pending.Add(id)) Logger.Info($"{id}: 戻しを {Duration - (Now - t):0.0} 秒待たせます", nameof(VanillaHnsAbilities));
        return true;
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckShapeshift))]
    private static class ShapeshiftPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
        {
            if (!Active || hostCall || !__instance || !target) return true;
            byte id = __instance.PlayerId;
            if (target == __instance)
            {
                if (Hold(id, pendingRevert)) return false;
                // 変身していない人の戻しは本編に任せ、待ち時間は始めない
                if (usedAt.ContainsKey(id)) Finished(id);
                return true;
            }
            // 変身している間 (客は 1 秒で解けたと思っている) と待ち時間の間の変身は通さない
            if (!CanUse(id))
            {
                __instance.RpcRejectShapeshift();
                return false;
            }
            usedAt[id] = Now;
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckVanish))]
    private static class VanishPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance)
        {
            if (!Active || !__instance) return true;
            byte id = __instance.PlayerId;
            if (!CanUse(id))
            {
                // 客の画面は押した時点で消える途中になっているので、本人にだけ消えて戻る電文を返して画面を戻す
                // (通常モードの PlayerControlCheckVanishPatch と同じ取り消し)。他の人の画面では消えていない
                if (!__instance.AmOwner)
                {
                    SendToSelf(__instance, RpcCalls.StartVanish, null);
                    var phantom = __instance;
                    _ = new LateTask(() =>
                    {
                        if (phantom && phantom.Data != null && !phantom.Data.Disconnected && Active) SendToSelf(phantom, RpcCalls.StartAppear, false);
                    }, 1f, "VanillaHnsAbilities.CancelVanish");
                }
                return false;
            }
            usedAt[id] = Now;
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckAppear))]
    private static class AppearPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance)
        {
            if (!Active || hostCall || !__instance) return true;
            byte id = __instance.PlayerId;
            // 待ち時間の間に押した「消える」の後に客が求めてくる出現は、取り消しの電文で戻すので通さない
            if (!usedAt.ContainsKey(id)) return __instance.AmOwner;
            if (Hold(id, pendingAppear)) return false;
            Finished(id);
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckProtect))]
    private static class ProtectPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
        {
            if (!Active || !__instance) return true;
            // 本編の守護天使は鬼も守れるが、かくれんぼでは隠れる側だけを守る
            if (!target || target.Data == null || target.Data.IsDead || target.Data.Role == null || target.Data.Role.IsImpostor) return false;
            byte id = __instance.PlayerId;
            if (guardReadyAt.TryGetValue(id, out var r) && Now < r) return false;
            guardReadyAt[id] = Now + Options.VanillaHnsGuardCooldown.GetInt();
            protectedUntil[target.PlayerId] = Now + Options.VanillaHnsGuardDuration.GetInt();
            Logger.Info($"{__instance.Data?.PlayerName} が {target.Data.PlayerName} を守りました", nameof(VanillaHnsAbilities));
            return true;
        }
    }

    // 守られている間のキルは、本編の守られて失敗したキルとして全員に返す
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
    private static class MurderPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
        {
            if (!Active || !__instance || !target || __instance == target) return true;
            if (!protectedUntil.TryGetValue(target.PlayerId, out var until) || Now >= until) return true;
            // キルできない要求 (死んだ人・鬼でない人・死んだ相手・透明化中のファントム) は本編の判定に任せる
            if (__instance.Data == null || __instance.Data.IsDead || __instance.Data.Role == null || !__instance.Data.Role.IsImpostor) return true;
            if (target.Data == null || target.Data.IsDead) return true;
            if (__instance.Data.Role.Role == RoleTypes.Phantom && usedAt.ContainsKey(__instance.PlayerId)) return true;
            if (lastBlockedAt.TryGetValue(__instance.PlayerId, out var last) && Now - last < BlockedNoticeInterval) return false;
            lastBlockedAt[__instance.PlayerId] = Now;
            __instance.RpcMurderPlayer(target, false);
            Logger.Info($"{__instance.Data?.PlayerName} のキルを {target.Data?.PlayerName} の護衛が防ぎました", nameof(VanillaHnsAbilities));
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    private static class TickPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            // 1 フレームに 1 回だけ回す
            if (__instance != PlayerControl.LocalPlayer || (pendingRevert.Count == 0 && pendingAppear.Count == 0 && !usedAt.ContainsKey(__instance.PlayerId)) || !Active) return;
            try
            {
                // ホスト自身の役職は長さが過ぎても戻しを求めてこないので、自分の分は求められたことにする
                var role = __instance.Data?.Role?.Role;
                if (usedAt.ContainsKey(__instance.PlayerId))
                {
                    if (role == RoleTypes.Phantom) pendingAppear.Add(__instance.PlayerId);
                    else if (role == RoleTypes.Shapeshifter) pendingRevert.Add(__instance.PlayerId);
                }
                Release(pendingRevert, pc => pc.CheckShapeshift(pc, true));
                Release(pendingAppear, pc => pc.CheckAppear(true));
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(VanillaHnsAbilities)); }
        }
    }

    private static void SendToSelf(PlayerControl pc, RpcCalls call, bool? arg)
    {
        var writer = AmongUsClient.Instance.StartRpcImmediately(pc.NetId, (byte)call, Hazel.SendOption.Reliable, pc.OwnerId);
        if (arg is bool b) writer.Write(b);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    // 長さが過ぎた戻しを、ホストが本編の判定に通す
    private static void Release(HashSet<byte> pending, Action<PlayerControl> pass)
    {
        if (pending.Count == 0) return;
        foreach (var id in new List<byte>(pending))
        {
            if (usedAt.TryGetValue(id, out var t) && Now - t < Duration) continue;
            pending.Remove(id);
            Finished(id);
            var pc = Players.ById(id);
            if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
            Logger.Info($"{pc.Data.PlayerName}: 長さが過ぎたので戻します", nameof(VanillaHnsAbilities));
            hostCall = true;
            try { pass(pc); }
            finally { hostCall = false; }
        }
    }
}
