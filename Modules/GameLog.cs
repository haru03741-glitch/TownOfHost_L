using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using TownOfHostL.Attributes;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 試合の出来事 (通報・投票・追放・サボタージュ・タスク完了・切断・役職の能力など) を日ごとに並べた記録。
/// キルは死亡の記録 (PlayerState.RealKiller) から試合の終わりに時刻順で差し込むので、ここへ個別に書かない。
/// 出来上がった文は /l と、試合後にロビーへ戻った時の自動表示で使う。
/// </summary>
/// <remarks>日ごとの記録の形は TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/Utils/UtilsLog.cs UtilsGameLog) を元にした。</remarks>
public static class GameLog
{
    readonly record struct Entry(DateTime At, int Day, string Type, string Text);

    static readonly List<Entry> entries = new();
    /// <summary>このプロセスで始めた試合の数 (1 から)</summary>
    public static int GameCount { get; private set; }
    static DateTime startedAt;
    /// <summary>直前の試合の記録 (試合の終わりに作る)</summary>
    public static string LastText { get; private set; } = "";

    [GameModuleInitializer]
    public static void Init()
    {
        entries.Clear();
        GameCount++;
        startedAt = DateTime.Now;
    }

    /// <summary>出来事を 1 件足す (ホストのみ)。type はログの見出し</summary>
    public static void Add(string type, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Logger.Info(text.RemoveHtmlTags(), string.IsNullOrEmpty(type) ? "GameLog" : $"GameLog.{type}");
        if (!GameStates.IsInGame || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        entries.Add(new(DateTime.Now, MeetingStates.Day, type, text));
    }

    static string Name(byte id)
    {
        var name = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(id, out var n) ? n : "???";
        return Utils.ColorString(Main.PlayerColors.TryGetValue(id, out var c) ? (Color)c : Color.white, name);
    }
    static string NameWithRole(byte id) => $"{Name(id)}({RoleText.GetRoleNameText(id, showSubRole: false)})";

    // ===== 出来事の配り口 =====

    public static void OnMeetingStart(NetworkedPlayerInfo reporter, NetworkedPlayerInfo target)
    {
        if (reporter == null) return;
        var caller = "\n\t\t┗  " + string.Format(GetString("Meeting.Shoushu"), Name(reporter.PlayerId));
        Add("Meeting", target == null
            ? GetString("Meeting.Button") + caller
            : Name(target.PlayerId) + GetString("Meeting.Report") + caller);
    }

    public static void OnVoteResult(NetworkedPlayerInfo exiled, bool isTie)
    {
        if (exiled != null) Add("Vote", string.Format(GetString("Log.Exiled"), NameWithRole(exiled.PlayerId)));
        else Add("Vote", GetString(isTie ? "votetie" : "voteskip"));
    }

    public static void OnSabotage(PlayerControl player, SystemTypes type)
        => Add("Sabotage", string.Format(GetString("Log.Sabotage"), Name(player.PlayerId), GetString($"sb.{type}")));

    public static void OnSabotageFixed(SystemTypes type)
        => Add("Sabotage", string.Format(GetString("Log.FixSab"), GetString($"sb.{type}")));

    /// <summary>会議が終わって日が進んだ時。その日の見出しを出すための区切りだけを積む</summary>
    public static void OnNewDay()
    {
        if (!GameStates.IsInGame || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        entries.Add(new(DateTime.Now, MeetingStates.Day, null, null));
    }

    public static void OnDisconnected(byte id) => Add("Disconnected", Name(id) + GetString("DeathReason.Disconnected"));

    // ===== 文にする =====

    /// <summary>試合の終わりに呼び、キルを時刻順に差し込んで記録を文にする</summary>
    public static void Finish()
    {
        var all = new List<Entry>(entries);
        foreach (var (id, state) in PlayerState.AllPlayerStates)
        {
            var at = state.RealKiller.Item1;
            if (at == DateTime.MinValue) continue;
            var killerId = state.GetRealKiller();
            var text = $"{NameWithRole(id)} [{Utils.GetVitalText(id)}]";
            if (killerId != byte.MaxValue && killerId != id)
                text += $"\n\t\t<size=75%>⇐ {NameWithRole(killerId)}</size>";
            all.Add(new(at, DayAt(at), "Kill", text));
        }
        // 時刻が同じなら足した順 (OrderBy は安定)
        var sorted = all.OrderBy(e => e.At);

        var sb = new StringBuilder();
        sb.Append("<size=100%><align=\"center\">").Append(GetString("GameLog")).Append("</align></size><size=70%>");
        sb.Append('\n').Append(string.Format(GetString("log.Start"), GameCount));
        var day = 0;
        foreach (var e in sorted)
        {
            if (e.Day != day)
            {
                day = e.Day;
                sb.Append('\n').Append(Utils.ColorString(Palette.Orange, string.Format(GetString("Message.Day"), day).Replace("【", "").Replace("】", "")));
            }
            if (e.Text == null) continue;
            // 種類の無い記録は直前の行の続き (役職が本文と続きの行を分けて書く時)
            if (string.IsNullOrEmpty(e.Type)) { sb.Append("\n\t\t").Append(e.Text); continue; }
            sb.Append('\n').Append(Elapsed(e.At)).Append(" [").Append(Label(e.Type)).Append("] ").Append(e.Text);
        }
        sb.Append('\n').Append(Elapsed(DateTime.Now)).Append(" [").Append(GetString("GameLog.End")).Append("] ").Append(WinnerText());
        LastText = Trim(sb.ToString());
    }

    /// <summary>
    /// チャットの送信待ちは宛先ごとに 20 通 (1 通 13 行) までなので、長すぎる記録は古い方を省いて後ろを残す。
    /// 黙って後ろが切れる (勝敗に近い所が見えなくなる) のを避けるため
    /// </summary>
    const int MaxLines = 220;
    static string Trim(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length <= MaxLines) return text;
        // 先頭の 2 行 (見出しと試合の番号) は残す
        return string.Join('\n', lines[..2]) + '\n' + GetString("GameLog.Omitted") + '\n' + string.Join('\n', lines[^(MaxLines - 3)..]);
    }

    /// <summary>見出し: 共通の種類は GameLog.種類、役職が書いた物は役職名、どちらも無ければそのまま</summary>
    static string Label(string type)
        => HasString($"GameLog.{type}") ? GetString($"GameLog.{type}") : HasString(type) ? GetString(type) : type;

    static string WinnerText() => CustomWinnerHolder.WinnerTeam switch
    {
        CustomWinner.Draw => GetString("ForceEnd"),
        CustomWinner.None or CustomWinner.Default => "-",
        var team => Utils.GetRoleColorAndName((Roles.Core.CustomRoles)team) + GetString("Win"),
    };

    /// <summary>キルの時刻がどの日に当たるか (記録した出来事の日を時刻で引く)</summary>
    static int DayAt(DateTime at)
    {
        var day = 1;
        foreach (var e in entries)
        {
            if (e.At > at) break;
            day = e.Day;
        }
        return day;
    }

    /// <summary>イントロの後なら試合の経過時間 (分:秒)、そうでなければ時刻</summary>
    static string Elapsed(DateTime at)
    {
        var origin = Main.IntroEndTime != DateTime.MinValue ? Main.IntroEndTime : startedAt;
        if (at < origin) return at.ToString("T");
        var d = at - origin;
        return $"{(int)d.TotalMinutes:00}:{d.Seconds:00}";
    }
}
