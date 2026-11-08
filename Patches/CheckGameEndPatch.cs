using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Hazel;
using UnityEngine;
using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;

namespace TownOfHostL
{
    [HarmonyPatch(typeof(LogicGameFlowNormal), nameof(LogicGameFlowNormal.CheckEndCriteria))]
    class GameEndChecker
    {
        private static GameEndRuleSet predicate;
        private static GameOverReason lastReason = GameOverReason.ImpostorsByKill;
        private static int errorLogCount;
        public static bool Prefix()
        {
            try
            {
                return PrefixCore();
            }
            catch (System.Exception ex)
            {
                // ここで例外を外へ出すと毎フレーム同じ例外になり、試合が終わらなくなる
                if (errorLogCount++ < 5) Logger.Exception(ex, "GameEndChecker");
                if (predicate != null && CustomWinnerHolder.WinnerTeam != CustomWinner.Default)
                {
                    // 勝者は決まっているので、後の処理が失敗しても試合は終わらせる
                    predicate = null;
                    try { ShipStatus.Instance.enabled = false; } catch { }
                    var reason = lastReason;
                    if (CustomWinnerHolder.WinnerTeam != CustomWinner.Crewmate
                        && (reason.Equals(GameOverReason.CrewmatesByTask) || reason.Equals(GameOverReason.CrewmatesByVote)))
                        reason = GameOverReason.ImpostorsByVote;
                    StartEndGame(reason);
                }
                return false;
            }
        }
        private static bool PrefixCore()
        {
            if (!AmongUsClient.Instance.AmHost) return true;

            //ゲーム終了判定済みなら中断
            if (predicate == null) return false;

            // 役職などが決めた勝ち (廃村以外) を止める。人数・タスク・妨害は各ルールの中で止める
            if (Options.NoGameEndByRole.GetBool()
                && CustomWinnerHolder.WinnerTeam is not (CustomWinner.Default or CustomWinner.Draw)) return false;

            //ゲーム終了判定
            predicate.Check(out var reason);
            HamoGameEnd.TakeOver(ref reason);
            lastReason = reason;

            if (CustomWinnerHolder.WinnerTeam == CustomWinner.Default) return false;

            Logger.Info($"GameEnd winner: {CustomWinnerHolder.WinnerTeam}, reason: {reason}", "GameEndChecker");
            //カモフラージュ強制解除
            Main.AllPlayerControls.Do(pc =>
            {
                Camouflage.RpcSetSkin(false, pc, ForceRevert: true);
                SkinChangeMode.RpcSetSkin(pc, pc);
            });
            Modules.Net.OutfitSync.SendAllNow();

            reason = WinnerResolver.Resolve(reason);
            ShipStatus.Instance.enabled = false;

            if (CustomWinnerHolder.WinnerTeam != CustomWinner.Crewmate
                && (reason.Equals(GameOverReason.CrewmatesByTask) || reason.Equals(GameOverReason.CrewmatesByVote)))
                reason = GameOverReason.ImpostorsByVote;
            StartEndGame(reason);
            predicate = null;
            return false;
        }
        public static void StartEndGame(GameOverReason reason)
        {
            AmongUsClient.Instance.StartCoroutine(CoEndGame(AmongUsClient.Instance, reason).WrapToIl2Cpp());
        }
        private static IEnumerator CoEndGame(AmongUsClient self, GameOverReason reason)
        {
            // サーバー側のパケットサイズ制限によりCustomRpcSenderが利用できないため，遅延を挟むことで順番の整合性を保つ．

            // バニラ画面でのアウトロを正しくするためのゴーストロール化
            List<byte> ReviveRequiredPlayerIds = new();
            var winner = CustomWinnerHolder.WinnerTeam;
            foreach (var pc in Main.AllPlayerControls)
            {
                if (winner == CustomWinner.Draw)
                {
                    SetGhostRole(ToGhostImpostor: true);
                    continue;
                }
                //if (Options.IsONMode && winner == CustomWinner.Crewmate)
                //    reason = GameOverReason.CrewmatesByVote;

                bool canWin = CustomWinnerHolder.WinnerIds.Contains(pc.PlayerId) ||
                    CustomWinnerHolder.WinnerRoles.Contains(pc.GetCustomRole());
                canWin &= !CustomWinnerHolder.CantWinPlayerIds.Contains(pc.PlayerId);
                bool isCrewmateWin = reason.Equals(GameOverReason.CrewmatesByVote) || reason.Equals(GameOverReason.CrewmatesByTask);
                SetGhostRole(ToGhostImpostor: canWin ^ isCrewmateWin);

                void SetGhostRole(bool ToGhostImpostor)
                {
                    var isDead = pc.Data.IsDead;
                    if (!isDead) ReviveRequiredPlayerIds.Add(pc.PlayerId);
                    if (ToGhostImpostor)
                    {
                        Logger.Info($"{pc.GetNameWithRole()}: ImpostorGhostに変更", "ResetRoleAndEndGame");
                        pc.RpcSetRoleNormal(RoleTypes.ImpostorGhost);
                    }
                    else
                    {
                        Logger.Info($"{pc.GetNameWithRole()}: CrewmateGhostに変更", "ResetRoleAndEndGame");
                        pc.RpcSetRoleNormal(RoleTypes.CrewmateGhost);
                    }
                    // 蘇生までの遅延の間にオートミュートをかけられないように元に戻しておく
                    pc.Data.IsDead = isDead;
                }
            }

            // CustomWinnerHolderの情報の同期
            var winnerWriter = self.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.EndGame, SendOption.Reliable);
            CustomWinnerHolder.WriteTo(winnerWriter);
            self.FinishRpcImmediately(winnerWriter);

