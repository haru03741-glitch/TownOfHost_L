using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TownOfHostL.Attributes;
using TownOfHostL.SettingsUI;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// ロビーの投票。/poll 質問 選択肢… で自由な投票、/pv で保存したプリセットの投票を始める。
/// 投票はチャットに番号だけを書く (Mod 無しの客もそのまま使える)。書き直すと票を入れ替える。
/// 決まった時間が経つか /pe で締め、結果を全員に出す。プリセットの投票は、一番多かったプリセットを読み込む (同数なら無作為)。
/// </summary>
/// <remarks>ロビーの投票とプリセットの投票は TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/LobbyVote.cs) と EndlessHostRoles (Patches/ChatCommandPatch.cs PollCommand) を元にした。</remarks>
public static class LobbyPoll
{
    public const int MaxChoices = 9;
    public const float Seconds = 30f;

    public static bool Active { get; private set; }
    public static string Question { get; private set; } = "";
    public static IReadOnlyList<string> Choices => choices;
    public static bool IsPresetVote { get; private set; }
    public static float Remaining => Active ? Mathf.Max(0f, endAt - Time.time) : 0f;

    static readonly List<string> choices = new();
    // 投票した人 → 選んだ番号 (0 から)
    static readonly Dictionary<byte, int> votes = new();
    static float endAt;
    // 投票を始めた部屋。部屋を作り直したら、前の部屋の投票は捨てる
    static int gameId;
    static bool announcedTen;

    // 抜けた人の票は数えない
    public static int CountOf(int choice) => votes.Count(kv => kv.Value == choice && Utils.GetPlayerById(kv.Key) != null);
    public static int? VoteOf(byte playerId) => votes.TryGetValue(playerId, out var v) ? v : null;

    /// <summary>/poll 質問 A B [C…]: 投票を始める。投票中に引数なしで打つと途中の結果を出す</summary>
    public static void PollCommand(ChatCommandContext c)
    {
        var words = c.Args.Skip(1).Where(a => a.Length > 0).ToArray();
        if (words.Length == 0 && Active)
        {
            Utils.SendMessageAutoSplit(Tally(final: false), true, GetString("LobbyPoll.Title"));
            return;
        }
        if (words.Length < 3)
        {
            Reply(c.Player, GetString("LobbyPoll.Usage"));
            return;
        }
        Start(words[0], words.Skip(1).Take(MaxChoices).ToArray(), preset: false, c.Player);
    }

    /// <summary>保存したプリセットの投票を始める</summary>
    public static void StartPresetVote() => StartPresetVote(null);

    public static void StartPresetVote(PlayerControl by)
    {
        var names = PresetStore.Names();
        if (names.Count < 2)
        {
            Reply(by, GetString("LobbyPoll.NoPresets"));
            return;
        }
        Start(GetString("LobbyPoll.PresetQuestion"), names.Take(MaxChoices).ToArray(), preset: true, by);
    }

    public static void Start(string question, string[] options, bool preset, PlayerControl by = null)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsLobby)
        {
            Reply(by, GetString("LobbyPoll.LobbyOnly"));
            return;
        }
        if (Active)
        {
            Reply(by, GetString("LobbyPoll.AlreadyRunning"));
            return;
        }
        Active = true;
        IsPresetVote = preset;
        Question = question;
        choices.Clear();
        choices.AddRange(options);
        votes.Clear();
        endAt = Time.time + Seconds;
        gameId = AmongUsClient.Instance.GameId;
        announcedTen = false;
        var sb = new StringBuilder();
        sb.Append(question).Append('\n');
        for (var i = 0; i < choices.Count; i++) sb.Append($"{i + 1}: {choices[i]}\n");
        sb.Append(string.Format(GetString("LobbyPoll.HowTo"), (int)Seconds));
        Utils.SendMessageAutoSplit(sb.ToString(), true, GetString("LobbyPoll.Title"));
        Logger.Info($"投票を始めた: {question} [{string.Join(", ", choices)}]", nameof(LobbyPoll));
    }

    /// <summary>数字だけの発言を票にする。票にしたら true</summary>
    public static bool TryVote(PlayerControl player, string text)
    {
        if (!Active || player == null || AmongUsClient.Instance.GameId != gameId) return false;
        var t = text.Trim();
        // 全角の数字も受ける
        if (t.Length == 1 && t[0] >= '１' && t[0] <= '９') t = ((char)(t[0] - '１' + '1')).ToString();
        if (!int.TryParse(t, out var n) || n < 1 || n > choices.Count) return false;
        votes[player.PlayerId] = n - 1;
        return true;
    }

    /// <summary>ホストの操作盤から票を入れる</summary>
    public static void VoteAsHost(int choice)
    {
        if (!Active || choice < 0 || choice >= choices.Count) return;
        votes[PlayerControl.LocalPlayer.PlayerId] = choice;
    }

    public static void Finish()
    {
        if (!Active) return;
        Active = false;
        Utils.SendMessageAutoSplit(Tally(final: true), true, GetString("LobbyPoll.Title"));
        if (!IsPresetVote || votes.Count == 0) return;
        var best = Enumerable.Range(0, choices.Count).Select(i => (choice: i, count: CountOf(i))).ToList();
        var max = best.Max(b => b.count);
        if (max == 0) return;
        var top = best.Where(b => b.count == max).Select(b => b.choice).ToList();
        var pick = top[IRandom.Instance.Next(top.Count)];
        var result = PresetStore.Load(choices[pick]);
        Utils.SendMessageAutoSplit(result, true, GetString("LobbyPoll.Title"));
    }

    static void Cancel()
    {
        Active = false;
        votes.Clear();
    }

    static string Tally(bool final)
    {
        var sb = new StringBuilder();
        sb.Append(Question).Append('\n');
        var max = choices.Count == 0 ? 0 : Enumerable.Range(0, choices.Count).Max(CountOf);
        for (var i = 0; i < choices.Count; i++)
        {
            var count = CountOf(i);
            var line = $"{i + 1}: {choices[i]}  {string.Format(GetString("LobbyPoll.Votes"), count)}";
            sb.Append(final && count == max && max > 0 ? $"<color=#ffd84d>{line}</color>" : line).Append('\n');
        }
        sb.Append(GetString(final ? "LobbyPoll.Closed" : "LobbyPoll.Running"));
        return sb.ToString();
    }

    static void Reply(PlayerControl by, string text)
    {
        if (by == null || by.AmOwner) HudManager.Instance?.Chat?.AddChat(PlayerControl.LocalPlayer, text);
        else Utils.SendMessageAutoSplit(text, true, sendTo: by.PlayerId);
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]
    static class LobbyUpdatePatch
    {
        public static void Postfix()
        {
            if (!Active || !AmongUsClient.Instance.AmHost) return;
            if (AmongUsClient.Instance.GameId != gameId)
            {
                Cancel();
                return;
            }
            var left = endAt - Time.time;
            if (!announcedTen && left <= 10f)
            {
                announcedTen = true;
                Utils.SendMessageAutoSplit(string.Format(GetString("LobbyPoll.TimeLeft"), 10), true, GetString("LobbyPoll.Title"));
            }
            if (left <= 0f) Finish();
        }
    }

    // 試合が始まったら途中の投票は捨てる
    [GameModuleInitializer]
    public static void OnGameStart() => Cancel();
}
