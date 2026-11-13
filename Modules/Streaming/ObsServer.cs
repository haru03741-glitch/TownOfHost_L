using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// OBS のブラウザソースで読む、配信のコメントとマップ投票のページ (http://localhost:52540/)。ゲームの中の板と同じ見た目で、背景は透明。
/// 自分の PC からだけ繋がる (127.0.0.1 で待ち、Host と Origin の見出しも確かめる)。
/// サーバーのスレッドはゲームの物に触らず、メインスレッドが作った文字列を返すだけ。ページを誰も読んでいない間は中身を作らない。
/// ページは視聴者の文を textContent で書く (HTML として読ませない)。
/// </summary>
/// <remarks>OBS 用に手元で小さな HTTP サーバーを立てる形は TownOfHost-hamo (rar006/TownOfHost-hamo Modules/Streamer/ObsOverlay.cs) を元にした。</remarks>
public static class ObsServer
{
    public const int Port = 52540;
    public static string Url => $"http://localhost:{Port}/";
    /// <summary>始められなかった理由 (操作盤に出す)。動いていれば null</summary>
    public static string Error { get; private set; }

    static TcpListener listener;
    static Thread thread;
    static volatile string stateJson = "{}";
    static long lastRequestMs = -100000;
    static float nextRetry, nextBuild;
    static int builtVersion = -1, builtVoteVersion = -1;

    /// <summary>毎フレーム (メインスレッド)。設定に合わせて始め・止め、読まれている間だけ中身を作り直す</summary>
    public static void Tick()
    {
        if (!StreamHub.ObsOn.Value)
        {
            Stop();
            Error = null;
            return;
        }
        if (listener == null)
        {
            if (Time.unscaledTime < nextRetry) return;
            nextRetry = Time.unscaledTime + 10f;
            Start();
            if (listener == null) return;
        }
        // 5 秒読まれていなければ作らない
        if (Environment.TickCount64 - Interlocked.Read(ref lastRequestMs) > 5000) return;
        var now = Time.unscaledTime;
        if (builtVersion == StreamHub.Version && builtVoteVersion == MapVote.Version && now < nextBuild) return;
        builtVersion = StreamHub.Version;
        builtVoteVersion = MapVote.Version;
        nextBuild = now + 1f;
        try { stateJson = BuildState(); }
        catch (Exception ex) { Logger.Exception(ex, nameof(ObsServer)); }
    }

    static string BuildState()
    {
        var items = new JsonArray();
        var feed = StreamHub.Feed;
        for (var i = Math.Max(0, feed.Count - 30); i < feed.Count; i++)
        {
            var e = feed[i];
            items.Add(new JsonObject
            {
                ["id"] = e.Serial,
                ["k"] = e.Kind switch { StreamHub.EntryKind.Paid => "p", StreamHub.EntryKind.Executed => "x", _ => "c" },
                ["a"] = e.Author,
                ["t"] = e.Text,
                ["p"] = e.Paid,
                ["c"] = "#" + ViewerLook.Hex(e.Look.BrightColor),
                ["x"] = e.Accent.a > 0f ? "#" + ViewerLook.Hex(e.Accent) : null,
                ["xt"] = e.Kind == StreamHub.EntryKind.Executed ? string.Format(GetString("Stream.Executed"), e.Author, e.Text) : null,
            });
        }
        var maps = new JsonArray();
        for (var i = 0; i < MapVote.Maps.Length; i++)
            maps.Add(new JsonObject { ["n"] = MapVote.Maps[i].Name, ["c"] = MapVote.CountAt(i) });
        var status = YouTubeChat.Active ? YouTubeChat.LastStatus : null;
        return new JsonObject
        {
            ["live"] = YouTubeChat.Active && status == "Reading",
            ["liveLabel"] = GetString("Stream.Live"),
            ["title"] = GetString("Stream.BoardTitle"),
            ["status"] = status is null or "Reading" ? "" : GetString(status is "Connecting" or "Ended" ? $"Stream.Status.{status}" : "Stream.Status.Retry"),
            ["perMinute"] = string.Format(GetString("Stream.PerMinute"), StreamHub.PerMinute),
            ["empty"] = GetString("Stream.Empty"),
            ["items"] = items,
            ["vote"] = new JsonObject
            {
                ["on"] = MapVote.Active,
                ["title"] = GetString("Stream.MapVoteTitle"),
                ["hint"] = MapVote.Total > 0 ? string.Format(GetString("Stream.MapVoteTotal"), MapVote.Total) : GetString("Stream.MapVoteHint"),
                ["maps"] = maps,
            },
        }.ToJsonString();
    }

