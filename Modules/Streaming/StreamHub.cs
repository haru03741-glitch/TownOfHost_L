using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// 配信のコメントの受け口。<see cref="YouTubeChat"/> が読んだコメントをここで 1 回だけ取り出し、
/// 締め出しと文字の無害化を済ませてから、視聴者の干渉・マップ投票・画面の表示 (観客席・コメントの板)・OBS 用のページへ配る。
/// 表示の設定はホストの手元だけの物 (プリセットには入れない) で、操作盤の「配信」欄から切り替える。
/// </summary>
public static class StreamHub
{
    // ===== 手元の設定 =====

    public static ConfigEntry<bool> ShowBoard { get; private set; }
    public static ConfigEntry<bool> ShowSeats { get; private set; }
    public static ConfigEntry<bool> HideInMeeting { get; private set; }
    public static ConfigEntry<bool> MapVoteOn { get; private set; }
    public static ConfigEntry<bool> ObsOn { get; private set; }
    public static ConfigEntry<bool> BoardCollapsed { get; private set; }
    public static ConfigEntry<bool> Sound { get; private set; }
    public static ConfigEntry<int> UiScale { get; private set; }
    /// <summary>観客席と板の置き場 (画面の幅・高さに対する割合)。負なら既定の場所</summary>
    public static ConfigEntry<float> DockX { get; private set; }
    public static ConfigEntry<float> DockY { get; private set; }

    public static void BindConfig(ConfigFile config)
    {
        ShowBoard = config.Bind("Stream", "ShowBoard", true);
        ShowSeats = config.Bind("Stream", "ShowSeats", true);
        HideInMeeting = config.Bind("Stream", "HideInMeeting", true);
        MapVoteOn = config.Bind("Stream", "MapVote", false);
        ObsOn = config.Bind("Stream", "Obs", false);
        BoardCollapsed = config.Bind("Stream", "BoardCollapsed", false);
        Sound = config.Bind("Stream", "Sound", true);
        UiScale = config.Bind("Stream", "Scale", 100);
        DockX = config.Bind("Stream", "DockX", -1f);
        DockY = config.Bind("Stream", "DockY", -1f);
    }

    /// <summary>大きさの選択肢 (%)。操作盤のボタンで順に回す</summary>
    public static readonly int[] Scales = { 80, 100, 125, 150 };

    static bool AmHost => AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

    /// <summary>配信のコメントを使う物がどれか 1 つでも ON のホストか (YouTube を読みに行くのはこの時だけ)</summary>
    public static bool Wanted => AmHost && (Audience.Enabled?.GetBool() == true || ShowBoard.Value || ShowSeats.Value || MapVoteOn.Value || ObsOn.Value);

    // ===== コメントの記録 (板・OBS が読む。新しい物が後ろ) =====

    public enum EntryKind : byte { Comment, Paid, Executed }

    /// <summary>
    /// 1 件ぶん。Text と Author は無害化済み (TextMeshPro と HTML のどちらに入れても書式を壊さない)。
    /// Look は視聴者ごとの見た目 (クルーの色と帽子)。Accent は干渉のコマンドの色 (コマンドでなければ a=0)
    /// </summary>
    public sealed class Entry
    {
        public EntryKind Kind;
        public string Id, Author, Text, Paid;
        public ViewerLook Look;
        public Color Accent;
        public float Time;
        public int Serial;
    }

    public const int FeedLimit = 60;
    static readonly List<Entry> feed = new();
    static int serial;
    /// <summary>記録が変わるたびに増える (表示はこれが変わった時だけ作り直す)</summary>
    public static int Version { get; private set; }
    public static IReadOnlyList<Entry> Feed => feed;
    /// <summary>直近 60 秒のコメントの数 (板の見出しに出す)</summary>
    public static int PerMinute
    {
        get
        {
            var now = UnityEngine.Time.realtimeSinceStartup;
            var n = 0;
            for (var i = feed.Count - 1; i >= 0 && now - feed[i].Time <= 60f; i--)
                if (feed[i].Kind != EntryKind.Executed) n++;
            return n;
        }
    }

    /// <summary>新しいコメントが届いた (観客席が聞く)</summary>
    public static event Action<Entry> Arrived;
    /// <summary>干渉が発動した (観客席が聞く)</summary>
    public static event Action<Entry> Executed;

    static bool resumed;
    static int mapVoteTick;

    /// <summary>毎フレーム (メインスレッド)。届いたコメントを配る</summary>
    public static void Pump()
    {
        var wanted = Wanted;
        // 前に読んでいた配信があれば、使う物が ON になって最初の 1 回だけ読み直す
        if (wanted && !resumed)
        {
            resumed = true;
            var saved = Main.YouTubeStreamUrl.Value;
            if (!YouTubeChat.Active && !string.IsNullOrWhiteSpace(saved)) YouTubeChat.Start(saved);
        }
        while (YouTubeChat.TryDequeue(out var c))
        {
            // 使う物が無い間に届いた分は溜めずに捨てる
            if (!wanted) continue;
            try { Deliver(c); }
            catch (Exception ex) { Logger.Exception(ex, nameof(StreamHub)); }
        }
        Audience.Tick();
        if ((++mapVoteTick & 15) == 0) MapVote.Tick();
        ObsServer.Tick();
    }

