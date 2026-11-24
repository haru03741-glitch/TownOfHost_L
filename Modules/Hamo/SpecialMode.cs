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
        // 名前の組み立てで全員の組ごとに呼ばれるので、設定を先に見て役職を引かずに抜ける
        if (seer == null || seen == null) return false;
        bool insider = IsInsider;
        if (!insider && !On(Options.CanSeeImpostorRole)) return false;
        if (!seer.GetCustomRole().IsImpostor()) return false;
        return insider || seen.GetCustomRole().IsImpostor();
    }

    public static bool KnowsTasks(PlayerControl seer)
        => seer != null && IsInsider && On(Options.InsiderModeCanSeeTask) && seer.GetCustomRole().IsImpostor();
}
