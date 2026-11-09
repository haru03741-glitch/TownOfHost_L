using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL;

/// <summary>
/// RoleBase のうち、本編のパッチから全員の役職へ配るフック (湧き・サボタージュ・ベント・投票・会議の文) の配り口。ホストのみ
/// </summary>
public static class RoleHooks
{
    /// <summary>今動いている役職の写し (配る途中で役職が替わっても回せるように)</summary>
    static RoleBase[] AllRoles => CustomRoleManager.AllActiveRoles.Values.ToArray();

    /// <summary>全員の役職に配る。1 人の役職の例外で残りの人や本編の処理を止めない</summary>
    public static void ForEachRole(System.Action<RoleBase> action, string tag)
    {
        foreach (var role in AllRoles)
        {
            try { action(role); }
            catch (System.Exception ex) { Logger.Exception(ex, tag); }
        }
    }

    /// <summary>ClearAndExile 系で会議を終えている途中か (VotingResults に渡す)</summary>
    public static bool InClearAndExile;
    /// <summary>今いるベントの番号 (ベントに入った人だけ)</summary>
    public static readonly Dictionary<byte, byte> NowVentId = new();
    /// <summary>次の会議の始めに送る文 (送り先, 文, 題)。役職が会議の前に積む</summary>
    public static readonly List<(byte sendTo, string text, string title)> MeetingStartMessages = new();

    [GameModuleInitializer]
    public static void Init()
    {
        Main.IsActiveSabotage = false;
        Main.SabotageType = SystemTypes.Hallway;
        Main.LastSab = byte.MaxValue;
        Main.SabotageActivetimer = 0;
        InClearAndExile = false;
        NowVentId.Clear();
        MeetingStartMessages.Clear();
        IUsePhantomButton.IPPlayerKillCooldown.Clear();
    }

