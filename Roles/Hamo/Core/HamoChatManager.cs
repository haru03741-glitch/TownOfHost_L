using System;
using System.Collections.Generic;
using System.Linq;
using Hazel;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Modules.ChatManager
{
    /// <summary>
    /// hamo の ChatManager の移植。
    /// 会議中にチャットで役職を当てた・撃った時、その書き込みを流すために
    /// 偽のコマンドを流してから、それまでの会話を送り直す。
    /// </summary>
    public class ChatManager
    {
        public static bool cancel = false;
        private static readonly List<string> chatHistory = new();
        private const int maxHistorySize = 20;

        [Attributes.GameModuleInitializer]
        public static void ResetChat() => chatHistory.Clear();

        public static bool CommandCheck(string meg) => meg.StartsWith("/");

        /// <summary>チャットを受け取った時に呼ぶ (ホストのみ)。普通の会話を記録する</summary>
        public static void SendMessage(PlayerControl player, string message)
        {
            if (player == null || !AmongUsClient.Instance.AmHost) return;
            if (!player.IsAlive() || !GameStates.IsInGame) return;
            var msg = message;
            var lower = message.ToLower().Trim();
            if (msg.StartsWith("<size=0>.</size>") || (!CommandCheck(lower)))
            {
                chatHistory.Add($"{player.PlayerId}: {msg}");
                if (chatHistory.Count > maxHistorySize) chatHistory.RemoveAt(0);
            }
            cancel = CommandCheck(lower);
        }

        public static void SendPreviousMessagesToAll(bool SendDiePlayer = true)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (Utils.IsRestriction()) return;
            ChatUpdatePatch.DoBlockChat = true;
            var rd = IRandom.Instance;
            var roles = EnumHelper.GetAllValues<CustomRoles>()
                .Where(role => role > CustomRoles.NotAssigned && role < CustomRoles.MaxMain && role is not CustomRoles.GM)
                .ToList();
            string[] specialTexts = { "bt" };

            for (int i = chatHistory.Count; i < 30; i++)
            {
                if (PlayerCatch.AllAlivePlayersCount == 0) break;
                var msg = "/" + specialTexts[rd.Next(0, specialTexts.Length)] + " " + rd.Next(0, 15) + " " + UtilsRoleText.GetRoleName(roles[rd.Next(0, roles.Count)]) + " ";
                var player = PlayerCatch.AllAlivePlayerControls.ToArray()[rd.Next(0, PlayerCatch.AllAlivePlayersCount)];
                SendChatAs(player, msg);
            }

            foreach (var entry in chatHistory.ToArray())
            {
                var idx = entry.IndexOf(':');
                if (idx < 0) continue;
                if (!byte.TryParse(entry[..idx].Trim(), out var senderId)) continue;
                var senderMessage = entry[(idx + 1)..].Trim();
                var isvote = senderMessage.StartsWith("<size=0>.</size>");
                var senderPlayer = Utils.GetPlayerById(senderId);
                if (senderPlayer == null) continue;

                var revive = !senderPlayer.IsAlive() && SendDiePlayer && !AntiBlackout.IsSet && !AntiBlackout.IsCached;
                if (revive) senderPlayer.Revive();
                if (isvote) SendVoteNote(senderPlayer);
                else SendChatAs(senderPlayer, senderMessage);
                if (revive) senderPlayer.Die(DeathReason.Kill, true);
            }
            ChatUpdatePatch.DoBlockChat = false;
        }

        static void SendChatAs(PlayerControl player, string msg)
        {
            DestroyableSingleton<HudManager>.Instance.Chat.AddChat(player, msg);
            var writer = CustomRpcSender.Create("MessagesToSend", SendOption.None);
            writer.StartMessage(-1);
            writer.StartRpc(player.NetId, (byte)RpcCalls.SendChat)
                .Write(msg)
                .EndRpc();
            writer.EndMessage();
            writer.SendMessage();
        }
        static void SendVoteNote(PlayerControl player)
        {
            DestroyableSingleton<HudManager>.Instance.Chat.AddChatNote(player.Data, ChatNoteTypes.DidVote);
            var writer = CustomRpcSender.Create("MessagesToSend", SendOption.None);
            writer.StartMessage(-1);
            writer.StartRpc(player.NetId, (byte)RpcCalls.SendChatNote)
                .Write(player.PlayerId)
                .Write((int)ChatNoteTypes.DidVote)
                .EndRpc();
            writer.EndMessage();
            writer.SendMessage();
        }

        /// <summary>
        /// hamo では「チャットコマンドを隠す」設定の時だけ、死んだ人の会話を生存者から隠す。
        /// TOH_L にはその設定が無いので何もしない。
        /// </summary>
        public static void OnDisconnectOrDeadPlayer(byte id) { }
    }
}

namespace TownOfHostL.Modules.ChatManager
{
    [HarmonyLib.HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    class HamoChatHistoryPatch
    {
        public static void Postfix(PlayerControl sourcePlayer, string chatText)
        {
            if (ChatUpdatePatch.DoBlockChat) return;
            if (!GameStates.IsMeeting) return;
            ChatManager.SendMessage(sourcePlayer, chatText);
        }
    }
}
