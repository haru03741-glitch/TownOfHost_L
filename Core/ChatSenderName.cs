using System;

using TownOfHostL;

namespace TownOfHostL.Core;

/// <summary>
/// チャットを流すために送り手の名前を見出しに書き換えた後、客の画面に戻す名前を決める。
/// 試合中、ホストの手元にある名前はホストから見た表示 (役職・タスク数・役職の色) に書き換わっているので、
/// それを戻すと客に送り手の役職が見えてしまう。試合中は、NotifyRoles が他人の名前として送る本当の名前で戻す。
/// </summary>
public static class ChatSenderName
{
    private static int resendToken;

    /// <summary>客の画面に戻す名前。ロビーでは今の名前 (名前の飾りを含む) をそのまま戻す</summary>
    public static string RestoreFor(PlayerControl sender)
    {
        if (!GameStates.IsInGame) return sender.Data.PlayerName;
        if (GameStates.IsMeeting) return sender.GetRealName(isMeeting: true);
        return Main.AllPlayerNames.TryGetValue(sender.PlayerId, out var name) ? name : sender.Data.PlayerName;
    }

    /// <summary>
    /// 戻した名前を、送った名前の覚え書きに写す。clientId が -1 なら全員に戻している。
    /// その後、本来の表示 (仲間の色など) を送り直すのは、メッセージの続きが止んでから 1 回だけにする。
    /// 戻しの送信と同じフレームで送り直すと、届く順番が入れ替わって本当の名前のまま残ることがある。
    /// </summary>
    public static void Restored(PlayerControl sender, string name, int clientId)
    {
        if (!GameStates.IsInGame || Main.LastNotifyNames == null) return;
        foreach (var seer in Main.AllPlayerControls)
        {
            // ホストの画面は手元の SetName で元の名前に戻している
            if (seer.AmOwner) continue;
            if (clientId != -1 && seer.GetClientId() != clientId) continue;
            Main.LastNotifyNames[(sender.PlayerId, seer.PlayerId)] = name;
        }
        int token = ++resendToken;
        float wait = Math.Max(Main.MessageWait.Value, 0.1f) + 0.3f;
        _ = new LateTask(() =>
        {
            if (token != resendToken || !GameStates.IsInGame) return;
            Utils.NotifyRoles();
        }, wait, "ChatSenderNameResend");
    }
}
