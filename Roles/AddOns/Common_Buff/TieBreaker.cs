using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using TownOfHostL.Attributes;

namespace TownOfHostL.Roles.AddOns.Common;

public static class TieBreaker
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.TieBreaker,
        AddOnGroup.Buff,
        "#204d42",
        "Tb",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "タイブレーカー",
        assignAtRandom: true);

    private static List<byte> playerIdList = new();
    private static Dictionary<byte, byte> TieBreakerVote = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(TabGroup.Addons, CustomRoles.TieBreaker);
    }
    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
        TieBreakerVote = new();
    }
    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId))
            playerIdList.Add(playerId);
    }

    public static void OnVote(byte voter, byte votedFor)
    {
        if (!playerIdList.Contains(voter)) return;

        Logger.Info($"{Utils.GetPlayerById(voter).GetNameWithRole()} が タイブレーカー投票({Utils.GetPlayerById(votedFor)?.GetNameWithRole() ?? votedFor.ToString()})", "TieBreaker");
        // 投票し直した時は上書きする
        TieBreakerVote[voter] = votedFor;
    }
    /// <summary>会議の始め。前の会議の票が結果を決める所を通らずに残っていても、持ち越さない</summary>
    public static void OnStartMeeting() => TieBreakerVote.Clear();
    public static (bool, NetworkedPlayerInfo) BreakingVote(bool IsTie, NetworkedPlayerInfo Exiled, Dictionary<byte, int> votedCounts, int maxVoteNum)
    {
        if (TieBreakerVote.Count == 0) return (IsTie, Exiled);

        if (IsTie)
        {
            var tiebreakerUse = false;
            var tiebreakerCollision = false;
            foreach (var data in votedCounts.Where(x => x.Value == maxVoteNum))
            {
                if (TieBreakerVote.ContainsValue(data.Key))
                {
                    if (tiebreakerUse) tiebreakerCollision = true;
                    Exiled = Utils.GetPlayerInfoById(data.Key);
                    tiebreakerUse = true;
                    Logger.Info($"{Exiled?.PlayerName}がTieBreakerで優先", "TieBreaker");
                }
            }
            if (tiebreakerCollision)
            {
                Logger.Info("TieBreakerの衝突", "TieBreaker");
                Exiled = null;
            }
            else
                IsTie = false;
        }
        TieBreakerVote.Clear();

        return (IsTie, Exiled);
    }

    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
}