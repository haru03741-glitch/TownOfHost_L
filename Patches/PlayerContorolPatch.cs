using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using UnityEngine;

using TownOfHostL.Modules;
using TownOfHostL.Roles;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Neutral;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.AddOns.Crewmate;
using static TownOfHostL.Translator;
using TownOfHostL.Roles.Impostor;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

using TownOfHostL.Core.Modes;

namespace TownOfHostL;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckProtect))]
class CheckProtectPatch
{
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost) return false;
        // CNO (客の手元の一覧に残る偽のプレイヤー) は Data がホストの物なので、守るとホストを守ったことになる
        if (Core.CustomNetObject.Is(target) || Core.CustomNetObject.Is(__instance)) return false;
        Logger.Info("CheckProtect発生: " + __instance.GetNameWithRole() + "=>" + target.GetNameWithRole(), "CheckProtect");
        // 本人の画面だけインポスターのクルー・マッドメイト (シェリフ系など) は、死んだ後に守護できない
        var role = __instance.GetCustomRole();
        if (role.GetRoleInfo()?.IsDesyncImpostor == true && (role.IsCrewmate() || role.IsMadmate()))
        {
            if (__instance.Data.IsDead)
            {
                Logger.Info("守護をブロックしました。", "CheckProtect");
                return false;
            }
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
class CheckMurderPatch
{
    public static Dictionary<byte, float> TimeSinceLastKill = new();
    public static void Update()
    {
        for (byte i = 0; i < 15; i++)
        {
            if (TimeSinceLastKill.ContainsKey(i))
            {
                TimeSinceLastKill[i] += Time.deltaTime;
                if (15f < TimeSinceLastKill[i]) TimeSinceLastKill.Remove(i);
            }
        }
    }
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        if (!AmongUsClient.Instance.AmHost || AmongUsClient.Instance.IsGameOver) return false;
        // CNO へのキルは Core の前置で捨てている (前置は全部走るので、ここでも見る)
        if (Core.CustomNetObject.Is(target) || Core.CustomNetObject.Is(__instance)) return false;
        RevealVisualRoleBeforeMurder(__instance, target);

        if (!CustomRoleManager.OnCheckMurder(__instance, target))
        {
            __instance.RpcMurderPlayer(target, false);
        }

        return false;
    }
    private static void RevealVisualRoleBeforeMurder(PlayerControl killer, PlayerControl target)
    {

        bool killerIsViper = killer.Is(RoleTypes.Viper);
        if (killerIsViper)
        {
            // 2 秒後に、見せる前の見え方へ戻す (続けてキルして Viper を見せている間なら Impostor)
            var before = new Dictionary<byte, RoleTypes>();
            foreach (var seer in Main.AllPlayerControls)
            {
                if (seer.PlayerId == killer.PlayerId) continue;
                if (seer.GetClientId() == -1) continue;
                var was = RoleView.Living(killer, seer);
                before[seer.PlayerId] = was == RoleTypes.Viper ? RoleTypes.Impostor : was;
                killer.RpcSetRoleDesync(RoleTypes.Viper, seer.GetClientId());
            }

            var capKiller = killer;
            _ = new LateTask(() =>
            {
                // 戻す前に死んでいたら送らない (幽霊の役職を生きた役職で上書きしない)
                if (capKiller == null || !capKiller.IsAlive()) return;
                foreach (var seer in Main.AllPlayerControls)
                {
                    if (seer.PlayerId == capKiller.PlayerId) continue;
                    if (seer.GetClientId() == -1) continue;
                    capKiller.RpcSetRoleDesync(before.TryGetValue(seer.PlayerId, out var back) ? back : RoleTypes.Impostor, seer.GetClientId());
                }
            }, 2f, "RevertViperVisualReveal");
        }

        bool targetIsNoisemaker = target.Is(RoleTypes.Noisemaker);
        if (targetIsNoisemaker)
        {
            foreach (var seer in Main.AllPlayerControls)
            {
                if (seer.PlayerId == target.PlayerId) continue;
                if (seer.GetClientId() == -1) continue;
                target.RpcSetRoleDesync(RoleTypes.Noisemaker, seer.GetClientId());
            }
        }
    }

    // 不正キル防止チェック
    public static bool CheckForInvalidMurdering(MurderInfo info, bool killerMyselfInvoke)
    {
        (var killer, var target) = info.AttemptTuple;

        // Killerが既に死んでいないかどうか
        if (!killer.IsAlive())
        {
            Logger.Info($"{killer.GetNameWithRole()}は死亡しているためキャンセルされました。", "CheckMurder");
            return false;
        }
        // targetがキル可能な状態か
        if (
            // PlayerDataがnullじゃないか確認
            target.Data == null ||
            // targetの状態をチェック
            target.inVent ||
            target.MyPhysics.Animations.IsPlayingEnterVentAnimation() ||
            target.MyPhysics.Animations.IsPlayingAnyLadderAnimation() ||
            target.inMovingPlat)
        {
            Logger.Info("targetは現在キルできない状態です。", "CheckMurder");
            return false;
        }
        // targetが既に死んでいないか
        if (!target.IsAlive())
        {
            Logger.Info("targetは既に死んでいたため、キルをキャンセルしました。", "CheckMurder");
            return false;
        }
        // 会議中・または会議コール直後のキルでないか
        if (MeetingHud.Instance != null || MeetingStates.MeetingCalled)
        {
            Logger.Info("会議中（またはコール直後）のためキルをキャンセルしました。", "CheckMurder");
            return false;
        }

        // 連打キルでないか
        if (!killerMyselfInvoke)
        {
            float minTime = Mathf.Max(0.02f, AmongUsClient.Instance.Ping / 1000f * 6f); //※AmongUsClient.Instance.Pingの値はミリ秒(ms)なので÷1000
            //TimeSinceLastKillに値が保存されていない || 保存されている時間がminTime以上 => キルを許可
            //↓許可されない場合
            if (TimeSinceLastKill.TryGetValue(killer.PlayerId, out var time) && time < minTime)
            {
                Logger.Info("前回のキルからの時間が早すぎるため、キルをブロックしました。", "CheckMurder");
                return false;
            }
        }
        TimeSinceLastKill[killer.PlayerId] = 0f;

        // HideAndSeek_キルボタンが使用可能か
        if (ModeCatalog.Current.IsHideAndSeek && Options.HideAndSeekKillDelayTimer > 0)
        {
            Logger.Info("HideAndSeekの待機時間中だったため、キルをキャンセルしました。", "CheckMurder");
            return false;
        }
        // キルが可能なプレイヤーか(遠隔は除く)
        if (!info.IsFakeSuicide && !killer.CanUseKillButton())
        {
            Logger.Info(killer.GetNameWithRole() + "はKillできないので、キルはキャンセルされました。", "CheckMurder");
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
class MurderPlayerPatch
{
    private static readonly LogHandler logger = Logger.Handler(nameof(PlayerControl.MurderPlayer));
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, [HarmonyArgument(1)] MurderResultFlags resultFlags, ref bool __state /* 成功したキルかどうか */ )
    {
        logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}({resultFlags})");
        var isProtectedByClient = resultFlags.HasFlag(MurderResultFlags.DecisionByHost) && target.IsProtected();
        var isProtectedByHost = resultFlags.HasFlag(MurderResultFlags.FailedProtected);
        var isFailed = resultFlags.HasFlag(MurderResultFlags.FailedError);
        var isSucceeded = __state = !isProtectedByClient && !isProtectedByHost && !isFailed;
        if (isProtectedByClient)
        {
            logger.Info("守護されているため，キルは失敗します");
        }
        if (isProtectedByHost)
        {
            logger.Info("守護されているため，キルはホストによってキャンセルされました");
        }
        if (isFailed)
        {
            logger.Info("キルはホストによってキャンセルされました");
        }

        if (isSucceeded)
        {
            if (target.shapeshifting)
            {
                //シェイプシフトアニメーション中
                //アニメーション時間を考慮して1s、加えてクライアントとのラグを考慮して+0.5s遅延する
                _ = new LateTask(
                    () =>
                    {
                        if (GameStates.IsInTask)
                        {
                            target.RpcShapeshift(target, false);
                        }
                    },
                    1.5f, "RevertShapeshift");
            }
            else
            {
                if (Main.CheckShapeshift.TryGetValue(target.PlayerId, out var shapeshifting) && shapeshifting)
                {
                    //シェイプシフト強制解除
                    target.RpcShapeshift(target, false);
                }
            }
            Camouflage.RpcSetSkin(Camouflage.IsCamouflage, target, ForceRevert: true);
            if (Options.IsSyncColorMode && Options.SCM_RestoredDeadPlayer.GetBool())
            {
                SkinChangeMode.RpcSetSkin(target, target);
            }
        }
    }
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, bool __state)
    {
        if (!__state) return; // キルが成功していない場合，何もしない

        if (target.AmOwner) RemoveDisableDevicesPatch.UpdateDisableDevices();
        if (!target.Data.IsDead || !AmongUsClient.Instance.AmHost) return;
        // 以降ホストしか処理しない
        // 処理は全てCustomRoleManager側で行う
        CustomRoleManager.OnMurderPlayer(__instance, target);
        _ = new LateTask(() =>
        {
            AntiBlackout.SetRoleChange(__instance);
        }, 0.5f, "PostMurderSetRoleChange");
        var killer = __instance;
        bool killerIsViper = killer.Is(RoleTypes.Viper);
        if (killerIsViper)
        {
            foreach (var seer in Main.AllPlayerControls)
            {
                if (seer.GetClientId() == -1) continue;
                killer.RpcSetRoleDesync(RoleTypes.Viper, seer.GetClientId());
            }
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckShapeshift))]
public static class PlayerControlCheckShapeshiftPatch
{
    private static readonly LogHandler logger = Logger.Handler(nameof(PlayerControl.CheckShapeshift));

    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target, [HarmonyArgument(1)] bool shouldAnimate)
    {
        if (AmongUsClient.Instance.IsGameOver || !AmongUsClient.Instance.AmHost)
        {
            return false;
        }

        // 無効な変身を弾く．これより前に役職等の処理をしてはいけない
        // CNO (客の手元の一覧に残る偽のプレイヤー) は Data がホストの物なので、ここで先に弾く
        if (Core.CustomNetObject.Is(target) || !CheckInvalidShapeshifting(__instance, target, shouldAnimate))
        {
            __instance.RpcRejectShapeshift();
            return false;
        }
        var shapeshifter = __instance;
        var shapeshifting = shapeshifter.PlayerId != target.PlayerId;
        // 変身したとき一番近い人をマッドメイトにする処理
        if (shapeshifter.CanMakeMadmate() && shapeshifting)
        {
            var sidekickable = shapeshifter.GetRoleClass() as ISidekickable;
            var targetRole = sidekickable?.SidekickTargetRole ?? CustomRoles.SKMadmate;

            Vector2 shapeshifterPosition = shapeshifter.transform.position;//変身者の位置
            Dictionary<PlayerControl, float> mpdistance = new();
            float dis;
            foreach (var p in Main.AllAlivePlayerControls)
            {
                if (p.Data.Role.Role != RoleTypes.Shapeshifter && !p.Is(CustomRoleTypes.Impostor) && !p.Is(targetRole))
                {
                    dis = Vector2.Distance(shapeshifterPosition, p.transform.position);
                    mpdistance.Add(p, dis);
                }
            }
            if (mpdistance.Count != 0)
            {
                var min = mpdistance.OrderBy(c => c.Value).FirstOrDefault();//一番値が小さい
                PlayerControl targetm = min.Key;
                targetm.RpcSetCustomRole(targetRole);
                Logger.Info($"Make SKMadmate:{targetm.name}", "Shapeshift");
                Main.SKMadmateNowCount++;
                Utils.MarkEveryoneDirtySettings();
                Utils.NotifyRoles();
            }
        }
        // 役職の処理
        var role = shapeshifter.GetRoleClass();
        if (role?.OnCheckShapeshift(target, ref shouldAnimate) == false)
        {
            if (role.CanDesyncShapeshift)
            {
                shapeshifter.RpcSpecificRejectShapeshift(target, shouldAnimate);
            }
            else
            {
                shapeshifter.RpcRejectShapeshift();
            }
            return false;
        }

        shapeshifter.RpcShapeshift(target, shouldAnimate);
        return false;
    }
    private static bool CheckInvalidShapeshifting(PlayerControl instance, PlayerControl target, bool animate)
    {
        logger.Info($"Checking shapeshift {instance.GetNameWithRole()} -> {(target == null || target.Data == null ? "(null)" : target.GetNameWithRole())}");

        if (!target || target.Data == null)
        {
            logger.Info("targetがnullのため変身をキャンセルします");
            return false;
        }
        if (!instance.IsAlive())
        {
            logger.Info("変身者が死亡しているため変身をキャンセルします");
            return false;
        }
        // RoleInfoによるdesyncシェイプシフター用の判定を追加
        if (instance.Data.Role.Role != RoleTypes.Shapeshifter && instance.GetCustomRole().GetRoleInfo()?.BaseRoleType?.Invoke() != RoleTypes.Shapeshifter)
        {
            logger.Info("変身者がシェイプシフターではないため変身をキャンセルします");
            return false;
        }
        if (instance.Data.Disconnected)
        {
            logger.Info("変身者が切断済のため変身をキャンセルします");
            return false;
        }
        if (target.IsMushroomMixupActive() && animate)
        {
            logger.Info("キノコカオス中のため変身をキャンセルします");
            return false;
        }
        if (MeetingHud.Instance && animate)
        {
            logger.Info("会議中のため変身をキャンセルします");
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Shapeshift))]
class ShapeshiftPatch
{
    public static void Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        // CNO は見た目を入れるためにホストへ変身する。役職の処理も名前の配り直しも要らない
        if (Core.CustomNetObject.Is(__instance)) return;
        Logger.Info($"{__instance?.GetNameWithRole()} => {target?.GetNameWithRole()}", "Shapeshift");

