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
    /// <summary>モデレーター (Modules/Moderators) が送った時も Host の処理を行う。返事はホストの画面とモデレーター本人へ出る</summary>
    public bool ForModerator;
    /// <summary>ホストの設定で、他の人 (Remote) に使わせないようにできる</summary>
    public bool Restrictable;
    /// <summary>Restrictable の時の「使わせない」設定</summary>
    public OptionItem RestrictOption;
    public ChatCommand(params string[] names) => Names = names;
    /// <summary>他の人がこのコマンドを使えないよう、ホストが止めているか</summary>
    public bool IsRestricted => RestrictOption != null && RestrictOption.GetBool();
    public string UsageText => Usage == null ? "" : GetString(Usage);
    /// <summary>/help の一覧に出す説明 (翻訳のキー CmdHelp.名前)</summary>
    public string HelpText
    {
        get
        {
            var key = "CmdHelp." + Names[0].TrimStart('/');
            return Translator.HasString(key) ? GetString(key) : "";
        }
    }
}

/// <summary>チャットコマンドの登録表。名前は大文字小文字を区別しない。</summary>
public static class ChatCommandTable
{
    private static readonly ChatCommand[] All = Build();
    private static readonly Dictionary<string, ChatCommand> byName = BuildMap();

    public static ChatCommand Find(string name) => name != null && byName.TryGetValue(name, out var cmd) ? cmd : null;

