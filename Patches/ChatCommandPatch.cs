using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Assets.CoreScripts;
using HarmonyLib;
using Hazel;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.Madmate;
using static TownOfHostL.Translator;

namespace TownOfHostL
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    class ChatCommands
    {
        public static List<string> ChatHistory = new();
        private static Dictionary<CustomRoles, string> roleCommands;

        public static bool Prefix(ChatController __instance)
        {
            if (__instance.freeChatField.textArea.text == "") return __instance.quickChatField.Visible;
            __instance.timeSinceLastMessage = 3f;
            var text = __instance.freeChatField.textArea.text;
            if (ChatHistory.Count == 0 || ChatHistory[^1] != text) ChatHistory.Add(text);
            ChatControllerUpdatePatch.CurrentHistorySelection = ChatHistory.Count;
            string[] args = text.Split(' ');
            var canceled = false;
            var cancelVal = "";
            Main.isChatCommand = true;
            Logger.Info(text, "SendChat");

            var hasCmdPrefix = args[0] == "/cmd" && args.Length >= 2;
            if (hasCmdPrefix)
            {
                string cmdArg = args[1].StartsWith("/") ? args[1] : "/" + args[1];
                string[] newArgs = new string[args.Length - 1];
                newArgs[0] = cmdArg;
                for (int i = 2; i < args.Length; i++) newArgs[i - 1] = args[i];
                args = newArgs;
                text = string.Join(" ", args);
            }

            var tag = !PlayerControl.LocalPlayer.Data.IsDead ? "SendChatHost" : "SendChatDeadHost";
            if (text.StartsWith("試合結果:") || text.StartsWith("キル履歴:")) tag = "SendSystemChat";
            VoiceReader.ReadHost(text, tag);

            // ホストは /cmd なしでもコマンドを使えるように、それ以外は /cmd 必須
            if (!hasCmdPrefix && !AmongUsClient.Instance.AmHost)
            {
                Main.isChatCommand = false;
                // 送信せず/cmdつきに自動で書き換えて送る
                if (args[0].StartsWith("/") && args[0] != "/cmd")
                {
                    __instance.freeChatField.textArea.SetText($"/cmd {text.TrimStart('/')}");
                    SendCommandFailedMessage(PlayerControl.LocalPlayer);
                    return false;
                }
                return true;
            }

            // 自分の画面だけで動くコマンド (ホストでなくても使える)
            var ctx = new ChatCommandContext(args, PlayerControl.LocalPlayer, __instance);
            var local = ChatCommandTable.Find(args[0])?.Local;
            local?.Invoke(ctx);
            canceled = local != null;
            Main.isChatCommand = local != null;
            if (AmongUsClient.Instance.AmHost)
            {
                var host = ChatCommandTable.Find(args[0])?.Host;
                host?.Invoke(ctx);
                canceled |= host != null;
                Main.isChatCommand = host != null;
            }
            cancelVal = ctx.CancelText;
            if (canceled)
            {
                Logger.Info("Command Canceled", "ChatCommand");
                __instance.freeChatField.textArea.Clear();
                __instance.freeChatField.textArea.SetText(cancelVal);
            }
            return !canceled;
        }
        public static void SendCustomChat(string SendName, string command, string name, PlayerControl sender = null)
        {
            Logger.Info($"SendCustomChat SendName: {SendName}, command: {command}, name: {name} sender: {sender?.name}", "SendCustomChat");

            if (sender == null)
                sender = PlayerControl.LocalPlayer;

            const int MaxLength = 320;
            const int MaxLines = 13;


            if (command.Length <= MaxLength &&
                (command.Split("\n")?.Count() ?? 0) <= MaxLines)
            {
                SendChunk(SendName, command, name, sender);
                return;
            }

            foreach (var chunk in SplitChatTextPreserveFormatting(command, MaxLength, MaxLines))
            {
                if (!string.IsNullOrWhiteSpace(chunk))
                    SendChunk(SendName, chunk, name, sender);
            }
        }

        private static IEnumerable<string> SplitChatTextPreserveFormatting(
            string text, int maxLength, int maxLines)
        {
            var lines = text.Split("\n");
            var activeTags = new List<(string Name, string Tag)>();

            var chunkLines = new List<string>();
            List<(string Name, string Tag)> chunkStartTags = null;

            foreach (var line in lines)
            {
                var lineStartTags = new List<(string Name, string Tag)>(activeTags);
                var nextActiveTags = UpdateActiveTags(activeTags, line);

                if (chunkLines.Count == 0)
                {
                    chunkStartTags = lineStartTags;
                }

                string candidateBody = string.Join("\n", chunkLines.Append(line));
                string candidateText =
                    BuildOpeningTags(chunkStartTags) +
                    candidateBody +
                    BuildClosingTags(nextActiveTags);

                bool exceeds =
                    chunkLines.Count > 0 &&
                    (candidateText.Length > maxLength ||
                     chunkLines.Count + 1 > maxLines);

                if (exceeds)
                {
                    var previousActiveTags = activeTags;
                    string previousBody = string.Join("\n", chunkLines);

                    yield return
                        BuildOpeningTags(chunkStartTags) +
                        previousBody +
                        BuildClosingTags(previousActiveTags);

                    chunkLines.Clear();
                    chunkStartTags = lineStartTags;
                }

                chunkLines.Add(line);
                activeTags = nextActiveTags;
            }

            if (chunkLines.Count > 0)
            {
                yield return
                    BuildOpeningTags(chunkStartTags) +
                    string.Join("\n", chunkLines) +
                    BuildClosingTags(activeTags);
            }
        }

        private static List<(string Name, string Tag)> UpdateActiveTags(
            List<(string Name, string Tag)> activeTags,
            string line)
        {
            var result = new List<(string Name, string Tag)>(activeTags);

            foreach (Match match in Regex.Matches(line, @"<(/?)([A-Za-z][A-Za-z0-9_-]*)(?:\s*[^>]*)?>"))
            {
                string name = match.Groups[2].Value.ToLowerInvariant();


                if (match.Value.EndsWith("/>") ||
                    name is "br" or "brk" or "sprite" or "quad" or "space")
                    continue;

                if (match.Groups[1].Success && match.Groups[1].Value == "/")
                {
                    for (int i = result.Count - 1; i >= 0; i--)
                    {
                        if (result[i].Name == name)
                        {
                            result.RemoveAt(i);
                            break;
                        }
                    }
                }
                else
                {
                    result.Add((name, match.Value));
                }
            }

            return result;
        }

        private static string BuildOpeningTags(List<(string Name, string Tag)> tags)
            => string.Concat(tags.Select(x => x.Tag));

        private static string BuildClosingTags(List<(string Name, string Tag)> tags)
            => string.Concat(tags.AsEnumerable().Reverse().Select(x => $"</{x.Name}>"));

        private static void SendChunk(string SendName, string text, string name, PlayerControl sender)
        {
            var crs = CustomRpcSender.Create("AllSend");
            var restoreName = GameStates.IsInGame ? TownOfHostL.Core.ChatSenderName.RestoreFor(sender) : name;

            crs.AutoStartRpc(sender.NetId, (byte)RpcCalls.SetName)
                .Write(sender.Data.NetId)
                .Write(SendName)
                .EndRpc()
                .AutoStartRpc(sender.NetId, (byte)RpcCalls.SendChat)
                .Write(text)
                .EndRpc()
                .AutoStartRpc(sender.NetId, (byte)RpcCalls.SetName)
                .Write(sender.Data.NetId)
                .Write(restoreName)
                .EndRpc()
                .SendMessage();
            TownOfHostL.Core.ChatSenderName.Restored(sender, restoreName, -1);

            sender.SetName(SendName);
            DestroyableSingleton<HudManager>.Instance.Chat.AddChat(sender, text);
            sender.SetName(name);
        }

        // 全員へ同じ名前を一時的に見せた後、見る人ごとの名前へ戻す。記録を捨てて NotifyRoles に全員分を組み直させる
        internal static void ForgetNotifiedNames(PlayerControl player)
        {
            if (player == null || !AmongUsClient.Instance.AmHost || Main.LastNotifyNames == null) return;
            foreach (var seer in Main.AllPlayerControls)
                Main.LastNotifyNames[(player.PlayerId, seer.PlayerId)] = null;
            if (GameStates.IsInGame) Utils.NotifyRoles();
        }

#if DEBUG
        internal static void SelectRolesCommand(string[] args)
        {
            if (!DebugModeManager.IsDebugMode)
            {
                // Debugモードでない時はコマンドを無視する
                return;
            }
            if (!GameStates.IsLobby)
            {
                Utils.SendMessageAutoSplit(GetString("Message.SelectRolesLobbyOnly"), true);
                return;
            }

            if (args.Length >= 2 && String.Compare(args[1], "clear", true) == 0)
            {
                Modules.SelectRoles.Clear();
                Utils.SendMessageAutoSplit(GetString("Message.SelectRolesCleared"), true);
                return;
            }
            if (args.Length < 3)
            {
                Utils.SendMessageAutoSplit(GetString("Message.SelectRolesUsage"), true);
                return;
            }

            var roleArg = args[^1];
            var nameArg = string.Join(" ", args[1..^1]);

            var target = Main.AllPlayerControls.FirstOrDefault(pc =>
                String.Compare(pc?.Data?.PlayerName, nameArg, true) == 0);
            target ??= Main.AllPlayerControls.FirstOrDefault(pc =>
                pc?.Data?.PlayerName?.Contains(nameArg, StringComparison.OrdinalIgnoreCase) == true);
            if (target == null)
            {
                Utils.SendMessageAutoSplit(string.Format(GetString("Message.SelectRolesNoPlayer"), nameArg), true);
                return;
            }

            if (!TryGetRoleByName(roleArg, out var role))
            {
                Utils.SendMessageAutoSplit(string.Format(GetString("Message.SelectRolesNoRole"), roleArg), true);
                return;
            }
            if (role.IsAddOn() || role.IsSpectator())
            {
                Utils.SendMessageAutoSplit(string.Format(GetString("Message.SelectRolesNotMainRole"), Utils.GetRoleName(role)), true);
                return;
            }

            Modules.SelectRoles.Set(target.PlayerId, role);
            Utils.SendMessageAutoSplit(string.Format(GetString("Message.SelectRolesSet"),
                target.Data.PlayerName, Utils.ColorString(Utils.GetRoleColor(role), Utils.GetRoleName(role))), true);
        }
#endif
        public static bool TryGetRoleByName(string input, out CustomRoles role)
        {
            InitRoleCommands();
            foreach (var r in roleCommands)
            {
                if ((int)r.Key < 0) continue;

                if (String.Compare(input, r.Key.ToString(), true) == 0 || String.Compare(input, r.Value, true) == 0)
                {
                    role = r.Key;
                    return true;
                }
            }
            role = CustomRoles.NotAssigned;
            return false;
        }
        public static void GetRolesInfo(string role, byte PlayerId = byte.MaxValue)
        {
            InitRoleCommands();

            foreach (var r in roleCommands)
            {
                var roleName = r.Key.ToString();
                var roleShort = r.Value;

                if (String.Compare(role, roleName, true) == 0 || String.Compare(role, roleShort, true) == 0)
                {
                    Utils.ShowRoleInfoLong(r.Key, PlayerId);
                    return;
                }
            }
            Utils.SendMessageAutoSplit(GetString("Message.HelpRoleNone"), true, sendTo: PlayerId);
        }
        private static void InitRoleCommands()
        {
            // 初回のみ処理
            if (roleCommands == null)
            {
#pragma warning disable IDE0028  // Dictionary初期化の簡素化をしない
                roleCommands = new Dictionary<CustomRoles, string>();

                // GM
                roleCommands.Add(CustomRoles.GM, "ゲームマスター");

                // Impostor役職
                roleCommands.Add((CustomRoles)(-1), $"== {GetString("Impostor")} ==");  // 区切り用
                ConcatCommands(CustomRoleTypes.Impostor);

                // Madmate役職
                roleCommands.Add((CustomRoles)(-2), $"== {GetString("Madmate")} ==");  // 区切り用
                ConcatCommands(CustomRoleTypes.Madmate);
                roleCommands.Add(CustomRoles.SKMadmate, "サイドキックマッドメイト");

                // Crewmate役職
                roleCommands.Add((CustomRoles)(-3), $"== {GetString("Crewmate")} ==");  // 区切り用
                ConcatCommands(CustomRoleTypes.Crewmate);

                // Neutral役職
                roleCommands.Add((CustomRoles)(-4), $"== {GetString("Neutral")} ==");  // 区切り用
                ConcatCommands(CustomRoleTypes.Neutral);

                // ユニット役職
                roleCommands.Add((CustomRoles)(-7), $"== {GetString("UnitRoles")} ==");  // 区切り用
                ConcatCommands(CustomRoleTypes.Unit);

                // 属性
                roleCommands.Add((CustomRoles)(-5), $"== {GetString("Addons")} ==");  // 区切り用
                foreach (var info in TownOfHostL.Roles.AddOns.AddOnList.All)
                    if (info.ChatCommand != null) roleCommands.Add(info.Role, info.ChatCommand);

                // HAS
                roleCommands.Add((CustomRoles)(-6), $"== {GetString("HideAndSeek")} ==");  // 区切り用
                roleCommands.Add(CustomRoles.HASFox, "hfo");
                roleCommands.Add(CustomRoles.HASTroll, "htr");
#pragma warning restore IDE0028
            }
        }
        private static void ConcatCommands(CustomRoleTypes roleType)
        {
            var roles = CustomRoleManager.AllRolesInfo.Values.Where(role => role.CustomRoleType == roleType);
            foreach (var role in roles)
            {
                if (role.ChatCommand is null) continue;

                roleCommands[role.RoleName] = role.ChatCommand;
            }
        }
        /// <summary>コマンドに /cmd が付いていないとき、送った本人にだけ使い方を教える</summary>
        private static void SendCommandFailedMessage(PlayerControl player)
        {
            if (player == null) return;
            var message = GetString("Error.CommandFailed");


            if (player.AmOwner)
            {
                if (HudManager.InstanceExists) HudManager.Instance.Chat?.AddChat(player, message);
                return;
            }
            if (!AmongUsClient.Instance.AmHost) return;
            Utils.SendMessageAutoSplit(message, true, sendTo: player.PlayerId);
        }
        // 客 1 人あたり、返事は ReplyInterval 秒に 1 回まで。連打の返事が溜まると、同じ宛先への送信が続いて公式サーバーに切られる恐れがある
        private const float ReplyInterval = 2f;
        private static readonly Dictionary<byte, float> lastReplyAt = new();
        private static bool TryTakeReplySlot(PlayerControl player)
        {
            if (player == null || player.AmOwner) return true;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (lastReplyAt.TryGetValue(player.PlayerId, out var last) && now - last < ReplyInterval) return false;
            lastReplyAt[player.PlayerId] = now;
            return true;
        }
        public static void OnReceiveChat(PlayerControl player, string text)
        {
            if (player != null)
            {
                var tag = !player.Data.IsDead ? "SendChatAlive" : "SendChatDead";
                VoiceReader.Read(text, Palette.GetColorName(player.Data.DefaultOutfit.ColorId), tag);
            }

            if (!AmongUsClient.Instance.AmHost) return;

            bool isNimrodMeeting = Nimrod.IsExecutionMeeting() || MadNimrod.IsExecutionMeeting();
            // 返事をしない発言はここで終わる
            if (!isNimrodMeeting && !text.StartsWith("/")) return;
            if (!TryTakeReplySlot(player))
            {
                Logger.Info($"{player?.Data?.PlayerName} の発言は前の返事から {ReplyInterval} 秒以内なので返事をしません", "OnReceiveChat");
                return;
            }

            // ニムロッド会議中
            if (isNimrodMeeting)
            {
                if (text.Length > 0)
                {
                    Utils.SendMessageAutoSplit(GetString("Message.NowNimrodMeeting"),
                        true, $"<color={Utils.GetRoleColorCode(CustomRoles.Nimrod)}>{GetString("IsNimrodMeetingTitle")}</color>");
                }
            }

            string[] args = text.Split(' ');

            if (args[0] != "/cmd" || args.Length <= 1)
            {
                if (args[0].StartsWith("/") && args[0] != "/cmd") SendCommandFailedMessage(player);
                return;
            }
            args = args.Skip(1).ToArray();
            if (args[0].StartsWith("/") is false) args[0] = $"/{args[0]}";

            ChatCommandTable.Find(args[0])?.Remote?.Invoke(new ChatCommandContext(args, player, null));
        }
    }
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Update))]
    class ChatUpdatePatch
    {
        public static bool DoBlockChat = false;
        public static void Postfix(ChatController __instance)
        {
            if (!AmongUsClient.Instance.AmHost || Main.MessagesToSend.Count < 1) return;
            // 個別宛てでも最低 0.1 秒は空ける (毎フレーム連射しない)
            float wait = Main.MessagesToSend[0].Item2 == byte.MaxValue ? Main.MessageWait.Value : 0.1f;
            if (wait > __instance.timeSinceLastMessage) return;
            if (DoBlockChat) return;
            var player = PlayerControl.LocalPlayer;
            if (player == null) return;
            (string msg, byte sendTo, string title, bool custom) = Main.MessagesToSend[0];
            Main.MessagesToSend.RemoveAt(0);

            if (custom)
            {
                SendCustomChat(msg, sendTo: sendTo);
                __instance.timeSinceLastMessage = 0f;
                return;
            }

            int clientId = sendTo == byte.MaxValue ? -1 : Utils.GetPlayerById(sendTo).GetClientId();
            var name = player.Data.PlayerName;
            if (clientId == -1)
            {
                player.SetName(title);
                AddChatAsAlive(player, msg);
                player.SetName(name);
            }

            if (player.Data.IsDead && GameStates.IsInGame)
            {
                SendMessageAsDeadHost(player, msg, title, sendTo);
            }
            else
            {
                var writer = CustomRpcSender.Create("MessagesToSend", SendOption.None);
                writer.StartMessage(clientId);
                writer.StartRpc(player.NetId, (byte)RpcCalls.SetName)
                    .Write(player.Data.NetId)
                    .Write(title)
                    .EndRpc();
                writer.StartRpc(player.NetId, (byte)RpcCalls.SendChat)
                    .Write(msg)
                    .EndRpc();
                var restoreName = TownOfHostL.Core.ChatSenderName.RestoreFor(player);
                writer.StartRpc(player.NetId, (byte)RpcCalls.SetName)
                    .Write(player.Data.NetId)
                    .Write(restoreName)
                    .EndRpc();
                writer.EndMessage();
                writer.SendMessage();
                TownOfHostL.Core.ChatSenderName.Restored(player, restoreName, clientId);
            }
            __instance.timeSinceLastMessage = 0f;
        }
        public static void SendCustomChat(string SendName, PlayerControl sender = null, byte sendTo = byte.MaxValue)
        {
            Logger.Info($"sender: {sender?.name}, sendTo: {sendTo}", "SendCustomChat");
            string command = "\n\n";
            sender = PlayerControl.LocalPlayer;
            string name = sender.Data?.PlayerName;
            int clientId = sendTo == byte.MaxValue ? -1 : Utils.GetPlayerById(sendTo).GetClientId();
            if (clientId == -1)
            {
                sender.SetName(SendName);
                AddChatAsAlive(sender, command);
                sender.SetName(name);
            }

            if (sender.Data.IsDead && GameStates.IsInGame)
            {
                SendMessageAsDeadHost(sender, command, SendName, sendTo);
            }
            else
            {
                var writer = CustomRpcSender.Create("CustomSend");
                var restoreName = TownOfHostL.Core.ChatSenderName.RestoreFor(sender);
                writer.StartMessage(clientId);
                writer.StartRpc(sender.NetId, (byte)RpcCalls.SetName)
                    .Write(sender.Data.NetId)
                    .Write(SendName)
                    .EndRpc()
                    .StartRpc(sender.NetId, (byte)RpcCalls.SendChat)
                    .Write(command)
                    .EndRpc()
                    .StartRpc(sender.NetId, (byte)RpcCalls.SetName)
                    .Write(sender.Data.NetId)
                    .Write(restoreName)
                    .EndRpc()
                    .EndMessage()
                    .SendMessage();
                TownOfHostL.Core.ChatSenderName.Restored(sender, restoreName, clientId);
            }
        }
        private static void AddChatAsAlive(PlayerControl player, string msg)
        {
            var wasDead = player.Data.IsDead;
            if (wasDead) player.Data.IsDead = false;
            DestroyableSingleton<HudManager>.Instance.Chat.AddChat(player, msg);
            if (wasDead) player.Data.IsDead = true;
        }
        private static void SendMessageAsDeadHost(PlayerControl host, string msg, string title, byte sendTo)
        {
            var targets = sendTo == byte.MaxValue
                ? Main.AllPlayerControls.Where(pc => pc.PlayerId != host.PlayerId)
                : new[] { Utils.GetPlayerById(sendTo) };

            GameDataSerializePatch.SerializeMessageCount++;
            try
            {
                foreach (var target in targets)
                {
                    if (target == null) continue;
                    int targetClientId = target.GetClientId();
                    if (targetClientId == -1) continue;

                    var writer = CustomRpcSender.Create("DeadHostChat", SendOption.None);
                    writer.StartMessage(targetClientId);

                    if (target.IsAlive())
                    {
                        host.Data.IsDead = false;
                        writer.stream.StartMessage(1);
                        writer.stream.WritePacked(host.Data.NetId);
                        host.Data.Serialize(writer.stream, false);
                        writer.stream.EndMessage();
                    }

                    writer.StartRpc(host.NetId, (byte)RpcCalls.SetName)
                        .Write(host.Data.NetId)
                        .Write(title)
                        .EndRpc();
                    writer.StartRpc(host.NetId, (byte)RpcCalls.SendChat)
                        .Write(msg)
                        .EndRpc();
                    var restoreName = TownOfHostL.Core.ChatSenderName.RestoreFor(host);
                    writer.StartRpc(host.NetId, (byte)RpcCalls.SetName)
                        .Write(host.Data.NetId)
                        .Write(restoreName)
                        .EndRpc();

                    if (target.IsAlive())
                    {
                        host.Data.IsDead = true;
                        writer.stream.StartMessage(1);
                        writer.stream.WritePacked(host.Data.NetId);
                        host.Data.Serialize(writer.stream, false);
                        writer.stream.EndMessage();
                    }

                    writer.EndMessage();
                    writer.SendMessage();
                    TownOfHostL.Core.ChatSenderName.Restored(host, restoreName, targetClientId);
                }
            }
            finally
            {
                GameDataSerializePatch.SerializeMessageCount--;
            }
        }
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    class AddChatPatch
    {
        public static bool Prefix(string chatText)
        {
            if (chatText != null && chatText.TrimStart('\n', '\r', ' ').StartsWith("/cmd")) return false;
            return true;
        }
        public static void Postfix(string chatText)
        {
            switch (chatText)
            {
                default:
                    break;
            }
            if (!AmongUsClient.Instance.AmHost) return;
        }
    }
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChat))]
    class RpcSendChatPatch
    {
        public static bool Prefix(PlayerControl __instance, string chatText, ref bool __result)
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(chatText))
            {
                __result = false;
                return false;
            }

            // Adjust for local name line feeds (guard for nulls)
            var localName = PlayerControl.LocalPlayer != null ? PlayerControl.LocalPlayer.name : string.Empty;
            int return_count = 0;
            if (!string.IsNullOrEmpty(localName))
            {
                return_count = localName.Count(x => x == '\n');
            }
            if (!chatText.StartsWith("/cmd"))
            {
                chatText = new StringBuilder(chatText).Insert(0, "\n", return_count).ToString();
            }

            // Local echo (safe guards)
            if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmClient && DestroyableSingleton<HudManager>.InstanceExists)
            {
                DestroyableSingleton<HudManager>.Instance.Chat.AddChat(__instance, chatText);
            }

            // Optional telemetry (guard instance)
            try
            {
                if (chatText.IndexOf("who", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (DestroyableSingleton<UnityTelemetry>.InstanceExists) DestroyableSingleton<UnityTelemetry>.Instance.SendWho();
                }
            }
            catch { /* ignore telemetry issues */ }

            // Send chat RPC using public API
            try
            {
                var writer = AmongUsClient.Instance.StartRpcImmediately(__instance.NetId, (byte)RpcCalls.SendChat, SendOption.None, -1);
                writer.Write(chatText);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
                __result = true;
            }
            catch (System.Exception ex)
            {
                Logger.Error($"RpcSendChat failed: {ex.Message}", nameof(RpcSendChatPatch));
                __result = false;
            }
            return false;
        }
    }
}