        var shapeshifter = __instance;
        var shapeshifting = shapeshifter.PlayerId != target.PlayerId;

        if (Main.CheckShapeshift.TryGetValue(shapeshifter.PlayerId, out var last) && last == shapeshifting)
        {
            Logger.Info($"{__instance?.GetNameWithRole()}:Cancel Shapeshift.Prefix", "Shapeshift");
            return;
        }

        Main.CheckShapeshift[shapeshifter.PlayerId] = shapeshifting;
        Main.ShapeshiftTarget[shapeshifter.PlayerId] = target.PlayerId;

        shapeshifter.GetRoleClass()?.OnShapeshift(target);

        if (!AmongUsClient.Instance.AmHost) return;

        if (!shapeshifting) Camouflage.RpcSetSkin(Camouflage.IsCamouflage, __instance);

        // 本編の変身は名前を変身先の名前に、解除は本名に戻すので、見る人ごとの名前 (役職表示や通信妨害中の隠した名前) を配り直す。
        // 変身はすぐに送る (送り直しの元になる LastNotifyNames を変身先の名前にしておく)。
        // 解除はタイミングがずれて名前が直せないことがあるので、少し待ってから送る
        if (shapeshifting)
        {
            Utils.NotifyRoles(NoCache: true);
        }
        else
        {
            _ = new LateTask(() =>
            {
                Utils.NotifyRoles(NoCache: true);
            },
            1.2f, "ShapeShiftNotify");
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckVanish))]
public static class PlayerControlCheckVanishPatch
{
    private static readonly LogHandler logger = Logger.Handler(nameof(PlayerControl.CheckVanish));