    /// <summary>他の人のコマンドを個別に止める設定 (見出し 1 つと、Restrictable なコマンド 1 つずつ)</summary>
    public static void SetupCustomOption()
    {
        var head = StringOptionItem.Create("RestrictGuestCommands", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.CrewmateBlue)
            .SetGameMode(CustomGameMode.All);
        foreach (var cmd in All)
        {
            if (!cmd.Restrictable) continue;
            var label = string.Join(" ", cmd.Names);
            cmd.RestrictOption = StringOptionItem.Create("RestrictCommand." + cmd.Names[0].TrimStart('/'), new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
                .SetParent(head)
                .SetOptionName(() => string.Format(GetString("RestrictCommand"), label))
                .SetGameMode(CustomGameMode.All);
        }
    }

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
            Restrictable = true,
            Host = _ => HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, PlayerIdList()),
            Remote = c => Utils.SendMessageAutoSplit(PlayerIdList(), true, sendTo: c.Player.PlayerId),
        },
        new("/start") { Usage = "CommandUsage.Start", ForModerator = true, Host = StartCommand },
        new("/end", "/abort") { ForModerator = true, Host = _ => EndAsDraw() },
        new("/mt", "/endmeeting") { ForModerator = true, Host = _ => { if (GameStates.IsMeeting) MeetingHud.Instance.RpcClose(); } },
        new("/kick") { Usage = "CommandUsage.Kick", ForModerator = true, Host = c => ChatCommands.KickCommand(c.Args, ban: false, c.Player) },
        new("/ban") { Usage = "CommandUsage.Ban", ForModerator = true, Host = c => ChatCommands.KickCommand(c.Args, ban: true, c.Player) },
        // 前の試合に居た人の再入室: 見つけた人を一括キック / このロビーだけ許す
        new("/kp", "/kickprevious") { ForModerator = true, Host = _ => Modules.JoinGuard.KickDetected() },
        new("/aj", "/allowjoin") { ForModerator = true, Host = _ => Modules.JoinGuard.AllowThisLobby() },
        // 試合の結果を送る Discord の Webhook (URL・on・off・test)
        new("/webhook") { Usage = "CommandUsage.Webhook", Local = Modules.DiscordWebhook.Command },
        // 配信の YouTube のコメントを読む (URL・stop) と、視聴者の干渉の管理
        new("/yt", "/youtube") { Usage = "CommandUsage.YouTube", Local = Modules.Streaming.Audience.YouTubeCommand },
        new("/audience") { Usage = "CommandUsage.Audience", Local = Modules.Streaming.Audience.AudienceCommand },
        new("/mod", "/moderator") { Usage = "CommandUsage.PlayerId", Host = Modules.Moderators.Command },
        // 設定を名前で探して変える (ロビーだけ)
        new("/cs", "/changesetting") { Usage = "CommandUsage.ChangeSetting", ForModerator = true, Host = Modules.SettingCommand.Command },
        // ロビーの投票: 自由な質問 / プリセット / 打ち切り
        new("/poll") { Usage = "CommandUsage.Poll", ForModerator = true, Host = Modules.LobbyPoll.PollCommand },
        new("/pv", "/presetvote") { ForModerator = true, Host = c => Modules.LobbyPoll.StartPresetVote(c.Player) },
        new("/pe", "/pollend") { ForModerator = true, Host = _ => Modules.LobbyPoll.Finish() },
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
            Restrictable = true,
            Host = _ => Utils.ShowLastResult(),
            Remote = c => Utils.ShowLastResult(c.Player.PlayerId),
        },
        new("/kl", "/killlog")
        {
            Restrictable = true,
            Host = _ => Utils.ShowKillLog(),
            Remote = c => Utils.ShowKillLog(c.Player.PlayerId),
        },
        // 名前の変更は EHR 系に合わせて /rn。/r は EHR 系と同じく役職の説明に使う
        new("/rn", "/rename", "/name") { Usage = "CommandUsage.Rename", Host = c => Main.nickName = c.Arg(1) },
        new("/r", "/hr")
        {
            Restrictable = true,
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
            Restrictable = true,
            Usage = "CommandUsage.Now",
            Host = c => ShowNow(c, byte.MaxValue),
            Remote = c => ShowNow(c, c.Player.PlayerId),
        },
        new("/mode") { Usage = "CommandUsage.Mode", Host = c => Core.VanillaHideAndSeek.Command(c.Args) },
        new("/w") { Usage = "CommandUsage.ForceWin", Host = ForceWin },
        new("/dis") { Host = ForceDisconnectEnd },
        new("/h", "/help")
        {
            Restrictable = true,
            Usage = "CommandUsage.Help",
            Host = c => ShowHelp(c, byte.MaxValue, showGeneralHelp: true),
            Remote = c => ShowHelp(c, c.Player.PlayerId, showGeneralHelp: false),
        },
        // 自分の戦績 (ホストの手元に記録した分)
        new("/me", "/stats") { Restrictable = true, Host = Modules.PlayerStats.Command, Remote = Modules.PlayerStats.Command },
        new("/m", "/myrole") { Restrictable = true, Host = ShowMyRole, Remote = ShowMyRole },
        // マッドマジックの変身先を選ぶ (会議で自投票した後)
        new("/mm")
        {
            Host = c => (PlayerControl.LocalPlayer.GetRoleClass() as Roles.Madmate.MadMagic)?.HandleCommand(c.Args),
            Remote = c => (c.Player.GetRoleClass() as Roles.Madmate.MadMagic)?.HandleCommand(c.Args),
        },
        new("/t", "/template")
        {
            Restrictable = true,
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
            ForModerator = true,
            Host = c =>
            {
                if (c.Args.Length <= 1) return;
                // モデレーターが打った時は、その人の名前を題にする
                var title = c.Player == null || c.Player.AmOwner
                    ? $"<color=#ff0000>{GetString("MessageFromTheHost")}</color>"
                    : $"<color=#ffaa00>{string.Format(GetString("MessageFromModerator"), c.Player.Data?.PlayerName)}</color>";
                Utils.SendMessageAutoSplit(c.Args.Skip(1).Join(delimiter: " "), true, title);
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
        new("/vo", "/voice") { Restrictable = true, Host = HostVoice, Remote = RemoteVoice },
        new("/killFlash", "/kf")
        {
            Restrictable = true,
            Host = c => Utils.SetKillFlashAfterDead(c.Player, true),
            Remote = c => Utils.SetKillFlashAfterDead(c.Player),
        },
        new("/ws", "/whisper")
        {
            Restrictable = true,
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
                ShowCommandList(c.Player, showGeneralHelp);
                break;
        }
    }

    /// <summary>
    /// 打った人が使えるコマンドの一覧を、その人にだけ出す。表から作るので、足したコマンドもそのまま載る。
    /// ホストは自分の手元で動く物、他の人はホストが止めていない物 (モデレーターはモデレーターの物も) を並べる
    /// </summary>
    private static void ShowCommandList(PlayerControl to, bool asHost)
    {
        if (to == null) return;
        var moderator = !asHost && Modules.Moderators.Is(to);
        var sb = new System.Text.StringBuilder(GetString("CommandList"));
        void Add(ChatCommand cmd)
        {
            sb.Append('\n').Append(cmd.Names[0]);
            if (cmd.Names.Length > 1) sb.Append(" (").Append(string.Join(" ", cmd.Names.Skip(1))).Append(')');
            var usage = cmd.UsageText;
            if (usage != "") sb.Append(' ').Append(usage);
            var help = cmd.HelpText;
            if (help != "") sb.Append(" - ").Append(help);
        }
        foreach (var cmd in All)
        {
            // 開発者だけのコマンドは、開発者にだけ見せる
            if (cmd.Names[0] is "/dev" or "/log" && !DebugModeManager.AmDebugger) continue;
            if (asHost ? cmd.Host != null || cmd.Local != null : cmd.Remote != null && !cmd.IsRestricted) Add(cmd);
        }
        if (moderator)
        {
            sb.Append("\n\n").Append(GetString("CommandList.Moderator"));
            foreach (var cmd in All)
                if (cmd.ForModerator && cmd.Host != null && cmd.Remote == null) Add(cmd);
        }
        if (asHost) sb.Append("\n\n").Append(GetString("CommandList.HostHint"));
        Utils.SendMessageAutoSplit(sb.ToString(), true, sendTo: to.PlayerId);
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
