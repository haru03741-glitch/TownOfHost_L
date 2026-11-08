using System.Collections.Generic;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Unit;
public sealed class JackOLantern : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(JackOLantern),
            player => new JackOLantern(player),
            CustomRoles.JackOLantern,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Unit,
            (int)Options.offsetId.UnitSpecial + 0,
            //(int)Options.offsetId.UnitMix + 100,
            null,
            "ジャック・オー・ランタン",
            "#e5a323",
            assignInfo: new RoleAssignInfo(CustomRoles.JackOLantern, CustomRoleTypes.Unit)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = new CustomRoles[3] { CustomRoles.Jack, CustomRoles.jO, CustomRoles.Lantern }
            },
            flags: RoleFlags.Pair | RoleFlags.DontShowOption  // 期間限定
        );
    public JackOLantern(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }
    // ジャック
    public static PlayerControl jack = null;

    public static OptionItem OptionJackKillCooldown;
    public static OptionItem OptionJackRoleRemoveKillCount;
    public static OptionItem OptionjORoleDetectCooldown;
    public static OptionItem OptionjOCanDetectNeutral;
    public static OptionItem OptionjOSendRoleCount;
    public static OptionItem OptionjOCanVent;
    public static OptionItem OptionLanternVisionDuringFixLight;
    public static OptionItem OptionLanternRoleReceivedExcludeNormalCrewmate;

    public static HashSet<byte> SentPlayer = new(15); //ジャック側で初期化
    enum OptionName
    {
        JackKillCooldown,
        JackRoleRemoveKillCount,
        jORoleDetectCooldown,
        jOCanDetectNeutral,
        jOSendRoleCount,
        jOCanVent,
        LanternVisionDuringFixLight,
        LanternRoleReceivedExcludeNormalCrewmate
    }
    // 直接設置
    public static void SetupRoleOptions()
    {
        TextOptionItem.Create("Head.LimitedTimeRoleH", TabGroup.UnitRoles)
            .SetColor(RoleInfo.RoleColor);
        var spawnOption = IntegerOptionItem.Create(CustomRoles.JackOLantern.ToString(), new(0, 100, 10), 0, TabGroup.UnitRoles, false)
            .SetColor(RoleInfo.RoleColor)
            .SetValueFormat(OptionFormat.Percent)
            .SetGameMode(CustomGameMode.Standard) as IntegerOptionItem;
        var countOption = IntegerOptionItem.Create("Maximum", new(1, 1, 1), 1, TabGroup.UnitRoles, false)
            .SetParent(spawnOption)
            .SetValueFormat(OptionFormat.Pair)
            .SetFixValue(true)
            .SetGameMode(CustomGameMode.Standard);

        Options.CustomRoleSpawnChances.Add(RoleInfo.RoleName, spawnOption);
        Options.CustomRoleCounts.Add(RoleInfo.RoleName, countOption);

        Dictionary<string, string> jackDic = new() { { "%jack%", Utils.ColorString(Palette.ImpostorRed, Utils.GetRoleName(CustomRoles.Jack)) } };
        Dictionary<string, string> joDic = new() { { "%jo%", Utils.ColorString(Palette.ImpostorRed, Utils.GetRoleName(CustomRoles.jO)) } };
        Dictionary<string, string> lanternDic = new() { { "%lantern%", Utils.ColorString(RoleInfo.RoleColor, Utils.GetRoleName(CustomRoles.Lantern)) } };

        // ジャック
        OptionJackKillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.JackKillCooldown, new(5.0f, 180f, 2.5f), 30f, false).SetReplacementDictionary(jackDic)
            .SetValueFormat(OptionFormat.Seconds);
        OptionJackRoleRemoveKillCount = IntegerOptionItem.Create(RoleInfo, OptionName.JackRoleRemoveKillCount, new(0, 15, 1), 2, false).SetReplacementDictionary(jackDic)
            .SetValueFormat(OptionFormat.Pieces);

        // オー
        OptionjORoleDetectCooldown = FloatOptionItem.Create(RoleInfo, OptionName.jORoleDetectCooldown, new(5.0f, 180f, 2.5f), 45f, false).SetReplacementDictionary(joDic)
            .SetValueFormat(OptionFormat.Seconds);
        OptionjOCanDetectNeutral = BooleanOptionItem.Create(RoleInfo, OptionName.jOCanDetectNeutral, false, false).SetReplacementDictionary(joDic);
        OptionjOSendRoleCount = IntegerOptionItem.Create(RoleInfo, OptionName.jOSendRoleCount, new(0, 15, 1), 2, false).SetReplacementDictionary(joDic)
            .SetValueFormat(OptionFormat.Pieces);
        OptionjOCanVent = BooleanOptionItem.Create(RoleInfo, OptionName.jOCanVent, true, false).SetReplacementDictionary(joDic);
        Options.SetUpAddOnOptions(CustomRoles.jO, RoleInfo.Tab, RoleInfo.RoleName, true);

        // ランタン
        OptionLanternVisionDuringFixLight = IntegerOptionItem.Create(RoleInfo, OptionName.LanternVisionDuringFixLight, new(20, 80, 10), 50, false).SetReplacementDictionary(lanternDic)
            .SetValueFormat(OptionFormat.Percent);
        OptionLanternRoleReceivedExcludeNormalCrewmate = BooleanOptionItem.Create(RoleInfo, OptionName.LanternRoleReceivedExcludeNormalCrewmate, false, false).SetReplacementDictionary(lanternDic);
    }
}
