namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem ONspecialMode;
    public static OptionItem InsiderMode;
    public static OptionItem InsiderModeCanSeeTask;
    public static OptionItem CanSeeImpostorRole;

    static void SetupSpecialModeOptions()
    {
        ONspecialMode = BooleanOptionItem.Create("ONspecialMode", false, TabGroup.ModMainSettings, false)
            .SetHeader(true)
            .SetColorcode("#00c1ff");
        InsiderMode = BooleanOptionItem.Create("InsiderMode", false, TabGroup.ModMainSettings, false).SetParent(ONspecialMode);
        InsiderModeCanSeeTask = BooleanOptionItem.Create("InsiderModeCanSeeTask", false, TabGroup.ModMainSettings, false).SetParent(InsiderMode);
        CanSeeImpostorRole = BooleanOptionItem.Create("CanSeeImpostorRole", false, TabGroup.ModMainSettings, false).SetParent(ONspecialMode);
    }
}

public static class SpecialMode
{
    static bool On(OptionItem opt) => opt != null && opt.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard;

    public static bool IsInsider => On(Options.InsiderMode);

    public static bool KnowsRole(PlayerControl seer, PlayerControl seen)
    {
        if (seer == null || seen == null) return false;
        var seerRole = seer.GetCustomRole();
        if (IsInsider && seerRole.IsImpostor()) return true;
        return On(Options.CanSeeImpostorRole) && seerRole.IsImpostor() && seen.GetCustomRole().IsImpostor();
    }

    public static bool KnowsTasks(PlayerControl seer)
        => seer != null && IsInsider && On(Options.InsiderModeCanSeeTask) && seer.GetCustomRole().IsImpostor();
}
