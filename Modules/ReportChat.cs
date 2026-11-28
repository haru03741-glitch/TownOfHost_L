using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TownOfHostL.Modules;

/// <summary>
/// 自分が開発者へ送った報告と、その報告の中での運営とのやりとり。
/// 報告は中継 (tools/ReportRelay) が Discord のフォーラムに投稿を作り、投稿の ID と、送った人だけが持つ合言葉を返す。
/// それを TOHL_DATA/MyReports.json に残し、報告の窓の「やりとり」で、投稿の中の運営の返事を読み・返事を書く。
/// 通信は裏で行い、結果はゲームの動いている側 (Pump) で反映する
/// </summary>
public static class ReportChat
{
    public class Message
    {
        public string Id { get; set; } = "";
        /// <summary>me = 自分 (ゲームから) / staff = 運営 / system = 状態のお知らせ</summary>
        public string From { get; set; } = "";
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public long At { get; set; }
    }

    public class Report
    {
        public string Id { get; set; } = "";
        public string Token { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsBug { get; set; }
        public long At { get; set; }
        /// <summary>open (未対応) / read (既読) / doing (修正中) / fixed (修正済み)</summary>
        public string Status { get; set; } = "open";
        /// <summary>最後に見た運営の返事の ID (これより新しい返事は「未読」)</summary>
        public string SeenId { get; set; } = "";
        public List<Message> Messages { get; set; } = new();

        public string LastId => Messages.Count > 0 ? Messages[^1].Id : "";
        public int Unread => Messages.Count(m => m.From != "me" && Newer(m.Id, SeenId));
    }

    static string Dir => Path.Combine(Environment.CurrentDirectory, "TOHL_DATA");
    static string FilePath => Path.Combine(Dir, "MyReports.json");
    const int Keep = 30;

    static List<Report> reports;
    public static IReadOnlyList<Report> Reports { get { Load(); return reports; } }
    public static int TotalUnread => Reports.Sum(r => r.Unread);

    static readonly ConcurrentQueue<Action> done = new();
    static readonly HashSet<string> busy = new();
    static readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static void Load()
    {
        if (reports != null) return;
        reports = new();
        try
        {
            if (File.Exists(FilePath))
                reports = JsonSerializer.Deserialize<List<Report>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) { Logger.Warn($"MyReports.json を読めなかった: {ex.Message}", nameof(ReportChat)); }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Logger.Warn($"MyReports.json を書けなかった: {ex.Message}", nameof(ReportChat)); }
    }

    /// <summary>Discord の ID (数字) を比べる: a が b より新しいか</summary>
    static bool Newer(string a, string b)
    {
        if (string.IsNullOrEmpty(b)) return !string.IsNullOrEmpty(a);
        if (a.Length != b.Length) return a.Length > b.Length;
        return string.CompareOrdinal(a, b) > 0;
    }

