using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using TownOfHostL.Attributes;

namespace TownOfHostL.Roles.AddOns.Common;

public static class Refusing
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.Refusing,
        AddOnGroup.Buff,
        "#61b26c",
        "Rf",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "リフュージング",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonBuff + 1200;
    private static List<byte> playerIdList = new();
    private static List<byte> IgnoreExiled = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.Refusing);
    }
    [GameModuleInitializer]
    public static void Init()
    {
        playerIdList = new();
        IgnoreExiled = new();
    }
    public static void Add(byte playerId)
    {
        if (!playerIdList.Contains(playerId))
            playerIdList.Add(playerId);
        if (!IgnoreExiled.Contains(playerId))
            IgnoreExiled.Add(playerId);
    }
    public static NetworkedPlayerInfo VoteChange(NetworkedPlayerInfo Exiled)
    {
        if (Exiled == null || !IgnoreExiled.Contains(Exiled.PlayerId)) return Exiled;

        IgnoreExiled.Remove(Exiled.PlayerId);
        return null;
    }

    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
}