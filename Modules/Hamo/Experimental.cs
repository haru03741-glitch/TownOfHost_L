using HarmonyLib;

namespace TownOfHostL;

/// <remarks>実験モード (会議明けの暗転・チャットの白黒) は TownOfHost-K が元 (TownOfHost-hamo 経由)。</remarks>
public static partial class Options
{
    public static OptionItem ExperimentalMode;
    public static OptionItem ExAftermeetingflash;
    public static OptionItem ExChatMonochrome;

    static void SetupExperimentalOptions()
    {
        ExperimentalMode = BooleanOptionItem.Create("ExperimentalMode", false, TabGroup.ModMainSettings, false)
            .SetHeader(true)
            .SetColor(Palette.CrewmateSettingChangeText);
        ExAftermeetingflash = BooleanOptionItem.Create("ExAftermeetingflash", false, TabGroup.ModMainSettings, false).SetParent(ExperimentalMode);
        ExChatMonochrome = BooleanOptionItem.Create("ExChatMonochrome", false, TabGroup.ModMainSettings, false).SetParent(ExperimentalMode);
    }
}

public static class HamoExperimental
{
    static bool On(OptionItem opt) => Options.ExperimentalMode != null && Options.ExperimentalMode.GetBool() && opt.GetBool();

    public static void AfterExile()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!On(Options.ExAftermeetingflash)) return;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default || FirstTurnMeeting.InFirst) return;
        if (Main.NormalOptions.MapId is 4)
            _ = new LateTask(() => Utils.AllPlayerKillFlash(), 3f, "AftermeetingFlash");
        else
            Utils.AllPlayerKillFlash();
    }

    static int monoDepth;
    public static bool Monochrome => monoDepth > 0 && On(Options.ExChatMonochrome);

    [HarmonyPatch(typeof(Utils))]
    static class MonochromePatch
    {
        [HarmonyTargetMethods]
        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> Targets()
        {
            foreach (var name in new[] { nameof(Utils.ShowLastResult), nameof(Utils.ShowKillLog), nameof(Utils.ShowActiveSettings), nameof(Utils.ShowActiveRoles) })
                yield return AccessTools.Method(typeof(Utils), name);
        }
        static void Prefix() => monoDepth++;
        static void Finalizer() { if (monoDepth > 0) monoDepth--; }
    }
}