    /// <summary>中継が報告を受け取った時 (裏で呼ばれる)。自分の報告として残す</summary>
    internal static void Added(string id, string token, string text, bool isBug)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token)) return;
        var title = (text ?? "").Replace("\r", "").Split('\n')[0];
        if (title.Length > 60) title = title[..60] + "…";
        done.Enqueue(() =>
        {
            Load();
            reports.RemoveAll(r => r.Id == id);
            reports.Insert(0, new Report { Id = id, Token = token, Title = title, IsBug = isBug, At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            if (reports.Count > Keep) reports.RemoveRange(Keep, reports.Count - Keep);
            Save();
        });
    }

    /// <summary>裏の通信の結果を反映する (報告の窓が毎フレーム呼ぶ)</summary>
    public static void Pump()
    {
        while (done.TryDequeue(out var a))
        {
            try { a(); } catch (Exception ex) { Logger.Exception(ex, nameof(ReportChat)); }
        }
    }

    public static bool Busy(Report r) => r != null && busy.Contains(r.Id);

    /// <summary>新しい返事と状態を取りに行く</summary>
    public static void Refresh(Report r)
    {
        var url = BugReport.RelayUrl;
        if (r == null || string.IsNullOrEmpty(url) || !busy.Add(r.Id)) return;
        var after = r.LastId;
        var id = r.Id;
        var token = r.Token;
        Task.Run(async () =>
        {
            string status = null;
            List<Message> got = null;
            bool gone = false;
            try
            {
                using var res = await http.GetAsync($"{url}/chat?id={Uri.EscapeDataString(id)}&token={Uri.EscapeDataString(token)}&after={Uri.EscapeDataString(after)}");
                var body = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (res.IsSuccessStatusCode && root.TryGetProperty("messages", out var arr))
                {
                    status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
                    got = new();
                    foreach (var m in arr.EnumerateArray())
                        got.Add(new Message
                        {
                            Id = m.GetProperty("id").GetString() ?? "",
                            From = m.GetProperty("from").GetString() ?? "",
                            Name = m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                            Text = m.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                            At = m.TryGetProperty("at", out var at) && at.TryGetInt64(out var v) ? v : 0,
                        });
                }
                else if ((int)res.StatusCode == 403) gone = true;
            }
            catch (Exception ex) { Logger.Warn($"やりとりを読めなかった: {ex.Message}", nameof(ReportChat)); }
            done.Enqueue(() =>
            {
                busy.Remove(id);
                if (gone) Logger.Warn($"報告 {id} のやりとりは読めない (合言葉が違う・期限切れ)", nameof(ReportChat));
                if (got == null) return;
                if (status != null) r.Status = status;
                foreach (var m in got)
                    if (!r.Messages.Any(x => x.Id == m.Id)) r.Messages.Add(m);
                r.Messages.Sort((a, b) => a.Id.Length != b.Id.Length ? a.Id.Length.CompareTo(b.Id.Length) : string.CompareOrdinal(a.Id, b.Id));
                Save();
            });
        });
    }

    /// <summary>返事を書いて送る。結果は onDone(エラーの理由 / 成功は null) で (ゲームの動いている側で) 返す</summary>
    public static void Send(Report r, string text, Action<string> onDone)
    {
        var url = BugReport.RelayUrl;
        if (r == null || string.IsNullOrEmpty(url)) { onDone?.Invoke("no_chat"); return; }
        var payload = JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = r.Id, ["token"] = r.Token, ["text"] = text });
        Task.Run(async () =>
        {
            string error = null;
            try
            {
                using var res = await http.PostAsync($"{url}/chat", new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json"));
                if (!res.IsSuccessStatusCode)
                {
                    error = "http_" + (int)res.StatusCode;
                    try
                    {
                        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                        if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString() ?? error;
                    }
                    catch { }
                }
            }
            catch (Exception ex) { error = "network"; Logger.Warn($"返事を送れなかった: {ex.Message}", nameof(ReportChat)); }
            done.Enqueue(() =>
            {
                onDone?.Invoke(error);
                if (error == null) Refresh(r);
            });
        });
    }

    // ---- みんなの報告の一覧 ----

    public class ListItem
    {
        public string Id = "", Title = "", Kind = "", Status = "", Summary = "";
        public long At;
        public bool IsBug => Kind != "request";
    }

    public static List<ListItem> List { get; private set; } = new();
    public static bool ListLoaded { get; private set; }
    static bool listBusy;
    static float nextListAt;

    /// <summary>一覧を取りに行く (force でなければ 60 秒に 1 回まで)</summary>
    public static void RefreshList(float now, bool force = false)
    {
        var url = BugReport.RelayUrl;
        if (string.IsNullOrEmpty(url) || listBusy || !force && now < nextListAt) return;
        nextListAt = now + 60f;
        listBusy = true;
        Task.Run(async () =>
        {
            List<ListItem> got = null;
            try
            {
                using var res = await http.GetAsync($"{url}/list");
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                if (res.IsSuccessStatusCode && doc.RootElement.TryGetProperty("items", out var arr))
                {
                    got = new();
                    string Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    foreach (var e in arr.EnumerateArray())
                        got.Add(new ListItem
                        {
                            Id = Str(e, "id"), Title = Str(e, "title"), Kind = Str(e, "kind"), Status = Str(e, "status"), Summary = Str(e, "summary"),
                            At = e.TryGetProperty("at", out var at) && at.TryGetInt64(out var v) ? v : 0,
                        });
                }
            }
            catch (Exception ex) { Logger.Warn($"報告の一覧を読めなかった: {ex.Message}", nameof(ReportChat)); }
            done.Enqueue(() =>
            {
                listBusy = false;
                if (got == null) return;
                List = got;
                ListLoaded = true;
            });
        });
    }

    public static bool ListBusy => listBusy;

    /// <summary>運営の返事を見たことにする</summary>
    public static void MarkSeen(Report r)
    {
        if (r == null) return;
        var last = r.Messages.LastOrDefault(m => m.From != "me");
        if (last == null || !Newer(last.Id, r.SeenId)) return;
        r.SeenId = last.Id;
        Save();
    }

    static float nextAllAt;

    /// <summary>窓が開いている間、全部の報告の状態と返事を時々取りに行く</summary>
    public static void RefreshAllSometimes(float now, float every)
    {
        if (now < nextAllAt) return;
        nextAllAt = now + every;
        foreach (var r in Reports.Take(10)) Refresh(r);
    }
}
