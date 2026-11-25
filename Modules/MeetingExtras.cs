using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 会議と試合の終わりの知らせ (ホストのみ)。
/// ・投票の残りが 30 秒・10 秒になったらチャットで知らせる
/// ・試合の終わりに、キル・正しい投票・タスク・勝利・生存から点を付けて MVP をロビーで発表する
/// </summary>
/// <remarks>TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/MeetingTimeAnnounce.cs・MvpAnnouncer.cs・WinStreak.cs) を元にした。</remarks>
public static class MeetingExtras
{
    public static OptionItem AnnounceVotingTime;
    public static OptionItem AnnounceMvp;
    public static OptionItem AnnounceWinStreak;

    public static void SetupCustomOption()
    {
        AnnounceVotingTime = StringOptionItem.Create("AnnounceVotingTime", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All).SetTopic(OptionTopic.Meeting);
        AnnounceMvp = StringOptionItem.Create("AnnounceMvp", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.Standard).SetTopic(OptionTopic.Meeting);
        SettingChangeNotice.SetupCustomOption();
        AnnounceWinStreak = StringOptionItem.Create("AnnounceWinStreak", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.Standard);
    }


    const float StopperBonus = 3f;
    static CustomWinner lastTeam = CustomWinner.Default;
    static int streak;
    static bool stoppedThisGame;

    static bool StreakOn => AnnounceWinStreak != null && AnnounceWinStreak.GetBool();

    static void CountStreak()
    {
        stoppedThisGame = false;
        var team = CustomWinnerHolder.WinnerTeam;
        if (team is CustomWinner.Draw or CustomWinner.None or CustomWinner.Default) return;
        var prevStreak = streak;
        var prevTeam = lastTeam;
        if (team == lastTeam) streak++;
        else
        {
            stoppedThisGame = prevStreak >= 2;
            streak = 1;
            lastTeam = team;
        }
        if (!StreakOn) return;
        var title = Utils.ColorString(new Color32(255, 211, 110, 255), GetString(stoppedThisGame ? "WinStreak.StopTitle" : "WinStreak.Title"));
        if (stoppedThisGame)
            Utils.SendMessage(string.Format(GetString("WinStreak.Stopped"), TeamName(prevTeam), prevStreak, TeamName(team)), byte.MaxValue, title);
        else if (streak >= 2)
            Utils.SendMessage(string.Format(GetString("WinStreak.Now"), TeamName(team), streak), byte.MaxValue, title);
    }

    static string TeamName(CustomWinner team)
    {
        try { return Utils.GetRoleColorAndName((CustomRoles)team); }
        catch { return team.ToString(); }
    }

    // ===== 投票の残り時間 =====

