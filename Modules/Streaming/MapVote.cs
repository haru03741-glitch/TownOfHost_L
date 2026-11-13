using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using TownOfHostL.Attributes;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// 配信の視聴者がコメント (!skeld・!ポーラス など) でマップに投票し、試合を始める時に一番票の多いマップにする (同じ数なら乱数)。
/// 1 人 1 票 (チャンネルの ID ごと・後から入れ直せる)。試合を始めたら票を空にする。ロビーの間だけ受ける。
/// </summary>
/// <remarks>コメントのマップ投票は TownOfHost-hamo (rar006/TownOfHost-hamo Modules/YouTubeMapVote.cs) を元にした。</remarks>
public static class MapVote
{
    public readonly record struct MapChoice(byte MapId, string Name, string[] Words);

    public static readonly MapChoice[] Maps =
    {
        new(0, "Skeld", new[] { "skeld", "スケルド" }),
        new(1, "MIRA HQ", new[] { "mira", "mirahq", "ミラ" }),
        new(2, "Polus", new[] { "polus", "ポーラス" }),
        new(4, "Airship", new[] { "airship", "エアシップ" }),
        new(5, "Fungle", new[] { "fungle", "ファングル" }),
    };

    static readonly Dictionary<string, byte> votes = new(StringComparer.Ordinal);
    static readonly int[] counts = new int[Maps.Length];
    /// <summary>票が変わるたびに増える (表示はこれが変わった時だけ作り直す)</summary>
    public static int Version { get; private set; }
    public static int Total => votes.Count;
    public static int CountAt(int index) => counts[index];

    /// <summary>票を受けるか (投票が ON・ロビー・本編の通常モード。かくれんぼの試合の設定には効かないので受けない)</summary>
    public static bool Active => StreamHub.MapVoteOn.Value && GameStates.IsLobby && normalMode;
    static bool normalMode = true;

    /// <summary>毎フレーム (StreamHub から)。受けない間に残った票は捨てる (前の部屋や前のロビーの票を次へ持ち越さない)</summary>
    public static void Tick()
    {
        // 起動の途中 (通信の部品が無い間) は、ロビーかどうかも本編の設定も読むと例外になる
        if (AmongUsClient.Instance == null)
        {
            if (votes.Count > 0) Clear();
            return;
        }
        if (StreamHub.MapVoteOn.Value && GameStates.IsLobby)
        {
            try
            {
                var gm = GameOptionsManager.Instance;
                normalMode = gm == null || gm.CurrentGameOptions == null || gm.CurrentGameOptions.GameMode != GameModes.HideNSeek;
            }
            catch { normalMode = true; }
        }
        if (!Active && votes.Count > 0 && !Decided) Clear();
    }

    public static void OnComment(string voterId, string text)
    {
        if (!Active || string.IsNullOrWhiteSpace(voterId) || string.IsNullOrWhiteSpace(text)) return;
        var t = text.Trim().Replace('！', '!');
        if (!t.StartsWith('!')) return;
        var end = t.IndexOfAny(new[] { ' ', '　' });
        var word = (end < 0 ? t[1..] : t[1..end]).ToLowerInvariant();
        for (var i = 0; i < Maps.Length; i++)
        {
            if (Array.IndexOf(Maps[i].Words, word) < 0) continue;
            if (votes.TryGetValue(voterId, out var before) && before == Maps[i].MapId) return;
            votes[voterId] = Maps[i].MapId;
            Recount();
            return;
        }
    }

    static void Recount()
    {
        Array.Clear(counts);
        foreach (var v in votes.Values)
            for (var i = 0; i < Maps.Length; i++)
                if (Maps[i].MapId == v) counts[i]++;
        Version++;
    }

    public static void Clear()
    {
        if (votes.Count == 0) return;
        votes.Clear();
        Recount();
    }

    /// <summary>
    /// 試合を始める時に呼ぶ (ホストのボタンでも自動スタートでも)。票があれば一番多いマップに替え、替えたら true。
    /// ランダムマップの設定より、視聴者の票を優先する
    /// </summary>
    public static bool ApplyAtGameStart()
    {
        if (!StreamHub.MapVoteOn.Value || votes.Count == 0 || Main.NormalOptions == null) return false;
        Decided = true;
        var max = 0;
        foreach (var c in counts) max = Math.Max(max, c);
        var top = new List<int>();
        for (var i = 0; i < Maps.Length; i++)
            if (counts[i] == max) top.Add(i);
        var pick = Maps[top[IRandom.Instance.Next(top.Count)]];
        Logger.Info($"配信のマップ投票で {pick.Name} に決めた ({max}/{votes.Count} 票)", nameof(MapVote));
        Clear();
        if (Main.NormalOptions.MapId == pick.MapId) return false;
        Main.NormalOptions.MapId = pick.MapId;
        return true;
    }

    /// <summary>この試合の開始で、投票でマップを決めたか (ランダムマップを飛ばす)。試合を始めるか秒読みを取り消したら外す</summary>
    public static bool Decided { get; private set; }

    public static void EndStart() => Decided = false;

    /// <summary>試合が始まったら印を外す (秒読みの後に開始の処理を通らない道があっても残さない)</summary>
    [GameModuleInitializer]
    public static void Init() => Decided = false;

    /// <summary>ApplyAtGameStart で替えた設定を、客へ送り直す</summary>
    public static void SyncSettings()
    {
        if (PlayerControl.LocalPlayer == null || GameOptionsManager.Instance == null || Main.NormalOptions == null) return;
        var opt = Main.NormalOptions.Cast<IGameOptions>();
        PlayerControl.LocalPlayer.RpcSyncSettings(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(opt, AprilFoolsMode.IsAprilFoolsModeToggledOn));
    }
}