    /// <summary>イントロが終わった時</summary>
    public static void OnIntroEnd()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        _ = new LateTask(() =>
        {
            ForEachRole(r => r.OnIntroEnd(), "OnIntroEnd");
            foreach (var pc in Main.AllPlayerControls)
            {
                (pc.GetRoleClass() as IUsePhantomButton)?.Init(pc);
                // エアシップは湧き位置を選んだ時に呼ぶ
                if (Main.NormalOptions.MapId != 4) pc.GetRoleClass()?.OnSpawn(true);
            }
        }, 0.5f, "RoleHooks.OnIntroEnd");
    }

    /// <summary>追放の演出が終わった時</summary>
    public static void OnExileWrapUp(bool airship)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!airship)
        {
            foreach (var pc in Main.AllPlayerControls) pc.GetRoleClass()?.OnSpawn();
        }
        // 追放の演出の後に送る (演出の間に本人の画面のキル役を増やすと、AntiBlackout の見積もりが外れる)
        _ = new LateTask(ApplyAfterMeetingRoles, airship ? 0.5f : 1.0f, "RoleHooks.AfterMeetingRole");
    }

    /// <summary>AfterMeetingRole を持つ役職の、本人の画面の本人の役職を送る (見え方の表に残る)</summary>
    static void ApplyAfterMeetingRoles()
    {
        if (!AmongUsClient.Instance.AmHost || GameStates.IsMeeting || !GameStates.IsInGame) return;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (!pc.IsAlive()) continue;
            if (pc.GetRoleClass()?.AfterMeetingRole is not RoleTypes role) continue;
            if ((pc.GetRoleClass() as IUsePhantomButton)?.IsPhantomRole is false && role is RoleTypes.Phantom) role = RoleTypes.Impostor;
            RoleView.Send(pc, pc, role);
        }
    }

    /// <summary>エアシップで湧き位置を選んだ時</summary>
    public static void OnAirshipSpawn(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || player == null) return;
        player.GetRoleClass()?.OnSpawn(MeetingStates.FirstMeeting);
    }

    /// <summary>サボタージュが直り切った時</summary>
    public static void CompleteSabotage()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Main.SabotageType is SystemTypes.Hallway) return;
        Main.IsActiveSabotage = false;
        Main.SabotageActivetimer = 0;
        ForEachRole(r => r.AfterSabotage(Main.SabotageType), "AfterSabotage");
    }

    /// <summary>サボタージュを直そうとした時</summary>
    public static void OnFixSabotage(PlayerControl player, byte amount)
    {
        if (!AmongUsClient.Instance.AmHost || player == null) return;
        ForEachRole(r => r.OnFixSabotage(player, Main.SabotageType, amount), "OnFixSabotage");
    }

    public static byte PeekByte(MessageReader msgReader)
    {
        var r = MessageReader.Get(msgReader);
        var b = r.ReadByte();
        r.Recycle();
        return b;
    }

    /// <summary>ベントの中で隣へ移れる役職か</summary>
    public static bool CanVentMoving(PlayerPhysics physics, int ventId)
        => physics?.myPlayer?.GetRoleClass()?.CanVentMoving(physics, ventId) ?? true;

    /// <summary>ベントに入った時、移れない役職なら入り直させて移動の矢印を消す</summary>
    public static void OnEnterVent(PlayerPhysics physics, int id)
    {
        var user = physics?.myPlayer;
        if (user == null || CanVentMoving(physics, id)) return;
        if (user.PlayerId == PlayerControl.LocalPlayer.PlayerId) return; // ホストは TryMoveToVent で止める
        int clientId = user.GetClientId();
        _ = new LateTask(() =>
        {
            var w = AmongUsClient.Instance.StartRpcImmediately(physics.NetId, (byte)RpcCalls.BootFromVent, SendOption.None, clientId);
            w.Write(id);
            AmongUsClient.Instance.FinishRpcImmediately(w);
        }, 0.1f, "Vent- BootFromVent");
        _ = new LateTask(() =>
        {
            var w = AmongUsClient.Instance.StartRpcImmediately(physics.NetId, (byte)RpcCalls.EnterVent, SendOption.None, clientId);
            w.Write(id);
            AmongUsClient.Instance.FinishRpcImmediately(w);
        }, 0.25f, "Vent- EnterVent");
    }

    /// <summary>
    /// 全員の役職の CheckAnyVote を呼ぶ。どれかが false なら投票を取り消す。
    /// 天秤の会議中は、天秤の対象以外への投票を天秤だけが見る
    /// </summary>
    public static bool CheckAnyVote(byte srcPlayerId, byte suspectPlayerId, PlayerControl voter)
    {
        var balancer = Roles.Crewmate.Balancer.Id;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.GetRoleClass() is not RoleBase role) continue;
            if (balancer != 255
                && !(suspectPlayerId == srcPlayerId || suspectPlayerId == Roles.Crewmate.Balancer.target1 || suspectPlayerId == Roles.Crewmate.Balancer.target2)
                && !pc.Is(CustomRoles.Balancer)
                && Roles.Crewmate.Balancer.OptionCanMeetingAbility.GetBool()) continue;
            try { if (!role.CheckAnyVote(suspectPlayerId, voter)) return false; }
            catch (System.Exception ex) { Logger.Exception(ex, "CheckAnyVote"); }
        }
        return true;
    }

    /// <summary>投票結果の書き換え。どれかが true を返すと同数時の特殊な決め方を使わない</summary>
    public static bool VotingResults(ref NetworkedPlayerInfo exiled, ref bool isTie, Dictionary<byte, int> votedCounts, byte[] mostVotedPlayers)
    {
        var skip = false;
        foreach (var role in AllRoles)
        {
            try
            {
                if (role.VotingResults(ref exiled, ref isTie, votedCounts, mostVotedPlayers, InClearAndExile))
                    skip = true;
            }
            catch (System.Exception ex) { Logger.Exception(ex, "VotingResults"); }
        }
        return skip;
    }

    /// <summary>会議の始め: 役職が積んだ文・役職の MeetingAddMessage・投票結果の文を、少し待ってから送る</summary>
    public static void OnMeetingStart()
    {
        MeetingStates.BeginMeeting();
        if (!AmongUsClient.Instance.AmHost) return;
        var sends = MeetingStartMessages.ToArray();
        MeetingStartMessages.Clear();
        var messages = new List<string>();
        ForEachRole(r =>
        {
            var m = r.MeetingAddMessage();
            if (!string.IsNullOrEmpty(m)) messages.Add(m);
        }, "MeetingAddMessage");
        if (!string.IsNullOrEmpty(MeetingVoteManager.Voteresult))
        {
            messages.Add(MeetingVoteManager.Voteresult);
            MeetingVoteManager.Voteresult = "";
        }
        if (sends.Length == 0 && messages.Count == 0) return;
        _ = new LateTask(() =>
        {
            foreach (var (sendTo, text, title) in sends) Utils.SendMessage(text, sendTo, title);
            foreach (var m in messages) Utils.SendMessage(m);
        }, 3.5f, "RoleHooks.MeetingStartMessages");
    }
}

public partial class Main
{
    // サボタージュの記録 (RoleHooks の配り口が書く。偽のサボタージュなどが見る)
    public static bool IsActiveSabotage;
    public static SystemTypes SabotageType;
    public static byte LastSab = byte.MaxValue;
    public static float SabotageActivetimer;
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
static class MeetingStartHookPatch
{
    public static void Postfix() => RoleHooks.OnMeetingStart();
}
[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoExitVent))]
static class ExitVentHookPatch
{
    public static void Prefix(PlayerPhysics __instance)
    {
        if (__instance?.myPlayer != null) RoleHooks.NowVentId.Remove(__instance.myPlayer.PlayerId);
    }
}