    public static bool Prefix(PlayerControl __instance)
    {
        if (AmongUsClient.Instance.IsGameOver)
        {
            return false;
        }

        if (!AmongUsClient.Instance.AmHost)
        {
            return true;
        }

        var phantom = __instance;
        Logger.Info($"{phantom.GetNameWithRole()} : CheckVanishStart", "CheckVanish");

        // 役職の処理
        var role = phantom.GetRoleClass();

        /***************************** 
         * ◆下記OnCheckVanishがfalseの場合は設定できる
         * ・killCooldown : キルクールが10秒に設定されてしまうため任意に設定する
         * 　　ホストは仕様上キルクールは強制されないが、バニラの仕様に合わせセットする
         * ・canResetAbilityCooldown : アビリティクールダウンをリセットするか設定する
         *****************************/
        // ファントムのボタンをワンクリックで使う役職
        if (role is IUsePhantomButton phantomButton && phantomButton.UseOneclickButton)
            return PhantomButton.CheckVanish(phantom, phantomButton);

        float killCooldown = 10.0f;
        bool canResetAbilityCooldown = false;
        if (role?.OnCheckVanish(ref killCooldown, ref canResetAbilityCooldown) == false)
        {
            Logger.Info($"{phantom.GetNameWithRole()} : OnCheckVanish() == false", "CheckVanish");

            if (phantom.IsAlive())
            {
                if (phantom.PlayerId != PlayerControl.LocalPlayer.PlayerId)
                {
                    SendVanishToSelfOnly(phantom);
                    _ = new LateTask(() =>
                    {
                        if (phantom != null && phantom.IsAlive()) SendAppearToSelfOnly(phantom);
                    }, 1.0f, "PhantomVanishCancel");
                }
                else
                {
                    phantom.RpcResetAbilityCooldown();
                }
            }

            // キルクールリセット           
            if (killCooldown >= 0f)
                phantom.SetKillCooldown(killCooldown);
            // アビリティクールリセット
            if (canResetAbilityCooldown)
            {
                phantom.RpcResetAbilityCooldown();
            }
            return false;
        }

        return true;
    }
    private static void SendVanishToSelfOnly(PlayerControl phantom)
    {
        var writer = AmongUsClient.Instance.StartRpcImmediately(
            phantom.NetId, (byte)RpcCalls.StartVanish, SendOption.Reliable, phantom.GetClientId());
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    private static void SendAppearToSelfOnly(PlayerControl phantom)
    {
        var writer = AmongUsClient.Instance.StartRpcImmediately(
            phantom.NetId, (byte)RpcCalls.StartAppear, SendOption.Reliable, phantom.GetClientId());
        writer.Write(false);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CmdCheckVanish))]
public static class PlayerControlCmdCheckVanishPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        if (!AmongUsClient.Instance.AmHost)
        {
            return true;
        }

        __instance?.CheckVanish();
        return false;
    }
}
[HarmonyPatch(typeof(PhantomRole), nameof(PhantomRole.UseAbility))]
public static class PhantomRoleUseAbilityPatch
{
    public static bool Prefix(PhantomRole __instance)
    {
        if (__instance.Player.AmOwner && !__instance.Player.Data.IsDead && __instance.Player.moveable && !Minigame.Instance && !__instance.IsCoolingDown && !__instance.fading)
        {
            System.Func<RoleEffectAnimation, bool> roleEffectAnimation = x => x.effectType == RoleEffectAnimation.EffectType.Vanish_Charge;
            if (!__instance.Player.currentRoleAnimations.Find(roleEffectAnimation) && !__instance.Player.walkingToVent && !__instance.Player.inMovingPlat)
            {
                if (__instance.isInvisible)
                {
                    __instance.MakePlayerVisible(true, true);
                    return false;
                }
                DestroyableSingleton<HudManager>.Instance.AbilityButton.SetSecondImage(__instance.Ability);
                DestroyableSingleton<HudManager>.Instance.AbilityButton.OverrideText(DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.PhantomAbilityUndo, new Il2CppReferenceArray<Il2CppSystem.Object>(0)));
                __instance.Player.CmdCheckVanish(10f);
                return false;
            }
        }
        // いずれの条件も満たさない場合はバニラ処理に委ねる
        return true;
    }

    private static float GetPhantomDuration()
    {
        var options = GameManager.Instance.LogicOptions;
        float duration = 5f;
        try
        {
            var field = options.GetType().GetField("_phantomDuration", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
                duration = (float)field.GetValue(options);
        }
        catch
        {
        }
        return duration;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
partial class ReportDeadBodyPatch
{
    public static NetworkedPlayerInfo reporter;
    public static NetworkedPlayerInfo ReportTarget;
    public static bool SpecialMeeting = reporter?.PlayerId == ReportTarget?.PlayerId;

    public static List<byte> CannotReportList;
    public static List<byte> CannotReportByDeadBodyList;
    public static List<byte> DontReportMarkList;
    public static Dictionary<byte, List<NetworkedPlayerInfo>> WaitReport = new();
    public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] NetworkedPlayerInfo target)
    {
        reporter = __instance.Data;
        ReportTarget = target;
        SpecialMeeting = reporter?.PlayerId == ReportTarget?.PlayerId;
        if (GameStates.IsMeeting) return false;
        Logger.Info($"{__instance.GetNameWithRole()} => {target?.Object?.GetNameWithRole() ?? "null"}", "ReportDeadBody");
        // 生き返った人の死体 (本編の死体は消せずに残る) は通報できない
        if (target != null && !SpecialMeeting && Revive.WasRevived(target.PlayerId) && !(PlayerState.GetByPlayerId(target.PlayerId)?.IsDead ?? true)) return false;
        // 復活の SetRole が落ち着く前に会議を開くと、本編の客の会議の画面が壊れる。1 件だけ預かり、落ち着いてから通す
        if (AmongUsClient.Instance.AmHost && Revive.HoldReport(__instance, target)) return false;
        // 役職の能力で確認なしに会議を開く
        if (ForceReport)
        {
            ForceReport = false;
            goto confirmed;
        }
        if (SpecialMeeting) return true;
        if (!ModeCatalog.Current.AllowsReport(__instance, target)) return false;
        if (target == null && Options.DisableButtonInMushroomMixup.GetBool() && MushroomMixupUpdateSystemPatch.InSabotage)
        {
            Logger.Info("DisableButton InMushroomMixup", "ReportDeadBody");
            return false;
        }
        if (target != null && TownOfHostL.Core.PlayerTraits.CannotReport(reporter.Object, target.Object))
        {
            DontReportMarkList.Add(reporter.PlayerId);
            Utils.NotifyRoles(SpecifySeer: reporter.Object);
            _ = new LateTask(() =>
            {
                DontReportMarkList.Remove(reporter.PlayerId);
                Utils.NotifyRoles(SpecifySeer: reporter.Object);
            }, 5f, "Don't Report Mark Remove");
            return false;
        }
        // Scavenger
        if (target != null && CannotReportByDeadBodyList.Contains(target.PlayerId))
        {
            DontReportMarkList.Add(reporter.PlayerId);
            Utils.NotifyRoles(SpecifySeer: reporter.Object);
            _ = new LateTask(() =>
            {
                DontReportMarkList.Remove(reporter.PlayerId);
                Utils.NotifyRoles(SpecifySeer: reporter.Object);
            }, 5f, "Scavenger DontReportMark Remove");
            return false;
        }

        if (CannotReportList.Contains(__instance.PlayerId))
        {
            WaitReport[__instance.PlayerId].Add(target);
            Logger.Warn($"{__instance.GetNameWithRole()}:通報禁止中のため可能になるまで待機します", "ReportDeadBody");
            return false;
        }

        //会議ボタンの場合、サボタージュ中に呼び出しを受けない
        if (target == null && Utils.IsActiveDontOpenMeetingSabotage(out var sabotage))
        {
            Logger.Warn($"{sabotage}サボタージュ中です", "ReportDeadBody");
            return false;
        }

        if (!AmongUsClient.Instance.AmHost) return true;

        //通報者が死んでいる場合、本処理で会議がキャンセルされるのでここで止める
        if (__instance.Data.IsDead) return false;

        if (Options.SyncButtonMode.GetBool() && target == null)
        {
            Logger.Info("最大:" + Options.SyncedButtonCount.GetInt() + ", 現在:" + Options.UsedButtonCount, "ReportDeadBody");
            if (Options.SyncedButtonCount.GetFloat() <= Options.UsedButtonCount)
            {
                Logger.Info("使用可能ボタン回数が最大数を超えているため、ボタンはキャンセルされました。", "ReportDeadBody");
                return false;
            }
            else Options.UsedButtonCount++;
            if (Options.SyncedButtonCount.GetFloat() == Options.UsedButtonCount)
            {
                Logger.Info("使用可能ボタン回数が最大数に達しました。", "ReportDeadBody");
            }
        }

        // 役職による通報の取り消し
        if (RoleCancelsReport(__instance, target)) return false;
        // 残りの緊急ボタンの数
        if (target == null)
        {
            var buttonState = PlayerState.GetByPlayerId(__instance.PlayerId);
            if (buttonState != null && buttonState.NumberOfRemainingButtons > 0) buttonState.NumberOfRemainingButtons--;
        }

    //=============================================
    //以下、ボタンが押されることが確定したものとする。
    //=============================================
    confirmed:
        MeetingStates.MeetingCalled = true;

        foreach (var pc in Main.AllPlayerControls)
        {
            var state = PlayerState.GetByPlayerId(pc.PlayerId);

            // ブラックアウト
            if (pc.IsAlive())
            {
                state.IsBlackOut = true;
                pc.MarkDirtySettings();
            }

            // 最終位置の部屋の取得・記憶
            state.LastRoom = pc.GetPlainShipRoom();

            // 変身解除処理
            if (Main.CheckShapeshift.ContainsKey(pc.PlayerId))
            {
                Camouflage.RpcSetSkin(Camouflage.IsCamouflage, pc);
            }
        }

        MushroomMixupDeterioratePatch.RestorName();

        foreach (var role in CustomRoleManager.AllActiveRoles.Values)
        {
            role.OnReportDeadBody(__instance, target);
        }

        // 役職変更表示のリセット
        Main.ShowChangeMainRole.Clear();

        MeetingTimeManager.OnReportDeadBody();
        // 会議で死体は全部片付くので、死体の覆いも外す
        DeadBodyEraser.OnReportDeadBody();

        Utils.NotifyRoles(isForMeeting: true, NoCache: true);

        Utils.SyncAllSettings();

        if (ShapeKiller.DummyReportDeadBody(__instance, target)) return false;

        if (!__instance.IsAlive())
        {
            //通報者が通報処理中に死亡した場合の強制会議実行
            RPC.ReportDeadBodyForced(__instance, target);
            return false;
        }

        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
public static class PlayerControlStartMeetingPatch
{
    public static void Prefix()
    {
        //foreach (var kvp in PlayerState.AllPlayerStates)
        //{
        //    var pc = Utils.GetPlayerById(kvp.Key);
        //    kvp.Value.LastRoom = pc.GetPlainShipRoom();
        //}
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Start))]
class PlayerStartPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        var roleText = UnityEngine.Object.Instantiate(__instance.cosmetics.nameText);
        roleText.transform.SetParent(__instance.cosmetics.nameText.transform);
        roleText.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        roleText.transform.localScale = new(1f, 1f, 1f);
        roleText.fontSize = Main.RoleTextSize;
        roleText.text = "RoleText";
        roleText.gameObject.name = "RoleText";
        roleText.enabled = false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetColor))]
