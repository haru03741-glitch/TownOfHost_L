using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;

namespace TownOfHostL.Roles.Core;

public partial class SimpleRoleInfo
{
    /// <summary>hamo の役職か</summary>
    public bool IsHamoRole { get; private set; }
    public (int TabNumber, int SortNumber) OptionSort { get; private set; }
    public CombinationRoles Combination { get; private set; } = CombinationRoles.None;
    public From From { get; private set; } = From.None;
    public bool IsCantSeeTeammates { get; private set; }
    public Func<CustomRoles?> AddHaveRole { get; private set; }
    public Func<string> Desc { get; private set; }
    public bool IsNewRole { get; private set; }

    /// <summary>hamo の SimpleRoleInfo.Create と同じ引数で作る</summary>
    public const int HamoConfigIdBase = 3_000_000;
    public static SimpleRoleInfo CreateHamo(
        Type classType,
        Func<PlayerControl, RoleBase> createInstance,
        CustomRoles roleName,
        Func<RoleTypes> baseRoleType,
        CustomRoleTypes customRoleType,
        int configId,
        OptionCreatorDelegate optionCreator,
        string chatCommand,
        string colorCode = "",
        (int TabNumber, int SortNumber)? OptionSort = null,
        bool isDesyncImpostor = false,
        TabGroup tab = TabGroup.ModMainSettings,
        Func<AudioClip> introSound = null,
        Func<bool> canMakeMadmate = null,
        CountTypes? countType = null,
        RoleAssignInfo assignInfo = null,
        CombinationRoles combination = CombinationRoles.None,
        From from = From.None,
        bool isCantSeeTeammates = false,
        Func<CustomRoles?> AddHaveRole = null,
        Func<string> Desc = null,
        bool isNewRole = false)
    {
        var flags = RoleFlags.None;
        // 組み合わせ役職の相方 (ブレイド・マーリン) は単独の出現設定を出さない (親の役職と一緒に出る)
        if (combination != CombinationRoles.None && assignInfo?.AssignUnitRoles == null) flags |= RoleFlags.DontShowOption;
        // hamo の番号は TOH_L の番号と重なるので、別の所へずらす
        var info = Create(classType, createInstance, roleName, baseRoleType, customRoleType, HamoConfigIdBase + configId, optionCreator, chatCommand,
            colorCode, isDesyncImpostor, tab, introSound, canMakeMadmate, countType, assignInfo, flags);
        info.IsHamoRole = true;
        info.OptionSort = OptionSort ?? (0, 0);
        info.Combination = combination;
        info.From = from;
        info.IsCantSeeTeammates = isCantSeeTeammates;
        info.AddHaveRole = AddHaveRole;
        info.Desc = Desc;
        info.IsNewRole = isNewRole;
        return info;
    }
}
