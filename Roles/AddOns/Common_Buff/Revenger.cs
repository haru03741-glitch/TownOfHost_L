using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using TownOfHostL.Attributes;

namespace TownOfHostL.Roles.AddOns.Common;

public static class Revenger
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.Revenger,
        AddOnGroup.Buff,
        "#00ffff",
        "Ｒ",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "リベンジャー",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonBuff + 600;
    private static List<byte> playerIdList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Revenger);
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