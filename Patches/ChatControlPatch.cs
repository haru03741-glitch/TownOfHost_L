using AmongUs.Data;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostL
{
    // チャットの種類は接続の握手 (Hello) でサーバーへ送られるので、部屋を立てるための接続を始める前にフリーチャットへ直す
    // (HostGame の時点では握手が済んでいて遅い)。クイックチャット専用で繋いだホストがフリーチャット (案内やコマンドの返事) を
    // 送ると、公式サーバーはホストを切断する。他人の部屋へ入る接続は触らない。
    [HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.Connect))]
    class HostGameChatModePatch
    {
        public static void Prefix([HarmonyArgument(0)] InnerNet.MatchMakerModes mode)
        {
            if (mode != InnerNet.MatchMakerModes.HostAndClient) return;
            if (DataManager.Settings.Multiplayer.ChatMode != InnerNet.QuickChatModes.QuickChatOnly) return;
            DataManager.Settings.Multiplayer.ChatMode = InnerNet.QuickChatModes.FreeChatOrQuickChat;
            Logger.Info("クイックチャット専用の設定をフリーチャットに直してから部屋を立てます", "HostGameChatMode");
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Update))]
    class ChatControllerUpdatePatch
    {
        public static int CurrentHistorySelection = -1;
        public static void Prefix()
        {
            if (AmongUsClient.Instance.AmHost && DataManager.Settings.Multiplayer.ChatMode == InnerNet.QuickChatModes.QuickChatOnly)
                DataManager.Settings.Multiplayer.ChatMode = InnerNet.QuickChatModes.FreeChatOrQuickChat; //コマンドを打つためにホストのみ常時フリーチャット開放
        }
        public static void Postfix(ChatController __instance)
        {
            if (!__instance.freeChatField.textArea.hasFocus) return;
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C))
                ClipboardHelper.PutClipboardString(__instance.freeChatField.textArea.text);
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.V))
                __instance.freeChatField.textArea.SetText(__instance.freeChatField.textArea.text + GUIUtility.systemCopyBuffer);
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.X))
            {
                ClipboardHelper.PutClipboardString(__instance.freeChatField.textArea.text);
                __instance.freeChatField.textArea.SetText("");
            }
            if (Input.GetKeyDown(KeyCode.UpArrow) && ChatCommands.ChatHistory.Count > 0)
            {
                CurrentHistorySelection = Mathf.Clamp(--CurrentHistorySelection, 0, ChatCommands.ChatHistory.Count - 1);
                __instance.freeChatField.textArea.SetText(ChatCommands.ChatHistory[CurrentHistorySelection]);
            }
            if (Input.GetKeyDown(KeyCode.DownArrow) && ChatCommands.ChatHistory.Count > 0)
            {
                CurrentHistorySelection++;
                if (CurrentHistorySelection < ChatCommands.ChatHistory.Count)
                    __instance.freeChatField.textArea.SetText(ChatCommands.ChatHistory[CurrentHistorySelection]);
                else __instance.freeChatField.textArea.SetText("");
            }
        }
    }
}