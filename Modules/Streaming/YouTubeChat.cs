using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HarmonyLib;
using TownOfHostL.Core.Modes;
using UnityEngine;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// 配信中の YouTube ライブのコメントを読む。ホストが配信の URL を入れた時だけ、別のスレッドで数秒ごとに取りに行く。
/// 読んだコメントは待ち行列に積み、メインスレッドで <see cref="Drain"/> から取り出す。
/// YouTube の API の鍵は使わず、配信のチャット欄のページと同じ取り方をする (YouTube の仕様が変わると読めなくなりうる)。
/// </summary>
/// <remarks>
/// 取り方 (ページから鍵と続きの値を抜き、get_live_chat を呼ぶ) は TownOfHost-K (Modules/Streamer/Readcomment.cs・ChatData.cs) を、
/// 続きの値の 3 つの型・失敗の時の待ち時間・配信終了の判定は End K not (waffle-ful/Aeterna-End-K-not Modules/YouTubeChat) を元にした。
/// </remarks>
public static class YouTubeChat
{
    /// <summary>
    /// AuthorId は視聴者のチャンネルの ID (表示名は他人と重なりうるので、ポイントや見た目はこちらで見分ける)。無ければ名前。
    /// Paid はスーパーチャットの金額の表示 (「¥500」など)。普通のコメントは null
    /// </summary>
    public readonly record struct Comment(string AuthorId, string Author, string Text, string Paid = null);

