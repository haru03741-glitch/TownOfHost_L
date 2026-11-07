using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using TownOfHostL.Attributes;

namespace TownOfHostL.Roles.AddOns.Common;

public static class AddBait
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.AddBait,
        AddOnGroup.Buff,
        "#00f7ff",
        "Ｂ",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        flags: RoleFlags.ReportableByAll,
        chatCommand: "ベイティング",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonBuff + 1100;
    private static List<byte> playerIdList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.AddBait);
    }
    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
    }
    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId))
            playerIdList.Add(playerId);
        CustomRoleManager.OnMurderPlayerOthers.Add(OnMurderPlayerOthers);
    }

    public static void OnMurderPlayerOthers(MurderInfo info)
    {
        if (info.IsMeeting) return;

        var (killer, target) = info.AttemptTuple;

        if (playerIdList.Contains(target.PlayerId) && !info.IsSuicide)
            _ = new LateTask(() =>
            {
                killer.CmdReportDeadBody(target.Data);
            }, 0.15f, "AddBait Self Report");
    }

    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
}