class SetColorPatch
{
    public static bool IsAntiGlitchDisabled = false;
    public static bool Prefix(PlayerControl __instance, int bodyColor)
    {
        //色変更バグ対策
        if (!AmongUsClient.Instance.AmHost || Core.CustomNetObject.Is(__instance) || __instance.CurrentOutfit.ColorId == bodyColor || IsAntiGlitchDisabled) return true;
        if (AmongUsClient.Instance.IsGameStarted && ModeCatalog.Current.KillsOnColorChange)
        {
            //ゲーム中に色を変えた場合
            __instance.RpcMurderPlayer(__instance);
        }
        return true;
    }
}
[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.CoEnterVent))]
class CoEnterVentPatch
{
    public static bool Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] int id)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            if (ModeCatalog.Current.BootsFromVents)
                __instance.RpcBootFromVent(id);
            // ベントの制限 (押し出す時も、ベントを能力のボタンにしている役職のために、役職の「ベントに入った」は起こす)
            else if (RpcEnterVentPatch.Depth == 0)
                Modules.VentLimit.OnEnterVent(__instance, id);

            // ホストが RpcEnterVent を呼んだ時は、その中で CoEnterVent も走る。役職へは RpcEnterVent の後置処理から 1 回だけ渡す
            if (RpcEnterVentPatch.Depth == 0)
                OnEnterVent(__instance, id);
        }
        return true;
    }

    public static void OnEnterVent(PlayerPhysics physics, int id)
    {
        var user = physics.myPlayer;
        if (user == null) return;
        // 誰かがベントに入った時の処理・今いるベントの記録
        RoleHooks.NowVentId[user.PlayerId] = (byte)id;
        foreach (var vent in CustomRoleManager.OnEnterVentOthers.ToArray())
        {
            try { vent(physics, id); }
            catch (System.Exception ex) { Logger.Exception(ex, "OnEnterVentOthers"); }
        }
        RoleHooks.OnEnterVent(physics, id);

        bool roleAllowsVent = user.GetRoleClass()?.OnEnterVent(physics, id) ?? false;
        bool ventTaskAllows = VentEnterTask.OnEnterVent(physics, id);

        if (!roleAllowsVent &&
            !ventTaskAllows &&
            !(user.GetCustomRole().IsCCCatRoles() && CatchCat.CatPlayer.CanUseVent(user)) &&
            user.Data.Role.Role != RoleTypes.Engineer &&
            !user.CanUseImpostorVentButton())
        {
            _ = new LateTask(() => { physics.RpcBootFromVent(id); }, 0.5f, "Cancel Vent");
        }
        else if (user.GetCustomRole().IsCCCatRoles() && !CatchCat.CatPlayer.CanUseVent(user))
        {
            _ = new LateTask(() => { physics.RpcBootFromVent(id); }, 0.5f, "Cancel Vent");
        }
    }
}
[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.RpcEnterVent))]
class RpcEnterVentPatch
{
    /// <summary>RpcEnterVent の実行中なら 1 以上</summary>
    public static int Depth;

