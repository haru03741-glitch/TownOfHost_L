using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Core.Modes;
using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;
using TownOfHostL.Modules;
using static TownOfHostL.Translator;

namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem TaskBattleCanVent;
    public static OptionItem TaskBattleVentCooldown;
    public static OptionItem TaskBattelShowAllTask;
    public static OptionItem TaskBattelCanSeeOtherPlayer;
    public static OptionItem TaskBattleShowFastestPlayer;
    public static OptionItem TaskBattleTeamMode;
    public static OptionItem TaskBattleTeamCount;
    public static OptionItem TaskBattleTeamWinType;
    public static OptionItem TaskBattleTeamWinTaskc;
    public static OptionItem TaskSoroeru;
    public static OptionItem TaskAddMode;
    public static OptionItem TaskBattleNumCommonTasks;
    public static OptionItem TaskBattleNumLongTasks;
    public static OptionItem TaskBattleNumShortTasks;
    public static OptionItem TaskBattleMaxAddCount;

    static void SetupTaskBattleOptions()
    {
        var color = "#87cffa";
        const CustomGameMode mode = CustomGameMode.TaskBattle;
        TaskBattleCanVent = BooleanOptionItem.Create("TaskBattleCanVent", false, TabGroup.ModMainSettings, false).SetColorcode(color).SetHeader(true).SetGameMode(mode);
        TaskBattleVentCooldown = FloatOptionItem.Create("TaskBattleVentCooldown", new(0f, 99f, 1f), 5f, TabGroup.ModMainSettings, false).SetParent(TaskBattleCanVent)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(mode);
        TaskBattelShowAllTask = BooleanOptionItem.Create("TaskBattelShowAllTask", false, TabGroup.ModMainSettings, false).SetColorcode(color).SetGameMode(mode);
        TaskBattelCanSeeOtherPlayer = BooleanOptionItem.Create("TaskBattelCanSeeOtherPlayer", false, TabGroup.ModMainSettings, false).SetColorcode(color).SetGameMode(mode);
        TaskBattleShowFastestPlayer = BooleanOptionItem.Create("TaskBattleShowFastestPlayer", false, TabGroup.ModMainSettings, false).SetColorcode(color).SetGameMode(mode);
        TaskBattleTeamMode = BooleanOptionItem.Create("TaskBattleTeamMode", false, TabGroup.ModMainSettings, false).SetColorcode(color).SetGameMode(mode);
        TaskBattleTeamCount = IntegerOptionItem.Create("TaskBattleTeamCount", new(1, 15, 1), 2, TabGroup.ModMainSettings, false).SetParent(TaskBattleTeamMode).SetGameMode(mode);
        TaskBattleTeamWinType = BooleanOptionItem.Create("TaskBattleTeamGameTaskComp", false, TabGroup.ModMainSettings, false).SetParent(TaskBattleTeamMode).SetGameMode(mode);
        TaskBattleTeamWinTaskc = IntegerOptionItem.Create("TaskBattleTeamWinTaskc", new(1, 999, 1), 20, TabGroup.ModMainSettings, false).SetParent(TaskBattleTeamWinType).SetGameMode(mode);
        TaskSoroeru = BooleanOptionItem.Create("TaskSoroeru", false, TabGroup.ModMainSettings, false).SetColorcode(color).SetGameMode(mode);
        TaskAddMode = BooleanOptionItem.Create("TaskAddMode", false, TabGroup.ModMainSettings, false).SetColorcode(color).SetGameMode(mode);
        TaskBattleNumCommonTasks = IntegerOptionItem.Create("WorkhorseNumCommonTasks", new(0, 99, 1), 1, TabGroup.ModMainSettings, false).SetParent(TaskAddMode)
            .SetValueFormat(OptionFormat.Pieces).SetGameMode(mode);
        TaskBattleNumLongTasks = IntegerOptionItem.Create("WorkhorseNumLongTasks", new(0, 99, 1), 1, TabGroup.ModMainSettings, false).SetParent(TaskAddMode)
            .SetValueFormat(OptionFormat.Pieces).SetGameMode(mode);
        TaskBattleNumShortTasks = IntegerOptionItem.Create("WorkhorseNumShortTasks", new(0, 99, 1), 1, TabGroup.ModMainSettings, false).SetParent(TaskAddMode)
            .SetValueFormat(OptionFormat.Pieces).SetGameMode(mode);
        TaskBattleMaxAddCount = IntegerOptionItem.Create("MaxAddCount", new(1, 99, 1), 1, TabGroup.ModMainSettings, false).SetParent(TaskAddMode).SetGameMode(mode);
    }
}

