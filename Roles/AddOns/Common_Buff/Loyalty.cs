using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using System.Linq;
using TownOfHostL.Attributes;

namespace TownOfHostL.Roles.AddOns.Common;

public static class Loyalty
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.Loyalty,
        AddOnGroup.Buff,
        "#b8fb4f",
        "Lo",
        onAdd: Add,
        chatCommand: "ロイヤルティ");

    private static List<byte> playerIdList = new();

    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
    }
    public static void Add(byte playerId)
    {
        playerIdList.Add(playerId);
        foreach (var target in Main.AllPlayerControls.Where(x => x.GetCustomRole().IsImpostor()))
        {
            NameColorManager.Add(playerId, target.PlayerId);
        }
    }
    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
}