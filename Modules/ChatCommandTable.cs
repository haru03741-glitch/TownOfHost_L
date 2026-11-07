using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

using TownOfHostL.Roles.Core;

using static TownOfHostL.Translator;

namespace TownOfHostL;

/// <summary>コマンドを打った人と引数</summary>
public sealed class ChatCommandContext
{
    public readonly string[] Args;
    public readonly PlayerControl Player;
    /// <summary>ホストのチャット欄。他の人の /cmd では null</summary>
    public readonly ChatController Chat;
    /// <summary>取り消した後にチャット欄へ戻す文字</summary>
    public string CancelText = "";

    public ChatCommandContext(string[] args, PlayerControl player, ChatController chat)
    {
        Args = args;
        Player = player;
        Chat = chat;
    }
    public string Arg(int index) => index < Args.Length ? Args[index] : "";
}

/// <summary>
/// 1 つのコマンド。動く場面ごとに処理を持つ。
/// Local は打った本人の画面だけで動く (ホストでなくても使える)。Host はホストが打った時、Remote は他の人が /cmd を付けて送った時にホストの側で動く。
/// </summary>
public sealed class ChatCommand
{
    public readonly string[] Names;
    public Action<ChatCommandContext> Local;
    public Action<ChatCommandContext> Host;
    public Action<ChatCommandContext> Remote;
    public ChatCommand(params string[] names) => Names = names;
}

/// <summary>チャットコマンドの登録表。名前は大文字小文字を区別しない。</summary>
public static class ChatCommandTable
{
    private static readonly ChatCommand[] All = Build();
    private static readonly Dictionary<string, ChatCommand> byName = BuildMap();

    public static ChatCommand Find(string name) => name != null && byName.TryGetValue(name, out var cmd) ? cmd : null;

