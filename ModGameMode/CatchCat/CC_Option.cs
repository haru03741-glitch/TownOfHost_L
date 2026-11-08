using TownOfHostL.Roles.Core;
using TownOfHostL.Modules;
using UnityEngine;
using static Il2CppSystem.Uri;

namespace TownOfHostL.CatchCat;

static class Option
{
    public static OptionItem IgnoreReport;

    public static OptionItem LeaderIgnoreVent;
    public static OptionItem LeaderKilled;
    public static OptionItem LK_CatCount;
    public static OptionItem LK_OneGuard;

    public static OptionItem WhenColorCatKilled;
    public static OptionItem ColorCatShowSameCamp;
    public static OptionItem TaskCompleteAbility;
    public static OptionItem T_KnowAllLeader;
    public static OptionItem T_OneGuardOwn;
    public static OptionItem T_CanUseVent;
    public static OptionItem T_VentCooldown;
    public static OptionItem T_VentMaxTime;
    public static OptionItem T_OwnLeaderKillcoolDecrease;

    public static OptionItem M_LeaderRemain;
    public static OptionItem M_NeutralCatRemain;
    public static OptionItem M_RemainCatShowName;
    public static OptionItem M_RemainCatShowNameNum;
    public static OptionItem M_ColorCatCount;

    public enum ColorCatKill
    {
        CCatJustKill,
        COtherCatKill,
        CCatOverride,
        CCatAlwaysGuard,
    };

