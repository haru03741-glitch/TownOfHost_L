using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using TownOfHostL.Core.Modes;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// /cs 語… 値: Mod の設定を名前で探して値を変える (ロビーだけ)。
/// 名前は設定画面の検索と同じそろえ方 (全角・カタカナ・大文字を区別しない) で、親の設定の名前も含めて探す (「シェリフ キル 20」)。
/// 当たりが複数なら、名前が語とちょうど同じ物を選び、それも無ければ候補を出して語を足してもらう。
/// </summary>
/// <remarks>設定を名前で変えるコマンドは EndlessHostRoles (Patches/ChatCommandPatch.cs ChangeSettingCommand) と TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/ModeratorSettingCommand.cs) を元にした。</remarks>
public static class SettingCommand
{
    const int MaxCandidates = 8;
    static readonly Regex Tags = new("<[^>]*>", RegexOptions.Compiled);

    public static void Command(ChatCommandContext c)
    {
        var by = c.Player;
        void Reply(string text)
        {
            if (by == null || by.AmOwner) HostNote.Post(text);
            else Utils.SendMessageAutoSplit(text, true, sendTo: by.PlayerId);
        }

        var words = c.Args.Skip(1).Where(a => a.Length > 0).ToArray();
        if (words.Length < 2)
        {
            Reply(GetString("ChangeSetting.Usage"));
            return;
        }
        if (!GameStates.IsLobby)
        {
            Reply(GetString("ChangeSetting.LobbyOnly"));
            return;
        }
        var value = words[^1];
        var tokens = SettingsSearch.Tokens(string.Join(" ", words[..^1]));
        if (tokens.Length == 0)
        {
            Reply(GetString("ChangeSetting.Usage"));
            return;
        }

        var hits = new List<OptionItem>();
        var exact = new List<OptionItem>();
        // 名前ちょうど (または「親 子」の名前をつないだ物ちょうど) の指定は、カナをそろえた形で比べる
        var joined = SettingsSearch.Normalize(string.Concat(words[..^1]));
        foreach (var opt in OptionItem.AllOptions)
        {
            if (!Settable(opt)) continue;
            // 客のコマンドの制限はホストだけが変える
            if (by != null && !by.AmOwner && opt.Key.StartsWith("RestrictGuestCommands")) continue;
            var own = SettingsSearch.Normalize(PlainName(opt));
            var path = Path(opt);
            // 親の名前も探す対象に入れる (役職の名前 + 設定の名前で指せるように)
            var hay = SettingsSearch.Haystack(path);
            if (!SettingsSearch.AllIn(tokens, hay)) continue;
            hits.Add(opt);
            if (own == joined || SettingsSearch.Normalize(path.Replace(" > ", "")) == joined) exact.Add(opt);
        }
        if (exact.Count == 1) hits = exact;

        // 本編 (バニラ) の設定 (キルクール・タスク数など)。MOD の設定に当たりが無い時か、名前がちょうど同じ時に使う
        var vanillaHits = Vanilla.Where(v => SettingsSearch.AllIn(tokens, SettingsSearch.Haystack(v.Ja + " " + v.En))).ToList();
        var vanillaExact = vanillaHits.Where(v => SettingsSearch.Normalize(v.Ja) == joined || SettingsSearch.Normalize(v.En.Replace(" ", "")) == joined).ToList();
        if (vanillaExact.Count == 1 && exact.Count == 0 || hits.Count == 0 && vanillaHits.Count == 1)
        {
            var v = vanillaExact.Count == 1 ? vanillaExact[0] : vanillaHits[0];
            if (Main.NormalOptions == null || !v.Set(SettingsSearch.Normalize(value)))
            {
                Reply(string.Format(GetString("ChangeSetting.BadValue"), v.Ja, v.Choices));
                return;
            }
            SyncVanilla();
            var whoV = by == null || by.AmOwner ? "" : $" ({by.Data?.PlayerName})";
            Utils.SendMessageAutoSplit(string.Format(GetString("ChangeSetting.Done"), v.Ja, v.Get()) + whoV, true);
            Logger.Info($"Vanilla {v.En} = {v.Get()}{whoV}", "ChangeSetting");
            return;
        }
        if (hits.Count == 0 && vanillaHits.Count > 1)
        {
            Reply(string.Format(GetString("ChangeSetting.Many"), vanillaHits.Count) + "\n" + string.Join("\n", vanillaHits.Select(v => $"・{v.Ja} = {v.Get()}")));
            return;
        }
        if (hits.Count == 0)
        {
            Reply(GetString("ChangeSetting.NotFound"));
            return;
        }
        if (hits.Count > 1)
        {
            var list = hits.Take(MaxCandidates).Select(o => $"・{Path(o)} = {Plain(o.GetString())}");
            Reply(string.Format(GetString("ChangeSetting.Many"), hits.Count) + "\n" + string.Join("\n", list)
                + (hits.Count > MaxCandidates ? "\n…" : ""));
            return;
        }

        var option = hits[0];
        if (!TryParseValue(option, value, out var index))
        {
            Reply(string.Format(GetString("ChangeSetting.BadValue"), Path(option), Choices(option)));
            return;
        }
        option.SetValue(index);
        var who = by == null || by.AmOwner ? "" : $" ({by.Data?.PlayerName})";
        // 変えたことは全員に知らせる
        Utils.SendMessageAutoSplit(string.Format(GetString("ChangeSetting.Done"), Path(option), Plain(option.GetString())) + who, true);
        Logger.Info($"{option.Key} = {option.GetString()}{who}", "ChangeSetting");
    }

