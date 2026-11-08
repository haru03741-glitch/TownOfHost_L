namespace TownOfHostL.Modules.ChatManager
{
    /// <summary>
    /// hamo の ChatManager の窓口。会議中の書き込みを流す処理は、大きな空白で押し出す ChatPushOut に任せる
    /// </summary>
    public class ChatManager
    {
        public static void SendPreviousMessagesToAll(bool SendDiePlayer = true) => ChatPushOut.PushOut();

        /// <summary>
        /// hamo では「チャットコマンドを隠す」設定の時だけ、死んだ人の会話を生存者から隠す。
        /// TOH_L にはその設定が無いので何もしない。
        /// </summary>
        public static void OnDisconnectOrDeadPlayer(byte id) { }
    }
}
