// Based on https://github.com/Gurge44/EndlessHostRoles Modules/SpamManager.cs and
// https://github.com/haru03741-glitch/More-Roles-Plus src/Commands/ChatGuard.cs (GPL-3.0)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// ホストの端末で、届いたチャットを見て荒らしを部屋から出す。禁止語は TOHL_DATA/BanWords.txt (1 行 1 語・# で始まる行は飛ばす)。
/// ロビーでの「開始」の催促も数えられる。決まった回数までは本人に注意を出し、達したらキックする。回数は部屋に入り直すまで覚える
/// </summary>
public static class ChatGuard
{
    public static OptionItem Enabled;
    public static OptionItem BanWords;
    public static OptionItem StartSpam;
    public static OptionItem Times;

    public static void SetupCustomOption()
    {
        Enabled = StringOptionItem.Create("ChatGuard", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.All);
        BanWords = StringOptionItem.Create("ChatGuard.BanWords", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetGameMode(CustomGameMode.All);
        StartSpam = StringOptionItem.Create("ChatGuard.StartSpam", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetGameMode(CustomGameMode.All);
        Times = IntegerOptionItem.Create("ChatGuard.Times", new(1, 10, 1), 3, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Times)
            .SetGameMode(CustomGameMode.All);
    }

    private static readonly string WordsPath = @"./TOHL_DATA/BanWords.txt";
    private static List<string> words;
    private static DateTime wordsStamp;
    private static readonly Dictionary<int, int> strikes = new(); // clientId → 回数

    // 「開始」の催促とみなす言葉 (文字と数字だけを残し、小文字にしてから全体で比べる)
    private static readonly HashSet<string> StartWords = new(StringComparer.Ordinal)
    {
        "start", "plsstart", "pleasestart", "startplease", "startpls",
        "開始", "開始して", "かいし", "はじめて", "始めて", "はよ", "はよはよ", "はやく", "早く", "すたーと", "スタート",
    };

    /// <summary>部屋に入った時に呼ぶ</summary>
    public static void Reset() => strikes.Clear();

    private static List<string> Words()
    {
        // 書き換えたら次のチャットから効くように、ファイルが変わった時だけ読み直す
        try
        {
            if (!File.Exists(WordsPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(WordsPath));
                File.WriteAllText(WordsPath, "# 1 行に 1 語。チャットにこの語が含まれていたら禁止語として数える (大文字と小文字は区別しない)\n");
            }
            var stamp = File.GetLastWriteTimeUtc(WordsPath);
            if (words != null && stamp == wordsStamp) return words;
            wordsStamp = stamp;
            words = new List<string>();
            foreach (string raw in File.ReadAllLines(WordsPath))
            {
                string w = raw.Trim();
                if (w.Length > 0 && w[0] != '#') words.Add(w);
            }
        }
        catch (Exception e) { Logger.Error($"禁止語の読み込みに失敗: {e.Message}", "ChatGuard"); }
        return words ??= new List<string>();
    }

    private static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c) || c == 'ー') sb.Append(c);
        return sb.ToString();
    }

    public enum Result { None, Warned, Kicked }

    /// <summary>客のチャットが届いた時に呼ぶ (ホストだけ)</summary>
    public static Result OnChat(PlayerControl sender, string text)
    {
        if (sender == null || sender.AmOwner || string.IsNullOrEmpty(text) || !AmongUsClient.Instance.AmHost || !Enabled.GetBool()) return Result.None;
        string why = null;
        if (StartSpam.GetBool() && GameStates.IsLobby && StartWords.Contains(Normalize(text)))
            why = GetString("ChatGuard.AskingToStart");
        else if (BanWords.GetBool())
        {
            foreach (var w in Words())
                if (text.Contains(w, StringComparison.OrdinalIgnoreCase)) { why = GetString("ChatGuard.BannedWord"); break; }
        }
        if (why == null) return Result.None;

        int clientId = sender.OwnerId;
        int n = strikes.TryGetValue(clientId, out int c) ? c + 1 : 1;
        strikes[clientId] = n;
        string name = ChatPushOut.Escape(sender.Data != null ? sender.Data.PlayerName : $"#{sender.PlayerId}");
        int limit = Times.GetInt();
        Logger.Info($"{sender.PlayerId} の書き込みを数えます ({why} {n}/{limit})", "ChatGuard");
        if (n >= limit)
        {
            Logger.SendInGame(string.Format(GetString("ChatGuard.Kicked"), name, why, n));
            AmongUsClient.Instance.KickPlayer(clientId, false);
            return Result.Kicked;
        }
        Utils.SendMessageAutoSplit(string.Format(GetString("ChatGuard.Strike"), why, n, limit), true, sendTo: sender.PlayerId);
        return Result.Warned;
    }
}