    public static void SetupCustomOption()
    {
        // CC の設定は機能の設定として作る (CC の設定一覧は自分の分を別に出し、機能の欄からは外す)
        OptionItem.CreatingFeatures = true;
        try { CreateOptions(); }
        finally { OptionItem.CreatingFeatures = false; }
    }
    private static void CreateOptions()
    {
        // 共通設定
        TextOptionItem.Create("CCCommonSetting", TabGroup.ModMainSettings)
            .SetColor(Palette.LightBlue)
            .SetGameMode(CustomGameMode.CatchCat);
        IgnoreReport = StringOptionItem.Create("IgnoreReport", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.LightBlue)
            .SetGameMode(CustomGameMode.CatchCat);

        // リーダー設定
        TextOptionItem.Create("CCLeaderSetting", TabGroup.ModMainSettings)
            .SetColor(Palette.Orange)
            .SetGameMode(CustomGameMode.CatchCat);
        SetupLeaderRoleOptions(CustomRoles.CCRedLeader);
        SetupLeaderRoleOptions(CustomRoles.CCBlueLeader);
        SetupAddLeaderRoleOptions(CustomRoles.CCYellowLeader);

        LeaderIgnoreVent = StringOptionItem.Create("IgnoreLeaderVent", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetHeader(true)
            .SetColor(Palette.Orange)
            .SetGameMode(CustomGameMode.CatchCat);
        LeaderKilled = StringOptionItem.Create("CCLeaderKilled", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.Orange)
            .SetGameMode(CustomGameMode.CatchCat);

        LK_CatCount = IntegerOptionItem.Create("CCLK_CatCount", new(0, 7, 1), 0, TabGroup.ModMainSettings, false)
            .SetParent(LeaderKilled)
            .SetValueFormat(OptionFormat.Players)
            .SetGameMode(CustomGameMode.CatchCat);
        LK_OneGuard = StringOptionItem.Create("CCLK_OneGuard", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetParent(LeaderKilled)
            .SetGameMode(CustomGameMode.CatchCat);

        // 猫設定
        TextOptionItem.Create("CCCatSetting", TabGroup.ModMainSettings)
            .SetColor(Color.gray)
            .SetGameMode(CustomGameMode.CatchCat);
        WhenColorCatKilled = StringOptionItem.Create("CCWhenColorCatKilled", EnumHelper.GetAllNames<ColorCatKill>(), 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.gray)
            .SetGameMode(CustomGameMode.CatchCat);
        ColorCatShowSameCamp = StringOptionItem.Create("CCColorCatShowSameCamp", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.gray)
            .SetGameMode(CustomGameMode.CatchCat);

        TaskCompleteAbility = StringOptionItem.Create("CCTaskCompleteAbility", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false)
            .SetColor(Color.gray)
            .SetGameMode(CustomGameMode.CatchCat);

        T_KnowAllLeader = IntegerOptionItem.Create("CCT_KnowAllLeader", new(0, 100, 10), 0, TabGroup.ModMainSettings, false)
            .SetParent(TaskCompleteAbility)
            .SetValueFormat(OptionFormat.Percent)
            .SetGameMode(CustomGameMode.CatchCat);
        T_OwnLeaderKillcoolDecrease = IntegerOptionItem.Create("CCT_OwnLeaderKillcoolDecrease", new(0, 100, 10), 0, TabGroup.ModMainSettings, false)
            .SetParent(TaskCompleteAbility)
            .SetValueFormat(OptionFormat.Percent)
            .SetGameMode(CustomGameMode.CatchCat);
        T_OneGuardOwn = IntegerOptionItem.Create("CCT_OneGuardOwn", new(0, 100, 10), 0, TabGroup.ModMainSettings, false)
            .SetParent(TaskCompleteAbility)
            .SetValueFormat(OptionFormat.Percent)
            .SetGameMode(CustomGameMode.CatchCat);
        T_CanUseVent = IntegerOptionItem.Create("CCT_CanUseVent", new(0, 100, 10), 0, TabGroup.ModMainSettings, false)
            .SetParent(TaskCompleteAbility)
            .SetValueFormat(OptionFormat.Percent)
            .SetGameMode(CustomGameMode.CatchCat);

        T_VentCooldown = FloatOptionItem.Create("VentCooldown", new(5f, 60f, 2.5f), 20f, TabGroup.ModMainSettings, false)
            .SetParent(T_CanUseVent)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.CatchCat);
        T_VentMaxTime = FloatOptionItem.Create("VentMaxTime", new(1f, 10f, 1f), 3f, TabGroup.ModMainSettings, false)
            .SetParent(T_CanUseVent)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.CatchCat);

        // 会議表示
        TextOptionItem.Create("CCMeetingDisplay", TabGroup.ModMainSettings)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.CatchCat);
        M_LeaderRemain = StringOptionItem.Create("CCM_LeaderRemain", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.CatchCat);
        M_NeutralCatRemain = StringOptionItem.Create("CCM_NeutralCatRemain", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.CatchCat);
        M_RemainCatShowName = StringOptionItem.Create("CCM_RemainCatShowName", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.CatchCat);
        M_RemainCatShowNameNum = IntegerOptionItem.Create("CCM_RemainCatShowNameNum", new(1, 13, 1), 2, TabGroup.ModMainSettings, false)
            .SetParent(M_RemainCatShowName)
            .SetValueFormat(OptionFormat.Players)
            .SetGameMode(CustomGameMode.CatchCat);
        M_ColorCatCount = StringOptionItem.Create("CCM_ColorCatCount", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.CatchCat);
    }

    private static void SetupLeaderRoleOptions(CustomRoles role)
    {
        // spawnOption は IntegerOptionItem 型で確実に扱う
        IntegerOptionItem spawnOption = IntegerOptionItem.Create(role.ToString() + "Fixed", new(100, 100, 1), 100, TabGroup.ModMainSettings, false);
        spawnOption.SetColor(Utils.GetRoleColor(role))
                   .SetValueFormat(OptionFormat.Percent)
                   .SetFixValue(true)
                   .SetGameMode(CustomGameMode.CatchCat);

        IntegerOptionItem countOption = IntegerOptionItem.Create("Maximum", new(1, 1, 1), 1, TabGroup.ModMainSettings, false);
        countOption.SetParent(spawnOption)
                   .SetValueFormat(OptionFormat.Players)
                   .SetGameMode(CustomGameMode.CatchCat);

        Options.CustomRoleSpawnChances.Add(role, spawnOption);
        Options.CustomRoleCounts.Add(role, countOption);
    }

    private static void SetupAddLeaderRoleOptions(CustomRoles role)
    {
        IntegerOptionItem spawnOption = IntegerOptionItem.Create(role.ToString(), new(0, 100, 100), 0, TabGroup.ModMainSettings, false);
        spawnOption.SetColor(Utils.GetRoleColor(role))
                   .SetValueFormat(OptionFormat.Percent)
                   .SetGameMode(CustomGameMode.CatchCat);

        IntegerOptionItem countOption = IntegerOptionItem.Create("Maximum", new(1, 1, 1), 1, TabGroup.ModMainSettings, false);
        countOption.SetParent(spawnOption)
                   .SetHidden(true)
                   .SetGameMode(CustomGameMode.CatchCat);

        Options.CustomRoleSpawnChances.Add(role, spawnOption);
        Options.CustomRoleCounts.Add(role, countOption);
    }


}
