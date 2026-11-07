using System.Collections.Generic;
using UnityEngine;

using TownOfHostL.Roles.Core;
using TownOfHostL.Attributes;
using static TownOfHostL.Options;

namespace TownOfHostL.Roles.AddOns.Common;

public static class Guarding
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.Guarding,
        AddOnGroup.Buff,
        "#8cffff",
        "Ｇ",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "ガーディング",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonBuff + 1000;
    private static List<byte> playerIdList = new();

    /// <summary>
    /// ガード未使用の場合Listにいる
    /// </summary>
    public static List<byte> GuardingList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Guarding);
    }
    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
        GuardingList = new();
    }
    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId))
            playerIdList.Add(playerId);
        if (!GuardingList.Contains(playerId))
            GuardingList.Add(playerId);
    }

    /// <summary>
    /// 使用する時true
    /// </summary>
    public static bool OnCheckMurder(MurderInfo info)
    {
        (var killer, var target) = info.AttemptTuple;
        if (!GuardingList.Contains(target.PlayerId)) return false;
        // 直接キル出来る役職チェック
        if (killer.GetCustomRole().IsDirectKillRole()) return false;

        killer.RpcProtectedMurderPlayer(target);
        info.CanKill = false;

        GuardingList.Remove(target.PlayerId);
        Logger.Info($"{killer.GetNameWithRole()}->{target.GetNameWithRole()}:ガード", "Guarding");

        return true;
    }
    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
}