/// <remarks>TownOfHost-K (KYMario/TownOfHost-K@7d6f8156) のタスクバトルを元にした (TownOfHost-hamo 経由)。</remarks>
public sealed class TaskBattleMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.TaskBattle;
    public override bool UsesStandardRoles => false;
    public override bool AllowsDoorSabotage => false;
    public override bool AllowsSabotage => false;
    public override bool AllowsReport(PlayerControl reporter, NetworkedPlayerInfo target) => false;
    public override bool HasMeetings => false;
    public override void SelectRoles() => TaskBattle.Assign();
    public override (string Key, Color Color)? Label => ("TaskBattle", new Color32(135, 207, 250, 255));
}

public static class TaskBattle
{
    public static bool IsActive => Options.CurrentGameMode == CustomGameMode.TaskBattle;
    public static bool IsTaskBattleTeamMode;
    public static Dictionary<byte, List<byte>> TaskBattleTeams = new();
    public static Dictionary<byte, List<byte>> SelectedTeams = new();
    public static Dictionary<byte, int> TaskAddCount = new();
    public static bool IsAdding;
    public static byte RTAPlayerId = byte.MaxValue;
    public static bool IsRTAMode;
    public static float Timer;
    public static float RtaTimer;
    static byte[] alignedTasks;

    [Attributes.GameModuleInitializer]
    public static void Init()
    {
        IsAdding = false;
        TaskAddCount = new();
        alignedTasks = null;
        Timer = 0f;
        RtaTimer = 0f;
        RTAPlayerId = byte.MaxValue;
        IsRTAMode = false;
        if (!IsActive) return;
        IsTaskBattleTeamMode = Options.TaskBattleTeamMode.GetBool();
        if (Options.TaskAddMode.GetBool())
        {
            foreach (var pc in Main.AllPlayerControls)
            {
                if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId && Options.EnableGM.GetBool()) continue;
                TaskAddCount.TryAdd(pc.PlayerId, 0);
            }
        }
    }

    static bool IsPlayer(PlayerControl pc) => !(Options.EnableGM.GetBool() && pc.PlayerId == PlayerControl.LocalPlayer.PlayerId);

    static void ResetAndSetTeam()
    {
        TaskBattleTeams.Clear();
        if (!Options.TaskBattleTeamMode.GetBool()) return;
        var rand = IRandom.Instance;
        var ap = Main.AllPlayerControls.Where(IsPlayer).ToList();
        ap.RemoveAll(x => SelectedTeams.Values.Any(list => list.Contains(x.PlayerId)));
        var teamc = (int)Math.Min(Options.TaskBattleTeamCount.GetFloat(), ap.Count);
        if (teamc <= 0) teamc = 1;
        var c = ap.Count / teamc;
        for (var i = 0; i < teamc; i++)
        {
            var playerlist = new List<byte>();
            for (var i2 = 0; i2 < c; i2++)
            {
                if (ap.Count == 0) continue;
                var player = ap[rand.Next(ap.Count)];
                playerlist.Add(player.PlayerId);
                ap.Remove(player);
            }
            TaskBattleTeams[(byte)(i + 1)] = playerlist;
        }
        foreach (var (teamId, players) in SelectedTeams)
        {
            if (!TaskBattleTeams.TryGetValue(teamId, out var list)) list = new();
            list.AddRange(players);
            TaskBattleTeams[teamId] = list;
        }
    }

    public static void Assign()
    {
        RpcSetRoleReplacer.StartReplace();
        ResetAndSetTeam();
        var canVent = Options.TaskBattleCanVent.GetBool();
        foreach (var pc in Main.AllPlayerControls)
        {
            var state = PlayerState.GetByPlayerId(pc.PlayerId);
            if (!IsPlayer(pc))
            {
                pc.RpcSetRole(RoleTypes.Crewmate);
                state.SetMainRole(CustomRoles.GM);
                continue;
            }
            pc.RpcSetRole(canVent ? RoleTypes.Engineer : RoleTypes.Crewmate);
            state.SetMainRole(CustomRoles.TaskPlayerB);
        }
        RpcSetRoleReplacer.Release();

        if (IsTaskBattleTeamMode)
        {
            foreach (var pc in Main.AllPlayerControls)
                foreach (var (_, players) in TaskBattleTeams)
                {
                    if (!players.Contains(pc.PlayerId)) continue;
                    foreach (var id in players.Where(id => id != pc.PlayerId))
                        NameColorManager.Add(pc.PlayerId, id);
                }
        }

        CustomRoleManager.MarkOthers.Add(GetMark);
        HamoModes.FinishAssign(Rules);
    }

    public static void OverrideTaskCount(ref bool hasCommonTasks, ref int numCommonTasks, ref int numLong, ref int numShort)
    {
        if (!IsActive || !IsAdding) return;
        hasCommonTasks = Options.TaskBattleNumCommonTasks.GetInt() > 0;
        numCommonTasks = Options.TaskBattleNumCommonTasks.GetInt();
        numLong = Options.TaskBattleNumLongTasks.GetInt();
        numShort = Options.TaskBattleNumShortTasks.GetInt();
    }

    public static bool ForcesReassign => IsActive && (IsAdding || Options.TaskSoroeru.GetBool());

    public static void Align(Il2CppSystem.Collections.Generic.List<byte> tasks)
    {
        if (!IsActive || IsAdding || !Options.TaskSoroeru.GetBool()) return;
        if (alignedTasks == null)
        {
            alignedTasks = new byte[tasks.Count];
            for (var i = 0; i < tasks.Count; i++) alignedTasks[i] = tasks[i];
            return;
        }
        tasks.Clear();
        foreach (var t in alignedTasks) tasks.Add(t);
    }

    public static void OnCompleteTask(PlayerControl pc, TaskState taskState)
    {
        if (!IsActive || !AmongUsClient.Instance.AmHost) return;
        if (!pc.Is(CustomRoles.TaskPlayerB) || !taskState.IsTaskFinished)
        {
            Log(pc, taskState);
            return;
        }
        if (Options.TaskAddMode.GetBool() && TaskAddCount.TryGetValue(pc.PlayerId, out var count) && count < Options.TaskBattleMaxAddCount.GetInt())
        {
            IsAdding = true;
            TaskAddCount[pc.PlayerId]++;
            taskState.AllTasksCount += Options.TaskBattleNumCommonTasks.GetInt() + Options.TaskBattleNumLongTasks.GetInt() + Options.TaskBattleNumShortTasks.GetInt();
            pc.Data.RpcSetTasks(Array.Empty<byte>());
            pc.SyncSettings();
            IsAdding = false;
            Utils.NotifyRoles();
            Log(pc, taskState);
            return;
        }
        if (IsTaskBattleTeamMode)
        {
            foreach (var team in TaskBattleTeams.Values)
            {
                if (team.Contains(pc.PlayerId)) continue;
                foreach (var id in team)
                {
                    var other = Utils.GetPlayerById(id);
                    if (other == null || !other.IsAlive()) continue;
                    other.RpcExileV3(CustomDeathReason.etc);
                }
            }
        }
        else
        {
            foreach (var other in Main.AllAlivePlayerControls.ToArray())
            {
                if (other == pc || other.GetPlayerTaskState().IsTaskFinished) continue;
                other.RpcExileV3(CustomDeathReason.etc);
            }
        }
        Utils.NotifyRoles();
        Log(pc, taskState);
    }

    static void Log(PlayerControl pc, TaskState taskState)
        => GameLog.Add("TaskBattle", string.Format(GetString("TB"), pc.ColoredName(), $"{taskState.CompletedTasksCount}/{taskState.AllTasksCount} ({TimerText(Timer)})"));

    public static string TimerText(float t)
    {
        int h = (int)t / 3600, m = (int)t % 3600 / 60, s = (int)t % 60, ms = (int)(t % 1 * 1000);
        return h > 0 ? $"{h:00} : {m:00} : {s:00} : {ms:000}" : $"{m:00} : {s:00} : {ms:000}";
    }

    static string GetMark(PlayerControl seer, PlayerControl seen, bool isForMeeting)
    {
        seen ??= seer;
        var mark = new StringBuilder();
        if (seer.PlayerId == seen.PlayerId)
        {
            if (Options.EnableGM.GetBool()) return "";
            bool byTeam = Options.TaskBattleTeamMode.GetBool() || Options.TaskBattleTeamWinType.GetBool();
            if (Options.TaskBattelShowAllTask.GetBool())
            {
                float all = 0;
                int done = 0;
                if (!byTeam)
                {
                    foreach (var pc in Main.AllPlayerControls)
                    {
                        all += pc.GetPlayerTaskState().AllTasksCount;
                        done += pc.GetPlayerTaskState().CompletedTasksCount;
                    }
                }
                else
                {
                    foreach (var t in TaskBattleTeams.Values)
                    {
                        if (!t.Contains(seer.PlayerId)) continue;
                        all = Options.TaskBattleTeamWinTaskc.GetFloat();
                        foreach (var id in t.Where(id => Utils.GetPlayerById(id)?.IsAlive() == true))
                            done += Utils.GetPlayerById(id).GetPlayerTaskState().CompletedTasksCount;
                    }
                }
                mark.Append($"<#ffff00>({done}/{all})</color>");
            }
            if (Options.TaskBattleShowFastestPlayer.GetBool())
            {
                var top = 0;
                if (!byTeam)
                {
                    foreach (var pc in Main.AllPlayerControls)
                        top = Math.Max(top, pc.GetPlayerTaskState().CompletedTasksCount);
                }
                else
                {
                    foreach (var t in TaskBattleTeams.Values)
                    {
                        var sum = 0;
                        foreach (var id in t.Where(id => Utils.GetPlayerById(id)?.IsAlive() == true))
                            sum += Utils.GetPlayerById(id).GetPlayerTaskState().CompletedTasksCount;
                        top = Math.Max(top, sum);
                    }
                }
                mark.Append($"<#00f7ff>({top})</color>");
            }
        }
        else if (Options.TaskBattelCanSeeOtherPlayer.GetBool())
        {
            var ts = seen.GetPlayerTaskState();
            mark.Append($"<#ffff00>({ts.CompletedTasksCount}/{ts.AllTasksCount})</color>");
        }
        return mark.ToString();
    }

    public static readonly GameEndRuleSet Rules = new(CheckGameEnd);

    static bool CheckGameEnd(out GameOverReason reason)
    {
        reason = GameOverReason.CrewmatesByTask;
        if (IsRTAMode)
        {
            var player = Utils.GetPlayerById(RTAPlayerId);
            if (player == null || !player.GetPlayerTaskState().IsTaskFinished) return false;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.TaskPlayerB);
            CustomWinnerHolder.WinnerIds.Add(player.PlayerId);
            RTAPlayerId = byte.MaxValue;
            return true;
        }
        if (!Options.TaskBattleTeamWinType.GetBool())
        {
            int alive = Main.AllAlivePlayerControls.Count(pc => pc.Is(CustomRoles.TaskPlayerB));
            bool win = alive <= 1;
            if (IsTaskBattleTeamMode && Main.AllPlayerControls.Any(pc => pc.Is(CustomRoles.TaskPlayerB) && pc.GetPlayerTaskState().IsTaskFinished))
                win = true;
            if (!win) return false;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.TaskPlayerB);
            foreach (var pc in Main.AllAlivePlayerControls)
                CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            return true;
        }
        foreach (var t in TaskBattleTeams.Values)
        {
            var task = t.Sum(id => PlayerState.GetByPlayerId(id)?.taskState.CompletedTasksCount ?? 0);
            if (Options.TaskBattleTeamWinTaskc.GetFloat() > task) continue;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.TaskPlayerB);
            foreach (var id in t) CustomWinnerHolder.WinnerIds.Add(id);
            return true;
        }
        return false;
    }

    public static void OnIntroEnd()
    {
        if (!IsActive) return;
        IsRTAMode = Main.AllPlayerControls.Count() == (Options.EnableGM.GetBool() ? 2 : 1);
        if (!IsRTAMode) return;
        _ = new LateTask(() =>
        {
            var player = Options.EnableGM.GetBool() ? Main.AllAlivePlayerControls.FirstOrDefault(p => p.PlayerId != PlayerControl.LocalPlayer.PlayerId) : PlayerControl.LocalPlayer;
            if (player == null) return;
            RTAPlayerId = player.PlayerId;
            RtaTimer = 0f;
        }, 1f, "TaskBattle TimerReset");
    }

    public static void OnFixedUpdate(PlayerControl player)
    {
        if (!IsActive || !GameStates.IsInTask || !HamoModes.IntroDone) return;
        if (player.PlayerId == PlayerControl.LocalPlayer.PlayerId) Timer += Time.fixedDeltaTime;
        if (IsRTAMode && RTAPlayerId == player.PlayerId && (RtaTimer != 0 || player.MyPhysics.Animations.IsPlayingRunAnimation()))
            RtaTimer += Time.fixedDeltaTime;
    }

    public static string HudText() => IsActive && IsRTAMode && GameStates.IsInTask ? TimerText(RtaTimer) : null;
}
