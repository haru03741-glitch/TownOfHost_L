using System.Linq;
using HarmonyLib;
using TownOfHostL.Roles.Core;
using TownOfHostL.Core.Win;

namespace TownOfHostL;

/// <remarks>サボタージュの勝ちの役職・時間切れの全滅・通信妨害と停電に触れない時間の設定は TownOfHost-K が元 (TownOfHost-hamo 経由)。</remarks>
public static partial class Options
{
    public static OptionItem ChangeSabotageWinRole;
    public static OptionItem OptionSabotageFinAllKill;
    public static OptionItem CommsSpecialSettings;
    public static OptionItem CommsDonttouch;
    public static OptionItem CommsDonttouchTime;
    public static OptionItem LightsOutSpecialSettings;
    public static OptionItem LightOutDonttouch;
    public static OptionItem LightOutDonttouchTime;

    static void SetupHamoSabotageOptions()
    {
        ChangeSabotageWinRole = BooleanOptionItem.Create("ChangeSabotageWinRole", false, TabGroup.ModMainSettings, false).SetGameMode(CustomGameMode.Standard);
        OptionSabotageFinAllKill = BooleanOptionItem.Create("OptionSabotageFinAllKill", false, TabGroup.ModMainSettings, false).SetGameMode(CustomGameMode.Standard);
        CommsSpecialSettings = BooleanOptionItem.Create("CommsSpecialSettings", false, TabGroup.ModMainSettings, false).SetColorcode("#999999").SetGameMode(CustomGameMode.Standard);
        CommsDonttouch = BooleanOptionItem.Create("CommsDonttouch", false, TabGroup.ModMainSettings, false).SetParent(CommsSpecialSettings);
        CommsDonttouchTime = FloatOptionItem.Create("CommsDonttouchTime", new(0f, 180f, 0.5f), 3.0f, TabGroup.ModMainSettings, false).SetParent(CommsDonttouch)
            .SetValueFormat(OptionFormat.Seconds);
        LightsOutSpecialSettings = BooleanOptionItem.Create("LightsOutSpecialSettings", false, TabGroup.ModMainSettings, false).SetColorcode("#ffcc66").SetGameMode(CustomGameMode.Standard);
        LightOutDonttouch = BooleanOptionItem.Create("LightOutDonttouch", false, TabGroup.ModMainSettings, false).SetParent(LightsOutSpecialSettings);
        LightOutDonttouchTime = FloatOptionItem.Create("LightOutDonttouchTime", new(0f, 180f, 0.5f), 3.0f, TabGroup.ModMainSettings, false).SetParent(LightOutDonttouch)
            .SetValueFormat(OptionFormat.Seconds);
    }
}

public static class HamoSabotage
{
    static bool Standard => Options.CurrentGameMode == CustomGameMode.Standard;
    static bool On(OptionItem opt) => opt != null && opt.GetBool() && Standard;

    public static void ApplyWinner()
    {
        if (!On(Options.ChangeSabotageWinRole)) return;
        var saboteur = Utils.GetPlayerById(Main.LastSab);
        if (saboteur == null) return;
        if (saboteur.Is(CustomRoleTypes.Impostor) || saboteur.Is(CustomRoleTypes.Madmate) || saboteur.Is(CustomRoles.SKMadmate)) return;
        var faction = GameEndRules.FactionOf(saboteur.GetCountTypes());
        if (faction == null || faction.Winner == CustomWinner.Impostor) return;
        CustomWinnerHolder.ResetAndSetWinner(faction.Winner);
        foreach (var role in faction.WinnerRoles) CustomWinnerHolder.WinnerRoles.Add(role);
        foreach (var role in faction.AdditionalWinnerRoles) CustomWinnerHolder.AdditionalWinnerRoles.Add(role);
        faction.AddWinners?.Invoke();
    }

    public static void AfterResolve(GameOverReason reason)
    {
        if (reason != GameOverReason.ImpostorsBySabotage) return;
        if (!On(Options.OptionSabotageFinAllKill)) return;
        foreach (var pc in Main.AllAlivePlayerControls.Where(p => !p.IsWinner()).ToArray())
        {
            var state = PlayerState.GetByPlayerId(pc.PlayerId);
            if (state != null) state.DeathReason = CustomDeathReason.Kill;
            Kill.Suicide(pc);
        }
    }

    static bool Blocks(SystemTypes type, OptionItem parent, OptionItem opt, OptionItem time)
    {
        if (!AmongUsClient.Instance.AmHost || !On(parent) || !opt.GetBool()) return false;
        if (!Main.IsActiveSabotage || Main.SabotageType != type) return false;
        return time.GetFloat() > Main.SabotageActivetimer;
    }

    [HarmonyPatch]
    static class CommsFixPatch
    {
        static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(HqHudSystemType), nameof(HqHudSystemType.UpdateSystem));
            yield return AccessTools.Method(typeof(HudOverrideSystemType), nameof(HudOverrideSystemType.UpdateSystem));
        }
        [HarmonyPriority(Priority.High)]
        static bool Prefix() => !Blocks(SystemTypes.Comms, Options.CommsSpecialSettings, Options.CommsDonttouch, Options.CommsDonttouchTime);
    }

    [HarmonyPatch(typeof(SwitchSystem), nameof(SwitchSystem.UpdateSystem))]
    static class LightsFixPatch
    {
        [HarmonyPriority(Priority.High)]
        static bool Prefix() => !Blocks(SystemTypes.Electrical, Options.LightsOutSpecialSettings, Options.LightOutDonttouch, Options.LightOutDonttouchTime);
    }
}