    static readonly int[] marks = { 30, 10 };
    static MeetingHud announcedFor;
    static int nextMark;

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
    static class MeetingUpdatePatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            if (AnnounceVotingTime == null || !AnnounceVotingTime.GetBool() || !AmongUsClient.Instance.AmHost) return;
            if (__instance == null || GameStates.ExiledAnimate) return;
            if (announcedFor != __instance) { announcedFor = __instance; nextMark = 0; }
            var votingTime = MeetingTimeManager.CurrentVotingTime;
            if (votingTime <= 0 || nextMark >= marks.Length) return;
            // MeetingVoteManager と同じ数え方: 話し合いの時間を過ぎてからの経過で残りを出す
            var remaining = votingTime - (__instance.discussionTimer - MeetingTimeManager.CurrentDiscussionTime);
            // 初めから 30 秒より短い時などは、過ぎた区切りを飛ばす
            while (nextMark < marks.Length && remaining <= marks[nextMark] - 1) nextMark++;
            if (nextMark >= marks.Length || remaining > marks[nextMark]) return;
            Utils.SendMessage(string.Format(GetString("VotingTimeLeft"), marks[nextMark]), byte.MaxValue,
                Utils.ColorString(new Color32(255, 211, 110, 255), GetString("VotingTimeLeftTitle")));
            nextMark++;
        }
    }

    // ===== MVP =====

    // クルーがインポスター陣営か第三陣営に入れた票の数
    static readonly Dictionary<byte, int> correctVotes = new();

    [GameModuleInitializer]
    public static void Init() => correctVotes.Clear();

    /// <summary>会議の票が決まった時に呼ぶ</summary>
    public static void OnVotes(IEnumerable<MeetingHud.VoterState> votes)
    {
        if (AnnounceMvp == null || !AnnounceMvp.GetBool()) return;
        foreach (var v in votes)
        {
            var voter = PlayerState.GetByPlayerId(v.VoterId)?.GetNowMainRole() ?? CustomRoles.NotAssigned;
            var target = PlayerState.GetByPlayerId(v.VotedForId)?.GetNowMainRole() ?? CustomRoles.NotAssigned;
            if (voter == CustomRoles.NotAssigned || target == CustomRoles.NotAssigned || !voter.IsCrewmate()) continue;
            if (!(target.IsImpostorTeam() || target.IsNeutral())) continue;
            correctVotes[v.VoterId] = correctVotes.GetValueOrDefault(v.VoterId) + 1;
        }
    }

    /// <summary>試合の終わりに呼ぶ。送った文はロビーに戻ってから届く</summary>
    public static void OnGameEnd()
    {
        announcedFor = null;
        if (!AmongUsClient.Instance.AmHost) return;
        if (Options.CurrentGameMode != CustomGameMode.Standard) return;
        CountStreak();
        if (AnnounceMvp == null || !AnnounceMvp.GetBool()) return;
        if (CustomWinnerHolder.WinnerTeam is CustomWinner.Draw or CustomWinner.None) return;
        var stopper = StreakOn && stoppedThisGame;
        var winners = CustomWinnerHolder.WinnerIds;
        var best = PlayerState.AllPlayerStates
            .Where(kv => kv.Value != null && kv.Value.GetNowMainRole() is not (CustomRoles.NotAssigned or CustomRoles.GM))
            .Select(kv =>
            {
                var state = kv.Value;
                var kills = state.GetKillCount(true);
                var votes = correctVotes.GetValueOrDefault(kv.Key);
                var task = state.taskState;
                var taskRate = task != null && task.hasTasks && task.AllTasksCount > 0 ? (float)task.CompletedTasksCount / task.AllTasksCount : 0f;
                var win = winners.Contains(kv.Key);
                var score = kills * 3 + votes * 2 + taskRate * 3 + (win ? 2 : 0) + (state.IsDead ? 0 : 1) + (stopper && win ? StopperBonus : 0);
                return (id: kv.Key, state, kills, votes, taskRate, win, score);
            })
            .OrderByDescending(x => x.score).ThenByDescending(x => x.kills).ThenByDescending(x => x.votes)
            .FirstOrDefault();
        if (best.state == null || best.score <= 0) return;

        var reasons = new List<string>();
        if (best.kills > 0) reasons.Add(string.Format(GetString("Mvp.Kills"), best.kills));
        if (best.votes > 0) reasons.Add(string.Format(GetString("Mvp.Votes"), best.votes));
        if (best.taskRate > 0) reasons.Add(string.Format(GetString("Mvp.Tasks"), (int)(best.taskRate * 100)));
        if (best.win) reasons.Add(GetString("Mvp.Win"));
        if (best.win && stopper) reasons.Add(GetString("Mvp.StoppedStreak"));
        if (!best.state.IsDead) reasons.Add(GetString("Mvp.Alive"));
        var name = Main.AllPlayerNames.TryGetValue(best.id, out var n) ? n : "?";
        Utils.SendMessage($"{name} ({Utils.GetRoleColorAndName(best.state.GetNowMainRole())})\n{string.Join(" / ", reasons)}", byte.MaxValue,
            Utils.ColorString(new Color32(255, 211, 110, 255), GetString("Mvp.Title")));
        Logger.Info($"MVP: {name} score={best.score:0.0}", "MeetingExtras");
        correctVotes.Clear();
    }
}
