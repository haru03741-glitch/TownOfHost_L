using System.Collections.Generic;
using System.Linq;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem CommnTaskResetAssing;
    public static OptionItem CanseeVoteresult;
    public static OptionItem OutroCrewWinreasonchenge;
    public static OptionItem OptionBatchSetting;
    public static OptionItem OptionAllImpostorKillCool;
    public static OptionItem OptionAllNeutralKillCool;

    static void SetupHamoConvenientOptions()
    {
        CommnTaskResetAssing = BooleanOptionItem.Create("CommnTaskResetAssing", false, TabGroup.ModMainSettings, false).SetGameMode(CustomGameMode.Standard);
        CanseeVoteresult = BooleanOptionItem.Create("CanseeVoteresult", false, TabGroup.ModMainSettings, false).SetGameMode(CustomGameMode.Standard);
        OutroCrewWinreasonchenge = BooleanOptionItem.Create("OutroCrewWinreasonchenge", false, TabGroup.ModMainSettings, false).SetGameMode(CustomGameMode.Standard);

        OptionBatchSetting = BooleanOptionItem.Create("OptionBatchSetting", false, TabGroup.ModMainSettings, false)
            .SetHeader(true)
            .SetColorcode("#6c831a");
        OptionAllImpostorKillCool = FloatOptionItem.Create("OptionAllImpostorKillCool", new(0f, 180f, 0.5f), 29.5f, TabGroup.ModMainSettings, false)
            .SetValueFormat(OptionFormat.Seconds)
            .SetParent(OptionBatchSetting)
            .RegisterUpdateValueEvent((_, _) => HamoConvenient.ApplyBatch(true));
        OptionAllNeutralKillCool = FloatOptionItem.Create("OptionAllNeutralKillCool", new(0f, 180f, 0.5f), 29.5f, TabGroup.ModMainSettings, false)
            .SetValueFormat(OptionFormat.Seconds)
            .SetParent(OptionBatchSetting)
            .RegisterUpdateValueEvent((_, _) => HamoConvenient.ApplyBatch(false));
    }
}

public static class HamoConvenient
{
    public static bool CommonReset => (Options.CommnTaskResetAssing?.GetBool() ?? false) && Options.CurrentGameMode == CustomGameMode.Standard;
    public static bool OutroCrew => (Options.OutroCrewWinreasonchenge?.GetBool() ?? false) && Options.CurrentGameMode == CustomGameMode.Standard;

    static Dictionary<OptionItem, CustomRoles> roleByRoot;

    static CustomRoles? RoleOf(OptionItem opt)
    {
        roleByRoot ??= Options.CustomRoleSpawnChances.ToDictionary(kv => (OptionItem)kv.Value, kv => kv.Key);
        for (var o = opt; o != null; o = o.Parent)
            if (roleByRoot.TryGetValue(o, out var role)) return role;
        return null;
    }

    public static void ApplyBatch(bool impostor)
    {
        if (Options.OptionBatchSetting == null || !Options.OptionBatchSetting.GetBool()) return;
        var value = (impostor ? Options.OptionAllImpostorKillCool : Options.OptionAllNeutralKillCool).GetFloat();
        foreach (var opt in OptionItem.AllOptions)
        {
            if (opt is not FloatOptionItem f || f.Name != "KillCooldown") continue;
            if (RoleOf(f) is not { } role) continue;
            if (impostor ? !role.IsImpostor() : !role.IsNeutral()) continue;
            var index = f.Rule.GetNearestIndex(value);
            if (f.CurrentValue != index) f.SetValue(index);
        }
    }
}