            // 蘇生を確実にゴーストロール設定の後に届けるための遅延
            yield return new WaitForSeconds(EndGameDelay);

            if (ReviveRequiredPlayerIds.Count > 0)
            {
                // 蘇生 パケットが膨れ上がって死ぬのを防ぐため，1送信につき1人ずつ蘇生する
                for (int i = 0; i < ReviveRequiredPlayerIds.Count; i++)
                {
                    var playerId = ReviveRequiredPlayerIds[i];
                    var playerInfo = GameData.Instance.GetPlayerById(playerId);
                    // 終了待ちの間に抜けた人は飛ばす (ここで止まると RpcEndGame が呼ばれず試合が終わらない)
                    if (playerInfo == null) continue;
                    // 蘇生
                    playerInfo.IsDead = false;
                    // 送信
                    playerInfo.SetDirtyBit(0b_1u << playerId);
                    AmongUsClient.Instance.SendAllStreamedObjects();
                }
                // ゲーム終了を確実に最後に届けるための遅延
                yield return new WaitForSeconds(EndGameDelay);
            }

            //try
            //{
            //    SetRoleSummaryText();
            //}
            //catch (System.Exception ex)
            //{
            //    Logger.Exception(ex, "SetRoleSummaryText");
            //}
            //yield return new WaitForSeconds(EndGameDelay);

            // ゲーム終了
            GameManager.Instance.RpcEndGame(reason, false);
          
