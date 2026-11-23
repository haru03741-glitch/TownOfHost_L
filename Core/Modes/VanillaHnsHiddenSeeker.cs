using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼで、鬼の正体を隠れる側から隠す。
/// 開始の配役で、鬼を「鬼本人とホスト」には鬼の役職、他の客にはエンジニアとして配る。
/// 隠れる側の手元では鬼がクルーなので、鬼のマスク (HideAndSeekManager.GetBodyType は手元の IsImpostor で決まる) も赤い名前も出ない。
/// 鬼が変身すると、隠れる側からは変身先と見分けがつかない (2026-11-24 実機)。
/// キル・時間・勝敗はホストが本当の役職で決めるので、本編のまま動く。
/// 試合中の SetRole は客に効かないので、開始の配役の時にしか決められない。
/// 隠れる側の結果画面では鬼も勝者の列に並ぶので、結果の知らせに鬼の名前を足す (VanillaHideAndSeekRoles)。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsHiddenSeeker
{
    private static readonly List<byte> seekers = new();

    public static bool Enabled => Options.VanillaHnsHiddenSeeker.GetBool();

    /// <summary>この試合で正体を隠した鬼の名前 (結果の知らせ用)。隠していない試合は空。</summary>
    public static IEnumerable<string> SeekerNames
        => seekers.Select(id => Players.ById(id)?.Data?.PlayerName).Where(n => n != null);

    /// <summary>役職を配る前に呼ぶ。</summary>
    public static void Reset() => seekers.Clear();

    // 鬼の役職の差し替え (VanillaHnsRules) の後に動かす
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
    private static class RpcSetRolePatch
    {
        [HarmonyPriority(Priority.Low)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] RoleTypes roleType, [HarmonyArgument(1)] bool canOverrideRole)
        {
            try
            {
                if (!VanillaHnsRules.Assigning || !Enabled || !RoleManager.IsImpostorRole(roleType)) return true;
                var seeker = __instance;
                foreach (var pc in Players.All)
                {
                    if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
                    // ホストは本当の役職を持つ (キル・勝敗の判定に使う)。ホストが隠れる側でもホストの画面には鬼が見える
                    if (pc.AmOwner) continue;
                    var shown = pc.PlayerId == seeker.PlayerId ? roleType : RoleTypes.Engineer;
                    int clientId = pc.GetClientId();
                    if (clientId < 0) continue;
                    var writer = AmongUsClient.Instance.StartRpcImmediately(seeker.NetId, (byte)RpcCalls.SetRole, Hazel.SendOption.Reliable, clientId);
                    writer.Write((ushort)shown);
                    writer.Write(canOverrideRole);
                    AmongUsClient.Instance.FinishRpcImmediately(writer);
                }
                // ホストの手元は本編の RpcSetRole と同じく、配役の canOverrideRole のまま CoSetRole で付ける。
                // 上書き可 (true) で付けると、ホストのかくれんぼの時計 (LogicGameFlowHnS) が始まらず時間切れで終わらなくなる (2026-11-24 実機)
                seeker.StartCoroutine(seeker.CoSetRole(roleType, canOverrideRole));
                if (!seekers.Contains(seeker.PlayerId)) seekers.Add(seeker.PlayerId);
                Logger.Info($"{seeker.Data?.PlayerName}: 鬼 ({roleType}) を隠れる側にはエンジニアとして配った", nameof(VanillaHnsHiddenSeeker));
                return false;
            }
            catch (Exception ex)
            {
                // 失敗したら本編の配り方に任せる (鬼が見えるだけで、試合は進む)
                Logger.Exception(ex, nameof(VanillaHnsHiddenSeeker));
                return true;
            }
        }
    }
}
