using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Assets.CoreScripts;
using HarmonyLib;
using Hazel;

using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.Madmate;
using static TownOfHostL.Translator;

namespace TownOfHostL
{
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
    class ChatCommands
    {
        private static Dictionary<CustomRoles, string> roleCommands;

        /// <summary>
        /// 先頭の /cmd を外す。/cmd は本編のサーバーが送った人とホストにしか届けない書き方で、付けると他の人に見えない。
        /// 「/cmd n」「/cmd /n」のどちらも「/n」にする。外したら true
        /// </summary>
        public static bool StripCmd(ref string[] args)
        {
            if (args.Length < 2 || !args[0].Equals("/cmd", StringComparison.OrdinalIgnoreCase)) return false;
            args = args.Skip(1).ToArray();
            if (!args[0].StartsWith('/')) args[0] = "/" + args[0];
            return true;
        }

        public static bool Prefix(ChatController __instance)
        {
            if (__instance.freeChatField.textArea.text == "") return __instance.quickChatField.Visible;
            __instance.timeSinceLastMessage = 3f;
            var text = __instance.freeChatField.textArea.text;
            ChatInput.Remember(text);
            string[] args = text.Split(' ');
            var canceled = false;
            var cancelVal = "";
            Main.isChatCommand = true;
            Logger.Info(text, "SendChat");

            if (StripCmd(ref args)) text = string.Join(" ", args);

            var tag = !PlayerControl.LocalPlayer.Data.IsDead ? "SendChatHost" : "SendChatDeadHost";
            if (text.StartsWith("試合結果:") || text.StartsWith("キル履歴:")) tag = "SendSystemChat";
            VoiceReader.ReadHost(text, tag);

            var command = ChatCommandTable.Find(args[0]);
            // ホストでない時、ホストで動くコマンドは /cmd を付けて送る (付けると他の人に見えない)
            if (!AmongUsClient.Instance.AmHost && command?.Local == null)
            {
                Main.isChatCommand = false;
                if (command?.Remote != null && !__instance.freeChatField.textArea.text.StartsWith("/cmd", StringComparison.OrdinalIgnoreCase))
                {
                    var cmdText = $"/cmd {text}";
                    ChatInput.SetText(__instance.freeChatField.textArea, cmdText, cmdText.Length);
                }
                return true;
            }
            if (AmongUsClient.Instance.AmHost && command == null) ChatPushOut.Record(PlayerControl.LocalPlayer, text);

            // 自分の画面だけで動くコマンド (ホストでなくても使える)
            var ctx = new ChatCommandContext(args, PlayerControl.LocalPlayer, __instance);
            var local = command?.Local;
            local?.Invoke(ctx);
            canceled = local != null;
            Main.isChatCommand = local != null;
            if (AmongUsClient.Instance.AmHost)
            {
                var host = command?.Host;
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

        /// <summary>
        /// /sr (/setrole) ID 役職: 次の試合の役職を予約する。ID の代わりに名前でもよい (ホスト専用・ロビーだけ)。/sr list で一覧、/sr clear で解除。
        /// 返事はホストの画面にだけ出す (予約した役職を他の人に見せない)。
        /// </summary>
        internal static void SelectRolesCommand(string[] args)
        {
            static void Reply(string text) => HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, text);

            if (!GameStates.IsLobby)
            {
                Reply(GetString("Message.SelectRolesLobbyOnly"));
                return;
            }

            if (args.Length >= 2 && String.Compare(args[1], "clear", true) == 0)
            {
                Modules.SelectRoles.Clear();
                Reply(GetString("Message.SelectRolesCleared"));
                return;
            }
            if (args.Length == 2 && String.Compare(args[1], "list", true) == 0)
            {
                var lines = Modules.SelectRoles.Reserved
                    .Select(p => (pc: Utils.GetPlayerById(p.Key), role: p.Value))
                    .Where(p => p.pc != null)
                    .Select(p => $"{p.pc.PlayerId}: {p.pc.Data.PlayerName}: {Utils.ColorString(Utils.GetRoleColor(p.role), Utils.GetRoleName(p.role))}");
                var text = string.Join('\n', lines);
                Reply(text == "" ? GetString("Message.SelectRolesNone") : text);
                return;
            }
            if (args.Length < 3)
            {
                Reply(GetString("Message.SelectRolesUsage"));
                return;
            }

            // 相手は ID が基本 (/id で一覧)。数字でない時だけ、最後の語を役職・その前を名前として探す
            PlayerControl target;
            string roleArg;
            if (byte.TryParse(args[1], out var id))
            {
                target = Utils.GetPlayerById(id);
                roleArg = string.Join(" ", args[2..]);
                if (target == null)
                {
                    Reply(string.Format(GetString("Message.SelectRolesNoPlayer"), args[1]));
                    return;
                }
            }
            else
            {
                roleArg = args[^1];
                var nameArg = string.Join(" ", args[1..^1]);
                target = FindPlayer(nameArg);
                if (target == null)
                {
                    Reply(string.Format(GetString("Message.SelectRolesNoPlayer"), nameArg));
                    return;
                }
            }

            if (!TryGetRoleByName(roleArg, out var role, out var candidates))
            {
                Reply(candidates.Count > 1
                    ? string.Format(GetString("Message.RoleAmbiguous"), roleArg, RoleCandidates(candidates))
                    : string.Format(GetString("Message.SelectRolesNoRole"), roleArg));
                return;
            }
            if (role.IsAddOn() || role.IsSpectator())
            {
                Reply(string.Format(GetString("Message.SelectRolesNotMainRole"), Utils.GetRoleName(role)));
                return;
            }

            Modules.SelectRoles.Set(target.PlayerId, role);
            Reply(string.Format(GetString("Message.SelectRolesSet"),
                target.Data.PlayerName, Utils.ColorString(Utils.GetRoleColor(role), Utils.GetRoleName(role))));
        }
        /// <summary>
        /// 名前でプレイヤーを探す。完全に一致する人を先に見て、無ければ名前の一部が 1 人だけに当たる時にその人を返す。
        /// <paramref name="exactOnly"/> なら部分一致は使わない。
        /// </summary>
        internal static PlayerControl FindPlayer(string name, bool exactOnly = false)
        {
            var exact = Main.AllPlayerControls.FirstOrDefault(pc => String.Compare(pc?.Data?.PlayerName, name, true) == 0);
            if (exact != null || exactOnly) return exact;
            var partial = Main.AllPlayerControls.Where(pc => pc?.Data?.PlayerName?.Contains(name, StringComparison.OrdinalIgnoreCase) == true).Take(2).ToList();
            return partial.Count == 1 ? partial[0] : null;
        }

        /// <summary>
        /// /kick 相手・/ban 相手: 相手は ID の数字か名前 (/ban は名前の完全一致だけ)。BAN は本編の BAN と同じく BanList に残る (ClientPatch の KickPlayer の前置き)。
        /// 返事はホストの画面にだけ出す。
        /// </summary>
        internal static void KickCommand(string[] args, bool ban)
        {
            static void Reply(string text) => HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, text);
            if (args.Length < 2)
            {
                Reply(GetString(ban ? "Message.BanUsage" : "Message.KickUsage"));
                return;
            }
            var arg = string.Join(" ", args[1..]);
            var target = byte.TryParse(arg, out var id) ? Utils.GetPlayerById(id) : null;
            target ??= FindPlayer(arg, exactOnly: ban);
            if (target == null || target.AmOwner)
            {
                Reply(string.Format(GetString("Message.SelectRolesNoPlayer"), arg));
                return;
            }
            var client = target.GetClient();
            if (client == null)
            {
                Reply(string.Format(GetString("Message.SelectRolesNoPlayer"), arg));
                return;
            }
            Logger.Info($"{(ban ? "BAN" : "キック")}: {target.Data?.PlayerName}", "KickCommand");
            AmongUsClient.Instance.KickPlayer(client.Id, ban);
        }