    // ---- 本編 (バニラ) の設定 ----

    sealed class VanillaSetting
    {
        public string Ja, En, Choices;
        public Func<string> Get;
        public Func<string, bool> Set;
    }

    static float? Num(string raw)
    {
        var t = raw.TrimEnd('%').TrimEnd('s', '秒', '人', 'x', '倍', '回', '個');
        return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
    }
    static bool? OnOff(string raw) => raw switch
    {
        "on" or "1" or "true" or "おん" or "有効" or "あり" or "オン" => true,
        "off" or "0" or "false" or "おふ" or "無効" or "なし" or "オフ" => false,
        _ => null,
    };

    static VanillaSetting FloatSetting(string ja, string en, float min, float max, float step, Func<float> get, Action<float> set, string unit = "")
        => new()
        {
            Ja = ja, En = en, Choices = $"{min}〜{max}{unit}",
            Get = () => $"{get():0.##}{unit}",
            Set = raw =>
            {
                if (Num(raw) is not float v || v < min || v > max) return false;
                set(Mathf.Round(v / step) * step);
                return true;
            },
        };
    static VanillaSetting IntSetting(string ja, string en, int min, int max, Func<int> get, Action<int> set, string unit = "")
        => FloatSetting(ja, en, min, max, 1f, () => get(), v => set(Mathf.RoundToInt(v)), unit);
    static VanillaSetting BoolSetting(string ja, string en, Func<bool> get, Action<bool> set)
        => new()
        {
            Ja = ja, En = en, Choices = "on / off",
            Get = () => get() ? "ON" : "OFF",
            Set = raw => { if (OnOff(raw) is not bool b) return false; set(b); return true; },
        };

    static readonly VanillaSetting[] Vanilla =
    {
        FloatSetting("キルのクールダウン時間", "Kill Cooldown", 2.5f, 60f, 2.5f, () => Main.NormalOptions.KillCooldown, v => Main.NormalOptions.KillCooldown = v, "秒"),
        IntSetting("インポスター数", "Impostors", 1, 5, () => Main.NormalOptions.NumImpostors, v => Main.NormalOptions.NumImpostors = v, "人"),
        IntSetting("通常タスク", "Common Tasks", 0, 4, () => Main.NormalOptions.NumCommonTasks, v => Main.NormalOptions.NumCommonTasks = v),
        IntSetting("ロングタスク", "Long Tasks", 0, 15, () => Main.NormalOptions.NumLongTasks, v => Main.NormalOptions.NumLongTasks = v),
        IntSetting("ショートタスク", "Short Tasks", 0, 23, () => Main.NormalOptions.NumShortTasks, v => Main.NormalOptions.NumShortTasks = v),
        IntSetting("緊急会議", "Emergency Meetings", 0, 9, () => Main.NormalOptions.NumEmergencyMeetings, v => Main.NormalOptions.NumEmergencyMeetings = v, "回"),
        IntSetting("緊急ボタンクールダウン", "Emergency Cooldown", 0, 60, () => Main.NormalOptions.EmergencyCooldown, v => Main.NormalOptions.EmergencyCooldown = v, "秒"),
        IntSetting("議論タイム", "Discussion Time", 0, 120, () => Main.NormalOptions.DiscussionTime, v => Main.NormalOptions.DiscussionTime = v, "秒"),
        IntSetting("投票時間", "Voting Time", 0, 300, () => Main.NormalOptions.VotingTime, v => Main.NormalOptions.VotingTime = v, "秒"),
        FloatSetting("プレイヤー速度", "Player Speed", 0.5f, 3f, 0.25f, () => Main.NormalOptions.PlayerSpeedMod, v => Main.NormalOptions.PlayerSpeedMod = v, "x"),
        FloatSetting("クルービジョン", "Crewmate Vision", 0.25f, 5f, 0.25f, () => Main.NormalOptions.CrewLightMod, v => Main.NormalOptions.CrewLightMod = v, "x"),
        FloatSetting("インポスタービジョン", "Impostor Vision", 0.25f, 5f, 0.25f, () => Main.NormalOptions.ImpostorLightMod, v => Main.NormalOptions.ImpostorLightMod = v, "x"),
        IntSetting("キル可能距離", "Kill Distance", 0, 2, () => Main.NormalOptions.KillDistance, v => Main.NormalOptions.KillDistance = v),
        BoolSetting("追放数を確認", "Confirm Ejects", () => Main.NormalOptions.ConfirmImpostor, v => Main.NormalOptions.ConfirmImpostor = v),
        BoolSetting("匿名投票", "Anonymous Votes", () => Main.NormalOptions.AnonymousVotes, v => Main.NormalOptions.AnonymousVotes = v),
        BoolSetting("視認タスク", "Visual Tasks", () => Main.NormalOptions.VisualTasks, v => Main.NormalOptions.VisualTasks = v),
    };

