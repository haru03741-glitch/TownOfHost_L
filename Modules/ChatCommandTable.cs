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
/// Local は打った本人の画面だけで動く (ホストでなくても使える)。Host はホストが打った時、Remote は他の人が送った時にホストの側で動く
/// (先頭の /cmd は有っても無くてもよい)。
/// </summary>
public sealed class ChatCommand
{
    public readonly string[] Names;
    public Action<ChatCommandContext> Local;
    public Action<ChatCommandContext> Host;
    public Action<ChatCommandContext> Remote;
    /// <summary>
    /// 打った文を他の人に見せたくないコマンド (耳打ちなど)。/cmd を付けずに送られた時は、本編がもう全員に配っているので、
    /// 大きな空白でチャット欄から押し出す。/cmd 付きは本編のサーバーがホストにしか届けないので押し出さない
    /// </summary>
    public bool Hidden;
    /// <summary>引数の書き方 (翻訳のキー)。チャット欄で名前の後ろに薄く出す</summary>
    public string Usage;
    public ChatCommand(params string[] names) => Names = names;
    public string UsageText => Usage == null ? "" : GetString(Usage);
}

/// <summary>チャットコマンドの登録表。名前は大文字小文字を区別しない。</summary>
public static class ChatCommandTable
{
    private static readonly ChatCommand[] All = Build();
    private static readonly Dictionary<string, ChatCommand> byName = BuildMap();

    public static ChatCommand Find(string name) => name != null && byName.TryGetValue(name, out var cmd) ? cmd : null;

