using System.Collections.Generic;
using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Unit;
public sealed class GodfatherAndJanitor : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(GodfatherAndJanitor),
            player => new GodfatherAndJanitor(player),
            CustomRoles.GodfatherAndJanitor,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Unit,
            (int)Options.offsetId.UnitImp + 100,
            SetupOptionItem,
            "ゴッドファーザー&ジャニター",
            "#ffffff",
            tab: TabGroup.UnitRoles,
            assignInfo: new RoleAssignInfo(CustomRoles.GodfatherAndJanitor, CustomRoleTypes.Impostor)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = new CustomRoles[2] { CustomRoles.Godfather, CustomRoles.Janitor }
            },
            flags: RoleFlags.Pair
        );
    public GodfatherAndJanitor(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }
    // ジャニターターゲット格納
    public static HashSet<byte> JanitorTarget = new(15);
    // ゴッドファーザー
    public static PlayerControl godfather = null;
    // ジャニター
    public static PlayerControl janitor = null;

    public static OptionItem OptionGodfatherKillCooldown;
    public static OptionItem OptionGodfatherLockDistance;
    public static OptionItem OptionJanitorCleanCooldown;
    public static OptionItem OptionJanitorSeeSelectedTiming;
    public static OptionItem OptionJanitorTrackTarget;
    public static OptionItem OptionJanitorTrackGodfather;
    public static OptionItem OptionJanitorIsAliveAfterGodfatherDead;
    public static OptionItem OptionJanitorCanKill;
    public static OptionItem OptionJanitorKillCooldown;
    enum OptionName
    {
        GodfatherKillCooldown,
        GodfatherLockDistance,
        JanitorCleanCooldown,
        JanitorSeeSelectedTiming,
        JanitorTrackTarget,
        JanitorTrackGodfather,
        JanitorAfterGotfatherDeadIsAlive,
        JanitorCanKill,
        JanitorKillCooldown,
    }
    private static void SetupOptionItem()
    {
        Dictionary<string, string> godfatherDic = new() { { "%godfather%", Utils.ColorString(Palette.ImpostorRed, Utils.GetRoleName(CustomRoles.Godfather)) } };
        Dictionary<string, string> janitorDic = new() { { "%janitor%", Utils.ColorString(Palette.ImpostorRed, Utils.GetRoleName(CustomRoles.Janitor)) } };

        OptionGodfatherKillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.GodfatherKillCooldown, new(5.0f, 180f, 2.5f), 30f, false).SetReplacementDictionary(godfatherDic)
            .SetValueFormat(OptionFormat.Seconds);
        OptionGodfatherLockDistance = FloatOptionItem.Create(RoleInfo, OptionName.GodfatherLockDistance, new(1.0f, 20f, 0.5f), 10f, false).SetReplacementDictionary(godfatherDic)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionJanitorCleanCooldown = FloatOptionItem.Create(RoleInfo, OptionName.JanitorCleanCooldown, new(5.0f, 180f, 2.5f), 30f, false).SetReplacementDictionary(janitorDic)
            .SetValueFormat(OptionFormat.Seconds);
        OptionJanitorSeeSelectedTiming = BooleanOptionItem.Create(RoleInfo, OptionName.JanitorSeeSelectedTiming, true, false).SetReplacementDictionary(janitorDic);
        OptionJanitorTrackTarget = BooleanOptionItem.Create(RoleInfo, OptionName.JanitorTrackTarget, true, false).SetReplacementDictionary(janitorDic);
        OptionJanitorTrackGodfather = BooleanOptionItem.Create(RoleInfo, OptionName.JanitorTrackGodfather, true, false).SetReplacementDictionary(janitorDic);
        OptionJanitorIsAliveAfterGodfatherDead = BooleanOptionItem.Create(RoleInfo, OptionName.JanitorAfterGotfatherDeadIsAlive, true, false).SetReplacementDictionary(janitorDic);
        OptionJanitorCanKill = BooleanOptionItem.Create(RoleInfo, OptionName.JanitorCanKill, true, false, OptionJanitorIsAliveAfterGodfatherDead).SetReplacementDictionary(janitorDic);
        OptionJanitorKillCooldown = FloatOptionItem.Create(RoleInfo, OptionName.JanitorKillCooldown, new(0f, 180f, 2.5f), 30f, false, OptionJanitorCanKill).SetReplacementDictionary(janitorDic)
            .SetValueFormat(OptionFormat.Seconds);
    }
}