        /// <summary>
        /// 入力に当たる役職を探す。内部名・短縮名・表示名 (今の言語・日本語・英語) のどれかと完全に一致する物があればそれだけを返し、
        /// 無ければ表示名か内部名に入力を含む物をすべて返す (「波動砲」→ 波動砲シェリフ・波動砲ジャッカル)
        /// </summary>
        public static List<CustomRoles> FindRoles(string input)
        {
            InitRoleCommands();
            var result = new List<CustomRoles>();
            input = input?.Trim();
            if (string.IsNullOrEmpty(input)) return result;

            IEnumerable<string> Names(KeyValuePair<CustomRoles, string> r)
            {
                var key = r.Key.ToString();
                yield return key;
                yield return r.Value;
                if (!HasString(key)) yield break;
                yield return GetString(key);
                yield return GetString(key, SupportedLangs.Japanese);
                yield return GetString(key, SupportedLangs.English);
            }
            var roles = roleCommands.Where(r => (int)r.Key >= 0).ToList();
            foreach (var r in roles)
                if (Names(r).Any(n => String.Compare(input, n, true) == 0)) result.Add(r.Key);
            if (result.Count > 0) return result;
            foreach (var r in roles)
                if (Names(r).Any(n => n != null && n.Contains(input, StringComparison.OrdinalIgnoreCase))) result.Add(r.Key);
            return result;
        }
        /// <summary>当たる役職が 1 つだけの時に true。<paramref name="candidates"/> には当たった物をすべて入れる</summary>
        public static bool TryGetRoleByName(string input, out CustomRoles role, out List<CustomRoles> candidates)
        {
            candidates = FindRoles(input);
            role = candidates.Count == 1 ? candidates[0] : CustomRoles.NotAssigned;
            return candidates.Count == 1;
        }
        public static bool TryGetRoleByName(string input, out CustomRoles role) => TryGetRoleByName(input, out role, out _);