    /// <summary>
    /// 打ちかけの名前 (/ から空白の前まで) に合う、今の自分が使えるコマンド。同じ名前があればそれ、無ければ表の順で前方が一致する最初の名前。
    /// ホストは Host と Local、それ以外は Remote と Local を持つコマンドだけを候補にする
    /// </summary>
    public static bool Suggest(string typed, out string name, out string usage)
    {
        name = null; usage = "";
        if (string.IsNullOrEmpty(typed) || typed.Length < 2) return false;
        bool host = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;
        bool Usable(ChatCommand c) => c.Local != null || (host ? c.Host != null : c.Remote != null);
        var exact = Find(typed);
        if (exact != null && Usable(exact))
        {
            name = typed; usage = exact.UsageText;
            return true;
        }
        foreach (var c in All)
        {
            if (!Usable(c)) continue;
            foreach (var n in c.Names)
                if (n.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) { name = n; usage = c.UsageText; return true; }
        }
        return false;
    }

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
        new("/dump") { Local = Modules.DevCommands.Dump },
        new("/log") { Local = Modules.DevCommands.LogCommand },
        new("/dev") { Local = Modules.DevCommands.DevCommand },
        new("/v", "/version") { Local = _ => ShowVersions() },
        new("/sr", "/setrole") { Usage = "CommandUsage.SelectRoles", Host = c => ChatCommands.SelectRolesCommand(c.Args) },
        new("/id", "/ids")
        {
            Host = _ => HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, PlayerIdList()),
            Remote = c => Utils.SendMessageAutoSplit(PlayerIdList(), true, sendTo: c.Player.PlayerId),
        },
        new("/start") { Usage = "CommandUsage.Start", Host = StartCommand },
        new("/end", "/abort") { Host = _ => EndAsDraw() },
        new("/mt", "/endmeeting") { Host = _ => { if (GameStates.IsMeeting) MeetingHud.Instance.RpcClose(); } },
        new("/kick") { Usage = "CommandUsage.Kick", Host = c => ChatCommands.KickCommand(c.Args, ban: false) },
        new("/ban") { Usage = "CommandUsage.Ban", Host = c => ChatCommands.KickCommand(c.Args, ban: true) },
        // 前の試合に居た人の再入室: 見つけた人を一括キック / このロビーだけ許す
        new("/kp", "/kickprevious") { Host = _ => Modules.JoinGuard.KickDetected() },
        new("/aj", "/allowjoin") { Host = _ => Modules.JoinGuard.AllowThisLobby() },
        new("/offhat") { Usage = "CommandUsage.OffSkin", Host = c => SkinControle.ClearCommand(c, true, false, false, false) },
        new("/offskin") { Usage = "CommandUsage.OffSkin", Host = c => SkinControle.ClearCommand(c, false, true, false, false) },
        new("/offvisor") { Usage = "CommandUsage.OffSkin", Host = c => SkinControle.ClearCommand(c, false, false, true, false) },
        new("/offpet") { Usage = "CommandUsage.OffSkin", Host = c => SkinControle.ClearCommand(c, false, false, false, true) },
        new("/offskinall") { Usage = "CommandUsage.OffSkin", Host = c => SkinControle.ClearCommand(c, true, true, true, true) },
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
        // 名前の変更は EHR 系に合わせて /rn。/r は EHR 系と同じく役職の説明に使う
        new("/rn", "/rename", "/name") { Usage = "CommandUsage.Rename", Host = c => Main.nickName = c.Arg(1) },
        new("/r", "/hr")
        {
            Usage = "CommandUsage.Role",
            Host = c => ShowRoleInfo(Words(c), 1, byte.MaxValue, ShowActiveRoles),
            Remote = c => ShowRoleInfo(Words(c), 1, c.Player.PlayerId, ShowActiveRoles),
        },
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
            Usage = "CommandUsage.Now",
            Host = c => ShowNow(c, byte.MaxValue),
            Remote = c => ShowNow(c, c.Player.PlayerId),
        },
        new("/mode") { Usage = "CommandUsage.Mode", Host = c => Core.VanillaHideAndSeek.Command(c.Args) },
        new("/w") { Usage = "CommandUsage.ForceWin", Host = ForceWin },
        new("/dis") { Host = ForceDisconnectEnd },
        new("/h", "/help")
        {
            Usage = "CommandUsage.Help",
            Host = c => ShowHelp(c, byte.MaxValue, showGeneralHelp: true),
            Remote = c => ShowHelp(c, c.Player.PlayerId, showGeneralHelp: false),
        },
        new("/m", "/myrole") { Host = ShowMyRole, Remote = ShowMyRole },
        // マッドマジックの変身先を選ぶ (会議で自投票した後)
        new("/mm")
        {
            Host = c => (PlayerControl.LocalPlayer.GetRoleClass() as Roles.Madmate.MadMagic)?.HandleCommand(c.Args),
            Remote = c => (c.Player.GetRoleClass() as Roles.Madmate.MadMagic)?.HandleCommand(c.Args),
        },
        new("/t", "/template")
        {
            Usage = "CommandUsage.Template",
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
            Usage = "CommandUsage.MessageWait",
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
            Usage = "CommandUsage.Say",
            Host = c =>
            {
                if (c.Args.Length > 1)
                    Utils.SendMessageAutoSplit(c.Args.Skip(1).Join(delimiter: " "), true, $"<color=#ff0000>{GetString("MessageFromTheHost")}</color>");
            },
        },
        new("/exile")
        {
            Usage = "CommandUsage.PlayerId",
            Host = c => { if (c.Args.Length > 1 && int.TryParse(c.Args[1], out int id)) Utils.GetPlayerById(id)?.RpcExileV3(); },
        },
        new("/kill")
        {
            Usage = "CommandUsage.PlayerId",
            Host = c => { if (c.Args.Length > 1 && int.TryParse(c.Args[1], out int id)) Utils.GetPlayerById(id)?.RpcMurderPlayer(Utils.GetPlayerById(id)); },
        },
        new("/vo", "/voice") { Host = HostVoice, Remote = RemoteVoice },
        new("/killFlash", "/kf")
        {
            Host = c => Utils.SetKillFlashAfterDead(c.Player, true),
            Remote = c => Utils.SetKillFlashAfterDead(c.Player),
        },
        new("/ws", "/whisper")
        {
            Usage = "CommandUsage.Whisper",
            Hidden = true,
            Host = Modules.Whisper.Command,
            Remote = Modules.Whisper.Command,
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

    /// <summary>「ID: 名前」を ID の順に並べた一覧。/sr /kick /ws などで相手を ID で指すため</summary>
    private static string PlayerIdList()
        => string.Join("\n", Main.AllPlayerControls
            .Where(pc => pc != null && pc.Data != null)
            .OrderBy(pc => pc.PlayerId)
            .Select(pc => $"{pc.PlayerId}: {pc.Data.PlayerName}"));

    /// <summary>
    /// /start: 開始のカウントダウンを始める。/start 秒数 で残りをその秒数 (60 秒まで) に、/start c でカウントダウンを取りやめる (ロビーだけ)
    /// </summary>
    private static void StartCommand(ChatCommandContext c)
    {
        if (!GameStates.IsLobby || !GameStartManager.InstanceExists) return;
        var gsm = GameStartManager.Instance;
        var arg = c.Arg(1).ToLowerInvariant();
        if (arg is "c" or "cancel")
        {
            if (GameStates.IsCountDown) gsm.ResetStartState();
            return;
        }
        if (!GameStates.IsCountDown) gsm.BeginGame();
        if (GameStates.IsCountDown && float.TryParse(arg, out var sec) && sec >= 0f)
            gsm.countDownTimer = Math.Min(sec, 60f);
    }

    /// <summary>/end: 廃村にする (Shift+Enter+L と同じ)</summary>
    internal static void EndAsDraw()
    {
        if (!GameStates.IsInGame || !Main.IsSetRoleFinished) return;
        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
        GameManager.Instance.LogicFlow.CheckEndCriteria();
    }

    private static void ShowMyRole(ChatCommandContext c)
    {
        if (AmongUsClient.Instance.IsGameStarted) Utils.ShowMyRoleInfo(c.Player, c.Player.PlayerId);
    }

    // 空白が続いて空の引数が挟まっても (「/n  r 役職」) 読めるように、空でない語だけを見る
    private static string[] Words(ChatCommandContext c) => c.Args.Where(a => a.Length > 0).ToArray();

    /// <summary>
    /// words[from] 以降を役職名として説明を返す (/r 役職名・/n r 役職名・/h r 役職名)。役職名が無い時は emptyAction
    /// </summary>
    private static void ShowRoleInfo(string[] words, int from, byte sendTo, Action<byte> emptyAction)
    {
        var role = string.Join(" ", words.Skip(from)).Trim();
        if (role.Length == 0) emptyAction(sendTo);
        else ChatCommands.GetRolesInfo(role, sendTo);
    }

    // 役職名が無い /r と /n r は有効な役職の一覧、/h r は使い方
    private static void ShowActiveRoles(byte sendTo) => Utils.ShowActiveRoles(sendTo);
    private static void ShowRoleHelp(byte sendTo) => ChatCommands.GetRolesInfo("", sendTo);

    private static void ShowNow(ChatCommandContext c, byte sendTo)
    {
        var words = Words(c);
        switch (words.ElementAtOrDefault(1)?.ToLowerInvariant())
        {
            case "r":
            case "roles":
                ShowRoleInfo(words, 2, sendTo, ShowActiveRoles);
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
        var words = Words(c);
        switch (words.ElementAtOrDefault(1)?.ToLowerInvariant())
        {
            case "r":
            case "roles":
                ShowRoleInfo(words, 2, sendTo, ShowRoleHelp);
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