// ===== サボタージュ =====
[HarmonyPatch(typeof(SabotageSystemType), nameof(SabotageSystemType.UpdateSystem))]
static class SabotageStartHookPatch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix([HarmonyArgument(1)] MessageReader msgReader, out byte __state)
        => __state = RoleHooks.PeekByte(msgReader);
    public static void Postfix([HarmonyArgument(0)] PlayerControl player, byte __state, bool __runOriginal)
    {
        if (!__runOriginal || !AmongUsClient.Instance.AmHost || player == null) return;
        Main.SabotageType = (SystemTypes)__state;
        Main.IsActiveSabotage = true;
        Main.LastSab = player.PlayerId;
        Main.SabotageActivetimer = 0;
    }
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.FixedUpdate))]
static class SabotageTimerHookPatch
{
    public static void Postfix()
    {
        if (Main.IsActiveSabotage && GameStates.IsInTask) Main.SabotageActivetimer += Time.fixedDeltaTime;
    }
}
[HarmonyPatch]
static class FixSabotageHookPatch
{
    static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ReactorSystemType), nameof(ReactorSystemType.UpdateSystem));
        yield return AccessTools.Method(typeof(SwitchSystem), nameof(SwitchSystem.UpdateSystem));
        yield return AccessTools.Method(typeof(HqHudSystemType), nameof(HqHudSystemType.UpdateSystem));
        yield return AccessTools.Method(typeof(HudOverrideSystemType), nameof(HudOverrideSystemType.UpdateSystem));
        yield return AccessTools.Method(typeof(LifeSuppSystemType), nameof(LifeSuppSystemType.UpdateSystem));
        yield return AccessTools.Method(typeof(HeliSabotageSystem), nameof(HeliSabotageSystem.UpdateSystem));
    }
    [HarmonyPriority(Priority.First)]
    public static void Prefix([HarmonyArgument(1)] MessageReader msgReader, out byte __state)
        => __state = RoleHooks.PeekByte(msgReader);
    public static void Postfix([HarmonyArgument(0)] PlayerControl player, byte __state, bool __runOriginal)
    {
        if (!__runOriginal) return;
        RoleHooks.OnFixSabotage(player, __state);
    }
}
[HarmonyPatch]
static class SabotageCompleteHookPatch
{
    static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ElectricTask), nameof(ElectricTask.Complete));
        yield return AccessTools.Method(typeof(HeliCharlesTask), nameof(HeliCharlesTask.Complete));
        yield return AccessTools.Method(typeof(HqHudOverrideTask), nameof(HqHudOverrideTask.Complete));
        yield return AccessTools.Method(typeof(MushroomMixupSabotageTask), nameof(MushroomMixupSabotageTask.Complete));
        yield return AccessTools.Method(typeof(NoOxyTask), nameof(NoOxyTask.Complete));
        yield return AccessTools.Method(typeof(ReactorTask), nameof(ReactorTask.Complete));
        yield return AccessTools.Method(typeof(HudOverrideTask), nameof(HudOverrideTask.Complete));
    }
    public static void Postfix() => RoleHooks.CompleteSabotage();
}

// ===== ベント =====
[HarmonyPatch(typeof(VentilationSystem), nameof(VentilationSystem.UpdateSystem))]
static class VentilationSystemHookPatch
{
    public static void Prefix([HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] MessageReader msgReader)
    {
        if (!AmongUsClient.Instance.AmHost || player == null) return;
        VentilationSystem.Operation op;
        byte ventId;
        {
            var r = MessageReader.Get(msgReader);
            r.ReadUInt16();
            op = (VentilationSystem.Operation)r.ReadByte();
            ventId = r.ReadByte();
            r.Recycle();
        }
        RoleHooks.NowVentId[player.PlayerId] = ventId;
        RoleHooks.ForEachRole(r => r.OnVentilationSystemUpdate(player, op, ventId), "OnVentilationSystemUpdate");
    }
}
[HarmonyPatch(typeof(Vent), nameof(Vent.TryMoveToVent))]
static class TryMoveToVentHookPatch
{
    public static bool Prefix(Vent __instance, ref bool __result)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        var pc = PlayerControl.LocalPlayer;
        if (pc == null || RoleHooks.CanVentMoving(pc.MyPhysics, __instance.Id)) return true;
        __result = false;
        return false;
    }
}
