using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using TownOfHostL.Attributes;

namespace TownOfHostL.Roles.AddOns.Common;

public static class InfoPoor
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.InfoPoor,
        AddOnGroup.Debuff,
        "#556b2f",
        "Ip",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "インフォプアー",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonDebuff + 200;
    private static List<byte> playerIdList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.InfoPoor);
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
    }
    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);

}