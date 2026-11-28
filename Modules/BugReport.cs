using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// バグや要望の報告。
/// ・他の人: チャットで /bug 内容 (/report・/req も同じ)
/// ・TOHL を入れている人: ロビー・試合中のボタン (役職辞典の隣) かホストの操作盤から、ゲームの中の窓 (ReportWindow) で文と画像・動画を付けて送る
/// 送った物はホストの PC の TOHL_DATA/Reports.txt に時刻・送った人・その時の状況と一緒に書き足し、付けたファイルは TOHL_DATA/Reports/ に写す。
/// ホストが Discord の Webhook を入れていれば、そこへも (付けたファイルごと) 送る
/// </summary>
public static class BugReport
{
    const int MaxLength = 300;
    const int MaxLengthForm = 2000;
    // 1 人が続けて送れる間隔 (秒)
    const float Interval = 20f;
    // Discord の Webhook へ 1 通で送れるファイルの合計 (サーバーのブーストが無い時の上限 10MB より少し下)
    const long MaxUpload = 9_500_000;
    static readonly Dictionary<byte, float> lastAt = new();

    static string Dir => Path.Combine(Environment.CurrentDirectory, "TOHL_DATA");
    static string FilePath => Path.Combine(Dir, "Reports.txt");
    static string AttachDir => Path.Combine(Dir, "Reports");

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
            OpenFolder();
            return;
        }
        if (host && text.Length == 0)
        {
            OpenForm();
            return;
        }
        if (text.Length == 0)
        {
            Reply(GetString("Report.HowTo"));
            return;
        }
        // 誰からの報告か分かるよう、フレンドコードの無い人 (ゲストのアカウントなど) からは受け付けない
        if (FriendCodeOf(c.Player).Length == 0)
        {
            Reply(GetString("Report.NoFriendCode"));
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

        // /req は要望 (ログを付けない)。/bug・/report はバグ (ログを自動で付ける)
        bool isBug = !c.Arg(0).Equals("/req", StringComparison.OrdinalIgnoreCase) && !c.Arg(0).Equals("req", StringComparison.OrdinalIgnoreCase);
        var name = Submit(c.Player, text, Array.Empty<string>(), isBug);
        Reply(GetString("Report.Thanks") + (SendsToDeveloper ? "\n" + GetString("Report.DevNote") : ""));
        // ホストへは、誰から何が届いたかを知らせる
        if (!host) HostNote.Post(string.Format(GetString("Report.ToHost"), name, text));
    }

    /// <summary>報告を残して送る。送った人の名前を返す</summary>
    static string Submit(PlayerControl pc, string text, IReadOnlyList<string> files, bool isBug, string fcOverride = null)
    {
        var name = pc?.Data?.PlayerName ?? "?";
        var fc = string.IsNullOrEmpty(fcOverride) ? FriendCodeOf(pc) : fcOverride;
        var stamp = DateTime.Now;
        var kindLabel = GetString(isBug ? "Report.KindBug" : "Report.KindRequest");
        var line = $"[{stamp:yyyy-MM-dd HH:mm:ss}] [{kindLabel}] {name} <{fc}> [{Versions}] ({Situation(pc)}) {text.Replace("\r", "").Replace("\n", " / ")}";

        // 付けたファイルを写す (元のファイルが消えても残るように)。バグの報告には、ホストのゲームのログを自動で付ける
        var saved = new List<string>();
        if (files.Count > 0 || isBug)
        {
            try
            {
                var dir = Path.Combine(AttachDir, stamp.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(dir);
                foreach (var f in files)
                {
                    if (!File.Exists(f)) continue;
                    var dest = Path.Combine(dir, Path.GetFileName(f));
                    File.Copy(f, dest, true);
                    saved.Add(dest);
                }
                if (isBug && SaveLogZip(Path.Combine(dir, "log.zip")) is { } log) saved.Insert(0, log);
                if (saved.Count > 0) line += $"  [{GetString("Report.Attached")}: {string.Join(", ", saved.Select(Path.GetFileName))} → Reports/{Path.GetFileName(dir)}]";
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
        }

        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
        Logger.Info(line, "Report");

        if (DiscordWebhook.HasUrl)
        {
            // 大きすぎるファイルは Discord へは送らず、フォルダに残したことを書き添える
            var upload = new List<string>();
            var skipped = new List<string>();
            long total = 0;
            foreach (var f in saved)
            {
                long len = new FileInfo(f).Length;
                if (total + len <= MaxUpload) { upload.Add(f); total += len; }
                else skipped.Add(Path.GetFileName(f));
            }
            var body = $"📝 {GetString("Report.WebhookTitle")}\n{line}";
            if (skipped.Count > 0) body += "\n" + string.Format(GetString("Report.TooBig"), string.Join(", ", skipped));
            DiscordWebhook.PostWithFiles(body, upload);
        }
        LastRelay = SendToDeveloper(pc, name, fc, text, saved, isBug);
        return name;
    }

    /// <summary>最後に開発者へ送った結果 (null = 送っていない)。Task の中身は失敗の時の理由 (成功は null)</summary>
    static Task<string> LastRelay;

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

    // 付けるログの長さ (最後の部分だけ)。zip にすると 1/10 ほどになる
    const int LogTailBytes = 4_000_000;

    /// <summary>
    /// ホストのゲームのログ (BepInEx/LogOutput.log の最後の部分と、TOHL の試合のログ) を zip にして保存する。作れなければ null。
    /// ログの中の Windows のユーザー名 (C:\Users\名前\ など) は伏せる
    /// </summary>
    static string SaveLogZip(string zipPath)
    {
        try
        {
            string Mask(string s)
            {
                var user = Environment.UserName;
                if (!string.IsNullOrEmpty(user)) s = s.Replace(@"\Users\" + user + @"\", @"\Users\<user>\", StringComparison.OrdinalIgnoreCase).Replace("/Users/" + user + "/", "/Users/<user>/", StringComparison.OrdinalIgnoreCase);
                return s;
            }
            var bepLog = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            using var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create);
            void AddText(string entryName, string text)
            {
                var e = zip.CreateEntry(entryName, System.IO.Compression.CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(Mask(text));
            }
            if (File.Exists(bepLog))
            {
                // BepInEx が書いている途中のファイルなので、書き込みを許したまま読む
                using var fs = new FileStream(bepLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length > LogTailBytes) fs.Seek(-LogTailBytes, SeekOrigin.End);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                AddText("LogOutput.log", sr.ReadToEnd());
            }
            if (!string.IsNullOrEmpty(GameLog.LastText)) AddText("gamelog.txt", GameLog.LastText);
            AddText("info.txt", $"TOHL {Main.PluginVersion} ({ThisAssembly.Git.Commit})\nAmong Us {UnityEngine.Application.version}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            return zipPath;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ログを付けられなかった: {ex.Message}", nameof(BugReport));
            return null;
        }
    }

    /// <summary>フレンドコード (無ければ空)</summary>
    static string FriendCodeOf(PlayerControl pc)
    {
        if (pc == null) return "";
        string fc = null;
        try { fc = pc.Data?.FriendCode; } catch { }
        if (string.IsNullOrWhiteSpace(fc))
            try { fc = AmongUsClient.Instance.GetClientFromCharacter(pc)?.FriendCode; } catch { }
        return fc?.Trim() ?? "";
    }

    // ---- 開発者の Discord へも送る ----

    /// <summary>
    /// 開発者の Discord へ報告を届ける中継 (tools/ReportRelay・Cloudflare Workers) の受け口。空なら送らない。
    /// Discord の Webhook の URL は中継の側に隠してあり、ここには書かない (ソースを公開しても Webhook を荒らされない)。
    /// 送る間隔・回数・大きさの制限と、止めたフレンドコードの確かめも中継の側で行う
    /// </summary>
    const string DevRelayUrl = "https://srv.nkzk.win";
    static readonly System.Net.Http.HttpClient relayHttp = new() { Timeout = TimeSpan.FromSeconds(60) };
    static readonly HashSet<string> relayExt = new(StringComparer.OrdinalIgnoreCase) { ".zip", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".mp4", ".mov", ".webm", ".mkv", ".avi" };

    static Task<string> SendToDeveloper(PlayerControl pc, string name, string fc, string text, IReadOnlyList<string> saved, bool isBug)
    {
        if (string.IsNullOrEmpty(DevRelayUrl) || fc.Length == 0) return null;
        bool fromSelf = pc != null && pc == PlayerControl.LocalPlayer;
        bool fromHost = fromSelf && AmHostOrAlone;
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kind"] = fromHost ? "host" : "player",
            ["type"] = isBug ? "bug" : "request",
            ["name"] = name,
            ["friendCode"] = fc,
            ["host"] = HostName(),
            // 客からの報告は、部屋のホストのフレンドコードも送る (中継はホストのフレンドコードが無いと受け付けない)
            ["hostFriendCode"] = HostFriendCode(),
            ["situation"] = Situation(pc),
            ["version"] = Main.PluginVersion,
            ["commit"] = ThisAssembly.Git.Commit,
            ["gameVersion"] = GameVersion,
            ["platform"] = Platform,
            ["text"] = text,
        });
        // 画像と動画だけを、中継の上限 (合計 9.5MB・5 個) までにする
        var upload = new List<string>();
        long total = 0;
        foreach (var f in saved)
        {
            if (upload.Count >= 6 || !relayExt.Contains(Path.GetExtension(f))) continue;
            long len = new FileInfo(f).Length;
            if (total + len <= MaxUpload) { upload.Add(f); total += len; }
        }
        return Task.Run(async () =>
        {
            try
            {
                using var form = new System.Net.Http.MultipartFormDataContent { { new System.Net.Http.StringContent(payload, Encoding.UTF8), "payload" } };
                foreach (var f in upload) form.Add(new System.Net.Http.ByteArrayContent(File.ReadAllBytes(f)), "files", Path.GetFileName(f));
                using var res = await relayHttp.PostAsync(DevRelayUrl.TrimEnd('/') + "/report", form);
                var body = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    Logger.Info("開発者へ報告を送った", nameof(BugReport));
                    // 自分の報告なら、運営とやりとりできるよう投稿の ID と合言葉を残す
                    if (fromSelf)
                    {
                        try
                        {
                            using var ok = JsonDocument.Parse(body);
                            var root = ok.RootElement;
                            if (root.TryGetProperty("id", out var rid) && root.TryGetProperty("token", out var rtk))
                                ReportChat.Added(rid.GetString(), rtk.GetString(), text, isBug);
                        }
                        catch { }
                    }
                    return (string)null;
                }
                Logger.Warn($"開発者への報告を送れなかった: {(int)res.StatusCode} {body}", nameof(BugReport));
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? "error";
                }
                catch { }
                return "http_" + (int)res.StatusCode;
            }
            catch (Exception ex)
            {
                Logger.Warn($"開発者への報告を送れなかった: {ex.Message}", nameof(BugReport));
                return "network";
            }
        });
    }

    /// <summary>Among Us のバージョン</summary>
    static string GameVersion
    {
        get
        {
            try { return UnityEngine.Application.version; } catch { return "?"; }
        }
    }

    /// <summary>遊んでいる所 (Steam / Epic など)</summary>
    static string Platform
    {
        get
        {
            try { return Constants.GetPurchasingPlatformType().ToString(); } catch { return ""; }
        }
    }

    /// <summary>報告に書くバージョン: MOD と Among Us</summary>
    static string Versions => $"TOHL {Main.PluginVersion} ({ThisAssembly.Git.Commit}) / Among Us {GameVersion}{(Platform.Length > 0 ? $" {Platform}" : "")}";

    /// <summary>中継が断った理由を、見せる文に</summary>
    public static string RelayMessage(string error) => error switch
    {
        "too_many" => string.Format(GetString("Report.TooMany"), PerDay),
        "too_fast" => GetString("Report.TooFast"),
        "no_host_friend_code" => GetString("Report.NoHostFriendCode"),
        "no_friend_code" => GetString("Report.NoFriendCode"),
        "blocked" => GetString("Report.Blocked"),
        _ => string.Format(GetString("Report.DevFailed"), error),
    };

    /// <summary>1 日に開発者へ送れる数 (中継の PER_DAY と同じ)。運営が Discord で既読などを押すと、その人はまた送れる</summary>
    public const int PerDay = 5;

    /// <summary>部屋のホストのフレンドコード (無ければ空)。自分がホストなら自分の</summary>
    static string HostFriendCode()
    {
        if (AmHostOrAlone) return LocalFriendCode();
        string fc = null;
        try { fc = AmongUsClient.Instance?.GetHost()?.FriendCode; } catch { }
        if (string.IsNullOrWhiteSpace(fc))
            try { fc = FriendCodeOf(AmongUsClient.Instance?.GetHost()?.Character); } catch { }
        return fc?.Trim() ?? "";
    }

    static string HostName()
    {
        try { return AmongUsClient.Instance?.GetHost()?.Character?.Data?.PlayerName ?? PlayerControl.LocalPlayer?.Data?.PlayerName ?? "?"; }
        catch { return "?"; }
    }

    /// <summary>開発者へも届く設定になっているか (入力画面の案内に使う)</summary>
    public static bool SendsToDeveloper => !string.IsNullOrEmpty(DevRelayUrl);
    /// <summary>中継の URL (最後の / 無し)。空なら開発者へは送らない</summary>
    public static string RelayUrl => DevRelayUrl.TrimEnd('/');

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, "");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
    }

    // ---- ゲームの中の報告の窓 (ReportWindow) から ----

    /// <summary>操作盤のボタン・/bug (ホストが中身なしで打った時) から: 報告の窓を開く</summary>
    public static void OpenForm() => ReportWindow.Open();
    public static bool FormOpen => ReportWindow.IsOpen;

    /// <summary>自分のフレンドコード (無ければ空)</summary>
    public static string LocalFriendCode()
    {
        var fc = FriendCodeOf(PlayerControl.LocalPlayer);
        if (fc.Length == 0)
            try { fc = EOSManager.Instance?.FriendCode?.Trim() ?? ""; } catch { }
        return fc;
    }

    static bool AmHostOrAlone => AmongUsClient.Instance == null || !AmongUsClient.Instance.AmConnected || AmongUsClient.Instance.AmHost;
    static float lastWindowAt = -999f;

    /// <summary>
    /// 窓で書いた報告を送る。ホスト (または 1 人で遊んでいる時) はこの PC に残して Webhook・開発者へ。
    /// 客は自分のログを付けて開発者へ直接送る (中継が無い時は、文だけをチャットの /bug でホストへ送る)
    /// </summary>
    public static bool SendFromWindow(string text, IReadOnlyList<string> files, bool isBug, out string message, out Task<string> relay)
    {
        relay = null;
        if (text.Length > MaxLengthForm) text = text[..MaxLengthForm];
        var fc = LocalFriendCode();
        if (fc.Length == 0) { message = GetString("Report.NoFriendCode"); return false; }
        // 客は、ホストがフレンドコードを持っていない部屋からは送れない
        if (!AmHostOrAlone && SendsToDeveloper && HostFriendCode().Length == 0) { message = GetString("Report.NoHostFriendCode"); return false; }
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (!AmHostOrAlone && now - lastWindowAt < Interval) { message = GetString("Report.TooFast"); return false; }
        lastWindowAt = now;
        try
        {
            LastRelay = null;
            Submit(PlayerControl.LocalPlayer, text, files, isBug, fc);
            relay = LastRelay;
            if (AmHostOrAlone)
                message = SendsToDeveloper ? GetString("Report.SentDev") : DiscordWebhook.HasUrl ? GetString("Report.SentWebhook") : GetString("Report.Saved");
            else if (SendsToDeveloper)
                message = GetString("Report.SentDev");
            else
            {
                var line = text.Replace("\r", "").Replace("\n", " ");
                if (line.Length > MaxLength) line = line[..MaxLength];
                PlayerControl.LocalPlayer.RpcSendChat($"{(isBug ? "/bug" : "/req")} {line}");
                message = GetString("Report.Thanks");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, nameof(BugReport));
            message = GetString("Report.WinFailed");
            return false;
        }
    }
}
