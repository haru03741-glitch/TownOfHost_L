using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using TownOfHostL.Modules;
using static TownOfHostL.Translator;

namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem GhostCanSeeKillerColor;
    public static OptionItem GhostCanSeeAllTasks;
    public static OptionItem GhostCanSeeNumberOfButtonsOnOthers;
    public static OptionItem GhostCanSeeKillflash;
    public static OptionItem GhostIgnoreTasksplayer;

    public static OptionItem LadderDeathNuuun;
    public static OptionItem LadderDeathZipline;

    public static OptionItem MeetingAndVoteOpt;
    public static OptionItem LowerLimitVotingTime;
    public static OptionItem MeetingTimeLimit;
    public static OptionItem ShowVoteResult;
    public static OptionItem ShowVoteJudgment;
    public static readonly string[] ShowVoteJudgments = { "Impostor", "Neutral", "CrewMate(Mad)", "Crewmate", "Role", "ShowTeam" };

    static void SetupHamoGhostOptions()
    {
        GhostCanSeeKillerColor = BooleanOptionItem.Create("GhostCanSeeKillerColor", false, TabGroup.ModMainSettings, true).SetParent(GhostCanSeeDeathReason);
        GhostCanSeeAllTasks = BooleanOptionItem.Create("GhostCanSeeAllTasks", false, TabGroup.ModMainSettings, true).SetColor(Palette.LightBlue).SetGameMode(CustomGameMode.All);
        GhostCanSeeNumberOfButtonsOnOthers = BooleanOptionItem.Create("GhostCanSeeNumberOfButtonsOnOthers", false, TabGroup.ModMainSettings, true).SetColor(Palette.LightBlue).SetGameMode(CustomGameMode.All);
        GhostCanSeeKillflash = BooleanOptionItem.Create("GhostCanSeeKillflash", false, TabGroup.ModMainSettings, true).SetColor(Palette.LightBlue).SetGameMode(CustomGameMode.All);
    }

    static void SetupHamoGhostIgnoreTasksOptions()
    {
        GhostIgnoreTasksplayer = IntegerOptionItem.Create("GhostIgnoreTasksplayer", new(0, 15, 1), 0, TabGroup.ModMainSettings, true).SetParent(GhostIgnoreTasks)
            .SetValueFormat(OptionFormat.Players).SetZeroNotation(OptionZeroNotation.Off);
    }

    static void SetupHamoLadderOptions()
    {
        LadderDeathNuuun = BooleanOptionItem.Create("LadderDeathNuuun", false, TabGroup.ModMainSettings, false).SetParent(LadderDeath);
        LadderDeathZipline = BooleanOptionItem.Create("LadderDeathZipline", false, TabGroup.ModMainSettings, false).SetParent(LadderDeath);
    }

    static void SetupHamoMeetingOptions()
    {
        MeetingAndVoteOpt = BooleanOptionItem.Create("MeetingAndVoteOpt", false, TabGroup.ModMainSettings, false).SetColor(Color.cyan).SetGameMode(CustomGameMode.Standard);
        LowerLimitVotingTime = FloatOptionItem.Create("LowerLimitVotingTime", new(0f, 300f, 5f), 0f, TabGroup.ModMainSettings, false).SetParent(MeetingAndVoteOpt)
            .SetValueFormat(OptionFormat.Seconds);
        MeetingTimeLimit = FloatOptionItem.Create("LimitMeetingTime", new(0f, 300f, 5f), 300f, TabGroup.ModMainSettings, false).SetParent(MeetingAndVoteOpt)
            .SetValueFormat(OptionFormat.Seconds);
        ShowVoteResult = BooleanOptionItem.Create("ShowVoteResult", false, TabGroup.ModMainSettings, false).SetParent(MeetingAndVoteOpt);
        ShowVoteJudgment = StringOptionItem.Create("ShowVoteJudgment", ShowVoteJudgments, 0, TabGroup.ModMainSettings, false).SetParent(ShowVoteResult);
    }
}

