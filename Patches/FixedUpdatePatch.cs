using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using TownOfHostL.Modules;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Impostor;
using UnityEngine;

using TownOfHostL.Core.Modes;

namespace TownOfHostL
{
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    class FixedUpdatePatch
    {
        private static StringBuilder Mark = new(20);
        private static StringBuilder Suffix = new(120);       
        private static float OptionsSendTimer = 1f;
        private const int NameUpdateInterval = 3;
        // RoleText は名前の子に一度作られたら変わらないので、毎フレーム探し直さずに覚えておく (nameText の場所で引く)
        private static readonly System.Collections.Generic.Dictionary<IntPtr, TMPro.TextMeshPro> RoleTextCache = new();
        private static void PeriodicSendDirtyGameOptions()
        {
            OptionsSendTimer -= Time.fixedDeltaTime;
            if (OptionsSendTimer > 0f) return;
            OptionsSendTimer = 0.2f; 
            try
            {
                GameOptionsSender.SendAllGameOptions();
            }
            catch (Exception ex)
            {
                Logger.Error($"PeriodicSendDirtyGameOptions で例外: {ex.Message}", "FixedUpdatePatch");
            }
        }
        public static void Postfix(PlayerControl __instance)
        {
            try
            {
                if (__instance == null) return; // 安全ガード
                // CNO (偽のプレイヤー) にも本編は FixedUpdate を呼ぶ
                if (Core.CustomNetObject.Is(__instance)) return;

                var player = __instance;

                if (!GameStates.IsModHost) return;

                TargetArrow.OnFixedUpdate(player);
                GetArrow.OnFixedUpdate(player);
                if (AmongUsClient.Instance.AmHost) (player.GetRoleClass() as TownOfHostL.Roles.Core.Interfaces.IUsePhantomButton)?.FixedUpdate(player);
                TargetDeadArrow.OnFixedUpdate(player);
                VentSelect.OnFixedUpdate(player);
                CustomRoleManager.OnFixedUpdate(player);

                if (AmongUsClient.Instance.AmHost)
                {
                    // 実行クライアントがホストの場合のみ実行
                    if (GameStates.IsLobby && (!Main.AllowPublicRoom || ModUpdater.hasUpdate || !VersionChecker.IsSupported || !Main.IsPublicAvailableOnThisVersion) && AmongUsClient.Instance.IsGamePublic)
                        AmongUsClient.Instance.ChangeGamePublic(false);

                    if (GameStates.IsInTask && !ReportDeadBodyPatch.CannotReportList.Contains(__instance.PlayerId) && ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Count > 0)
                    {
                        var info = ReportDeadBodyPatch.WaitReport[__instance.PlayerId][0];
                        ReportDeadBodyPatch.WaitReport[__instance.PlayerId].Clear();
                        Logger.Info($"{__instance.GetNameWithRole()}:通報可能になったため通報処理を行います", "ReportDeadbody");
                        __instance.ReportDeadBody(info);
                    }

                    DoubleTrigger.OnFixedUpdate(player);

                    // ターゲットのリセット
                    if (GameStates.IsInTask && player.IsAlive() && Options.LadderDeath.GetBool())
                    {
                        FallFromLadder.FixedUpdate(player);
                    }

                    if (GameStates.IsInGame && player.AmOwner)
                        DisableDevice.FixedUpdate();

                    if (__instance.AmOwner)
                    {
                        Utils.ApplySuffix();
                        PeriodicSendDirtyGameOptions();
                    }
                }

                // LocalPlayer専用
                if (__instance.AmOwner)
                {
                    // キルターゲットの上書き処理
                    if (GameStates.IsInTask && !((__instance.Is(CustomRoleTypes.Impostor) && !__instance.GetCustomRole().IsLoneImpostor()) || __instance.Is(CustomRoles.Egoist)) && __instance.CanUseKillButton() && !__instance.Data.IsDead)
                    {
                        var players = __instance.GetPlayersInAbilityRangeSorted(false);
                        PlayerControl closest = players.Count <= 0 ? null : players[0];
                        var hud = HudManager.Instance;
                        if (hud != null && hud.KillButton != null)
                        {
                            hud.KillButton.SetTarget(closest);
                        }
                    }
                }

                // 役職テキストの表示
                // safety: cosmetics/nameText may be null in some states; check before use
                var nameTextObj = __instance?.cosmetics?.nameText;
                if (nameTextObj == null) return;

                // 名前と役職表示の組み立ては重い (人数に比例する) ので、各プレイヤーにつき 3 回に 1 回 (約 10 回/秒) にする。
                // PlayerId でずらして、同じ回に全員分が集中しないようにする。
                int step = (int)Math.Round(Time.fixedTime / Time.fixedDeltaTime);
                if ((step + __instance.PlayerId) % NameUpdateInterval != 0) return;

                TMPro.TextMeshPro RoleText = null;
                if (RoleTextCache.TryGetValue(nameTextObj.Pointer, out var cachedRoleText) && TextWriteCache.IsAlive(cachedRoleText))
                    RoleText = cachedRoleText;
                var RoleTextTransform = RoleText is not null ? null : nameTextObj.transform.Find("RoleText");
                if (RoleTextTransform != null)
                {
                    // GetComponent が null 参照を投げないようチェック
                    try
                    {
                        RoleText = RoleTextTransform.GetComponent<TMPro.TextMeshPro>();
                        if (RoleText != null)
                        {
                            if (RoleTextCache.Count > 64) RoleTextCache.Clear();
                            RoleTextCache[nameTextObj.Pointer] = RoleText;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"RoleText GetComponent で例外: {ex.Message}", "FixedUpdatePatch");
                        RoleText = null;
                    }
                }

                if (RoleText == null)
                {
                    // RoleText が null の場合は以降の表示処理を行わない
                    return;
                }

                // GameStates が null などの可能性を考慮して以降を保護
                if (GameStates.IsLobby)
                {
                    if (Main.playerVersion.TryGetValue(__instance.PlayerId, out var ver))
                    {
                        try
                        {
                            if (Main.ForkId != ver.forkId) // フォークIDが違う場合
                                TextWriteCache.SetText(nameTextObj, $"<color=#ff0000><size=1.2>{ver.forkId}</size>\n{__instance?.name}</color>");
                            else if (Main.version.CompareTo(ver.version) != 0)
                                TextWriteCache.SetText(nameTextObj, $"<color=#ff0000><size=1.2>v{ver.version}</size>\n{__instance?.name}</color>");
                            else
                                TextWriteCache.SetText(nameTextObj, __instance?.Data?.PlayerName);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Lobby nameText 書き換えで例外: {ex.Message}", "FixedUpdatePatch");
                        }
                    }
                    else
                    {
                        try { TextWriteCache.SetText(nameTextObj, __instance?.Data?.PlayerName); } catch { }
                    }
                }

                if (GameStates.IsInGame)
                {
                    // まず seer (表示する側) を取得。LocalPlayer が null かもしれないので代替を用意
                    var seer = PlayerControl.LocalPlayer ?? __instance;
                    // seer が null であればロール表示を諦める
                    if (seer == null)
                    {
                        RoleText.enabled = false;
                        RoleText.text = "";
                        return;
                    }

                    // Utils.GetRoleNameAndProgressTextData は内部で ShipStatus 等を参照するので例外を吸収して安全に扱う
                    bool enabled = false;
                    string roleTextStr = "";
                    try
                    {
                        (enabled, roleTextStr) = Utils.GetRoleNameAndProgressTextData(false, seer, __instance);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"GetRoleNameAndProgressTextData で例外: {ex.Message}", "FixedUpdatePatch");
                        enabled = false;
                        roleTextStr = "";
                    }

                    RoleText.enabled = enabled;
                    TextWriteCache.SetText(RoleText, roleTextStr);

                    if (!AmongUsClient.Instance.IsGameStarted && AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay)
                    {
                        RoleText.enabled = false; // ゲームが始まっておらずフリープレイでなければロールを非表示
                        if (!__instance.AmOwner)
                        {
                            try { TextWriteCache.SetText(nameTextObj, __instance?.Data?.PlayerName); } catch { }
                        }
                    }

                    // 以下は name display の組み立て（多くの参照に nullガードを入れる）
                    var target = __instance;
                    string RealName = target.GetRealName();
                    Mark.Clear();
                    Suffix.Clear();

                    // 名前色等の処理（seer・target の null をチェックしつつ）
                    try
                    {
                        if (target.AmOwner && AmongUsClient.Instance.IsGameStarted)
                        {
                            if (TownOfHostL.Core.PlayerTraits.LearnsExiledTeam(target))
                            {
                                string str = Sending.RealNameChange();
                                if (!string.IsNullOrEmpty(str))
                                {
                                    RealName = str;
                                }
                            }
                            else if (ModeCatalog.IsCatchCat)
                            {
                                RealName = Utils.ColorString(seer.GetRoleColor(), seer.GetRoleInfo());
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"名前変更処理で例外: {ex.Message}", "FixedUpdatePatch");
                    }

                    // NameColorManager 準拠の処理（安全第一でラップ）
                    try
                    {
                        RealName = RealName.ApplyNameColorData(seer, target, false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"ApplyNameColorData で例外: {ex.Message}", "FixedUpdatePatch");
                    }

                    // trueRoleName の color 上書き（null 安全に）
                    try
                    {
                        // Color.clear や Color の比較演算子はゲーム側を呼ぶので、成分を直接比べる
                        (Color c, string t) = (default(Color), "");
                        var targetRoleClass = target.GetRoleClass();
                        targetRoleClass?.ApplyShownMainRole(ref c, ref t);
                        if (c.r != 0f || c.g != 0f || c.b != 0f || c.a != 0f)
                        {
                            RealName = RealName.Color(c); // 既存の Color 拡張を利用
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"OverrideShowMainRoleText で例外: {ex.Message}", "FixedUpdatePatch");
                    }

                    // Mark / Suffix の組み立て（null 安全に）
                    try
                    {
                        TownOfHostL.Core.NameParts.AppendMark(Mark, seer, target, false);
                        if (seer == target) TownOfHostL.Core.NameParts.AppendReportMark(Mark, seer);
                        TownOfHostL.Core.NameParts.AppendSeenSuffix(Suffix, seer, target, false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Mark/Suffix 組み立てで例外: {ex.Message}", "FixedUpdatePatch");
                    }

                    try
                    {
                        if (TownOfHostL.Core.NameParts.HideSeenName(false))
                            RealName = $"<size=0>{RealName}</size> ";
                    }
                    catch (Exception ex)
                    {
                        // Utils.IsActive で例外が出るケースがあるので吸収して続行
                        Logger.Warn($"IsActive チェックで例外: {ex.Message}", "FixedUpdatePatch");
                    }

                    string DeathReason = "";
                    try
                    {
                        if (seer?.Data != null)
                            DeathReason = TownOfHostL.Core.NameParts.DeathReason(seer, target);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"DeathReason 取得で例外: {ex.Message}", "FixedUpdatePatch");
                    }

                    // 画面反映
                    try
                    {
                        // 書き込みは 1 回にまとめる (以前は書いた後に読み戻して足していた)
                        if (Suffix.Length != 0)
                        {
                            RoleText.transform.SetLocalY(0.45f);
                            TextWriteCache.SetText(nameTextObj, $"{RealName}{DeathReason}{Mark}\r\n{Suffix}");
                        }
                        else
                        {
                            RoleText.transform.SetLocalY(0.3f);
                            TextWriteCache.SetText(nameTextObj, $"{RealName}{DeathReason}{Mark}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"名前表示反映で例外: {ex.Message}", "FixedUpdatePatch");
                    }
                }
                else
                {
                    // ゲーム中でない場合は RoleText 座標を初期に戻す
                    try { RoleText.transform.SetLocalY(0.3f); } catch { }
                }
            }
            catch (Exception ex)
            {
                // Postfix 全体での保険: ここで例外が出てもゲームループを止めない
                Logger.Error($"FixedUpdatePatch.Postfix で例外: {ex.Message}", "FixedUpdatePatch");
                Logger.Exception(ex, "FixedUpdatePatch");
            }
        }
    }
}