    static void Start()
    {
        try
        {
            var l = new TcpListener(IPAddress.Loopback, Port);
            l.Start();
            listener = l;
            thread = new Thread(() => Serve(l)) { IsBackground = true, Name = "TOHL OBS" };
            thread.Start();
            Error = null;
            Logger.Info($"OBS 用のページを開いた: {Url}", nameof(ObsServer));
        }
        catch (Exception ex)
        {
            listener = null;
            Error = string.Format(GetString("Stream.ObsPortBusy"), Port);
            Logger.Warn($"OBS 用のページを開けなかった (ポート {Port}): {ex.Message}", nameof(ObsServer));
        }
    }

    public static void Stop()
    {
        var l = listener;
        if (l == null) return;
        listener = null;
        thread = null;
        try { l.Stop(); } catch { }
        Logger.Info("OBS 用のページを閉じた", nameof(ObsServer));
    }

    // ===== サーバーのスレッド (ゲームの物に触らない) =====

    static void Serve(TcpListener l)
    {
        while (listener == l)
        {
            TcpClient client;
            try { client = l.AcceptTcpClient(); }
            catch (Exception) when (listener != l) { return; }
            catch (Exception) { continue; }
            // 何も送らずに繋ぎっぱなしの接続 (ブラウザの先読み) があっても、他の応答を待たせない
            System.Threading.Tasks.Task.Run(() => Handle(client));
        }
    }

