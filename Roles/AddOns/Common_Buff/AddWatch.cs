using System.Collections.Generic;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;

namespace TownOfHostL.Roles.AddOns.Common;

public static class AddWatch
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.AddWatch,
        AddOnGroup.Buff,
        "#800080",
        "Ｗ",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        applyGameOptions: ApplyGameOptions,
        chatCommand: "ウォッチング",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonBuff + 200;
    private static List<byte> playerIdList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.AddWatch);
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
    public static void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetBool(BoolOptionNames.AnonymousVotes, false);
    }
    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);
}