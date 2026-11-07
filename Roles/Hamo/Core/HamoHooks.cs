using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL;

/// <summary>
/// TOH_L のパッチから hamo の役職のフックを呼ぶ所
/// </summary>
public static class HamoHooks
{
    public const byte SkipVoteId = MeetingVoteManager.Skip;
    /// <summary>今完了したタスクの番号 (CompleteTask の中だけ有効)</summary>
    public static uint CurrentTaskId;
    /// <summary>ClearAndExile 系で会議を終えている途中か</summary>
    public static bool InClearAndExile;

    public static IEnumerable<HamoRoleBase> AllHamoRoles => CustomRoleManager.AllActiveRoles.Values.OfType<HamoRoleBase>().ToArray();

    /// <summary>hamo 形式の役職名の書き換えを TOH_L 形式へつなぐ</summary>
    public static void ForwardRoleName(PlayerControl owner, PlayerControl seer, PlayerControl seen, ref bool enabled, ref string roleText, bool asSeen)
    {
        if (owner.GetRoleClass() is not HamoRoleBase role) return;
        var seenRole = seen.GetCustomRole();
        var color = Utils.GetRoleColor(seenRole);
        var text = Utils.GetRoleName(seenRole);
        var origColor = color;
        var origText = text;
        bool addon = false;
        if (asSeen) role.OverrideDisplayRoleNameAsSeen(seer, ref enabled, ref color, ref text, ref addon);
        else role.OverrideDisplayRoleNameAsSeer(seen, ref enabled, ref color, ref text, ref addon);
        if (color != origColor || text != origText)
            roleText = Utils.ColorString(color, text);
    }

    /// <summary>hamo の役職がいる時は、他の人の名前も作り直す</summary>
    public static bool NeedsOtherNameLoop(PlayerControl seer)
        => CustomRoleManager.AllActiveRoles.Values.Any(r => r is HamoRoleBase);

    /// <summary>
    /// hamo の CheckVoteAsVoter を全員の役職で呼ぶ。どれかが false なら投票を取り消す。
    /// 天秤の会議中は、天秤の対象以外への投票を天秤だけが見る。
    /// </summary>
    public static bool CheckVoteAsVoter(byte srcPlayerId, byte suspectPlayerId, PlayerControl voter)
    {
        var balancer = TownOfHostL.Roles.Hamo.Crewmate.Balancer.Id;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.GetRoleClass() is not HamoRoleBase role) continue;
            if (balancer != 255
                && !(suspectPlayerId == srcPlayerId || suspectPlayerId == TownOfHostL.Roles.Hamo.Crewmate.Balancer.target1 || suspectPlayerId == TownOfHostL.Roles.Hamo.Crewmate.Balancer.target2)
                && !pc.Is(CustomRoles.Balancer)
                && TownOfHostL.Roles.Hamo.Crewmate.Balancer.OptionCanMeetingAbility.GetBool()) continue;
            if (!role.CheckVoteAsVoter(suspectPlayerId, voter)) return false;
        }
        return true;
    }

    /// <summary>投票結果の書き換え。どれかが true を返すと同数時の特殊モードを使わない</summary>
    public static bool VotingResults(ref NetworkedPlayerInfo exiled, ref bool isTie, Dictionary<byte, int> votedCounts, byte[] mostVotedPlayers)
    {
        var skip = false;
        foreach (var role in AllHamoRoles)
        {
            if (role.VotingResults(ref exiled, ref isTie, votedCounts, mostVotedPlayers, InClearAndExile))
                skip = true;
        }
        return skip;
    }
}