    /// <summary>変えた本編の設定を全員へ送る</summary>
    static void SyncVanilla()
    {
        if (PlayerControl.LocalPlayer == null || GameOptionsManager.Instance == null || Main.NormalOptions == null) return;
        var opt = Main.NormalOptions.Cast<AmongUs.GameOptions.IGameOptions>();
        PlayerControl.LocalPlayer.RpcSyncSettings(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(opt, AprilFoolsMode.IsAprilFoolsModeToggledOn));
    }

    static bool Settable(OptionItem opt)
        => opt is not TextOptionItem and not PresetOptionItem && !opt.IsFixValue && opt.IsShown()
           && (opt.Parent == null || opt.Parent.IsShown());

    static string Plain(string s) => Tags.Replace(s ?? "", "").Trim();
    static string PlainName(OptionItem opt) => Plain(opt.GetName(disableColor: true));

    // 「親 > 子」の名前 (見出しは含めない)
    static string Path(OptionItem opt)
    {
        var names = new List<string>();
        for (var o = opt; o != null; o = o.Parent) names.Add(PlainName(o));
        names.Reverse();
        return string.Join(" > ", names);
    }

    static bool TryParseValue(OptionItem opt, string text, out int index)
    {
        index = 0;
        var raw = SettingsSearch.Normalize(text).TrimEnd('%');
        // 数の後ろの単位は外して読む (20s・20秒・3人)
        var t = raw.TrimEnd('s', '秒', '人', 'x', '倍');
        switch (opt)
        {
            case FloatOptionItem f when float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv):
                if (fv < f.Rule.MinValue || fv > f.Rule.MaxValue) return false;
                index = f.Rule.GetNearestIndex(fv);
                return true;
            case IntegerOptionItem i when int.TryParse(t, out var iv):
                if (iv < i.Rule.MinValue || iv > i.Rule.MaxValue) return false;
                index = i.Rule.GetNearestIndex(iv);
                return true;
            case StringOptionItem s:
                for (var k = 0; k < s.Selections.Length; k++)
                {
                    var sel = SettingsSearch.Normalize(Plain(GetString(s.Selections[k]))).TrimEnd('%');
                    if (sel == raw) { index = k; return true; }
                }
                if (s.Selections.Length == 2 && s.Selections[0] == "ColoredOff")
                {
                    if (raw is "on" or "1" or "true" or "おん" or "有効" or "あり") { index = 1; return true; }
                    if (raw is "off" or "0" or "false" or "おふ" or "無効" or "なし") { index = 0; return true; }
                }
                return false;
            case BooleanOptionItem:
                if (raw is "on" or "1" or "true" or "おん" or "有効") { index = 1; return true; }
                if (raw is "off" or "0" or "false" or "おふ" or "無効") { index = 0; return true; }
                return false;
        }
        return false;
    }

    static string Choices(OptionItem opt) => opt switch
    {
        FloatOptionItem f => $"{f.Rule.MinValue}〜{f.Rule.MaxValue}",
        IntegerOptionItem i => $"{i.Rule.MinValue}〜{i.Rule.MaxValue}",
        StringOptionItem s => string.Join(" / ", s.Selections.Take(12).Select(x => Plain(GetString(x)))),
        BooleanOptionItem => "on / off",
        _ => "",
    };
}