    private static Dictionary<string, ChatCommand> BuildMap()
    {
        var map = new Dictionary<string, ChatCommand>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in All)
            foreach (var name in cmd.Names)
                if (!map.TryAdd(name, cmd)) Logger.Error($"コマンド {name} が二重に登録されています", "ChatCommandTable");
        return map;
    }

    private static ChatCommand[] Build() => new ChatCommand[]
    {
        new("/dump") { Local = _ => Utils.DumpLog() },
        new("/v", "/version") { Local = _ => ShowVersions() },
#if DEBUG
        new("/sr", "/selectroles") { Host = c => ChatCommands.SelectRolesCommand(c.Args) },
#endif
        new("/win", "/winner")
        {
            Host = _ => Utils.SendMessageAutoSplit("Winner: " + string.Join(",", Main.winnerList.Select(b => Main.AllPlayerNames[b])), true),
        },
        new("/l", "/lastresult")
        {
            Host = _ => Utils.ShowLastResult(),
            Remote = c => Utils.ShowLastResult(c.Player.PlayerId),
        },
        new("/kl", "/killlog")
        {
            Host = _ => Utils.ShowKillLog(),
            Remote = c => Utils.ShowKillLog(c.Player.PlayerId),
        },
        new("/r", "/rename") { Host = c => Main.nickName = c.Arg(1) },
        new("/hn", "/hidename")
        {
            Host = c =>
            {
                Main.HideName.Value = c.Args.Length > 1 ? c.Args.Skip(1).Join(delimiter: " ") : Main.HideName.DefaultValue.ToString();
                GameStartManagerPatch.HideName.text = Main.HideName.Value;
            },
        },
        new("/n", "/now")
        {
            Host = c => ShowNow(c.Arg(1), byte.MaxValue),
            Remote = c => ShowNow(c.Arg(1), c.Player.PlayerId),
        },
        new("/mode") { Host = c => Core.VanillaHideAndSeek.Command(c.Args) },
        new("/w") { Host = ForceWin },
        new("/dis") { Host = ForceDisconnectEnd },
        new("/h", "/help")
        {
            Host = c => ShowHelp(c, byte.MaxValue, showGeneralHelp: true),
            Remote = c => ShowHelp(c, c.Player.PlayerId, showGeneralHelp: false),
        },
        new("/m", "/myrole") { Host = ShowMyRole, Remote = ShowMyRole },
        new("/t", "/template")
        {
            Host = c =>
            {
                if (c.Args.Length > 1) TemplateManager.SendTemplate(c.Args[1]);
                else HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, $"{GetString("ForExample")}:\n{c.Args[0]} test");
            },
            Remote = c =>
            {
                if (c.Args.Length > 1) TemplateManager.SendTemplate(c.Args[1], c.Player.PlayerId);
                else Utils.SendMessageAutoSplit($"{GetString("ForExample")}:\n{c.Args[0]} test", true, sendTo: c.Player.PlayerId);
            },
        },
        new("/mw", "/messagewait")
        {
            Host = c =>
            {
                if (c.Args.Length > 1 && float.TryParse(c.Args[1], out float sec))
                {
                    Main.MessageWait.Value = sec;
                    Utils.SendMessageAutoSplit(string.Format(GetString("Message.SetToSeconds"), sec), true, sendTo: 0);
                }
                else Utils.SendMessageAutoSplit($"{GetString("Message.MessageWaitHelp")}\n{GetString("ForExample")}:\n{c.Args[0]} 3", true, sendTo: 0);
            },
        },
        new("/say")
        {
            Host = c =>
            {
                if (c.Args.Length > 1)
                    Utils.SendMessageAutoSplit(c.Args.Skip(1).Join(delimiter: " "), true, $"<color=#ff0000>{GetString("MessageFromTheHost")}</color>");
            },
        },
        new("/exile")
        {
            Host = c => { if (c.Args.Length > 1 && int.TryParse(c.Args[1], out int id)) Utils.GetPlayerById(id)?.RpcExileV3(); },
        },
        new("/kill")
        {
            Host = c => { if (c.Args.Length > 1 && int.TryParse(c.Args[1], out int id)) Utils.GetPlayerById(id)?.RpcMurderPlayer(Utils.GetPlayerById(id)); },
        },
        new("/vo", "/voice") { Host = HostVoice, Remote = RemoteVoice },
        new("/killFlash", "/kf")
        {
            Host = c => Utils.SetKillFlashAfterDead(c.Player, true),
            Remote = c => Utils.SetKillFlashAfterDead(c.Player),
        },
        new("/brackout", "/bo")
        {
            Host = _ => { if (GameStates.InGame) Main.AllPlayerControls.Do(pc => pc.KillFlash()); },
        },
    };

    private static void ShowVersions()
    {
        string versionText = "";
        foreach (var kvp in Main.playerVersion.OrderBy(pair => pair.Key))
            versionText += $"{kvp.Key}:{Utils.GetPlayerById(kvp.Key)?.Data?.PlayerName}:{kvp.Value.forkId}/{kvp.Value.version}({kvp.Value.tag})\n";
        if (versionText != "") HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, versionText);
    }

    private static void ShowMyRole(ChatCommandContext c)
    {
        if (AmongUsClient.Instance.IsGameStarted) Utils.ShowMyRoleInfo(c.Player, c.Player.PlayerId);
    }

    private static void ShowNow(string sub, byte sendTo)
    {
        switch (sub)
        {
            case "r":
            case "roles":
                Utils.ShowActiveRoles(sendTo);
                break;
            case "v":
            case "vanilla":
                Utils.ShowVanillaSetting(sendTo);
                break;
            default:
                Utils.ShowActiveSettings(sendTo);
                break;
        }
    }

    private static void ShowHelp(ChatCommandContext c, byte sendTo, bool showGeneralHelp)
    {
        switch (c.Arg(1))
        {
            case "r":
            case "roles":
                ChatCommands.GetRolesInfo(c.Arg(2), sendTo);
                break;
            case "n":
            case "now":
                Utils.ShowActiveSettingsHelp(sendTo);
                break;
            default:
                if (showGeneralHelp) Utils.ShowHelp();
                break;
        }
    }

    private static void ForceWin(ChatCommandContext c)
    {
        if (!GameStates.IsInGame) return;
        switch (c.Arg(1))
        {
            case "crewmate":
                GameManager.Instance.enabled = false;
                CustomWinnerHolder.WinnerTeam = CustomWinner.Crewmate;
                foreach (var player in Main.AllPlayerControls.Where(pc => pc.Is(CustomRoleTypes.Crewmate)))
                    CustomWinnerHolder.WinnerIds.Add(player.PlayerId);
                GameEndChecker.StartEndGame(GameOverReason.CrewmatesByTask);
                break;
            case "impostor":
                GameManager.Instance.enabled = false;
                CustomWinnerHolder.WinnerTeam = CustomWinner.Impostor;
                foreach (var player in Main.AllPlayerControls.Where(pc => pc.Is(CustomRoleTypes.Impostor) || pc.Is(CustomRoleTypes.Madmate)))
                    CustomWinnerHolder.WinnerIds.Add(player.PlayerId);
                GameEndChecker.StartEndGame(GameOverReason.ImpostorsByKill);
                break;
            case "none":
                GameManager.Instance.enabled = false;
                CustomWinnerHolder.WinnerTeam = CustomWinner.None;
                GameEndChecker.StartEndGame(GameOverReason.ImpostorsByKill);
                break;
            case "jackal":
                GameManager.Instance.enabled = false;
                CustomWinnerHolder.WinnerTeam = CustomWinner.Jackal;
                GameEndChecker.StartEndGame(GameOverReason.ImpostorsByKill);
                break;
            default:
                c.Chat.AddChat(PlayerControl.LocalPlayer, "crewmate | impostor | jackal | none");
                c.CancelText = "/w";
                break;
        }
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Admin, 0);
    }

    private static void ForceDisconnectEnd(ChatCommandContext c)
    {
        switch (c.Arg(1))
        {
            case "crewmate":
                GameManager.Instance.enabled = false;
                GameManager.Instance.RpcEndGame(GameOverReason.CrewmateDisconnect, false);
                break;
            case "impostor":
                GameManager.Instance.enabled = false;
                GameManager.Instance.RpcEndGame(GameOverReason.ImpostorDisconnect, false);
                break;
            default:
                c.Chat.AddChat(PlayerControl.LocalPlayer, "crewmate | impostor");
                c.CancelText = "/dis";
                break;
        }
        ShipStatus.Instance.RpcUpdateSystem(SystemTypes.Admin, 0);
    }

    private static void HostVoice(ChatCommandContext c)
    {
        if (c.Arg(1) == "reset")
            VoiceReader.ResetVoiceNo();
        else if (c.Arg(1) == "random")
            VoiceReader.SetRandomVoiceNo();
        else if (c.Args.Length > 1 && int.TryParse(c.Args[1], out int voiceNo))
        {
            var name = VoiceReader.SetHostVoiceNo(voiceNo);
            if (!string.IsNullOrEmpty(name))
                Utils.SendMessageAutoSplit(string.Format(GetString("Message.VoiceChangeHost"), name), true, sendTo: 0);
        }
        else
            Utils.SendMessageAutoSplit(VoiceReader.GetVoiceIdxMsg(), true, sendTo: 0);
    }

    private static void RemoteVoice(ChatCommandContext c)
    {
        var to = c.Player.PlayerId;
        var color = Palette.GetColorName(c.Player.Data.DefaultOutfit.ColorId);
        if (VoiceReader.VoiceReaderMode == null || !VoiceReader.VoiceReaderMode.GetBool())
            Utils.SendMessageAutoSplit(string.Format(GetString("Message.VoiceNotAvailable")), true, sendTo: to);
        else if (c.Arg(1) == "n")
            Utils.SendMessageAutoSplit(string.Format(GetString("Message.VoiceNow"), color, VoiceReader.GetVoiceName(color)), true, sendTo: to);
        else if (c.Args.Length > 1 && int.TryParse(c.Args[1], out int voiceNo))
        {
            var name = VoiceReader.SetVoiceNo(color, voiceNo);
            if (!string.IsNullOrEmpty(name))
                Utils.SendMessageAutoSplit(string.Format(GetString("Message.VoiceChange"), color, name), true, sendTo: to);
            else
                Utils.SendMessageAutoSplit(string.Format(GetString("Message.VoiceChangeFailed"), color), true, sendTo: to);
        }
        else
            Utils.SendMessageAutoSplit(VoiceReader.GetVoiceIdxMsg(), true, sendTo: to);
    }
}
