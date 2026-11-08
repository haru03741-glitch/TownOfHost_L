using AmongUs.Data;
using HarmonyLib;
using TownOfHostL.Modules;

namespace TownOfHostL
{
    // チャットの種類は接続の握手 (Hello) でサーバーへ送られるので、部屋を立てるための接続を始める前にフリーチャットへ直す
    // (HostGame の時点では握手が済んでいて遅い)。クイックチャット専用で繋いだホストがフリーチャット (案内やコマンドの返事) を
    // 送ると、公式サーバーはホストを切断する。他人の部屋へ入る接続は触らない。
    // 本編のかくれんぼで立てる部屋でも TOHL はチャットを送るので、Core の束に置く
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
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
        public static void Prefix()
        {
            if (AmongUsClient.Instance.AmHost && DataManager.Settings.Multiplayer.ChatMode == InnerNet.QuickChatModes.QuickChatOnly)
                DataManager.Settings.Multiplayer.ChatMode = InnerNet.QuickChatModes.FreeChatOrQuickChat; //コマンドを打つためにホストのみ常時フリーチャット開放
        }
        // 貼り付け・Tab の補い・履歴などの入力欄の操作 (Modules/ChatInput)
        public static void Postfix(ChatController __instance) => ChatInput.Tick(__instance);
    }
}