            //float delay = EndGameDelay;
            //for (int i = 0; i < PostEndGameResendCount; i++)
            //{
            //    yield return new WaitForSeconds(delay);
            //    try
            //    {
            //        SetRoleSummaryText();
            //    }
            //    catch (System.Exception ex)
            //    {
            //        Logger.Exception(ex, $"SetRoleSummaryText(post RpcEndGame #{i + 1})");
            //    }
            //    delay += EndGameDelay; 
            //}
        }
        private static void SetRoleSummaryText(CustomRpcSender sender = null)
        {
            var winners = new List<PlayerControl>();
            foreach (var pc in Main.AllPlayerControls)
            {
                if (CustomWinnerHolder.WinnerIds.Contains(pc.PlayerId)) winners.Add(pc);
            }
            foreach (var team in CustomWinnerHolder.WinnerRoles)
            {
                winners.AddRange(Main.AllPlayerControls.Where(p => p.Is(team) && !winners.Contains(p)));
            }
            foreach (var id in CustomWinnerHolder.CantWinPlayerIds)
            {
                var pc = Main.AllPlayerControls.FirstOrDefault(p => p.PlayerId == id);
                if (pc == null) continue;
                winners.Remove(pc);
            }

            List<byte> winnerList = new();
            if (winners.Count != 0)
                foreach (var pc in winners)
                {
                    if (CustomWinnerHolder.WinnerTeam is not CustomWinner.Draw && pc.GetCustomRole().IsSpectator()) continue;
                    if (CustomWinnerHolder.WinnerIds.Contains(pc.PlayerId) && winnerList.Contains(pc.PlayerId)) continue;
                    if (CustomWinnerHolder.CantWinPlayerIds.Contains(pc.PlayerId)) continue;

                    winnerList.Add(pc.PlayerId);
                }
            string CustomWinnerColor;
            string CustomWinnerText;
            switch (CustomWinnerHolder.WinnerTeam)
            {
                case CustomWinner.Draw:
                    CustomWinnerText = Translator.GetString("ForceEndText");
                    CustomWinnerColor = "#AAAAAAFF"; // gray
                    break;
                case CustomWinner.None:
                    CustomWinnerText = Translator.GetString("EveryoneDied");
                    CustomWinnerColor = "#AAAAAAFF"; // gray
                    break;
                default:
                    var winnerRole = (CustomRoles)CustomWinnerHolder.WinnerTeam;
                    CustomWinnerText = $"{Utils.GetRoleName(winnerRole)}{Translator.GetString("Win")}";
                    CustomWinnerColor = (CustomWinnerHolder.WinnerTeam == CustomWinner.Crewmate
                        ? Utils.GetRoleColorCode(CustomRoles.Engineer)
                        : Utils.GetRoleColorCode(winnerRole)) ?? "#FFFFFFFF";
                    break;
            }
            var winnerSize = GetScale(CustomWinnerText.RemoveHtmlTags().Length, 1.6, 2.5);
            CustomWinnerText = $"<size={winnerSize}><color={CustomWinnerColor}>{CustomWinnerText}</color></size>";
            static double GetScale(int input, double min, double max)
                => min + (max - min) * (1 - (double)(input - 1) / 13);

            foreach (var pc in Main.AllPlayerControls)
            {
                if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
                if (pc == null) continue;
                try
                {
                    var target = (winnerList.Contains(pc.PlayerId) ? pc : (winnerList.Count == 0 ? pc : Main.AllPlayerControls.FirstOrDefault(p => p.PlayerId == winnerList.OrderBy(x => x).FirstOrDefault()) ?? pc)) ?? pc;
                    var targetname = Main.AllPlayerNames[target.PlayerId];

                    var text = $"<voffset=50>{CustomWinnerText}\n<voffset=0>{targetname}\n\n<voffset=24><size=40%></size>";
                    if (text.Length > 320)
                    {
                        text = text.Replace("</color>", "");
                        if (text.Length > 320)
                        {
                            text = text.RemoveColorTags();
                        }
                    }

                    if (sender == null)
                    {
                        target.RpcSetNamePrivate(text, true, pc, true);
                    }
                    else
                    {
                        sender.AutoStartRpc(pc.NetId, (byte)RpcCalls.SetName, pc.GetClientId())
                            .Write(pc.Data.NetId)
                            .Write(text)
                            .Write(true)
                            .EndRpc();
                    }
                }
                catch (System.Exception ex)
                {
                    Logger.Exception(ex, "SetRoleSummaryText(per player)");
                }
            }
        }
        private const float EndGameDelay = 0.2f;
        private const int PostEndGameResendCount = 8;

        public static void SetPredicateToNormal() => SetPredicate(GameEndRuleSet.Normal);
        public static void SetPredicateToHideAndSeek() => SetPredicate(GameEndRuleSet.HideAndSeek);
        public static void SetPredicateToCatchCat() => SetPredicate(CatchCat.GameEnd.Rules);
        private static void SetPredicate(GameEndRuleSet rules) { predicate = rules; errorLogCount = 0; GameEndRules.ResetForNewGame(); }
    }
}
