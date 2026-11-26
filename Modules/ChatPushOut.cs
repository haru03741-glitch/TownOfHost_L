// The blank push-out is based on ChatManager.ClearChat / SendPreviousMessagesToAll of https://github.com/waffle-ful/Aeterna-End-K-not
// and https://github.com/Gurge44/EndlessHostRoles Patches/ChatControlPatch.cs (base from https://github.com/Rabek009/MoreGamemodes Modules/Utils.cs) (GPL-3.0).
// The push-out after the ejection screen is based on HasMessageDuringEjectionScreen of https://github.com/Gurge44/EndlessHostRoles Patches/ExilePatch.cs (GPL-3.0)
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
    // 送り直すのは新しい方から 20 件・1 件 300 文字 (TOHL 入りの客の入力の上限) まで。
    // まとめはバイト数で何通かに割れても、送信待ちの列を 1 通ずつ通る
    private const int MaxHistory = 20;
    private const int MaxLength = 300;
    private static readonly List<(byte id, string text)> history = new();

    // 追放の画面の間に、生きている扱いの人の書き込みが届いたか。追放される人は画面が終わるまで生きている扱いなので、
    // 投票が終わった後の書き込みが生きている人のチャット欄に残る。試合に戻ってから押し出して見えなくする。
    // 死んだ人の書き込みは本編が死んだ人にしか見せないので数えない
    private static bool chattedDuringExile;
    private const float ExilePushOutDelay = 3f;

    // 一時的に幽霊になっている人 (TempGhost) は、本編では生きている扱いのまま幽霊のチャット欄を持つので、
    // 試合中の書き込みが生きている人のチャット欄に残り、次の会議で見える。試合中に押し出しても客のチャット欄に残らないので
    // (2026-11-26 実機)、次の会議の始まりに押し出す
    private static bool pushOutAtMeeting;

    [GameModuleInitializer]
    public static void Reset()
    {
        history.Clear();
        chattedDuringExile = false;
        pushOutAtMeeting = false;
    }

    /// <summary>客の書き込みが届いた時に呼ぶ (ホストだけ)。追放の画面の間なら覚えておく</summary>
    public static void NoteChat(PlayerControl player)
    {
        if (player == null || player.AmOwner || !AmongUsClient.Instance.AmHost) return;
        if (ExileController.Instance != null && player.Data != null && !player.Data.IsDead) chattedDuringExile = true;
        // バイタルで生きているように見せている死者も、嘘を受けた人の画面では生きている扱いなので書き込みが残る
        if (GameStates.IsInTask && !pushOutAtMeeting && (TempGhost.IsGhost(player) || Net.VitalsMask.ShowsChatOf(player.PlayerId)))
        {
            Logger.Info($"{player.GetNameWithRole()} の試合中の書き込みを、次の会議の始まりに押し出す", "ChatPushOut");
            pushOutAtMeeting = true;
        }
    }

    /// <summary>会議の始まりに呼ぶ (ホストだけ)。試合中に残った書き込みがあれば、会議の知らせより前に押し出す</summary>
    public static void OnMeetingStart()
    {
        if (!pushOutAtMeeting || !AmongUsClient.Instance.AmHost) return;
        pushOutAtMeeting = false;
        PushOut(resend: false);
    }

    /// <summary>追放の画面が終わって試合に戻った時に呼ぶ (ホストだけ)</summary>
    public static void AfterExile()
    {
        if (!chattedDuringExile || !AmongUsClient.Instance.AmHost) return;
        chattedDuringExile = false;
        _ = new LateTask(() =>
        {
            if (!GameStates.IsInGame || GameStates.IsMeeting) return;
            Logger.Info("追放の画面の間の書き込みを押し出します", "ChatPushOut");
            PushOut(resend: false);
        }, ExilePushOutDelay, "ChatPushOutAfterExile");
    }

    /// <summary>
    /// チャットを送り直しの控えに入れる (ホストだけ)。コマンドは入れない。
    /// 試合中は会議の間の、生きている人の書き込みだけ (死んだ人の会話を生きている人へ送り直さない)
    /// </summary>
    public static void Record(PlayerControl player, string text)
    {
        if (player == null || string.IsNullOrWhiteSpace(text) || !AmongUsClient.Instance.AmHost) return;
        text = text.Trim();
        if (text.StartsWith('/')) return;
        if (GameStates.IsInGame && (!GameStates.IsMeeting || !player.IsAlive() || ExileController.Instance != null)) return;
        if (text.Length > MaxLength) text = text[..MaxLength] + "…";
        history.Add((player.PlayerId, text));
        if (history.Count > MaxHistory) history.RemoveAt(0);
    }

    private const float HiddenCommandInterval = 3f;
    private static float hiddenPushedAt = float.NegativeInfinity;
    /// <summary>
    /// 見せたくないコマンドを /cmd 無しで送られた時の押し出し。全員に見える発言 (ロビー・会議中の生きた人) の時だけ、
    /// 3 秒に 1 回まで押し出す (連打で全員のチャット欄を消し続け、送信の列を埋めないように)
    /// </summary>
    public static void PushOutForHiddenCommand(PlayerControl sender)
    {
        bool visibleToAll = !GameStates.IsInGame || (GameStates.IsMeeting && sender != null && sender.IsAlive());
        if (!visibleToAll) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now - hiddenPushedAt < HiddenCommandInterval) return;
        hiddenPushedAt = now;
        PushOut();
    }

    /// <summary>全員のチャット欄を大きな空白で押し出し、resend なら控えの会話を送り直す</summary>
    public static void PushOut(bool resend = true)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        Logger.Info("チャットを空白で押し出します", "ChatPushOut");
        // 続けて押し出させても、送信待ちに同じ空白・同じまとめがあれば重ねて積まない (Utils.EnqueueMessage)
        Utils.SendRawChat(Blank);
        if (!resend || history.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var (id, text) in history)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null) continue;
            var name = pc.GetPlainName() ?? pc.Data.PlayerName;
            sb.Append(Utils.ColorString(Palette.PlayerColors[pc.Data.DefaultOutfit.ColorId], Escape(name)))
                .Append(": ").Append(Escape(text)).Append('\n');
        }
        if (sb.Length == 0) return;
        // 何通かに割れても、続きの字の大きさを 1 通目とそろえる
        Utils.SendMessageAutoSplit(sb.ToString().TrimEnd('\n'), true, $"<color=#aaaaff>{GetString("ChatPushOut.Title")}</color>", formatTag: "");
    }

    /// <summary>人が打った文や名前の &lt; &gt; を全角にする (装飾を混ぜて、ホストのお知らせに見える偽の表示を作れないように)</summary>
    public static string Escape(string s) => s.Replace('<', '＜').Replace('>', '＞');
}