    const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
    const float MinInterval = 5f;
    static readonly Regex KeyRegex = new("\"INNERTUBE_API_KEY\":\"(.+?)\"", RegexOptions.Compiled);
    static readonly Regex ContinuationRegex = new("\"continuation\":\"(.+?)\"", RegexOptions.Compiled);
    static readonly Regex VisitorRegex = new("\"visitorData\":\"(.+?)\"", RegexOptions.Compiled);
    static readonly Regex ClientVersionRegex = new("\"clientVersion\":\"(.+?)\"", RegexOptions.Compiled);
    static readonly Regex[] IdRegexes =
    {
        new(@"^([A-Za-z0-9_-]{11})$", RegexOptions.Compiled),
        new(@"youtu\.be/([A-Za-z0-9_-]{11})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"youtube\.com/live/([A-Za-z0-9_-]{11})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"[?&]v=([A-Za-z0-9_-]{11})", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    static readonly ConcurrentQueue<Comment> incoming = new();
    /// <summary>別のスレッドの取得の結果。状態の書き換えと記録はメインスレッドの <see cref="Tick"/> で行う</summary>
    readonly record struct Result(Session Session, List<Comment> Comments, string Error, string Detail);
    static readonly ConcurrentQueue<Result> results = new();
    static readonly HttpClient http = CreateClient();

    /// <summary>読み取りの 1 本ぶんの状態。止めたり別の配信に替えたら作り直すので、古いスレッドの結果は捨てられる</summary>
    sealed class Session
    {
        public readonly string VideoId;
        public string Key, Continuation, VisitorData, ClientVersion;
        public readonly HashSet<string> Seen = new();
        /// <summary>Seen に入れた順 (古い物から消す。全部消すと、直近の応答に残っていたコメントがもう一度新しい物として届く)</summary>
        public readonly Queue<string> SeenOrder = new();
        public float HintSeconds;
        public bool WarnedContinuation;
        /// <summary>知らない形の続きの値 (メインスレッドで 1 回だけ記録する)</summary>
        public string UnknownContinuation;
        public Session(string videoId) => VideoId = videoId;
    }

    static Session session;
    static bool inFlight;
    static float waited;
    static float backoff;
    static int failures, notFound;

    public static bool Active => session != null;
    public static string VideoId => session?.VideoId;
    /// <summary>最後の様子 (操作盤と /yt に出す)</summary>
    public static string LastStatus { get; private set; }

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        return c;
    }

    public static string ExtractVideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        foreach (var r in IdRegexes)
        {
            var m = r.Match(url);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    /// <summary>読み始める。URL が読めなければ false</summary>
    public static bool Start(string url)
    {
        var id = ExtractVideoId(url);
        if (id == null) return false;
        session = new Session(id);
        inFlight = false;
        waited = MinInterval;
        backoff = 0f;
        failures = notFound = 0;
        Main.YouTubeStreamUrl.Value = url.Trim();
        LastStatus = "Connecting";
        Logger.Info($"YouTube のコメントを読み始めた ({id})", "YouTubeChat");
        return true;
    }

    public static void Stop(bool forget = true)
    {
        if (session == null) return;
        session = null;
        if (forget) Main.YouTubeStreamUrl.Value = "";
        LastStatus = null;
        Logger.Info("YouTube のコメントを読むのをやめた", "YouTubeChat");
    }

    /// <summary>手で積む (動きの確かめ用)。読み取りと同じ道を通す。本文が「$金額 本文」ならスーパーチャットとして積む</summary>
    public static void Inject(string author, string text)
    {
        string paid = null;
        if (text.StartsWith('$'))
        {
            var sp = text.IndexOf(' ');
            paid = sp > 1 ? text[1..sp] : text[1..];
            text = sp > 1 ? text[(sp + 1)..] : "";
        }
        incoming.Enqueue(new(author, author, text, paid));
    }

    public static bool TryDequeue(out Comment comment) => incoming.TryDequeue(out comment);

    static void Tick(float delta)
    {
        while (results.TryDequeue(out var r)) Handle(r);
        // 配信の機能 (干渉・表示・投票・OBS) のどれかを使うホストの時だけ読む (使わない間に YouTube を取りに行き続けない)
        if (!StreamHub.Wanted) return;
        var s = session;
        if (s == null || inFlight) return;
        waited += delta;
        if (waited < Mathf.Max(MinInterval, s.HintSeconds) + backoff) return;
        waited = 0f;
        inFlight = true;
        Task.Run(async () =>
        {
            try { results.Enqueue(new(s, await FetchAsync(s), null, null)); }
            catch (FetchFailed f) { results.Enqueue(new(s, null, f.Message, null)); }
            catch (Exception ex) { results.Enqueue(new(s, null, "Exception", ex.Message)); }
        });
    }

    static void Handle(Result r)
    {
        // 止めた・替えた後の結果は捨てる (その時はもう次の取得を待っていない)
        if (session != r.Session) return;
        inFlight = false;
        if (r.Session.UnknownContinuation != null)
        {
            Logger.Warn($"次に読む位置が知らない形で返った: {r.Session.UnknownContinuation}", "YouTubeChat");
            r.Session.UnknownContinuation = null;
        }
        if (r.Comments != null)
        {
            failures = notFound = 0;
            backoff = 0f;
            LastStatus = "Reading";
            foreach (var c in r.Comments) incoming.Enqueue(c);
            if (r.Comments.Count > 0) Logger.Info($"新しいコメント {r.Comments.Count} 件", "YouTubeChat");
            return;
        }
        failures++;
        if (r.Error == "NotFound" && ++notFound >= 3)
        {
            Logger.Warn("配信が 3 回続けて見つからないので読むのをやめる (配信の終わり)", "YouTubeChat");
            session = null;
            Main.YouTubeStreamUrl.Value = "";
            LastStatus = "Ended";
            return;
        }
        backoff = r.Error switch
        {
            "RateLimited" => Pick(30f, 60f, 300f),
            "Forbidden" => Pick(60f, 180f, 600f),
            "NotFound" => 600f,
            _ => Pick(5f, 15f, 60f),
        };
        LastStatus = r.Error;
        Logger.Warn($"コメントを読めなかった ({r.Error}{(r.Detail == null ? "" : ": " + r.Detail)})。{backoff} 秒待つ ({failures} 回目)", "YouTubeChat");
    }

    static float Pick(float a, float b, float c) => failures switch { 1 => a, 2 => b, _ => c };

    sealed class FetchFailed : Exception { public FetchFailed(string m) : base(m) { } }

    static async Task<List<Comment>> FetchAsync(Session s)
    {
        if (s.Key == null)
        {
            using var page = await http.GetAsync("https://www.youtube.com/live_chat?v=" + s.VideoId);
            if (page.StatusCode == HttpStatusCode.NotFound) throw new FetchFailed("NotFound");
            if (!page.IsSuccessStatusCode) throw new FetchFailed("InitFailed");
            var html = await page.Content.ReadAsStringAsync();
            var key = KeyRegex.Match(html);
            var cont = ContinuationRegex.Match(html);
            var vis = VisitorRegex.Match(html);
            var ver = ClientVersionRegex.Match(html);
            // 配信がまだ・もう無い時はチャット欄のページに続きの値が入っていない
            if (!key.Success || !cont.Success || !vis.Success || !ver.Success) throw new FetchFailed("NotFound");
            s.Key = key.Groups[1].Value;
            s.Continuation = cont.Groups[1].Value;
            s.VisitorData = vis.Groups[1].Value;
            s.ClientVersion = ver.Groups[1].Value;
        }

        var body = new JsonObject
        {
            ["context"] = new JsonObject { ["client"] = new JsonObject { ["visitorData"] = s.VisitorData, ["clientName"] = "WEB", ["clientVersion"] = s.ClientVersion } },
            ["continuation"] = s.Continuation,
        }.ToJsonString();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await http.PostAsync("https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?key=" + s.Key, content);
        if (res.StatusCode == HttpStatusCode.TooManyRequests) throw new FetchFailed("RateLimited");
        if (res.StatusCode == HttpStatusCode.Forbidden) throw new FetchFailed("Forbidden");
        if (res.StatusCode == HttpStatusCode.NotFound) throw new FetchFailed("NotFound");
        if (!res.IsSuccessStatusCode) throw new FetchFailed("HttpError");

        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync());
        var live = json?["continuationContents"]?["liveChatContinuation"];
        UpdateContinuation(s, live?["continuations"]?.AsArray());
        return Parse(s, live?["actions"]?.AsArray());
    }

    /// <summary>
    /// 次に読む位置を進める。進めないと同じ応答が返り続け、読んだ印で全部捨てて新しいコメントが 0 件のまま黙って止まる。
    /// 型は 3 種あり、配列のどこに来るかも決まっていないので全部見る
    /// </summary>
    static void UpdateContinuation(Session s, JsonArray continuations)
    {
        if (continuations == null) return;
        foreach (var c in continuations)
        {
            var node = c?["invalidationContinuationData"] ?? c?["timedContinuationData"] ?? c?["reloadContinuationData"];
            if (node == null) continue;
            var next = node["continuation"]?.ToString();
            if (!string.IsNullOrEmpty(next)) s.Continuation = next;
            // YouTube が返す「これより短い間隔で読まない」の値
            // 数でも文字列でも来うるので、文字にしてから読む
            if (int.TryParse(node["timeoutMs"]?.ToString(), out var timeout) && timeout > 0) s.HintSeconds = timeout / 1000f;
            return;
        }
        if (!s.WarnedContinuation)
        {
            s.WarnedContinuation = true;
            s.UnknownContinuation = continuations.ToJsonString();
        }
    }

    static List<Comment> Parse(Session s, JsonArray actions)
    {
        var list = new List<Comment>();
        if (actions == null) return list;
        foreach (var action in actions)
        {
            var item = action?["addChatItemAction"]?["item"];
            if (item == null) continue;
            // 普通のコメントと、スーパーチャット (金額つき・本文は無いこともある)
            var r = item["liveChatTextMessageRenderer"];
            string paid = null;
            if (r == null)
            {
                r = item["liveChatPaidMessageRenderer"];
                if (r == null) continue;
                paid = r["purchaseAmountText"]?["simpleText"]?.ToString();
                if (string.IsNullOrEmpty(paid)) continue;
            }
            var id = r["id"]?.ToString();
            if (string.IsNullOrEmpty(id) || !s.Seen.Add(id)) continue;
            s.SeenOrder.Enqueue(id);
            var author = r["authorName"]?["simpleText"]?.ToString() ?? "";
            var sb = new StringBuilder();
            if (r["message"]?["runs"]?.AsArray() is { } runs)
                foreach (var run in runs)
                {
                    var t = run?["text"]?.ToString();
                    if (!string.IsNullOrEmpty(t)) sb.Append(t);
                    else if (run?["emoji"]?["shortcuts"]?[0]?.ToString() is { Length: > 0 } emoji) sb.Append(emoji);
                }
            var text = sb.ToString().Trim();
            var channel = r["authorExternalChannelId"]?.ToString();
            if (author.Length > 0 && (text.Length > 0 || paid != null)) list.Add(new(string.IsNullOrEmpty(channel) ? author : channel, author, text, paid));
        }
        while (s.SeenOrder.Count > 2000) s.Seen.Remove(s.SeenOrder.Dequeue());
        return list;
    }

    [PatchBundle(PatchBundles.Core)]
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    static class DrivePatch
    {
        public static void Postfix()
        {
            Tick(Time.unscaledDeltaTime);
            StreamHub.Pump();
        }
    }
}
