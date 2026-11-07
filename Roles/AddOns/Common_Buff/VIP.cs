using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using TownOfHostL.Attributes;

namespace TownOfHostL.Roles.AddOns.Common;

public static class VIP
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.VIP,
        AddOnGroup.Buff,
        "#ffff00",
        "Ｖ",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "VIP",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonBuff + 500;
    private static List<byte> playerIdList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.VIP);
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