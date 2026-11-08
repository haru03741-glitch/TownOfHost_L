// The blank push-out is based on ChatManager.ClearChat / SendPreviousMessagesToAll of https://github.com/waffle-ful/Aeterna-End-K-not
// and https://github.com/Gurge44/EndlessHostRoles Patches/ChatControlPatch.cs (base from https://github.com/Rabek009/MoreGamemodes Modules/Utils.cs) (GPL-3.0)
using System.Collections.Generic;
using System.Text;
using TownOfHostL.Attributes;

using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 見せたくない書き込みを、大きな空白の 1 通でチャット欄から押し出す (ホストだけ)。押し出した後に、それまでの普通の会話を
/// 1 通にまとめて送り直す (押し出すと前の会話も画面の上へ消えるので)。
/// 書き込みそのものを消す手段は無い (頭の上の吹き出しも残る)。本当に隠せるのは /cmd を付けて送った時だけ。
/// 送るのは本編のチャットの送信 1 回ずつを送信待ちの列に積む形で、1 フレームに何通も送らない
/// (公式サーバーは同じフレームのチャットの送信が多いと Hacking として切断する)
/// </summary>
public static class ChatPushOut
{
    private const string Blank = "<size=32767>.";
    // 送り直すのは新しい方から 10 件・1 件 80 文字まで (まとめが何通にも割れて送信待ちの列を埋めないように)
    private const int MaxHistory = 10;
    private const int MaxLength = 80;
    private static readonly List<(byte id, string text)> history = new();

    [GameModuleInitializer]
    public static void Reset() => history.Clear();

    /// <summary>
    /// チャットを送り直しの控えに入れる (ホストだけ)。コマンドは入れない。
    /// 試合中は会議の間の、生きている人の書き込みだけ (死んだ人の会話を生きている人へ送り直さない)
    /// </summary>
    public static void Record(PlayerControl player, string text)
    {
        if (player == null || string.IsNullOrWhiteSpace(text) || !AmongUsClient.Instance.AmHost) return;
        text = text.Trim();
        if (text.StartsWith('/')) return;
        if (GameStates.IsInGame && (!GameStates.IsMeeting || !player.IsAlive())) return;
        if (text.Length > MaxLength) text = text[..MaxLength] + "…";
        history.Add((player.PlayerId, text));
        if (history.Count > MaxHistory) history.RemoveAt(0);
    }

    /// <summary>全員のチャット欄を大きな空白で押し出し、控えの会話を送り直す</summary>
    public static void PushOut()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        Logger.Info("チャットを空白で押し出します", "ChatPushOut");
        // 続けて押し出させても、送信待ちに同じ空白・同じまとめがあれば重ねて積まない (Utils.EnqueueMessage)
        Utils.SendRawChat(Blank);
        if (history.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var (id, text) in history)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null) continue;
            var name = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(id, out var n) ? n : pc.Data.PlayerName;
            sb.Append(Utils.ColorString(Palette.PlayerColors[pc.Data.DefaultOutfit.ColorId], Escape(name)))
                .Append(": ").Append(Escape(text)).Append('\n');
        }
        if (sb.Length == 0) return;
        Utils.SendMessageAutoSplit(sb.ToString().TrimEnd('\n'), true, $"<color=#aaaaff>{GetString("ChatPushOut.Title")}</color>");
    }

    /// <summary>人が打った文や名前の &lt; &gt; を全角にする (装飾を混ぜて、ホストのお知らせに見える偽の表示を作れないように)</summary>
    public static string Escape(string s) => s.Replace('<', '＜').Replace('>', '＞');
}
