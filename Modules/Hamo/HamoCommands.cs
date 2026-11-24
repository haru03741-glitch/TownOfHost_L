using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Translator;

namespace TownOfHostL;

public static class HamoCommands
{
    static readonly Regex DicePattern = new(@"^/(\d+)d(\d+)$", RegexOptions.IgnoreCase);

    public static bool IsDice(string name) => name != null && DicePattern.IsMatch(name);

    public static ChatCommand[] Build() => new ChatCommand[]
    {
        new("/rule", "/rl") { Host = c => ShowRule(byte.MaxValue), Remote = c => ShowRule(c.Player.PlayerId), Restrictable = true },
        new("/s", "/set") { ForModerator = true, Usage = "CommandUsage.SetRule", Host = SetRule },
        new("/d", "/delete") { ForModerator = true, Host = DeleteRule },
        new("/nc") { Usage = "CommandUsage.NameColor", Host = c => NameColor(c) },
        new("/ns") { Usage = "CommandUsage.NameSize", Host = c => NameSize(c) },
        new("/8ball") { Restrictable = true, Host = EightBall, Remote = EightBall },
        new("/dice", "/roll") { Restrictable = true, Usage = "CommandUsage.Dice", Host = Dice, Remote = Dice },
        new("/kc") { Usage = "CommandUsage.KillCooldown", Host = LobbyKillCooldown },
        new("/apt", "/allplayertp") { Host = _ => AllPlayerTp() },
        new("/timer", "/tr") { Restrictable = true, Host = _ => ShowTimer(byte.MaxValue), Remote = c => ShowTimer(c.Player.PlayerId) },
        new("/sw") { ForModerator = true, Usage = "CommandUsage.SetWinner", Host = SetWinner },
        new("/fm", "/callmeeting", "/cm") { ForModerator = true, Host = ForceMeeting },
        new("/rev", "/revive") { Usage = "CommandUsage.Revive", Host = ReviveCommand },
    };

    static string RuleFilePath => Path.Combine(Environment.CurrentDirectory, "TOHL_Rule.txt");
    static string ruleText;

    static string RuleText
    {
        get
        {
            if (ruleText != null) return ruleText;
            try { ruleText = File.Exists(RuleFilePath) ? File.ReadAllText(RuleFilePath) : ""; }
            catch (Exception ex) { Logger.Exception(ex, "HamoCommands.LoadRule"); ruleText = ""; }
            return ruleText;
        }
        set
        {
            ruleText = value;
            try { File.WriteAllText(RuleFilePath, value); }
            catch (Exception ex) { Logger.Exception(ex, "HamoCommands.SaveRule"); }
        }
    }

    static bool IsRuleSub(ChatCommandContext c) => c.Arg(1).ToLowerInvariant() is "r" or "rule";

    static void ShowRule(byte to)
    {
        if (RuleText == "") Utils.SendMessage(GetString("Hamo.RuleNotSet"), to == byte.MaxValue ? PlayerControl.LocalPlayer.PlayerId : to);
        else Utils.SendMessage($"<size=90%><color=#ff0000>📋 {GetString("Hamo.Rule")}</color>\n{RuleText}</size>", to);
    }

    static void SetRule(ChatCommandContext c)
    {
        if (!IsRuleSub(c)) return;
        var text = string.Join(" ", c.Args.Skip(2));
        var existed = RuleText != "";
        RuleText = text;
        Utils.SendMessage($"<size=90%><color=#ff0000>📋 {GetString(existed ? "Hamo.RuleChanged" : "Hamo.RuleSet")}</color>\n{RuleText}</size>");
    }

    static void DeleteRule(ChatCommandContext c)
    {
        if (!IsRuleSub(c)) return;
        if (RuleText == "")
        {
            Utils.SendMessage(GetString("Hamo.RuleNotSet"), c.Player.PlayerId);
            return;
        }
        RuleText = "";
        Utils.SendMessage($"<color=#ff0000>📋 {GetString("Hamo.RuleDeleted")}</color>");
    }

    static string ColorHex(string col) => col.ToLowerInvariant() switch
    {
        "レッド" or "赤" or "red" => "#ff0000",
        "ブルー" or "青" or "blue" => "#0000ff",
        "グリーン" or "緑" or "green" => "#00ff00",
        "ピンク" or "pink" => "#ff69b4",
        "オレンジ" or "orange" => "#ffa500",
        "イエロー" or "黄" or "yellow" => "#ffff00",
        "パープル" or "紫" or "purple" => "#800080",
        "ブラック" or "黒" or "black" => "#000000",
        "ホワイト" or "白" or "white" => "#ffffff",
        "シアン" or "cyan" => "#00ffff",
        "ライム" or "lime" => "#00ff80",
        "グレー" or "gray" => "#808080",
        "ブラウン" or "brown" => "#8b4513",
        "ローズ" or "rose" => "#ff007f",
        "バナナ" or "banana" => "#ffe135",
        "コーラル" or "coral" => "#ff7f50",
        "タン" or "tan" => "#d2b48c",
        _ => null,
    };

