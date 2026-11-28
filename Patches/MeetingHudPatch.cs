using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;
using TownOfHostL.Modules;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Neutral;
using UnityEngine;
using static TownOfHostL.Translator;
using TownOfHostL.Core.Modes;
using TownOfHostL.Core;

namespace TownOfHostL;

[HarmonyPatch]
public static partial class MeetingHudPatch
{
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CheckForEndVoting))]
    class CheckForEndVotingPatch
    {
        public static bool Prefix()
        {
            if (!AmongUsClient.Instance.AmHost) return true;
            MeetingVoteManager.Instance?.CheckAndEndMeeting();
            return false;
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CastVote))]
    public static class CastVotePatch
    {
        public static bool Prefix(MeetingHud __instance, [HarmonyArgument(0)] PlayerId srcPlayerId /* 投票した人 */ , [HarmonyArgument(1)] PlayerId suspectPlayerId /* 投票された人 */ )
        {
            var voter = Utils.GetPlayerById(srcPlayerId);
            var voted = Utils.GetPlayerById(suspectPlayerId);
            if (voter == null) return false;
            // 締めた後 (結果の表示の間) に遅れて届いた票で、役職の能力 (ゲッサーの撃ちなど) を走らせない
            if (AmongUsClient.Instance.AmHost && __instance.state is MeetingHud.MeetingStates.Results or MeetingHud.MeetingStates.Proceeding) return false;
            // 初手会議で能力を使えない時は、誰に入れても飛ばしとして数える
            if (AmongUsClient.Instance.AmHost && FirstTurnMeeting.CantAbilityNow)
            {
                MeetingVoteManager.Instance?.SetVote(srcPlayerId, MeetingVoteManager.Skip);
                return true;
            }
            if (voter.GetRoleClass()?.CheckVoteAsVoter(voted) == false)
            {
                __instance.RpcClearVote(voter.PlayerId);
                Logger.Info($"{voter.GetNameWithRole()} は投票しない", nameof(CastVotePatch));
                return false;
            }
            // 全員の役職に投票を見せる (天秤などは他の人の投票も止める)
            if (!RoleHooks.CheckAnyVote(srcPlayerId, suspectPlayerId, voter))
            {
                __instance.RpcClearVote(voter.PlayerId);
                Logger.Info($"{voter.GetNameWithRole()} は投票しない！ => {suspectPlayerId}", nameof(CastVotePatch));
                return false;
            }
            // 自分に投票して能力を使う役職
            if (voter.GetRoleClass() is TownOfHostL.Roles.Core.Interfaces.ISelfVoter selfVoter && selfVoter.CanUseVoted())
            {
                MeetingVoteManager.Instance?.SetVote(srcPlayerId, suspectPlayerId, isoverride: false);
                __instance.RpcClearVote(voter.PlayerId);
                Utils.SendMessage(Translator.GetString("SelfVoterCancelMessage"), voter.PlayerId);
                return true;
            }

            MeetingVoteManager.Instance?.SetVote(srcPlayerId, suspectPlayerId);
            return true;
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.SetJudgeOverrule))]
    public static class SetJudgeOverrulePatch
    {
        public static void ShowOverruleLocally(ushort overruleNonce)
        {
            var meetingHud = MeetingHud.Instance;
            if (meetingHud == null) return;

            bool anotherJudgeBeatYouToIt = false;
            var judgeRole = PlayerControl.LocalPlayer?.Data?.Role?.TryCast<JudgeRole>();
            if (judgeRole != null && judgeRole.HasAlreadyOverruledThisMeeting)
            {
                if (judgeRole.OverruleNonce == overruleNonce) judgeRole.ConsumeOverruleVotesUsage();
                else anotherJudgeBeatYouToIt = true;
            }
            meetingHud.ShowJudgeOverrule(anotherJudgeBeatYouToIt);
        }

        public static bool Prefix(MeetingHud __instance,
            [HarmonyArgument(0)] PlayerId judgePlayerId /* 裁決した人 */,
            [HarmonyArgument(1)] PlayerId targetPlayerId /* 裁決された人 */,
            [HarmonyArgument(2)] ushort overruleNonce)
        {
            if (!AmongUsClient.Instance.AmHost) return false;

            var voter = Utils.GetPlayerById(judgePlayerId);
            var votefor = Utils.GetPlayerById(targetPlayerId);
            if (voter == null || votefor == null)
            {
                Logger.Warn($"裁決者({judgePlayerId})か対象({targetPlayerId})が見つかりません", nameof(SetJudgeOverrulePatch));
                return false;
            }
            Logger.Info($"{voter.GetNameWithRole()} => {votefor.GetNameWithRole()} (nonce: {overruleNonce})", nameof(SetJudgeOverrulePatch));

            if (MeetingVoteManager.Instance != null && MeetingVoteManager.Instance.AllVotes.Values.Any(v => v.IsJudgeOverrule))
            {
                __instance.RpcClearVote(voter.PlayerId);
                return false;
            }

            byte exilePlayerId = byte.MaxValue;
            if (voter.GetRoleClass()?.CallJudgeVote(voter, votefor, ref exilePlayerId) != true
                || exilePlayerId == byte.MaxValue)
            {
                __instance.RpcClearVote(voter.PlayerId);
                Logger.Info($"{voter.GetNameWithRole()} の裁決は無効化されました", nameof(SetJudgeOverrulePatch));
                return false;
            }

            MeetingVoteManager.Instance?.SetVote(
                judgePlayerId, targetPlayerId,
                judgeExiledId: exilePlayerId, judgeNonce: overruleNonce);
            return false;
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    class StartPatch
    {
        public static void Prefix()
        {
            Logger.Info("------------会議開始------------", "Phase");
            VentilationSystemPatch.ClearVent();
            ChatUpdatePatch.DoBlockChat = true;
            GameStates.AlreadyDied |= !Utils.IsAllAlive;
            // ここで例外が出ると本編の MeetingHud.Start も Postfix の投票の管理も走らず、会議が終わらない
            try
            {
                Main.AllPlayerControls.Do(x => { if (ReportDeadBodyPatch.WaitReport.TryGetValue(x.PlayerId, out var wait)) wait.Clear(); });
                ChainShifterAddon.OnStartMeeting();
                foreach (var tm in Main.AllAlivePlayerControls.Where(TownOfHostL.Core.PlayerTraits.RefreshesNameOnMeetingStart))
                    Utils.NotifyRoles(true, tm);
                TargetDeadArrow.OnStartMeeting();

                Sending.OnStartMeeting();

                // 前の会議で追放されたリヴェラーの役職を次の会議(今)から公開
                Revealer.OnStartMeeting();
            }
            catch (System.Exception ex) { Logger.Exception(ex, "MeetingHud.Start.Prefix"); }
        }
        public static void Postfix(MeetingHud __instance)
        {
            MeetingStates.BeginMeeting();
            MeetingVoteManager.Start();

            SoundManager.Instance.ChangeAmbienceVolume(0f);
            if (!GameStates.IsModHost) return;
            // 試合中に残った書き込みの押し出しは、会議の知らせより前に積む
            try { ChatPushOut.OnMeetingStart(); }
            catch (System.Exception ex) { Logger.Exception(ex, "ChatPushOut.OnMeetingStart"); }
            // 名前の欄の組み立ての例外で、下の役職の会議の始めの処理と知らせの送信を飛ばさない
            try
            {
            foreach (var pva in __instance.playerStates)
            {
                var pc = Utils.GetPlayerById(pva.PlayerId);
                if (pc == null) continue;
                foreach (var name in new[] { "RoleTextMeeting", "SuffixTextMeeting" })
                {
                    var old = pva.PlayerIcon.transform.Find(name);
                    if (old != null) Object.Destroy(old.gameObject);
                }

                var roleTextMeeting = Object.Instantiate(pva.NameText);
                var suffixTextMeeting = Object.Instantiate(pva.NameText);
                roleTextMeeting.transform.SetParent(pva.PlayerIcon.transform);
                suffixTextMeeting.transform.SetParent(pva.PlayerIcon.transform);

                roleTextMeeting.transform.localPosition = new Vector3(3.25f, 1.02f, -5f);
                roleTextMeeting.fontSize = 1.5f;
                roleTextMeeting.gameObject.name = "RoleTextMeeting";
                roleTextMeeting.enableWordWrapping = false;
                (roleTextMeeting.enabled, roleTextMeeting.text)
                    = Utils.GetRoleNameAndProgressTextData(true, PlayerControl.LocalPlayer, pc);


                suffixTextMeeting.transform.localPosition = new Vector3(3.25f, 0.02f, 0f);
                suffixTextMeeting.fontSize = 1.5f;
                suffixTextMeeting.gameObject.name = "SuffixTextMeeting";
                suffixTextMeeting.enableWordWrapping = false;
                suffixTextMeeting.enabled = false;
                suffixTextMeeting.text = "";

                // シンクロカラーモード
                if (Options.IsSyncColorMode && Options.SCM_NothingMeetingNameColor.GetBool()
                    && PlayerControl.LocalPlayer.IsAlive())
                {
                    roleTextMeeting.enabled = false;
                    continue;
                }

                var suffixBuilder = new StringBuilder(32);
                TownOfHostL.Core.NameParts.AppendSuffix(suffixBuilder, PlayerControl.LocalPlayer, pc, isMeeting: true);

                if (suffixBuilder.Length > 0)
                {
                    suffixTextMeeting.text = suffixBuilder.ToString();
                    suffixTextMeeting.enabled = true;
                }
                // 役職/属性テキストは NameText の外(PlayerIcon側)に置いているため、
                // 以前のように NameText 自体をずらす必要はない
            }
            }
            catch (System.Exception ex) { Logger.Exception(ex, "MeetingHud.Start.Postfix.Names"); }
            RoleHooks.ForEachRole(role => role.OnStartMeeting(), "OnStartMeeting");
            // 役職が OnStartMeeting で積んだ知らせを送るので、その後に呼ぶ
            RoleHooks.OnMeetingStart();

            List<string> messageList = new();

            if (Options.SyncButtonMode.GetBool())
            {
                messageList.Add(string.Format(GetString("Message.SyncButtonLeft"), Options.SyncedButtonCount.GetFloat() - Options.UsedButtonCount));
                Logger.Info("緊急会議ボタンはあと" + (Options.SyncedButtonCount.GetFloat() - Options.UsedButtonCount) + "回使用可能です。", "SyncButtonMode");
            }
            if (Options.ShowReportReason.GetBool())
            {
                if (ReportDeadBodyPatch.ReportTarget == null)
                {
                    messageList.Add(GetString("Message.isButton"));
                }
                else if (!ReportDeadBodyPatch.SpecialMeeting)
                {
                    var playerName = Utils.GetPlayerById(ReportDeadBodyPatch.ReportTarget.PlayerId).GetRealName(true);
                    messageList.Add(string.Format(GetString("Message.isReport"),
                        $"{playerName}{ReportDeadBodyPatch.ReportTarget.ColorName.Color(ReportDeadBodyPatch.ReportTarget.Color)}"));
                }
            }
            if (Options.ShowRevengeTarget.GetBool())
            {
                foreach (var Exiled_Target in RevengeTargetPlayer)
                {
                    messageList.Add(string.Format(GetString("Message.RevengeText"),
                        $"{Exiled_Target.exiled.PlayerName}{Exiled_Target.exiled.ColorName.Color(Exiled_Target.exiled.Color)}", $"{Exiled_Target.revengeTarget.PlayerName}{Exiled_Target.revengeTarget.ColorName.Color(Exiled_Target.revengeTarget.Color)}"));
                }
            }

            if (AntiBlackout.OverrideExiledPlayer && !ModeCatalog.IsCatchCat)
            {
                messageList.Add(GetString("Warning.OverrideExiledPlayer"));
            }
            if (ModeCatalog.IsCatchCat)
            {
                CatchCat.Infomation.ShowMeeting();
            }

            if (messageList.Count > 0)
            {
                string formatTag = "<size=70%><line-height=1.6pic>";

                var message = string.Join("\n", messageList);
                Utils.SendMessageAutoSplit($"{formatTag}{message}", true, formatTag: formatTag);
            }

            if (MeetingStates.FirstMeeting) TemplateManager.SendTemplate("OnFirstMeeting", noErr: true);
            {
                TemplateManager.SendTemplate("OnMeeting", noErr: true);
            }

            if (AmongUsClient.Instance.AmHost)
            {
                _ = new LateTask(() =>
                {
                    if (!GameStates.IsMeeting)
                    {
                        ChatUpdatePatch.DoBlockChat = false;
                        return;
                    }
                    foreach (var seen in Main.AllPlayerControls)
                    {
                        var seenName = seen.GetRealName(isMeeting: true);
                        var coloredName = Utils.ColorString(seen.GetRoleColor(true), seenName);
                        foreach (var seer in Main.AllPlayerControls)
                        {
                            // 他の人の名前も色を書いて送る (インポスターの仲間は役職の色、それ以外は白)。
                            // 何も書かないと、本編の客の画面で会議の名前が赤く見えることがある (初手強制会議など)
                            seen.RpcSetNamePrivate(
                                seer == seen ? coloredName : seenName.ApplyNameColorData(seer, seen, true),
                                true,
                                seer);
                        }
                    }

                    foreach (var pc in Main.AllPlayerControls)
                    {
                        if (!Main.ShowRoleInfoAtMeeting.Contains(pc.PlayerId)) continue;

                        Utils.ShowMyRoleInfo(pc, pc.PlayerId);
                        Main.ShowRoleInfoAtMeeting.Remove(pc.PlayerId);
                    }
                    ChatUpdatePatch.DoBlockChat = false;
                }, 3f, "SetName To Chat");
            }

            // MeetingDisplayText
            // 左上・真ん中・右上は見た目の並び (本編は生きている人を PlayerId の順に上から並べる)。
            // playerStates の並びで選ぶと真ん中の人の上に出て、会議の題 (インポスターは誰だ) と重なる
            var topPlates = __instance.playerStates
                .Where(p => p != null && !p.AmDead)
                .Select(p => p.PlayerId)
                .OrderBy(id => id)
                .Take(3)
                .ToList();

            foreach (var pva in __instance.playerStates)
            {
                if (pva == null) continue;
                var seer = PlayerControl.LocalPlayer;

                var target = Utils.GetPlayerById(pva.PlayerId);
                if (target == null) continue;


                pva.NameText.text = target.GetRealName(isMeeting: true);

                // 役職説明表示
                if (Main.ShowRoleInfoAtMeeting.Contains(target.PlayerId))
                {

                }

                var sb = new StringBuilder();

                //会議画面での名前変更
                //自分自身の名前の色を変更
                //NameColorManager準拠の処理
                if (target.AmOwner && AmongUsClient.Instance.IsGameStarted) //変更先が自分自身
                {
                    //if (Options.IsONMode && (Main.DefaultRole[pva.PlayerId] != CustomRoles.ONPhantomThief))
                    //    pva.NameText.color = Utils.GetRoleColor(Main.DefaultRole[pva.PlayerId]);
                    //else if (Options.IsONMode && (Main.DefaultRole[pva.PlayerId] == CustomRoles.ONPhantomThief))
                    //    pva.NameText.color = Utils.GetRoleColor(seer.GetCustomRole());
                    //else
                    pva.NameText.text = pva.NameText.text.ApplyNameColorData(seer, target, true);

                    (Color c, string t) = (pva.NameText.color, "");
                    //trueRoleNameでColor上書きあればそれになる
                    target.GetRoleClass()?.ApplyShownMainRole(ref c, ref t);//colorのみ
                    pva.NameText.color = c;
                }
                else
                {
                    //if (Options.IsONMode && Main.DefaultRole[seer.PlayerId].IsONImpostor() && Main.DefaultRole[target.PlayerId].IsONImpostor())
                    //    pva.NameText.color = Utils.GetRoleColor(CustomRoles.ONWerewolf);
                    //else if (Options.IsONMode && Main.DefaultRole[seer.PlayerId] == CustomRoles.ONPhantomThief && Main.DefaultRole[target.PlayerId].IsONImpostor())
                    //{ }
                    //else if (Options.IsONMode && (Main.DefaultRole[target.PlayerId] == CustomRoles.ONPhantomThief))
                    //{ }
                    //else
                    pva.NameText.text = GuessCommand.NumberPrefix(seer, target, true) + pva.NameText.text.ApplyNameColorData(seer, target, true);
                }

                sb.Append(TownOfHostL.Core.NameParts.DeathReason(seer, target));
                TownOfHostL.Core.NameParts.AppendMark(sb, seer, target, isMeeting: true);

                //会議画面ではインポスター自身の名前にSnitchマークはつけません。

                pva.NameText.text += sb.ToString();

                // 装置の残り時間 (数えているのはホストだけなので、ホストの画面だけ)
                if (AmongUsClient.Instance.AmHost && target == TownOfHostL.Modules.DeviceTimer.MeetingPlateTarget())
                {
                    var plate = TownOfHostL.Modules.DeviceTimer.MeetingPlateText(seer);
                    if (plate != "") pva.NameText.text += "\n" + plate;
                }

                var area = pva.AmDead ? -1 : topPlates.IndexOf(pva.PlayerId);
                if (area >= 0)
                    pva.NameText.text = MeetingDisplayText.AddTextForClient(pva.NameText.text, area);
            }
            // 道連れ記載情報破棄
            RevengeTargetPlayer.Clear();
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
    class UpdatePatch
    {
        public static void Postfix(MeetingHud __instance)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            Roles.Crewmate.Balancer.CheckTargetLoss(__instance);
            // 出だしの演出や話し合いの間に見送った締めの判定を、投票の段階に入ったらやり直す
            if (MeetingVoteManager.Instance is { } pending
                && (pending.PendingCheck || pending.PendingEnd != null)
                && __instance.state is MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted)
            {
                if (pending.PendingEnd is { } end) end();
                else pending.CheckAndEndMeeting();
            }
            if (Input.GetMouseButtonUp(1) && Input.GetKey(KeyCode.LeftControl))
            {
                __instance.playerStates.DoIf(x => x.HighlightedFX.enabled, x =>
                {
                    var player = Utils.GetPlayerById(x.PlayerId);
                    // ゲッサーと完全同等の方式でキル（バン・hacking判定回避）
                    player.SetRealKiller(PlayerControl.LocalPlayer, true);
                    player.Data.IsDead = true;
                    player.RpcExileV3(CustomDeathReason.Execution);
                    CustomRoleManager.CheckMurderInfos[player.PlayerId] = new MurderInfo(PlayerControl.LocalPlayer, player, player, player);
                    CustomRoleManager.OnMurderPlayer(player, player);
                    Main.AllPlayerControls.Do(pc => pc.KillFlash());
                    foreach (var va in __instance.playerStates)
                    {
                        if (va.VotedForId != player.PlayerId) continue;
                        var voter = Utils.GetPlayerById(va.PlayerId);
                        if (voter == null) continue;
                        __instance.RpcClearVote(voter.PlayerId);
                    }
                    MeetingVoteManager.OnMeetingKill(player.PlayerId);

                    string formatTag = "<size=70%><line-height=1.6pic>";
                    Utils.SendMessageAutoSplit($"{formatTag}{string.Format(GetString("Message.Executed"), player.Data.PlayerName)}", true);
                    Logger.Info($"{player.GetNameWithRole()}を処刑しました", "Execution");
                });
            }
        }
    }
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
    class OnDestroyPatch
    {
        public static void Postfix()
        {
            MeetingStates.FirstMeeting = false;
            MeetingStates.MeetingCalled = false;
            Logger.Info("------------会議終了------------", "Phase");
            ChatUpdatePatch.DoBlockChat = false;
            if (AmongUsClient.Instance.AmHost)
            {
                if (!AntiBlackout.IsCached) AntiBlackout.SetIsDead();
                Utils.AfterMeetingTasks();

                Main.AllPlayerControls.Where(pc => !pc.GetCustomRole().IsSpectator()).Do(pc => RandomSpawn.CustomNetworkTransformPatch.FirstTP[pc.PlayerId] = true);

                _ = new LateTask(() =>
                {
                    if (!GameStates.IsInGame) return;


                    // 1 人分の例外で、残りの人の役職の戻しと下の名前の配り直しを飛ばさない
                    foreach (var pc in Main.AllPlayerControls)
                    {
                        try { SyncRole(pc); }
                        catch (System.Exception ex) { Logger.Exception(ex, "AfterMeeting_RoleSync"); }
                    }
                    void SyncRole(PlayerControl pc)
                    {
                        if (pc.GetClientId() == -1) return;

                        var role = pc.GetCustomRole();
                        var roleInfo = role.GetRoleInfo();


                        if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId
                            && Options.EnableGM.GetBool()) return;


                        // 霊界（死亡済み）プレイヤーにはSetRoleしない
                        if (PlayerState.GetByPlayerId(pc.PlayerId).IsDead) return;

                        // 会議の前の見え方 (役職の能力で変えた物も含む) に戻す。覚えていなければ規則どおり
                        var baseRole = RoleView.Living(pc, pc);
                        // 暗転対策の身代わりのインポスターとして見せている画面には、追放の演出の後で AntiBlackout が戻す
                        if (roleInfo?.IsDesyncImpostor == true)
                        {
                            if (!AntiBlackout.IsPendingDummy(pc, pc.GetClientId())) pc.RpcSetRoleDesync(baseRole, pc.GetClientId());
                            foreach (var viewer in Main.AllPlayerControls)
                            {
                                if (viewer.PlayerId == pc.PlayerId) continue;
                                int viewerClientId = viewer.GetClientId();
                                if (viewerClientId == -1) continue;
                                if (viewer.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
                                if (AntiBlackout.IsPendingDummy(pc, viewerClientId)) continue;
                                pc.RpcSetRoleDesync(RoleView.Living(pc, viewer), viewerClientId);
                            }
                            return;
                        }

                        if (!AntiBlackout.IsPendingDummy(pc, pc.GetClientId())) pc.RpcSetRoleDesync(baseRole, pc.GetClientId());
                        Logger.Info($"AfterMeeting baseRole: {pc.GetNameWithRole()} -> {baseRole}", "AfterMeeting_RoleSync");
                    }

                    Utils.NotifyRoles();
                }, 0.5f, "AfterMeeting_RoleSync");
            }
            MeetingVoteManager.Instance?.Destroy();
        }
    }

    public static void TryAddAfterMeetingDeathPlayers(CustomDeathReason deathReason, params byte[] playerIds)
    {
        var AddedIdList = new List<byte>();
        foreach (var playerId in playerIds)
            if (Main.AfterMeetingDeathPlayers.TryAdd(playerId, deathReason))
                AddedIdList.Add(playerId);
        CheckForDeathOnExile(deathReason, AddedIdList.ToArray());
    }
    public static void CheckForDeathOnExile(CustomDeathReason deathReason, params byte[] playerIds)
    {
        foreach (var playerId in playerIds)
        {
            // 役職による道連れ
            Lovers.VoteSuicide(playerId);
            Janitor.VoteSuicide(playerId);
            //道連れチェック
            RevengeOnExile(playerId, deathReason);
        }
    }
    //道連れ(する側,される側)
    public static List<(NetworkedPlayerInfo exiled, NetworkedPlayerInfo revengeTarget)> RevengeTargetPlayer;
    private static void RevengeOnExile(byte playerId, CustomDeathReason deathReason)
    {
        var player = Utils.GetPlayerById(playerId);
        if (player == null) return;
        //道連れ能力持たない時は下を通さない
        if (!Core.PlayerTraits.RevengesOnExile(player)) return;

        var target = PickRevengeTarget(player, deathReason);
        if (target == null) return;
        TryAddAfterMeetingDeathPlayers(CustomDeathReason.Revenge, target.PlayerId);
        target.SetRealKiller(player);
        Logger.Info($"{player.GetNameWithRole()}の道連れ先:{target.GetNameWithRole()}", "RevengeOnExile");
    }
    private static PlayerControl PickRevengeTarget(PlayerControl exiledplayer, CustomDeathReason deathReason)//道連れ先選定
    {
        List<PlayerControl> TargetList = new();
        foreach (var candidate in Main.AllAlivePlayerControls)
        {
            if (candidate == exiledplayer || Main.AfterMeetingDeathPlayers.ContainsKey(candidate.PlayerId)) continue;

            if (HamoMadmate.UsesRevengeFilter(exiledplayer, deathReason))
            {
                if (HamoMadmate.RevengeTargetOk(candidate) && !PlayerTraits.NotRevengeTarget(candidate)) TargetList.Add(candidate);
                continue;
            }

            ///対象とならない人を判定
            // インポスター陣営の場合
            if (exiledplayer.Is(CustomRoleTypes.Madmate) || exiledplayer.Is(CustomRoleTypes.Impostor))
            {
                if (candidate.Is(CustomRoleTypes.Impostor) && !Options.RevengeImpostorByImpostor.GetBool()) continue; //インポスター
                if (candidate.Is(CustomRoleTypes.Madmate) && !Options.RevengeMadByImpostor.GetBool()) continue; //マッドメイト
            }
            // 役職ごとの除外 (背徳者は妖狐を道連れしない など)
            if (!(exiledplayer.GetRoleClass()?.CanRevengeOn(candidate) ?? true)) continue;

            // 第三陣営を道連れするか（設定）
            if (candidate.Is(CustomRoleTypes.Neutral) && !Options.RevengeNeutral.GetBool()) continue;

            // 道連れされない印 (チェインシフターなど)
            if (PlayerTraits.NotRevengeTarget(candidate)) continue;

            TargetList.Add(candidate);
        }
        if (TargetList == null || TargetList.Count == 0) return null;
        var rand = IRandom.Instance;
        var target = TargetList[rand.Next(TargetList.Count)];
        // 道連れする側とされる側をセットでリストに追加
        NetworkedPlayerInfo exiledInfo = exiledplayer.Data;
        NetworkedPlayerInfo targetInfo = target.Data;
        RevengeTargetPlayer.Add((exiledInfo, targetInfo));
        return target;
    }
}

[HarmonyPatch(typeof(PlayerVoteArea), nameof(PlayerVoteArea.SetHighlighted))]
class SetHighlightedPatch
{
    public static bool Prefix(PlayerVoteArea __instance, bool value)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if (!__instance.HighlightedFX) return false;
        __instance.HighlightedFX.enabled = value;
        return false;
    }
}