public static class HamoGhost
{
    static bool Dead(PlayerControl pc) => pc != null && !pc.IsAlive() && !GmOptions.IsGM(pc);

    public static bool SeesKillerColor(PlayerControl seer)
        => Dead(seer) && (Options.GhostCanSeeKillerColor?.GetBool() ?? false);

    public static bool SeesKillFlash(PlayerControl seer, PlayerControl target)
        => Dead(seer) && seer != target && (Options.GhostCanSeeKillflash?.GetBool() ?? false);

    public static string ExtraProgress(PlayerControl seer, PlayerControl seen)
    {
        if (!Dead(seer) || seen == null) return "";
        var sb = new System.Text.StringBuilder();
        if ((Options.GhostCanSeeNumberOfButtonsOnOthers?.GetBool() ?? false) && !Core.NativeObject.Same(seer, seen))
        {
            var st = PlayerState.GetByPlayerId(seen.PlayerId);
            if (st != null) sb.Append(Utils.ColorString((Color)new Color32(255, 140, 60, 255), $"[{(int)st.NumberOfRemainingButtons}]"));
        }
        if ((Options.GhostCanSeeAllTasks?.GetBool() ?? false) && Core.NativeObject.Same(seer, seen) && GameData.Instance)
            sb.Append(Utils.ColorString(Color.yellow, $"({GameData.Instance.CompletedTasks}/{GameData.Instance.TotalTasks})"));
        return sb.ToString();
    }

    static readonly Dictionary<byte, bool> ignoreDecided = new();

    [GameModuleInitializer]
    public static void Init() => ignoreDecided.Clear();

    public static bool IgnoreTasksFor(NetworkedPlayerInfo p)
    {
        var n = Options.GhostIgnoreTasksplayer?.GetInt() ?? 0;
        if (n <= 0) return true;
        if (ignoreDecided.TryGetValue(p.PlayerId, out var decided)) return decided;
        decided = Main.AllAlivePlayerControls.Count() >= n;
        ignoreDecided[p.PlayerId] = decided;
        return decided;
    }
}

public static class HamoFall
{
    static int Chance => (Options.LadderDeathChance as StringOptionItem).GetChance();
    static bool Rolls() => IRandom.Instance.Next(1, 101) <= Chance;
    static readonly HashSet<byte> ziplineDoomed = new();

    [GameModuleInitializer]
    public static void Init() => ziplineDoomed.Clear();

    static void Fall(PlayerControl pc)
    {
        if (pc == null || !pc.IsAlive()) return;
        var state = PlayerState.GetByPlayerId(pc.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.Fall;
        pc.RpcMurderPlayerV2(pc);
        state?.SetDead(CustomDeathReason.Fall);
    }

    static bool LadderOn(OptionItem opt) => Options.LadderDeath.GetBool() && opt != null && opt.GetBool();

    [HarmonyPatch(typeof(MovingPlatformBehaviour), nameof(MovingPlatformBehaviour.Use), typeof(PlayerControl))]
    static class PlatformPatch
    {
        [HarmonyPriority(Priority.Low)]
        static bool Prefix([HarmonyArgument(0)] PlayerControl player)
        {
            if (!AmongUsClient.Instance.AmHost || GameStates.IsFreePlay || player == null) return true;
            if (GameStates.IsMeeting || !player.IsAlive() || !LadderOn(Options.LadderDeathNuuun)) return true;
            if (!Rolls()) return true;
            Fall(player);
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckUseZipline))]
    static class ZiplinePatch
    {
        static void Postfix(PlayerControl target, bool fromTop, bool __runOriginal)
        {
            if (!__runOriginal || !AmongUsClient.Instance.AmHost || target == null) return;
            if (GameStates.IsMeeting || !target.IsAlive() || !LadderOn(Options.LadderDeathZipline)) return;
            if (!Rolls()) return;
            var id = target.PlayerId;
            _ = new LateTask(() => { if (GameStates.IsInTask && target.IsAlive()) ziplineDoomed.Add(id); }, 3f, "ZipLineDeath");
            _ = new LateTask(() =>
            {
                ziplineDoomed.Remove(id);
                if (GameStates.IsInTask) Fall(target);
            }, fromTop ? 5f : 8f, "ZipLineFall");
        }
    }

