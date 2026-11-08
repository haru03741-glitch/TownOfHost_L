// Based on the whisper (/w) of https://github.com/Gurge44/EndlessHostRoles and
// https://github.com/haru03741-glitch/More-Roles-Plus src/Commands/Whisper.cs (GPL-3.0)
using System.Linq;
using UnityEngine;

using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 耳打ち (/ws 相手 文): ホストが相手にだけ文を送り直す。相手は ID の数字か名前 (名前の一部が 1 人だけに当たればそれ)。
/// 使えるのはロビー・会議中の生きている人同士・死んだ人同士。打った文は他の人に見せないコマンドとして扱う
/// (/cmd を付けずに送られたら、空白で押し出してから送り直す)
/// </summary>
public static class Whisper
{
    public static OptionItem Enabled;
    public static OptionItem Announce;

    public static void SetupCustomOption()
    {
        Enabled = StringOptionItem.Create("Whisper", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.All);
        Announce = StringOptionItem.Create("Whisper.Announce", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetGameMode(CustomGameMode.All);
    }

    private static string Title(string key, string name) => $"<color=#b48cff>{string.Format(GetString(key), name)}</color>";

    private static string NameOf(PlayerControl pc)
        => ChatPushOut.Escape(Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(pc.PlayerId, out var n) ? n : pc.Data?.PlayerName ?? "?");

    private static bool CanWhisper(PlayerControl from, PlayerControl to)
    {
        if (GameStates.IsLobby) return true;
        if (!GameStates.IsInGame) return false;
        bool fromAlive = from.IsAlive(), toAlive = to.IsAlive();
        if (!fromAlive && !toAlive) return true;
        return fromAlive && toAlive && GameStates.IsMeeting;
    }

    /// <summary>
    /// 1 人にだけ見えるお知らせ。ホスト宛ても送信待ちの列に積む (先に積んだ空白の押し出しより後に出すため)
    /// </summary>
    private static void Tell(PlayerControl to, string title, string text) => Utils.SendMessageAutoSplit(text, true, title, to.PlayerId);

    public static void Command(ChatCommandContext c)
    {
        var from = c.Player;
        if (from == null) return;
        string system = $"<color=#aaaaff>{GetString("DefaultSystemMessageTitle")}</color>";
        if (!Enabled.GetBool())
        {
            Tell(from, system, GetString("Whisper.Disabled"));
            return;
        }
        if (c.Args.Length < 3)
        {
            Tell(from, system, $"{c.Args[0]} {GetString("CommandUsage.Whisper")}");
            return;
        }
        var arg = c.Args[1];
        var to = byte.TryParse(arg, out var id) ? Utils.GetPlayerById(id) : null;
        to ??= ChatCommands.FindPlayer(arg);
        if (to == null || to.Data == null || to.Data.Disconnected || to == from)
        {
            Tell(from, system, string.Format(GetString("Message.SelectRolesNoPlayer"), ChatPushOut.Escape(arg)));
            return;
        }
        if (!CanWhisper(from, to))
        {
            Tell(from, system, GetString("Whisper.NotNow"));
            return;
        }
        var text = ChatPushOut.Escape(string.Join(" ", c.Args.Skip(2)));
        Logger.Info($"耳打ち: {from.PlayerId} → {to.PlayerId}", "Whisper");
        Tell(to, Title("Whisper.From", NameOf(from)), text);
        Tell(from, Title("Whisper.To", NameOf(to)), text);
        if (Announce.GetBool())
            Utils.SendMessageAutoSplit(string.Format(GetString("Whisper.Announced"), NameOf(from), NameOf(to)), true);
    }
}