    static void Deliver(YouTubeChat.Comment c)
    {
        if (string.IsNullOrWhiteSpace(c.AuthorId)) return;
        var author = Clean(c.Author, 30);
        var text = Clean(c.Text, 200);
        if (author.Length == 0 || (text.Length == 0 && c.Paid == null)) return;
        if (!Audience.Accept(c.AuthorId, author)) return;

        var isCommand = Audience.TryGetCommand(text, out var accent);
        var e = Add(new Entry
        {
            Kind = c.Paid != null ? EntryKind.Paid : EntryKind.Comment,
            Id = c.AuthorId,
            Author = author,
            Text = text,
            Paid = c.Paid == null ? null : Clean(c.Paid, 16),
            Look = ViewerLook.For(c.AuthorId),
            Accent = isCommand ? accent : default,
        });
        // 干渉のコマンドの名前は本文のまま (無害化した < > は使わない)
        Audience.OnComment(c.AuthorId, author, text);
        MapVote.OnComment(c.AuthorId, text);
        Arrived?.Invoke(e);
    }

    /// <summary>干渉を行った時に <see cref="Audience"/> から呼ぶ</summary>
    public static void OnExecuted(string id, string author, string what, Color accent)
    {
        var e = Add(new Entry
        {
            Kind = EntryKind.Executed,
            Id = id,
            Author = Clean(author, 30),
            Text = Clean(what, 40),
            Look = ViewerLook.For(id),
            Accent = accent,
        });
        try { Executed?.Invoke(e); }
        catch (Exception ex) { Logger.Exception(ex, nameof(StreamHub)); }
    }

    static Entry Add(Entry e)
    {
        e.Time = UnityEngine.Time.realtimeSinceStartup;
        e.Serial = ++serial;
        feed.Add(e);
        if (feed.Count > FeedLimit) feed.RemoveRange(0, feed.Count - FeedLimit);
        Version++;
        return e;
    }

    /// <summary>
    /// Mod を外す前に呼ぶ。Harmony のパッチを外しても部品 (MonoBehaviour) は動き続けるので、
    /// 画面に置いた絵・操作盤の開閉ボタン・OBS 用のサーバーをここで止める
    /// </summary>
    public static void ShutdownForUnload()
    {
        try
        {
            StreamOverlay.Shutdown();
            ObsServer.Stop();
            HostPanelButton.Shutdown();
            YouTubeChat.Stop(false);
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(StreamHub)); }
    }

    /// <summary>板の記録を消す (観客席の子はそのまま)</summary>
    public static void ClearFeed()
    {
        feed.Clear();
        Version++;
    }

    /// <summary>
    /// 視聴者の文を、TextMeshPro の書式 (&lt;color&gt; など) と HTML のどちらにも効かない形にする。
    /// 不等号は全角へ、改行と制御文字は空白へ。長すぎる物は切る
    /// </summary>
    public static string Clean(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, max + 1));
        foreach (var ch in s)
        {
            if (sb.Length >= max) { sb.Append('…'); break; }
            sb.Append(ch switch
            {
                '<' => '＜',
                '>' => '＞',
                '&' => '＆',
                _ when char.IsControl(ch) => ' ',
                _ => ch,
            });
        }
        return sb.ToString().Trim();
    }
}

/// <summary>
/// 視聴者ごとの見た目。チャンネルの ID から決めるので、同じ人はいつも同じ色と帽子のクルーになり、板の名前の色もそれにそろう
/// </summary>
public readonly struct ViewerLook
{
    public readonly int ColorId;
    public readonly uint Hash;

    ViewerLook(int colorId, uint hash)
    {
        ColorId = colorId;
        Hash = hash;
    }

    public static ViewerLook For(string id)
    {
        // 名前や ID が同じなら毎回同じ (FNV-1a)
        uint h = 2166136261;
        foreach (var c in id ?? "") h = (h ^ c) * 16777619;
        var n = Mathf.Max(1, Palette.PlayerColors.Length);
        return new ViewerLook((int)(h % (uint)n), h);
    }

    public Color32 Color => ColorId >= 0 && ColorId < Palette.PlayerColors.Length ? Palette.PlayerColors[ColorId] : new Color32(255, 255, 255, 255);

    /// <summary>紺の板の上で読める明るさに寄せた色 (黒や茶のクルーでも名前が沈まない)</summary>
    public Color32 BrightColor
    {
        get
        {
            Color c = Color;
            var lum = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            return lum < 0.55f ? UnityEngine.Color.Lerp(c, UnityEngine.Color.white, 0.55f - lum + 0.15f) : c;
        }
    }

    /// <summary>3 人に 2 人くらいは帽子をかぶる</summary>
    public string HatId
    {
        get
        {
            try
            {
                var h = Hash >> 8;
                if (h % 3 == 0 || !HatManager.InstanceExists) return "hat_NoHat";
                var hats = HatManager.Instance.allHats;
                if (hats == null || hats.Length == 0) return "hat_NoHat";
                return hats[(int)(h % (uint)hats.Length)].ProdId;
            }
            catch { return "hat_NoHat"; }
        }
    }

    public bool FaceLeft => (Hash & 0x10000) != 0;
    /// <summary>呼吸の揺れの位相 (0〜2π)</summary>
    public float Phase => (Hash & 0xff) / 255f * 6.28f;

    public static string Hex(Color32 c) => $"{c.r:X2}{c.g:X2}{c.b:X2}";
}
