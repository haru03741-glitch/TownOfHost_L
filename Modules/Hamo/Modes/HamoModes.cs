using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHostL.Core.Modes;
using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;
using TownOfHostL.Modules;

namespace TownOfHostL;

public static partial class Options
{
    static void SetupHamoModeOptions()
    {
        SetupTaskBattleOptions();
        SetupMurderMysteryOptions();
        SetupKedoroOptions();
    }

    /// <summary>役職の設定 (確率・人数・子の設定) を、そのモードでだけ出す</summary>
    public static void MoveRoleOptionsToMode(CustomRoles role, CustomGameMode mode)
    {
        if (!CustomRoleSpawnChances.TryGetValue(role, out var spawn)) return;
        foreach (var opt in OptionItem.AllOptions)
        {
            var root = opt;
            while (root.Parent != null) root = root.Parent;
            if (root == spawn) opt.SetGameMode(mode);
        }
    }
}

public static class HamoModes
{
    public static bool IntroDone;

    [Attributes.GameModuleInitializer]
    public static void Init() => IntroDone = false;

    public static bool IsHamoMode => Options.CurrentGameMode is CustomGameMode.TaskBattle or CustomGameMode.MurderMystery
        or CustomGameMode.Kedoro;

    /// <summary>通常の役職の仕組みを使わないモードの割り当ての後始末 (CatchCat と同じ)</summary>
    public static void FinishAssign(GameEndRuleSet rules)
    {
        foreach (var pc in Main.AllPlayerControls)
        {
            HudManager.Instance.SetHudActive(true);
            Main.AllPlayerKillCooldown[pc.PlayerId] = Options.DefaultKillCooldown;
        }
        GameEndChecker.SetPredicateTo(rules);

        GameOptionsSender.AllSenders.Clear();
        foreach (var pc in Main.AllPlayerControls)
            if (!pc.AmOwner) GameOptionsSender.AllSenders.Add(new PlayerGameOptionsSender(pc));
        foreach (var pc in Main.AllPlayerControls)
            if (pc.AmOwner) GameOptionsSender.AllSenders.Add(new PlayerGameOptionsSender(pc));

        Utils.CountAlivePlayers(true);
        Utils.SyncAllSettings();
        SetColorPatch.IsAntiGlitchDisabled = false;
    }

    /// <summary>通常の役職の仕組みで配った後に、モードの勝敗と仕組みに差し替える</summary>
    public static void AfterStandardAssign()
    {
        switch (Options.CurrentGameMode)
        {
            case CustomGameMode.MurderMystery: MurderMystery.AfterAssign(); break;
        }
    }

    public static void ApplyGameOptions(PlayerControl player, IGameOptions opt)
    {
        switch (Options.CurrentGameMode)
        {
            case CustomGameMode.TaskBattle:
                if (Options.TaskBattleCanVent.GetBool())
                {
                    opt.SetFloat(FloatOptionNames.EngineerCooldown, Options.TaskBattleVentCooldown.GetFloat());
                    opt.SetFloat(FloatOptionNames.EngineerInVentMaxTime, 0f);
                }
                break;
            case CustomGameMode.MurderMystery: MurderMystery.ApplyGameOptions(player, opt); break;
            case CustomGameMode.Kedoro: Kedoro.ApplyGameOptions(player, opt); break;
        }
    }

    public static void OnCompleteTask(PlayerControl pc, TaskState taskState)
    {
        switch (Options.CurrentGameMode)
        {
            case CustomGameMode.TaskBattle: TaskBattle.OnCompleteTask(pc, taskState); break;
            case CustomGameMode.MurderMystery: MurderMystery.OnCompleteTask(pc); break;
            case CustomGameMode.Kedoro: Kedoro.OnCompleteTask(pc); break;
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    static class IntroEnd
    {
        static void Postfix()
        {
            IntroDone = true;
            if (!AmongUsClient.Instance.AmHost) return;
            TaskBattle.OnIntroEnd();
            MurderMystery.OnIntroEnd();
            Kedoro.OnIntroEnd();
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    static class Tick
    {
        static void Postfix(PlayerControl __instance)
        {
            if (!GameStates.IsInGame || !IsHamoMode || __instance == null) return;
            TaskBattle.OnFixedUpdate(__instance);
            if (!AmongUsClient.Instance.AmHost || __instance.PlayerId != PlayerControl.LocalPlayer.PlayerId) return;
            MurderMystery.OnFixedUpdate();
            Kedoro.OnFixedUpdate();
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class Hud
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix()
        {
            if (!GameStates.IsInGame || !IsHamoMode || HudManagerPatch.LowerInfoText == null) return;
            var text = TaskBattle.HudText();
            if (text == null) return;
            HudManagerPatch.LowerInfoText.enabled = true;
            HudManagerPatch.LowerInfoText.text = text;
        }
    }
}