    public static void OnReportDeadBody()
    {
        foreach (var id in ziplineDoomed.ToArray()) Fall(Utils.GetPlayerById(id));
        ziplineDoomed.Clear();
    }
}

public static class HamoMeeting
{
    static bool On => Options.MeetingAndVoteOpt != null && Options.MeetingAndVoteOpt.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard;

    public static void ApplyLimits(ref int min, ref int max)
    {
        if (!On) return;
        min = Options.LowerLimitVotingTime.GetInt();
        max = Options.MeetingTimeLimit.GetInt();
    }

    static string Judge(string playername, string what, bool isRole)
        => string.Format(GetString("fortuihourole"), playername, what, GetString(isRole ? "fortuihouisrole" : "fortuihouisnotrole"));

    public static string ExpelledText(NetworkedPlayerInfo exiled, bool isTie)
    {
        if (isTie) return GetString("votetie");
        if (exiled == null) return GetString("voteskip");
        var playername = Utils.ColorString((Color)(Main.PlayerColors.TryGetValue(exiled.PlayerId, out var c) ? c : Color.white), exiled.PlayerName);
        var role = PlayerState.GetByPlayerId(exiled.PlayerId)?.GetNowMainRole() ?? CustomRoles.NotAssigned;
        if (On && Options.ShowVoteResult.GetBool() && role is not CustomRoles.NotAssigned)
        {
            switch (Options.ShowVoteJudgments[Options.ShowVoteJudgment.GetValue()])
            {
                case "Impostor": return Judge(playername, Utils.GetRoleColorAndName(CustomRoles.Impostor), role.IsImpostor());
                case "Neutral": return Judge(playername, $"<#cccccc>{GetString("Neutral")}</color>", role.IsNeutral());
                case "CrewMate(Mad)": return Judge(playername, Utils.GetRoleColorAndName(CustomRoles.Crewmate), role.IsCrewmate() || role.IsMadmate());
                case "Crewmate": return Judge(playername, Utils.GetRoleColorAndName(CustomRoles.Crewmate), role.IsCrewmate());
                case "Role": return Judge(playername, Utils.GetRoleColorAndName(role), true);
                case "ShowTeam": return Judge(playername, TeamName(exiled), true);
            }
        }
        return string.Format(GetString("fortuihou"), playername);
    }

    static string TeamName(NetworkedPlayerInfo exiled)
    {
        var pc = exiled.Object;
        if (pc == null) return "...?";
        var count = pc.GetCountTypes();
        if (count == CountTypes.Impostor) return Utils.GetRoleColorAndName(CustomRoles.Impostor);
        if (count == CountTypes.Crew) return Utils.GetRoleColorAndName(CustomRoles.Crewmate);
        var faction = Core.Win.GameEndRules.FactionOf(count);
        if (faction != null && faction.WinnerRoles.Length > 0) return Utils.GetRoleColorAndName(faction.WinnerRoles[0]);
        return Utils.GetRoleColorAndName(pc.GetCustomRole());
    }

    public static void OnVoteResult(NetworkedPlayerInfo exiled, bool isTie)
    {
        bool show = On && Options.ShowVoteResult.GetBool();
        bool onName = Options.CanseeVoteresult?.GetBool() ?? false;
        if (!show && !onName) return;
        if (MeetingVoteManager.Voteresult != "") return;
        MeetingVoteManager.Voteresult = ExpelledText(exiled, isTie);
    }

    public static string LowerText(PlayerControl seer, bool isMeeting)
    {
        if (isMeeting || seer == null) return "";
        if (!(Options.CanseeVoteresult?.GetBool() ?? false) || string.IsNullOrEmpty(MeetingVoteManager.Voteresult)) return "";
        return "<#ffffff><size=75%>" + MeetingVoteManager.Voteresult + "</size></color>";
    }
}