    static void Handle(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                using var stream = client.GetStream();
                // 見出しの終わり (空行) まで読む。分かれて届くことがある
                var buf = new byte[8192];
                var len = 0;
                while (len < buf.Length)
                {
                    var n = stream.Read(buf, len, buf.Length - len);
                    if (n <= 0) break;
                    len += n;
                    if (Encoding.ASCII.GetString(buf, 0, len).Contains("\r\n\r\n")) break;
                }
                var (status, type, body) = Respond(Encoding.ASCII.GetString(buf, 0, len));
                var head = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                stream.Write(head, 0, head.Length);
                stream.Write(body, 0, body.Length);
            }
        }
        catch (Exception) { }
    }

    static (string, string, byte[]) Respond(string request)
    {
        var lines = request.Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2 || first[0] != "GET") return ("405 Method Not Allowed", "text/plain", Encoding.ASCII.GetBytes("GET only"));
        // 他のサイトのページからの読み取りと、名前の付け替え (DNS rebinding) を断る
        string host = null, origin = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) host = line[5..].Trim();
            else if (line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase)) origin = line[7..].Trim();
        }
        if (!IsLocal(host) || (origin != null && origin != "null" && !IsLocal(origin.Replace("http://", ""))))
            return ("403 Forbidden", "text/plain", Encoding.ASCII.GetBytes("forbidden"));
        var path = first[1];
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        switch (path)
        {
            case "/state":
                Interlocked.Exchange(ref lastRequestMs, Environment.TickCount64);
                return ("200 OK", "application/json; charset=utf-8", Encoding.UTF8.GetBytes(stateJson));
            case "/":
                Interlocked.Exchange(ref lastRequestMs, Environment.TickCount64);
                return ("200 OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page));
            default:
                return ("404 Not Found", "text/plain", Encoding.ASCII.GetBytes("not found"));
        }
    }

    static bool IsLocal(string host) => host == $"localhost:{Port}" || host == $"127.0.0.1:{Port}";

    // ===== ページ (?only=chat|vote・?max=件数・?scale=倍率) =====

    const string Page = """
<!doctype html>
<html><head><meta charset="utf-8"><title>TOHL Stream</title>
<style>
:root{--navy:rgba(15,18,38,.92);--rim:rgba(92,122,199,.55);--text:#eef2ff;--muted:#9eadcc;--red:#e81a3d;--gold:#ffd54a}
*{box-sizing:border-box}
html,body{margin:0;background:transparent;color:var(--text);font-family:"BIZ UDPGothic","Yu Gothic UI","Meiryo",sans-serif;overflow:hidden}
#wrap{position:fixed;left:12px;right:12px;bottom:12px;display:flex;flex-direction:column;gap:10px;transform-origin:left bottom}
.card{background:var(--navy);border:1.5px solid var(--rim);border-radius:16px;padding:10px 14px 12px;box-shadow:0 6px 24px rgba(0,0,0,.35)}
.head{display:flex;align-items:center;gap:8px;padding-bottom:7px;margin-bottom:6px;border-bottom:1px solid rgba(255,255,255,.08)}
.pill{background:#59617a;color:#fff;font-weight:700;font-size:13px;padding:3px 9px;border-radius:999px;letter-spacing:.04em}
.pill.on{background:var(--red)}
.title{font-weight:700;font-size:15px;color:#dbe6ff;flex:1}
.meta{font-size:13px;color:var(--muted)}
#list{display:flex;flex-direction:column;gap:5px;font-size:18px;line-height:1.35}
.row{animation:in .28s ease-out;word-break:break-word}
@keyframes in{from{opacity:0;transform:translateY(8px)}to{opacity:1;transform:none}}
.name{font-weight:700}
.sep{color:#7f8aa3}
.cmd{font-weight:700}
.paid{background:rgba(255,193,7,.16);border-left:3px solid var(--gold);padding:3px 8px;border-radius:6px}
.amount{color:var(--gold);font-weight:700;margin-right:6px}
.exec{font-weight:700}
.empty{color:var(--muted)}
.vrow{display:grid;grid-template-columns:92px 1fr 34px;align-items:center;gap:8px;font-size:15px;font-weight:700;margin:3px 0}
.track{height:10px;border-radius:5px;background:rgba(255,255,255,.08);overflow:hidden}
.bar{height:100%;border-radius:5px;background:#66b3ff;transition:width .35s ease}
.lead .bar{background:#ffc733}.lead{color:var(--gold)}
.count{text-align:right;color:var(--muted)}.lead .count{color:var(--gold)}
</style></head><body>
<div id="wrap">
 <div class="card" id="vote" hidden><div class="head"><span class="title" id="vtitle"></span><span class="meta" id="vhint"></span></div><div id="vlist"></div></div>
 <div class="card" id="chat"><div class="head"><span class="pill" id="pill"></span><span class="title" id="title"></span><span class="meta" id="meta"></span></div><div id="list"></div></div>
</div>
<script>
const qs=new URLSearchParams(location.search);
const only=qs.get('only'),max=Math.max(1,Math.min(30,+qs.get('max')||12));
const wrap=document.getElementById('wrap');
const sc=+qs.get('scale');if(sc>0)wrap.style.transform=`scale(${sc})`;
if(only==='vote')document.getElementById('chat').hidden=true;
const list=document.getElementById('list');const shown=new Map();
function el(tag,cls,text){const e=document.createElement(tag);if(cls)e.className=cls;if(text!=null)e.textContent=text;return e}
function row(it){
 const r=el('div','row');
 if(it.k==='x'){const s=el('span','exec','▶ '+it.xt);s.style.color=it.x;r.append(s);return r}
 if(it.k==='p'){r.classList.add('paid');r.append(el('span','amount',it.p))}
 const n=el('span','name',it.a);n.style.color=it.c;r.append(n);
 if(it.t){r.append(el('span','sep','：'));const t=el('span',it.x?'cmd':'',it.t);if(it.x)t.style.color=it.x;r.append(t)}
 return r}
function render(s){
 document.getElementById('pill').textContent='● '+s.liveLabel;
 document.getElementById('pill').classList.toggle('on',s.live);
 document.getElementById('title').textContent=s.title;
 document.getElementById('meta').textContent=(s.status?s.status+'  ':'')+s.perMinute;
 const items=s.items.slice(-max);const keep=new Set(items.map(i=>i.id));
 for(const [id,e] of shown)if(!keep.has(id)){e.remove();shown.delete(id)}
 for(const it of items)if(!shown.has(it.id)){const e=row(it);list.append(e);shown.set(it.id,e)}
 let empty=list.querySelector('.empty');
 if(items.length===0&&!empty)list.append(el('div','empty',s.empty));else if(items.length&&empty)empty.remove();
 const v=document.getElementById('vote');v.hidden=only==='chat'||!s.vote.on;
 if(!v.hidden){document.getElementById('vtitle').textContent=s.vote.title;document.getElementById('vhint').textContent=s.vote.hint;
  const vl=document.getElementById('vlist');const top=Math.max(0,...s.vote.maps.map(m=>m.c));
  vl.replaceChildren(...s.vote.maps.map(m=>{const r=el('div','vrow'+(m.c>0&&m.c===top?' lead':''));r.append(el('span','',m.n));
   const tr=el('div','track');const b=el('div','bar');b.style.width=(top?100*m.c/top:0)+'%';tr.append(b);r.append(tr);r.append(el('span','count',String(m.c)));return r}))}}
async function poll(){try{const r=await fetch('/state',{cache:'no-store'});if(r.ok)render(await r.json())}catch(e){}setTimeout(poll,1000)}
poll();
</script></body></html>
""";
}