    static string RawName => PlayerControl.LocalPlayer.Data.PlayerName.RemoveHtmlTags();

    static void NameColor(ChatCommandContext c)
    {
        var hex = ColorHex(c.Arg(1));
        if (hex == null) return;
        PlayerControl.LocalPlayer.RpcSetNameEx($"<color={hex}>{RawName}</color>");
    }

    static void NameSize(ChatCommandContext c)
    {
        if (!float.TryParse(c.Arg(1), out var size)) return;
        PlayerControl.LocalPlayer.RpcSetNameEx($"<size={size}%>{RawName}</size>");
    }

    static readonly string[] EightBallAnswers =
    {
        "確実にそうです！", "そうでしょう！", "おそらくそうです。", "YES！", "そう思います。", "もちろんはい！", "いいえに決まってんだろー!!",
        "そうかもしれません。", "わかりません。", "自分で考えろよカス", "はいはいそうだね～", "今は教えられません。", "期待しない方がいいでしょう。",
        "違うと思います。", "おそらく違います。", "絶対に違います！",
    };

    static void EightBall(ChatCommandContext c)
    {
        if (c.Args.Length < 2) return;
        var question = string.Join(" ", c.Args.Skip(1));
        var answer = EightBallAnswers[IRandom.Instance.Next(EightBallAnswers.Length)];
        Utils.SendMessage($"8ball {c.Player.Data.PlayerName}「{question}」\n→ {answer}");
    }

    static void Dice(ChatCommandContext c)
    {
        var spec = IsDice(c.Arg(0)) ? c.Arg(0) : "/" + c.Arg(1);
        var m = DicePattern.Match(spec);
        int min = 1, max = 6;
        if (m.Success)
        {
            min = int.Parse(m.Groups[1].Value);
            max = int.Parse(m.Groups[2].Value);
        }
        if (max < min) (min, max) = (max, min);
        var result = IRandom.Instance.Next(min, max + 1);
        var colorName = Palette.GetColorName(c.Player.Data.DefaultOutfit.ColorId);
        Utils.SendMessage(string.Format(GetString("Hamo.DiceResult"), c.Player.Data.PlayerName, colorName, min, max, result));
    }

    static void LobbyKillCooldown(ChatCommandContext c)
    {
        if (!GameStates.IsLobby) return;
        if (float.TryParse(c.Arg(1), out var sec))
        {
            if (sec <= 0) sec = 0.00000000000000001f;
            Main.NormalOptions.SetFloat(FloatOptionNames.KillCooldown, sec);
        }
        var opt = Main.NormalOptions.Cast<IGameOptions>();
        PlayerControl.LocalPlayer.RpcSyncSettings(GameOptionsManager.Instance.gameOptionsFactory.ToBytes(opt, AprilFoolsMode.IsAprilFoolsModeToggledOn));
    }

    static void AllPlayerTp()
    {
        if (!GameStates.IsLobby) return;
        foreach (var pc in Main.AllPlayerControls)
            pc.RpcSnapTo(Vector2.zero, true);
    }

    static void ShowTimer(byte to)
    {
        if (GameStates.IsInGame) return;
        var t = GameStartManagerPatch.LobbyTimer;
        Utils.SendMessage($"{(int)t / 60:00}:{(int)t % 60:00}", to);
    }

    static void SetWinner(ChatCommandContext c)
    {
        if (!GameStates.IsInGame || !Main.IsSetRoleFinished)
        {
            Utils.SendMessage(GetString("Hamo.InGameOnly"), c.Player.PlayerId);
            return;
        }
        CustomWinner? winner = c.Arg(1).ToLowerInvariant() switch
        {
            "crewmate" or "crew" or "クルー" or "クルーメイト" => CustomWinner.Crewmate,
            "impostor" or "imp" or "インポ" or "インポスター" or "インポス" => CustomWinner.Impostor,
            "draw" or "引き分け" => CustomWinner.Draw,
            "none" or "なし" => CustomWinner.None,
            _ => null,
        };
        if (winner == null)
        {
            Utils.SendMessage(GetString("CommandUsage.SetWinner"), c.Player.PlayerId);
            return;
        }
        CustomWinnerHolder.ResetAndSetWinner(winner.Value);
        GameManager.Instance.LogicFlow.CheckEndCriteria();
    }

    static void ForceMeeting(ChatCommandContext c)
    {
        if (!GameStates.IsInGame || GameStates.IsMeeting || !Main.IsSetRoleFinished)
        {
            Utils.SendMessage(GetString("Hamo.CannotNow"), c.Player.PlayerId);
            return;
        }
        var reporter = c.Player != null && c.Player.IsAlive() ? c.Player : Main.AllAlivePlayerControls.FirstOrDefault();
        reporter?.ReportDeadBodyForced(null);
    }

    static void ReviveCommand(ChatCommandContext c)
    {
        if (!GameStates.IsInGame) return;
        var target = c.Player;
        if (byte.TryParse(c.Arg(1), out var id)) target = Utils.GetPlayerById(id) ?? target;
        if (target == null || target.IsAlive()) return;
        Modules.Revive.Request(target.PlayerId);
    }
}