        /// <summary>候補の役職の名前を色付きで並べる (多すぎる時は先頭だけ)</summary>
        internal static string RoleCandidates(List<CustomRoles> roles)
        {
            const int Max = 15;
            var text = string.Join(", ", roles.Take(Max).Select(r => Utils.ColorString(Utils.GetRoleColor(r), Utils.GetRoleName(r))));
            return roles.Count > Max ? text + ", …" : text;
        }
        public static void GetRolesInfo(string role, byte PlayerId = byte.MaxValue)
        {
            var found = FindRoles(role);
            if (found.Count == 1)
                Utils.ShowRoleInfoLong(found[0], PlayerId);
            else if (found.Count > 1)
                Utils.SendMessageAutoSplit(string.Format(GetString("Message.RoleAmbiguous"), role, RoleCandidates(found)), true, sendTo: PlayerId);
            else
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
                    roleCommands.TryAdd(info.Role, info.ChatCommand ?? "");  // 短縮名の無い物も表示名で引けるように入れる

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
                // 短縮名の無い役職も表示名で引けるように入れる
                roleCommands[role.RoleName] = role.ChatCommand ?? "";
            }
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
            var guard = ChatGuard.OnChat(player, text);
            if (guard == ChatGuard.Result.Kicked) return;

            string[] args = text.Trim().Split(' ');
            bool viaCmd = StripCmd(ref args);
            var command = ChatCommandTable.Find(args[0]);
            // 注意を受けた書き込み (禁止語など) は送り直さない
            if (command == null && guard == ChatGuard.Result.None) ChatPushOut.Record(player, text);

            // 見せたくないコマンドを /cmd 無しで送られた時は、本編がもう全員に配っているので、返事より先に空白で押し出す
            // (後に押し出すと、返事まで画面の上へ消える)。返事の間隔で止めた時も押し出す
            bool pushedOut = command?.Remote != null && command.Hidden && !viaCmd;
            if (pushedOut) ChatPushOut.PushOut();

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

            if (command?.Remote == null) return;
            if (pushedOut) Utils.SendMessageAutoSplit(GetString("Message.UseCmdToHide"), true, sendTo: player.PlayerId);
            command.Remote(new ChatCommandContext(args, player, null));
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

            var target = sendTo == byte.MaxValue ? null : Utils.GetPlayerById(sendTo);
            if (sendTo != byte.MaxValue && target == null) { __instance.timeSinceLastMessage = 0f; return; }
            int clientId = target == null ? -1 : target.GetClientId();
            var name = player.Data.PlayerName;
            // ホスト宛ては自分の画面に足すだけ (送信待ちの順番どおりに出す)
            if (target != null && target.AmOwner)
            {
                player.SetName(title);
                AddChatAsAlive(player, msg);
                player.SetName(name);
                __instance.timeSinceLastMessage = 0f;
                return;
            }
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
            var target = sendTo == byte.MaxValue ? null : Utils.GetPlayerById(sendTo);
            if (sendTo != byte.MaxValue && target == null) return;
            int clientId = target == null ? -1 : target.GetClientId();
            // ホスト宛ては自分の画面に足すだけ (送信待ちの順番どおりに出す)
            if (clientId == -1 || target.AmOwner)
            {
                sender.SetName(SendName);
                AddChatAsAlive(sender, command);
                sender.SetName(name);
                if (target != null) return;
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