    public static void Prefix() => Depth++;
    public static void Finalizer() => Depth--;

    public static void Postfix(PlayerPhysics __instance, [HarmonyArgument(0)] int id)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        Modules.VentLimit.OnEnterVent(__instance, id);
        CoEnterVentPatch.OnEnterVent(__instance, id);
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.SetName))]
class SetNamePatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] string name)
    {
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CompleteTask))]
class PlayerControlCompleteTaskPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        var pc = __instance;

        Logger.Info($"TaskComplete:{pc.GetNameWithRole()}", "CompleteTask");
        var taskState = pc.GetPlayerTaskState();
        taskState.Update(pc);

        var roleClass = pc.GetRoleClass();
        var ret = true;
        if (roleClass != null)
        {
            ret = roleClass.OnCompleteTask();
        }
        //属性クラスの扱いを決定するまで仮置き
        ret &= Workhorse.OnCompleteTask(pc);
        CompreteCrew.OnCompleteTask(pc);

        if (ModeCatalog.IsCatchCat) CatchCat.CatPlayer.OnCompleteTask(pc, taskState);

        Utils.NotifyRoles();
        return ret;
    }
    public static void Postfix()
    {
        //人外のタスクを排除して再計算
        GameData.Instance.RecomputeTaskCounts();
        Logger.Info($"TotalTaskCounts = {GameData.Instance.CompletedTasks}/{GameData.Instance.TotalTasks}", "TaskState.Update");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ProtectPlayer))]
