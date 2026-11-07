using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Options;
using TownOfHostL.Attributes;

namespace TownOfHostL.Roles.AddOns.Common;

public static class PlusVote
{
    public static readonly AddOnInfo Info = new(
        CustomRoles.PlusVote,
        AddOnGroup.Buff,
        "#204d42",
        "Pv",
        setupOptions: SetupCustomOption,
        onAdd: Add,
        chatCommand: "プラスボート",
        assignAtRandom: true);

    private static readonly int Id = (int)offsetId.AddonBuff + 900;
    private static List<byte> playerIdList = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.PlusVote);
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
    public static int OnVote(byte voter, int numVotes)
    {
        if (playerIdList.Contains(voter)) numVotes += 1;

        return numVotes;
    }

    public static bool IsEnable => playerIdList.Count > 0;
    public static bool IsThisRole(byte playerId) => playerIdList.Contains(playerId);

}