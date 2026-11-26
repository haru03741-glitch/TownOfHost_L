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
                    // 勝者の決め方の途中で落ちた時は、勝者の名簿が空のまま終わらないよう、決め方をもう 1 度だけ通す
                    if (CustomWinnerHolder.WinnerIds.Count == 0)
                    {
                        try { reason = WinnerResolver.Resolve(reason); }
                        catch (System.Exception ex2) { Logger.Exception(ex2, "GameEndChecker.Resolve"); }
                    }
                    StartEndGame(OutroReason(reason));
                }
                return false;
            }
        }
        /// <summary>本編の終わりの画面に渡す理由。クルーの勝ち以外 (と勝因を変える設定の時) は、クルーの勝ちの理由をインポスターの投票勝ちに替える</summary>
        private static GameOverReason OutroReason(GameOverReason reason)
        {
            bool crewReason = reason is GameOverReason.CrewmatesByTask or GameOverReason.CrewmatesByVote;
            if (crewReason && (CustomWinnerHolder.WinnerTeam != CustomWinner.Crewmate || HamoConvenient.OutroCrew))
                return GameOverReason.ImpostorsByVote;
            return reason;
        }
        private static bool PrefixCore()
        {
            if (GameStates.IsFreePlay) return false;
            if (!AmongUsClient.Instance.AmHost) return true;
            // 鬼の体の窓の間は GameManager を差し替えていて、試合を終わらせる電文を出せない
            if (Modules.SeekerBody.InWindow) return false;

            //ゲーム終了判定済みなら中断
            if (predicate == null) return false;

            // 役職などが決めた勝ち (廃村以外) を止める。人数・タスク・妨害は各ルールの中で止める
            if (Options.NoGameEndByRole.GetBool()
                && CustomWinnerHolder.WinnerTeam is not (CustomWinner.Default or CustomWinner.Draw)) return false;

            //ゲーム終了判定
            predicate.Check(out var reason);
            WinTakeOvers.RunOnGameEnd(ref reason);
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
            HamoSabotage.AfterResolve(reason);
            ShipStatus.Instance.enabled = false;

            StartEndGame(OutroReason(reason));
            predicate = null;
            return false;
        }
        /// <summary>この試合の終わりの送信 (CoEndGame) を始めたか。始めた後の通報などは受けない</summary>
        public static bool EndingStarted { get; private set; }
        [Attributes.GameModuleInitializer]
        public static void ResetEnding() => EndingStarted = false;
        public static void StartEndGame(GameOverReason reason)
        {
            // 終わりの送信は 1 試合に 1 度 (重ねると、全員への幽霊の役職と終わりの電文が二重に出る)
            if (EndingStarted)
            {
                Logger.Info($"終わりの送信は始まっているので、{reason} の終わりは重ねない", "GameEndChecker");
                return;
            }
            EndingStarted = true;
            AmongUsClient.Instance.StartCoroutine(CoEndGame(AmongUsClient.Instance, reason).WrapToIl2Cpp());
        }
        private static IEnumerator CoEndGame(AmongUsClient self, GameOverReason reason)
        {
            // サーバー側のパケットサイズ制限によりCustomRpcSenderが利用できないため，遅延を挟むことで順番の整合性を保つ．

            // バニラ画面でのアウトロを正しくするためのゴーストロール化
            // バイタルの嘘を本物へ戻してから終わりの送信を始める
            try { Modules.Net.VitalsMask.EndAll(); }
            catch (System.Exception ex) { Logger.Exception(ex, "CoEndGame.VitalsMask"); }
            List<byte> ReviveRequiredPlayerIds = new();
            var winner = CustomWinnerHolder.WinnerTeam;
            foreach (var pc in Main.AllPlayerControls)
            {
                // 1 人分の例外でコルーチンが止まると RpcEndGame が出ず、試合が終わらない
                try { SetGhostRoleFor(pc); }
                catch (System.Exception ex) { Logger.Exception(ex, "CoEndGame.SetGhostRole"); }
            }
            void SetGhostRoleFor(PlayerControl pc)
            {
                if (winner == CustomWinner.Draw)
                {
                    SetGhostRole(ToGhostImpostor: true);
                    return;
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
                if (pc.LacksClient("WinnerName")) continue;
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
        public static void SetPredicateToCatchCat() => SetPredicate(CatchCat.GameEnd.Rules);
        public static void SetPredicateTo(GameEndRuleSet rules) => SetPredicate(rules);
        private static void SetPredicate(GameEndRuleSet rules) { predicate = rules; errorLogCount = 0; GameEndRules.ResetForNewGame(); }
    }
}