class PlayerControlProtectPlayerPatch
{
    public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
    {
        Logger.Info($"{__instance.GetNameWithRole()} => {target.GetNameWithRole()}", "ProtectPlayer");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RemoveProtection))]
class PlayerControlRemoveProtectionPatch
{
    public static void Postfix(PlayerControl __instance)
    {
        Logger.Info($"{__instance.GetNameWithRole()}", "RemoveProtection");
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
class PlayerControlSetRolePatch
{
    private const float GhostRoleSyncDelay = 0.5f;

    public static bool Prefix(PlayerControl __instance, ref RoleTypes roleType, bool canOverrideRole)
    {
        if (RpcSetRoleReplacer.DoReplace()) return true;

        var target = __instance;
        var targetName = __instance.GetNameWithRole();
        Logger.Info($"{targetName} =>{roleType}", "PlayerControl.RpcSetRole");
        if (!ShipStatus.Instance.enabled) return true;
        bool isGhostConversion = roleType is RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost;
        if (isGhostConversion)
        {
            var ghostRoles = new Dictionary<PlayerControl, RoleTypes>();
            // 海賊の一味になる時は、全員から見てクルーの幽霊にする。一味にする処理 (役職・タスクの配り直し) は 1 回だけ
            bool becomesGang = Pirate.TargetSetGhostAndTask(target);
            foreach (var seer in Main.AllPlayerControls)
                ghostRoles[seer] = becomesGang ? RoleTypes.CrewmateGhost : RoleView.Ghost(target, seer);
            if (ghostRoles.All(kvp => kvp.Value == RoleTypes.CrewmateGhost))
            {
                roleType = RoleTypes.CrewmateGhost;
            }
            else if (ghostRoles.All(kvp => kvp.Value == RoleTypes.ImpostorGhost))
            {
                roleType = RoleTypes.ImpostorGhost;
            }
            else
            {
                foreach ((var seer, var role) in ghostRoles)
                {
                    Logger.Info($"Desync {targetName} =>{role} for{seer.GetNameWithRole()}", "PlayerControl.RpcSetRole");
                    var capTarget = target;
                    var capRole = role;
                    var capSeer = seer;
                    _ = new LateTask(() =>
                    {
                        capTarget.RpcSetRoleDesync(capRole, capSeer.GetClientId());
                    }, GhostRoleSyncDelay, "DelayedGhostRoleSyncDesync");
                }
                return false;
            }
        }

        if (isGhostConversion)
        {
            var capTarget2 = target;
            var capRoleType = roleType;
            var capCanOverride = canOverrideRole;
            _ = new LateTask(() =>
            {
                capTarget2.RpcSetRoleNormal(capRoleType, capCanOverride);
            }, GhostRoleSyncDelay, "DelayedGhostRoleSyncNormal");
        }
        else
        {
            target.RpcSetRoleNormal(roleType, canOverrideRole);
        }

        return false;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.Die))]
public static class PlayerControlDiePatch
{
    public static void Postfix(PlayerControl __instance)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            // 死者の最終位置にペットが残るバグ対応
            __instance.RpcSetPet("");
        }
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckUseZipline))]
public static class PlayerControlCheckUseZiplinePatch
{
    public static bool Prefix(PlayerControl target, ZiplineBehaviour ziplineBehaviour, bool fromTop)
    {
        if (AmongUsClient.Instance.AmHost)
        {
            if (Options.FungleCanUseZiplineFromTop.GetBool() && fromTop) return false;
            if (Options.FungleCanUseZiplineFromUnder.GetBool() && !fromTop) return false;
        }

        return true;
    }
}
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckSporeTrigger))]
public static class PlayerControlCheckSporeTriggerPatch
{
    public static bool Prefix()
    {
        return !Options.FungleCanSporeTrigger.GetBool();
    }
}