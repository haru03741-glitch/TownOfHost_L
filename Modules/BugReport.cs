using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// ゲームの中からバグや要望を報告する (/report 内容)。誰でも送れる。
/// ホストの PC の TOHL_DATA/Reports.txt に、時刻・送った人・その時の状況 (ロビーか試合中か・マップ・役職) と一緒に書き足す。
/// ホストが Discord の Webhook を入れていれば、そこへも送る。ホストは /report open で入っているフォルダを開ける
/// </summary>
public static class BugReport
{
    const int MaxLength = 300;
    // 1 人が続けて送れる間隔 (秒)
    const float Interval = 20f;
    static readonly System.Collections.Generic.Dictionary<byte, float> lastAt = new();

    static string Dir => Path.Combine(Environment.CurrentDirectory, "TOHL_DATA");
    static string FilePath => Path.Combine(Dir, "Reports.txt");

    public static void Host(ChatCommandContext c) => Handle(c, true);
    public static void Remote(ChatCommandContext c) => Handle(c, false);

    static void Handle(ChatCommandContext c, bool host)
    {
        var to = c.Player != null ? c.Player.PlayerId : byte.MaxValue;
        void Reply(string text)
        {
            if (host) HostNote.Post(text);
            else Utils.SendMessageAutoSplit(text, true, sendTo: to);
        }
        var text = string.Join(" ", c.Args.Skip(1)).Trim();
        if (host && text.ToLowerInvariant() is "open" or "folder")
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, "");
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
            return;
        }
        if (text.Length == 0)
        {
            Reply(GetString("Report.HowTo"));
            return;
        }
        if (text.Length > MaxLength) text = text[..MaxLength];
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (!host && lastAt.TryGetValue(to, out var last) && now - last < Interval)
        {
            Reply(GetString("Report.TooFast"));
            return;
        }
        lastAt[to] = now;

        var name = c.Player?.Data?.PlayerName ?? "?";
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {name} ({Situation(c.Player)}) {text.Replace("\n", " ")}";
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
        Logger.Info(line, "Report");
        if (DiscordWebhook.HasUrl) DiscordWebhook.PostText($"📝 {GetString("Report.WebhookTitle")}\n{line}");

        Reply(GetString("Report.Thanks"));
        // ホストへは、誰から何が届いたかを知らせる
        if (!host) HostNote.Post(string.Format(GetString("Report.ToHost"), name, text));
    }

    // 送った時の状況: ロビー / 試合中 (マップ・役職・生死)
    static string Situation(PlayerControl pc)
    {
        try
        {
            if (!GameStates.IsInGame) return "Lobby";
            var map = ((MapNames)Main.NormalOptions.MapId).ToString();
            if (pc == null) return map;
            return $"{map} {pc.GetCustomRole()}{(pc.IsAlive() ? "" : " dead")}{(GameStates.IsMeeting ? " meeting" : "")}";
        }
        catch { return "?"; }
    }
}
