using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AmongUs.Data;
using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Il2CppInterop.Runtime.InteropTypes;
using InnerNet;
using Microsoft.VisualBasic;
using TownOfHostL.Modules;
using TownOfHostL.Roles;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.AddOns.Crewmate;
using TownOfHostL.Roles.AddOns.Impostor;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Neutral;
using UnityEngine;
using static TownOfHostL.Translator;

using TownOfHostL.Core;
using TownOfHostL.Core.Modes;
using TownOfHostL.Roles.AddOns;

namespace TownOfHostL;

public static partial class Utils
{
    public static bool IsActive(SystemTypes type)
    {
        if (ShipStatus.Instance == null) return false;
        if (ShipStatus.Instance.Systems == null) return false;

        // 反転スケルドはスケルドと同じサボタージュを持つ
        MapNames map = GameMap.Base;

        switch (type)
        {
            case SystemTypes.Electrical:
                {
                    if (map is MapNames.Fungle) return false;
                    if (!ShipStatus.Instance.Systems.ContainsKey(type)) return false;
                    var system = ShipStatus.Instance.Systems[type];
                    if (system == null) return false;
                    var SwitchSystem = system.Cast<SwitchSystem>();
                    return SwitchSystem != null && SwitchSystem.IsActive;
                }
            case SystemTypes.Reactor:
                {
                    if (map is MapNames.Polus or MapNames.Airship) return false;
                    if (!ShipStatus.Instance.Systems.ContainsKey(type)) return false;
                    var system = ShipStatus.Instance.Systems[type];
                    if (system == null) return false;
                    var ReactorSystemType = system.Cast<ReactorSystemType>();
                    return ReactorSystemType != null && ReactorSystemType.IsActive;
                }
            case SystemTypes.Laboratory:
                {
                    if (map is not MapNames.Polus) return false;
                    if (!ShipStatus.Instance.Systems.ContainsKey(type)) return false;
                    var system = ShipStatus.Instance.Systems[type];
                    if (system == null) return false;
                    var ReactorSystemType = system.Cast<ReactorSystemType>();
                    return ReactorSystemType != null && ReactorSystemType.IsActive;
                }
            case SystemTypes.LifeSupp:
                {
                    if (map is not MapNames.Skeld and not MapNames.MiraHQ) return false;
                    if (!ShipStatus.Instance.Systems.ContainsKey(type)) return false;
                    var system = ShipStatus.Instance.Systems[type];
                    if (system == null) return false;
                    var LifeSuppSystemType = system.Cast<LifeSuppSystemType>();
                    return LifeSuppSystemType != null && LifeSuppSystemType.IsActive;
                }
            case SystemTypes.Comms:
                {
                    if (!ShipStatus.Instance.Systems.ContainsKey(type)) return false;
                    var system = ShipStatus.Instance.Systems[type];
                    if (system == null) return false;

                    if (map is MapNames.MiraHQ or MapNames.Fungle)
                    {
                        var HqHudSystemType = system.Cast<HqHudSystemType>();
                        return HqHudSystemType != null && HqHudSystemType.IsActive;
                    }
                    else
                    {
                        var HudOverrideSystemType = system.Cast<HudOverrideSystemType>();
                        return HudOverrideSystemType != null && HudOverrideSystemType.IsActive;
                    }
                }
            case SystemTypes.HeliSabotage:
                {
                    if (map is not MapNames.Airship) return false;
                    if (!ShipStatus.Instance.Systems.ContainsKey(type)) return false;
                    var system = ShipStatus.Instance.Systems[type];
                    if (system == null) return false;
                    var HeliSabotageSystem = system.Cast<HeliSabotageSystem>();
                    return HeliSabotageSystem != null && HeliSabotageSystem.IsActive;
                }
            case SystemTypes.MushroomMixupSabotage:
                {
                    if (map is not MapNames.Fungle) return false;
                    if (!ShipStatus.Instance.Systems.ContainsKey(type)) return false;
                    var system = ShipStatus.Instance.Systems[type];
                    if (system == null) return false;
                    var mushroomMixupSabotageSystem = system.TryCast<MushroomMixupSabotageSystem>();
                    return mushroomMixupSabotageSystem != null && mushroomMixupSabotageSystem.IsActive;
                }
            default:
                return false;
        }
    }
    public static bool IsActiveDontOpenMeetingSabotage(out SystemTypes sabotage)
    {
        sabotage = SystemTypes.Admin;
        SystemTypes[] Sabotage = { SystemTypes.Electrical, SystemTypes.Comms,
            SystemTypes.Reactor, SystemTypes.Laboratory,
            SystemTypes.LifeSupp,  SystemTypes.HeliSabotage };

        foreach (SystemTypes type in Sabotage)
        {
            if (IsActive(type))
            {
                sabotage = type;
                return true;
            }
        }

        return false;
    }
    public static SystemTypes GetCriticalSabotageSystemType() => GameMap.Current switch
    {
        MapNames.Polus => SystemTypes.Laboratory,
        MapNames.Airship => SystemTypes.HeliSabotage,
        _ => SystemTypes.Reactor,
    };
    public static void SetVision(this IGameOptions opt, bool HasImpVision)
    {
        if (HasImpVision)
        {
            opt.SetFloat(
                FloatOptionNames.CrewLightMod,
                opt.GetFloat(FloatOptionNames.ImpostorLightMod));
            if (IsActive(SystemTypes.Electrical))
            {
                opt.SetFloat(
                FloatOptionNames.CrewLightMod,
                opt.GetFloat(FloatOptionNames.CrewLightMod) * 5);
            }
            return;
        }
        else
        {
            opt.SetFloat(
                FloatOptionNames.ImpostorLightMod,
                opt.GetFloat(FloatOptionNames.CrewLightMod));
            if (IsActive(SystemTypes.Electrical))
            {
                opt.SetFloat(
                FloatOptionNames.ImpostorLightMod,
                opt.GetFloat(FloatOptionNames.ImpostorLightMod) / 5);
            }
            return;
        }
    }
    //誰かが死亡したときのメソッド
    public static void TargetDies(MurderInfo info)
    {
        PlayerControl killer = info.AppearanceKiller, target = info.AttemptTarget;

        if (!target.Data.IsDead || GameStates.IsMeeting) return;
        foreach (var seer in Main.AllPlayerControls)
        {
            if (KillFlashCheck(info, seer))
            {
                seer.KillFlash();
            }
        }
    }
    public static bool KillFlashCheck(MurderInfo info, PlayerControl seer)
    {
        PlayerControl killer = info.AppearanceKiller, target = info.AttemptTarget;
        // 霊界キルフラッシュ
        if (GmOptions.IsGM(seer)) return GmOptions.Get(GmOptions.CanSeeKillFlash);
        if (seer.GetCustomRole().IsSpectator()) return true;
        if (CheckKillFlashAfterDead(seer) && seer != target) return true;
        if (HamoGhost.SeesKillFlash(seer, target)) return true;
        // 殺された人の側
        if (PlayerTraits.KillFlashToAll(target)) return true;
        if (target == BestieWolf.EnableKillFlash) return true;

        if (seer.Data.IsDead || TownOfHostL.Core.NativeObject.Same(killer, seer) || TownOfHostL.Core.NativeObject.Same(target, seer)) return false;
        if (SuddenDeath.SeesKillFlash(seer)) return true;

        // 属性などの印は、役職ごとの条件より先に見る (役職の条件が偽でも属性の光は消さない)
        if (PlayerTraits.SeesKillFlash(seer)) return true;
        return seer.GetRoleClass() is IKillFlashSeeable killFlashSeeable && killFlashSeeable.CheckKillFlash(info);
    }
    public static void KillFlash(this PlayerControl player)
    {
        bool ReactorCheck = IsActive(GetCriticalSabotageSystemType());

        var Duration = Options.KillFlashDuration.GetFloat();
        if (ReactorCheck) Duration += 0.2f;
        //キルフラッシュ(ブラックアウト+リアクターフラッシュ)の処理
        var state = PlayerState.GetByPlayerId(player.PlayerId);
        state.IsBlackOut = true;//ブラックアウト
        if (player.PlayerId == 0)
        {
            FlashColor(new(1f, 0f, 0f, 0.5f));
            if (Constants.ShouldPlaySfx()) RPC.PlaySound(player.PlayerId, Sounds.KillSound);
        }
        else if (!ReactorCheck) player.ReactorFlash(0f);//リアクターフラッシュ
        player.MarkDirtySettings();
        _ = new LateTask(() =>
        {
            state.IsBlackOut = false;//ブラックアウト解除
            player.MarkDirtySettings();
        }, Options.KillFlashDuration.GetFloat(), "RemoveKillFlash");
    }
    public static void BlackOut(this IGameOptions opt, bool IsBlackOut)
    {
        opt.SetFloat(FloatOptionNames.ImpostorLightMod, Main.DefaultImpostorVision);
        opt.SetFloat(FloatOptionNames.CrewLightMod, Main.DefaultCrewmateVision);
        if (IsBlackOut)
        {
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, 0);
            opt.SetFloat(FloatOptionNames.CrewLightMod, 0);
        }
        return;
    }
    /// <summary>
    /// seerが自分であるときのseenのRoleName + ProgressText
    /// </summary>
    /// <param name="seer">見る側</param>
    /// <param name="seen">見られる側</param>
    /// <returns>RoleName + ProgressTextを表示するか、構築する色とテキスト(bool, Color, string)</returns>
    public static (bool enabled, string text) GetRoleNameAndProgressTextData(bool isMeeting, PlayerControl seer, PlayerControl seen = null)
    {
        seen ??= seer;// CO可否表示
        var coDisplay = (TownOfHostL.Core.NativeObject.Same(seer, seen) && isMeeting) ? DisplayComingOut.GetString(seer.GetCustomRole()) : "";
        var teamMark = RoleText.GetDisplayTeamMark(seer, seen);
        var roleName = RoleText.GetDisplayRoleName(isMeeting, seer, seen);
        var progressText = GetProgressText(seer, seen);
        var text = coDisplay + teamMark + roleName + (roleName != "" ? " " : "") + progressText;
        return (text != "", text);
    }
    public static string GetRoleName(CustomRoles role)
    {
        // 名前の組み立てから見る人 × 見られる人のぶん呼ばれる。列挙の名前 (リフレクション) は役職ごとに 1 回だけ引く
        if (!roleKeys.TryGetValue(role, out var key)) roleKeys[role] = key = Enum.GetName(typeof(CustomRoles), role);
        return GetRoleString(key);
    }
    static readonly Dictionary<CustomRoles, string> roleKeys = new();
    public static string GetAddonAbilityInfo(CustomRoles role)
    {
        var text = role.ToString();
        return GetString($"{text}Info1");
    }
    public static string GetDeathReason(CustomDeathReason status)
    {
        return GetString("DeathReason." + Enum.GetName(typeof(CustomDeathReason), status));
    }
    public static Color GetRoleColor(CustomRoles role) => TownOfHostL.Core.RoleCatalog.Get(role).Color;
    public static string GetRoleColorCode(CustomRoles role) => TownOfHostL.Core.RoleCatalog.Get(role).ColorCode;
    public static string GetVitalText(byte playerId, bool RealKillerColor = false)
    {
        var state = PlayerState.GetByPlayerId(playerId);
        string deathReason = state.IsDead ? GetString("DeathReason." + state.DeathReason) : GetString("Alive");
        if (RealKillerColor)
        {
            var KillerId = state.GetRealKiller();
            Color color = KillerId != byte.MaxValue ? Main.PlayerColors[KillerId] : GetRoleColor(CustomRoles.Doctor);
            deathReason = ColorString(color, deathReason);
        }
        return deathReason;
    }

    public static bool HasTasks(NetworkedPlayerInfo p, bool ForRecompute = true)
    {
        if (GameStates.IsLobby) return false;
        //Tasksがnullの場合があるのでその場合タスク無しとする
        if (p.Tasks == null) return false;
        if (p.Role == null) return false;
        if (p.Disconnected) return false;

        var hasTasks = true;
        var States = PlayerState.GetByPlayerId(p.PlayerId);
        if (p.Role.IsImpostor)
            hasTasks = false;
        //タスクはCustomRoleを元に判定する
        if (ModeCatalog.IsCatchCat)
        {
            if (States.GetNowMainRole().IsCCLeaderRoles()) hasTasks = false;
        }
        else
        {
            // 死んでいて，死人のタスク免除が有効なら確定でfalse
            if (p.IsDead && Options.GhostIgnoreTasks.GetBool() && !TownOfHostL.Core.PlayerTraits.KeepsTasksWhenDead(p.Object) && HamoGhost.IgnoreTasksFor(p))
            {
                return false;
            }
            // その試合の役職が登録した免除 (妖狐の死後タスクの免除・忠犬の主人の死後など)
            foreach (var isExempt in CustomRoleManager.TaskExemptionsOthers)
            {
                bool exempt;
                try { exempt = isExempt(p); }
                catch (Exception ex) { Logger.Exception(ex, "TaskExemption"); exempt = false; }
                if (exempt) return false;
            }
            var role = States.GetNowMainRole();
            var roleClass = CustomRoleManager.GetByPlayerId(p.PlayerId);
            if (roleClass != null)
            {
                switch (roleClass.HasTasks)
                {
                    case HasTask.True:
                        hasTasks = true;
                        break;
                    case HasTask.False:
                        hasTasks = false;
                        break;
                    case HasTask.ForRecompute:
                        hasTasks = !ForRecompute;
                        break;
                }
            }
            // クラスを持たない役職 (GM など) は表の印で、インポスターは陣営で、タスクを持たない
            if (role.HasRoleFlag(RoleFlags.NoTasks) || role.IsImpostor()) hasTasks = false;
            //タスクを勝利用にカウントしない
            foreach (var subRole in States.SubRoles)
                if (subRole.HasRoleFlag(RoleFlags.TasksNotCountedForWin))
                    hasTasks &= !ForRecompute;
        }
        return hasTasks;
    }
    private static string GetProgressText(PlayerControl seer, PlayerControl seen = null)
    {
        seen ??= seer;
        //seer側による変更
        bool enabled = TownOfHostL.Core.NativeObject.Same(seer, seen)
                    || (Main.VisibleTasksCount && (GmOptions.IsGM(seer) ? GmOptions.Get(GmOptions.CanSeeTaskProgress) : !seer.IsAlive() && !Options.GhostCantSeeOtherTasks.GetBool()))
                    || (seen.GetRoleClass()?.ShowsTaskProgressToAll ?? false)
                    || SpecialMode.KnowsTasks(seer);
        // 上書きは text を読まずに差し替えるだけなので、既定の字は見える時だけ組む (null = 上書きされていない)
        string text = null;

        seer.GetRoleClass()?.OverrideProgressTextAsSeer(seen, ref enabled, ref text);
        // 見られる側による変更
        seen.GetRoleClass()?.OverrideProgressTextAsSeen(seer, ref enabled, ref text);
        var gmExtra = GmOptions.ExtraProgress(seer, seen);
        gmExtra += HamoGhost.ExtraProgress(seer, seen);
        if (!enabled) return gmExtra;
        text ??= GetProgressText(seen.PlayerId, IsActive(SystemTypes.Comms));
        text += gmExtra;
        if (ModeCatalog.IsCatchCat && TownOfHostL.Core.NativeObject.Same(seer, seen))
        {
            text += CatchCat.Common.GetMark(seer);
        }

        return text;
    }
    /// <summary>結果の一覧に出す進み具合 (タスクと役職ごとの数)</summary>
    public static string GetProgressTextFor(byte playerId) => GetProgressText(playerId);
    private static string GetProgressText(byte playerId, bool comms = false)
    {
        var ProgressText = new StringBuilder();
        var State = PlayerState.GetByPlayerId(playerId);
        var role = State.GetNowMainRole();
        var roleClass = CustomRoleManager.GetByPlayerId(playerId);
        ProgressText.Append(GetTaskProgressText(playerId, comms));
        ProgressText.Append(VentEnterTask.GetProgressText(playerId, comms));
        if (roleClass != null)
        {
            ProgressText.Append(roleClass.GetProgressText(comms));
        }

        if (GetPlayerById(playerId).CanMakeMadmate()) ProgressText.Append(ColorString(Palette.ImpostorRed.ShadeColor(0.5f), $"[{Options.CanMakeMadmateCount.GetInt() - Main.SKMadmateNowCount}]"));

        return ProgressText.ToString();
    }
    public static string GetTaskProgressText(byte playerId, bool comms = false)
    {
        var state = PlayerState.GetByPlayerId(playerId);
        if (state == null || state.taskState == null || !state.taskState.hasTasks)
        {
            return "";
        }

        Color TextColor;
        var info = GetPlayerInfoById(playerId);
        var TaskCompleteColor = HasTasks(info) ? ColorGreen : GetRoleColor(state.GetNowMainRole()).ShadeColor(0.5f);
        //タスク完了後の色
        var NonCompleteColor = HasTasks(info) ? ColorYellow : ColorWhite;
        //カウントされない人外は白色
        if (Workhorse.IsThisRole(playerId))
            NonCompleteColor = Workhorse.RoleColor;

        var NormalColor = state.taskState.IsTaskFinished ? TaskCompleteColor : NonCompleteColor;
        if (state.taskState.GetNeedCountOrAll() <= state.taskState.CompletedTasksCount) NormalColor = TaskCompleteColor;

        TextColor = comms ? ColorGray : NormalColor;
        string Completed = comms ? "?" : $"{state.taskState.CompletedTasksCount}";
        return ColorString(TextColor, $"({Completed}/{state.taskState.GetNeedCountOrAll()})");

    }
    // Color.yellow などの static プロパティは呼ぶたびにゲーム側を通るので、値で持っておく (Unity の定義と同じ値)
    private static readonly Color ColorYellow = new() { r = 1f, g = 0.92156863f, b = 0.015686275f, a = 1f };
    private static readonly Color ColorGreen = new() { r = 0f, g = 1f, b = 0f, a = 1f };
    private static readonly Color ColorWhite = new() { r = 1f, g = 1f, b = 1f, a = 1f };
    private static readonly Color ColorGray = new() { r = 0.5f, g = 0.5f, b = 0.5f, a = 1f };
    public static (int, int) GetTasksState()
    {
        var completed = 0;
        var all = 0;
        foreach (var pc in Main.AllPlayerControls)
        {
            var taskState = PlayerState.GetByPlayerId(pc.PlayerId).taskState;
            if (taskState.hasTasks && HasTasks(pc.Data))
            {
                completed += taskState.CompletedTasksCount;
                all += taskState.AllTasksCount;
            }
        }
        (int vtComp, int vtTotal) = VentEnterTask.TaskWinCountData();
        return (completed + vtComp, all + vtTotal);
    }

    public static void ShowMyRoleInfo(PlayerControl player, byte PlayerId = byte.MaxValue)
    {
        if (!GameStates.IsInGame) return;
        // /h r (役職辞典) と同じ形で出す: 大きな役職名 → イントロ → 陣営・置き換え役職・判定 → From → 説明 (1 文 1 行) → 設定
        // 他の役職として見せる役職は、名前・色・設定の一覧をその役職で出す。長い説明は正体を書いているので、短い説明を出す
        var displayedRole = player.GetRoleClass()?.DisplayedRole;
        var myRole = displayedRole ?? player.GetCustomRole();
        var roleInfoLong = player.GetRoleInfo(displayedRole == null);
        var intro = displayedRole == null ? player.GetRoleInfo(false) : null;
        var roleName = player.GetRoleClass()?.GetRoleKey(true) ?? myRole.ToString();
        var title = ColorString(GetRoleColor(myRole), GetString("RoleInfoTitle"));
        SendRoleCard(myRole, GetString(roleName), intro, roleInfoLong, PlayerId, !myRole.IsDontShowOptionRole(), title);

        foreach (var subRole in player.GetCustomSubRoles())
        {
            if (subRole == CustomRoles.NotAssigned) continue;
            // 役職を預かる印の属性は、預かっている役職の説明を出す (その役職そのものなら出さない)
            var standsFor = AddOnList.Get(subRole)?.StandsFor ?? CustomRoles.NotAssigned;
            if (standsFor != CustomRoles.NotAssigned && myRole == standsFor) continue;
            var shown = standsFor != CustomRoles.NotAssigned ? standsFor : subRole;
            var key = shown.ToString();
            SendRoleCard(shown, GetString(key), Translator.HasString(key + "Info") ? GetString(key + "Info") : null,
                GetString($"{key}InfoLong"), PlayerId, false);
        }
    }

    /// <summary>
    /// 役職の説明を役職辞典 (/h r) と同じ形で送る。name は出す名前、intro は名前の下の一言 (無ければ null)、
    /// infoLong は長い説明 (最後の From は上の行に出すので外す)。withSettings なら設定を別の通で送る
    /// </summary>
    static void SendRoleCard(CustomRoles role, string name, string intro, string infoLong, byte PlayerId, bool withSettings, string title = "")
    {
        infoLong ??= "";
        foreach (var tail in new[] { FromLine(role), FromLine(role.VanillaRoleConversion()) })
            if (tail.Length > 0 && infoLong.EndsWith(tail)) infoLong = infoLong[..^tail.Length];
        var color = GetRoleColor(role).ToReadableColor();
        string formatTag = "<size=75%><line-height=1.7pic>";

        // 行の高さを決めると、大きな字の行が次の行と重なる。見出しの 2 行には行の高さを付けず、字の大きさに任せる
        var sb0 = new StringBuilder();
        sb0.Append("<size=75%><size=140%><b>").Append(name.Color(color)).Append("</b></size>\n");
        if (!string.IsNullOrEmpty(intro))
            sb0.Append("<size=110%>").Append(intro.Trim()).Append("</size>\n");
        sb0.Append("<line-height=1.7pic>");
        sb0.Append("<#9aa4b2>").Append(GetString("RoleDictionary.Team")).Append("<pos=34%>").Append(GetString("RoleDictionary.BaseRole"))
           .Append("<pos=68%>").Append(GetString("Basis")).Append("</color>\n");
        sb0.Append(RoleTeamText(role)).Append("<pos=34%>").Append(RoleBaseText(role)).Append("<pos=68%>").Append(RoleCountText(role)).Append('\n');
        var from = Roles.Core.RoleFrom.GetText(role);
        if (!string.IsNullOrEmpty(from)) sb0.Append(from).Append('\n');
        var detail = SplitSentences(StripTeamPrefix(infoLong));
        if (detail.Length > 0)
            sb0.Append("<#7fdcff>■ ").Append(GetString("RoleDictionary.Description")).Append("</color>\n").Append(detail);
        SendMessageAutoSplit(sb0.ToString(), false, title, PlayerId, formatTag);

        if (withSettings && Options.CustomRoleSpawnChances.TryGetValue(role, out var opt))
        {
            var sb = new StringBuilder();
            ShowChildrenSettings(opt, ref sb);
            if (sb.Length > 0)
            {
                formatTag = "<size=70%><line-height=1.6pic>";
                sb.Insert(0, $"{formatTag}<#b7ff8c>■ {GetString("RoleDictionary.Settings")}</color>（{name}）\n");
                SendMessageAutoSplitAndClearSB(PlayerId, sb, "", formatTag);
            }
        }
    }

    private static string GetRoleInfoLongText(CustomRoles role)
    {
        var r = role.VanillaRoleConversion();
        string roleInfoLong = "";
        if (Translator.HasString($"{role}InfoLong"))
        {
            roleInfoLong = GetString($"{role}InfoLong");
        }
        else if (Translator.HasString($"{r}InfoLong"))
        {
            roleInfoLong = GetString($"{r}InfoLong");
        }
        else if (r.IsVanilla())
        {
            var tag = "BlurbLong";
            if (r is CustomRoles.Impostor or CustomRoles.Crewmate or CustomRoles.Viper)
            {
                tag = "Blurb";
            }
            roleInfoLong = "\n" + GetString($"{r}{tag}");
        }
        else
        {
            roleInfoLong = GetString($"{role.ToString()}InfoLong");
        }

        return roleInfoLong + FromLine(role);
    }

    public static string FromLine(CustomRoles role)
    {
        var from = TownOfHostL.Roles.Core.RoleFrom.GetText(role);
        return from.Length == 0 ? "" : "\n" + from;
    }

    public static string GetRoleInfoLongWithColor(CustomRoles role)
    {
        string roleInfoLong = GetRoleInfoLongText(role);

        return $"{GetString(role.ToString()).Color(GetRoleColor(role).ToReadableColor())}\n{roleInfoLong}";
    }

    public static void ShowRoleInfoLong(CustomRoles role, byte PlayerId = byte.MaxValue)
    {
        // 並び: 大きな役職名 → 少し大きなイントロ → 陣営・置き換え役職・判定の表 → From → 説明 (1 文 1 行) → 設定 (別の通)
        var r = role.VanillaRoleConversion();
        SendRoleCard(role, GetString(role.ToString()), Translator.HasString(role + "Info") ? GetString(role + "Info") : null,
            GetRoleInfoLongText(r), PlayerId, !role.IsDontShowOptionRole());
    }

    // 陣営の名前を陣営の色で (属性は「属性」)
    static string RoleTeamText(CustomRoles role)
    {
        if (role.IsAddOn() || role.IsOtherAddOn()) return "<#ffd98c>" + GetString("RoleDictionary.AddOn") + "</color>";
        return role.GetCustomRoleTypes() switch
        {
            CustomRoleTypes.Impostor => "<#ff5a5a>" + GetString("TeamImpostor") + "</color>",
            CustomRoleTypes.Madmate => "<#ff7340>" + GetString("Madmate") + "</color>",
            CustomRoleTypes.Neutral => "<#d9a6ff>" + GetString("Neutral") + "</color>",
            CustomRoleTypes.Unit => "<#99ff99>" + GetString("RoleDictionary.Unit") + "</color>",
            _ => "<#8cd9ff>" + GetString("TeamCrewmate") + "</color>",
        };
    }

    // 本編のどの役職として動くか (置き換え役職)。属性には無いので「-」
    static string RoleBaseText(CustomRoles role)
    {
        if (role.IsAddOn() || role.IsOtherAddOn()) return "-";
        var rt = role.GetRoleTypes();
        var key = rt.ToString();
        var name = Translator.HasString(key) ? GetString(key) : key;
        var col = rt is RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom or RoleTypes.ImpostorGhost ? "ff5a5a" : "8cd9ff";
        return $"<#{col}>{name}</color>";
    }

    // 勝ち負けを決める時に、どの側として数えるか (判定)
    static string RoleCountText(CustomRoles role)
    {
        var info = role.GetRoleInfo();
        if (info == null) return "-";
        var c = info.CountType;
        switch (c)
        {
            case CountTypes.Crew: return "<#8cd9ff>" + GetString("TeamCrewmate") + "</color>";
            case CountTypes.Impostor: return "<#ff5a5a>" + GetString("TeamImpostor") + "</color>";
            case CountTypes.None:
            case CountTypes.OutOfGame: return GetString("RoleDictionary.CountNone");
        }
        var key = c.ToString();
        if (!Translator.HasString(key)) return key;
        var col = Enum.TryParse<CustomRoles>(key, out var cr) ? GetRoleColor(cr).ToReadableColor() : color2;
        return GetString(key).Color(col);
    }
    static readonly Color color2 = Color.white;

    // 説明の頭の「(クルー陣営):」「[クルー陣営]」は陣営の行で出すので外す
    static readonly System.Text.RegularExpressions.Regex TeamPrefix = new(@"^\s*(\([^\n()]{1,20}\)\s*[:：]?|\[[^\n\[\]]{1,20}\]|【[^\n【】]{1,20}】)\s*\n?");
    static string StripTeamPrefix(string s) => string.IsNullOrEmpty(s) ? "" : TeamPrefix.Replace(s.Replace("\\n", "\n"), "", 1).Trim();

    // 長い説明を 1 文ごとに改行する (「。」の後。括弧を閉じる前では切らない)
    static readonly System.Text.RegularExpressions.Regex SentenceEnd = new(@"。(?![\n」』）)】]|$)");
    static string SplitSentences(string s) => string.IsNullOrEmpty(s) ? "" : SentenceEnd.Replace(s, "。\n");

    // Help Now
    public static void ShowActiveSettingsHelp(byte PlayerId = byte.MaxValue)
    {
        string formatTag = "<size=65%><line-height=1.5pic>";

        if (ModeCatalog.IsCatchCat)
        {
            CatchCat.Infomation.ShowSettingHelp(PlayerId);
        }
        else
        {
            //if (Options.IsONMode)
            //{
            //    SendMessage(GetString("ONInfoStart") + ":", PlayerId);
            //    SendMessage(GetString("ONInfo1"), PlayerId);
            //    SendMessage(GetString("ONInfo2"), PlayerId);
            //    SendMessage(GetString("ONInfo3"), PlayerId);
            //}
            //else

            //if (Options.DisableDevices.GetBool()) { SendMessage(GetString("DisableDevicesInfo"), PlayerId); }
            //if (Options.SyncButtonMode.GetBool()) { SendMessage(GetString("SyncButtonModeInfo"), PlayerId); }
            //if (Options.SabotageTimeControl.GetBool()) { SendMessage(GetString("SabotageTimeControlInfo"), PlayerId); }

            formatTag = "<size=70%><line-height=1.6pic>";
            if (Options.RandomMapsMode.GetBool())
            {
                SendMessageAutoSplit($"{formatTag}{GetString("RandomMapsModeInfo")}", false, "", PlayerId, formatTag);
            }
            if (Options.EnableGM.GetBool())
            {
                SendMessageAutoSplit($"{formatTag}{GetRoleName(CustomRoles.GM)}{GetString("GMInfoLong")}", false, "", PlayerId, formatTag);
            }

            foreach (var role in RoleOrder.Sort(CustomRolesHelper.AllStandardRoles))
            {
                //if (Options.IsONMode && !role.IsONRole()) continue;
                if (!role.IsEnable() || role.IsVanilla()) continue;
                if (role is CustomRoles.NormalImpostor) continue;

                string infoLongText = "";
                var roleName = Enum.GetName(typeof(CustomRoles), role);
                var vanillaName = Enum.GetName(typeof(CustomRoles), role.VanillaRoleConversion());
                if (Translator.HasString(roleName + "InfoLong"))
                {
                    infoLongText = GetString(roleName + "InfoLong");
                }
                else if (Translator.HasString(vanillaName + "InfoLong"))
                {
                    infoLongText = GetString(vanillaName + "InfoLong");
                }
                else if (role.IsNormalVanillaRole())
                {
                    infoLongText = '\n' + GetString(vanillaName + "BlurbLong");
                }
                else
                {
                    infoLongText = GetString(roleName + "InfoLong");
                }

                var sb = new StringBuilder();
                formatTag = "<size=70%><line-height=1.6pic>";

                sb.Append($"<size=85%>{GetRoleName(role)}</size>".Color(GetRoleColor(role).ToReadableColor()))
                    .Append(formatTag).Append(infoLongText);
                SendMessageAutoSplitAndClearSB(PlayerId, sb, "", formatTag);

                //setting
                formatTag = "<size=60%><line-height=1.5pic>";
                ShowChildrenSettings(Options.CustomRoleSpawnChances[role], ref sb);
                if (sb.Length > 0)
                {
                    sb.Insert(0, formatTag);
                    SendMessageAutoSplit(sb.ToString(), false, "", PlayerId, formatTag);
                }
            }

            foreach (var role in RoleOrder.Sort(CustomRolesHelper.AllAddOnRoles.Where(role => role.IsOtherAddOn())))
            {
                if (!role.IsEnable()) continue;
                var addonName = role.ToString();
                var sb = new StringBuilder();
                formatTag = "<size=70%><line-height=1.6pic>";

                sb.Append($"<size=85%>{GetRoleName(role)}</size>".Color(GetRoleColor(role).ToReadableColor()))
                    .Append(formatTag).Append(GetString($"{addonName}InfoLong"));
                SendMessageAutoSplitAndClearSB(PlayerId, sb, "", formatTag);

                //setting
                formatTag = "<size=60%><line-height=1.5pic>";
                ShowChildrenSettings(Options.CustomRoleSpawnChances[role], ref sb);
                if (sb.Length > 0)
                {
                    sb.Insert(0, formatTag);
                    SendMessageAutoSplit(sb.ToString(), false, "", PlayerId, formatTag);
                }
            }

            foreach (var role in RoleOrder.Sort(CustomRolesHelper.AllAddOnRoles.Where(role => role.IsAddOn())))
            {
                if (!role.IsEnable()) continue;
                var addonName = role.ToString();
                var sb = new StringBuilder();
                formatTag = "<size=70%><line-height=1.6pic>";

                sb.Append($"<size=85%>{GetRoleName(role)}</size>".Color(GetRoleColor(role).ToReadableColor()))
                    .Append(formatTag).Append(GetString($"{addonName}InfoLong"));

                SendMessageAutoSplit(sb.ToString(), false, "", PlayerId, formatTag);
            }
        }
        if (Options.NoGameEnd.GetBool())
        {
            formatTag = "<size=70%><line-height=1.6pic>";
            SendMessageAutoSplit($"{formatTag}{GetString("NoGameEndInfo")}", false, "", PlayerId, formatTag);
        }
    }
    // Now
    /// <param name="settingsOnly">true (/n s) なら役職の確率は出さず、アサインと設定だけを出す</param>
    public static void ShowActiveSettings(byte PlayerId = byte.MaxValue, bool settingsOnly = false)
    {
        bool roleAssignRandomMode = RoleAssignManager.OptionAssignMode.GetBool();
        string formatTag = $"<size={ActiveSettingsSize}>";

        var title = $"【{GetString("Settings")}】".Color(Color.yellow);
        if (Options.HideGameSettings.GetBool() && PlayerId != byte.MaxValue)
        {
            SendMessageAutoSplit(GetString("Message.HideGameSettings"), true, title, PlayerId);
            return;
        }
        var sb = new StringBuilder();
        if (ModeCatalog.IsCatchCat)
        {
            CatchCat.Infomation.ShowSetting(sb);
            SendMessageAutoSplitAndClearSB(PlayerId, sb, title, formatTag);
        }
        else
        {
            //if (Options.IsONMode)
            //{
            //    sb.Append(GetString("ONInfoWarning")).Append("\n");
            //    sb.Append(GetString("Settings")).Append(":");
            //    foreach (var role in Options.CustomRoleCounts)
            //    {
            //        if (role.Key.IsEnable() && role.Key.IsOneNightRoles())
            //        {
            //            sb.Append($"\n【{GetRoleName(role.Key)}×{role.Key.GetCount()}】\n");
            //            ShowChildrenSettings(Options.CustomRoleSpawnChances[role.Key], ref sb);
            //            var text = sb.ToString();
            //            sb.Clear().Append(text);
            //        }
            //    }
            //}
            //else
            {
                sb.AppendFormat("<size={0}>", ActiveSettingsSize);

                formatTag = "<size=65%><line-height=1.5pic>";
                if (PlayerCountComposition.IsOn)
                {
                    // 人数ごとの構成: 今の人数の分だけ出す
                    var players = GameData.Instance ? GameData.Instance.PlayerCount : 0;
                    sb.AppendFormat("<size={0}>{1}\n<line-height=1.5pic>", "65%", SettingHead($"{PlayerCountComposition.Enabled.GetName(true)} ({players})"));
                    foreach (var type in new[] { CustomRoleTypes.Impostor, CustomRoleTypes.Madmate, CustomRoleTypes.Crewmate, CustomRoleTypes.Neutral })
                    {
                        var (min, max) = PlayerCountComposition.Get(players, type);
                        sb.Append(SettingLine(GetString($"CustomRoleTypes.{type}"), min == max ? $"{min}" : $"{min}〜{max}")).Append('\n');
                    }
                }
                else
                {
                    sb.AppendFormat("<size={0}>{1}\n<line-height=1.5pic>", "65%", SettingHead($"{RoleAssignManager.OptionAssignMode.GetName(true)}: {RoleAssignManager.OptionAssignMode.GetString()}"));
                    if (roleAssignRandomMode)
                    {
                        ShowChildrenSettings(RoleAssignManager.OptionAssignMode, ref sb);
                    }
                }
                sb.Append("\n</line-height></size>");
                SendMessageAutoSplitAndClearSB(PlayerId, sb, title, formatTag);
                if (!settingsOnly)
                {

                // 区分の見出しを付けて設定画面と同じ順に並べ、1 通に入る分ずつ詰めて送る (役職の途中では割らない)
                formatTag = "<size=65%><line-height=1.5pic>";
                var blocks = new List<string>();
                TabGroup? lastTab = null;
                foreach (var role in RoleOrder.Sort(Options.CustomRoleCounts.Keys))
                {
                    if (!role.IsEnable() || role.IsHASRole() || role.IsCCRole())
                    {
                        continue;
                    }
                    var tab = RoleOrder.TabOf(role);
                    if (tab is TabGroup t && tab != lastTab) sb.Append("<size=85%>").Append(RoleOrder.Header(t)).Append("</size>\n");
                    lastTab = tab;

                    sb.Append("<size=85%>").Append($"<u><b>{GetRoleName(role)}</b></u>".Color(GetRoleColor(role).ToReadableColor()))
                        .Append(' ').Append(RoleChanceCount(role, roleAssignRandomMode)).Append("</size>");
                    int head = sb.Length;
                    ShowChildrenSettings(Options.CustomRoleSpawnChances[role], ref sb);
                    if (sb.Length > head)
                    {
                        while (sb.Length > head && sb[^1] == '\n') sb.Length--;
                        // 設定の行の字の大きさは、各通の頭に付く formatTag に任せる (</size> で 1 つ前の大きさに戻る)
                        sb.Insert(head, "\n");
                    }
                    blocks.Add(sb.ToString());
                    sb.Clear();
                }
                SendBlocks(blocks, "", PlayerId, formatTag);
                }
            }

            formatTag = "<size=65%><line-height=1.5pic>";
            foreach (var opt in OptionItem.AllOptions.Where(x => x.GetBool() && x.Parent == null && x.IsFeatureOption && x.IsShown()))
            {
                // 常時表示しないオプション
                if (Options.NotShowOption(opt.Name)) continue;
                // 人数ごとの構成は上で今の人数の分だけ出したので、ここでは人数ごとの表を並べない
                if (opt == PlayerCountComposition.Enabled) continue;

                // アクティブマップ毎に表示しないオプション
                if (opt.Name == "MapOption_Skeld" && !Options.IsActiveSkeld) continue;
                if (opt.Name == "MapOption_MiraHQ" && !Options.IsActiveMiraHQ) continue;
                if (opt.Name == "MapOption_Polus" && !Options.IsActivePolus) continue;
                if (opt.Name == "MapOption_Airship" && !Options.IsActiveAirship) continue;
                if (opt.Name == "MapOption_Fungle" && !Options.IsActiveFungle) continue;

                if (opt.Name is "NameChangeMode" && Options.GetNameChangeModes() != NameChange.None
                    || opt.Name is "SyncColorMode" && Options.GetSyncColorMode() != SyncColorMode.None)
                {
                    sb.Append($"<size=72%>{SettingHead($"{opt.GetName(true)}: {opt.GetString()}")}\n</size>");
                }
                else
                {
                    sb.Append($"<size=72%>{SettingHead(opt.GetName(true))}\n</size>");
                }

                sb.Append(formatTag);
                ShowChildrenSettings(opt, ref sb);
                sb.Append("</line-height>\n</size>");
            }
            SendMessageAutoSplitAndClearSB(PlayerId, sb, formatTag: formatTag);
        }
    }

    public static void CopyCurrentSettings()
    {
        var sb = new StringBuilder();
        if (Options.HideGameSettings.GetBool() && !AmongUsClient.Instance.AmHost)
        {
            ClipboardHelper.PutClipboardString(GetString("Message.HideGameSettings"));
            return;
        }
        sb.Append($"━━━━━━━━━━━━【{GetString("Roles")}】━━━━━━━━━━━━");
        foreach (var role in Options.CustomRoleCounts.OrderBy(kv => RoleOrder.Key(kv.Key)))
        {
            if (!role.Key.IsEnable() || role.Key.IsHASRole() || role.Key.IsCCRole() /*|| role.Key.IsONRole()*/) continue;

            if (role.Key.IsAddOn() || role.Key.IsOtherAddOn())
                sb.Append($"\n〖{GetRoleName(role.Key)}×{role.Key.GetCount()}〗\n");
            else
                sb.Append($"\n【{GetRoleName(role.Key)}×{role.Key.GetCount()}】\n");
            ShowChildrenSettings(Options.CustomRoleSpawnChances[role.Key], ref sb);
            var text = sb.ToString();
            sb.Clear().Append(text.RemoveHtmlTags());
        }
        sb.Append($"━━━━━━━━━━━━【{GetString("Settings")}】━━━━━━━━━━━━");
        foreach (var opt in OptionItem.AllOptions.Where(x => x.GetBool() && x.Parent == null && x.IsFeatureOption && x.IsShown()))
        {
            if (!Options.NotShowOption(opt.Name))
                sb.Append($"\n【{opt.GetName(true)}】\n");
            ShowChildrenSettings(opt, ref sb);
            var text = sb.ToString();
            sb.Clear().Append(text.RemoveHtmlTags());
        }
        sb.Append($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        ClipboardHelper.PutClipboardString(sb.ToString());
    }
    // Now Role
    public static void ShowActiveRoles(byte PlayerId = byte.MaxValue)
    {
        if (Options.HideGameSettings.GetBool() && PlayerId != byte.MaxValue)
        {
            SendMessageAutoSplit(GetString("Message.HideGameSettings"), true, sendTo: PlayerId);
            return;
        }

        bool roleAssignRandomMode = RoleAssignManager.OptionAssignMode.GetBool();
        string formatTag = $"<size=80%><line-height=1.8pic>";
        var sb = new StringBuilder();

        //if (Options.IsONMode) sb.Append("\n").Append(GetString("ONInfoWarning")).Append("\n");
        if (Options.EnableGM.GetBool())
        {
            sb.AppendFormat("{0}{1} ：{2}</size>\n</line-height>", formatTag, $"<color={GetRoleColorCode(CustomRoles.GM)}>{GetRoleName(CustomRoles.GM)}</color>", Options.EnableGM.GetString());
        }
        // TOHY独自のMODゲームモードがあるため各モードに分けてに書き換え
        if (!ModeCatalog.Current.UsesStandardRoles)
        {
            CustomRoles[] targetRoles = Array.Empty<CustomRoles>();
            if (ModeCatalog.IsCatchCat) targetRoles = CustomRolesHelper.AllCCRoles.Where(role => role.IsCCLeaderRoles()).ToArray();
            //else if (Options.IsONMode) targetRoles = CustomRolesHelper.AllONRoles;

            sb.Append(formatTag);
            foreach (CustomRoles role in targetRoles)
            {
                if (!role.IsEnable()) continue;

                // 役職名表示
                sb.Append($" {GetRoleName(role)}".Color(GetRoleColor(role)));
                // 確率＆人数
                sb.Append($" x{role.GetCount()}\n");
            }
            SendMessageAutoSplitAndClearSB(PlayerId, sb, $"【{GetString("Roles")}】".Color(Color.yellow), formatTag);
        }
        else
        {
            // 区分ごとに見出しを付け、区分の中は設定画面と同じ順。1 行 1 役職で、陣営の印は見出しに任せる
            // (1 行のタグを減らすほど 1 通に多く入る)。1 通に入る分ずつ区分の境で詰めて送る
            var blocks = new List<string>();
            if (sb.Length > 0)
            {
                blocks.Add(sb.ToString());
                sb.Clear();
            }
            TabGroup? lastTab = null;
            bool first = true;
            foreach (var role in RoleOrder.Sort(CustomRolesHelper.AllStandardRoles.Concat(CustomRolesHelper.AllAddOnRoles)))
            {
                if (!role.IsEnable()) continue;
                // バニラ役職(元)は反映させないので表示させない
                if (role.IsVanilla()) continue;
                var tab = RoleOrder.TabOf(role);
                if (first || tab != lastTab)
                {
                    if (sb.Length > 0) blocks.Add(sb.ToString());
                    sb.Clear();
                    if (tab is TabGroup t) sb.Append(RoleOrder.Header(t));
                }
                first = false;
                lastTab = tab;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append('<').Append(GetRoleColorCode(role)).Append('>').Append(GetRoleName(role)).Append("</color>")
                    .Append(RoleChanceCountColumns(role, roleAssignRandomMode));
            }
            if (sb.Length > 0) blocks.Add(sb.ToString());
            sb.Clear();
            SendBlocks(blocks, $"【{GetString("Roles")}】".Color(Color.yellow), PlayerId, formatTag);
        }
    }

    /// <summary>
    /// 役職の出現率と人数の短い書き方 (「×1」「50%×1」)。出現率は 100% でない時に出す
    /// (割り当てが固定でも 100% 未満は確率で抜けるので)。ランダム割り当ての時は 100% も出す
    /// </summary>
    /// <summary>
    /// /h r (役職名なし) の案内。使い方と例だけを 1 通で出す (役職の一覧は /n r で見られると添える)。
    /// 役職の名前を全部並べると何通にもなって流れるので、ここでは並べない
    /// </summary>
    public static void ShowRoleHelpIndex(byte PlayerId = byte.MaxValue)
    {
        bool hide = Options.HideGameSettings.GetBool() && PlayerId != byte.MaxValue;
        var first = hide ? new List<CustomRoles>()
            : RoleOrder.Sort(CustomRolesHelper.AllStandardRoles).Where(r => r.IsEnable() && !r.IsVanilla() && r != CustomRoles.NormalImpostor).Take(1).ToList();
        var example = GetRoleName(first.Count > 0 ? first[0] : CustomRoles.Sheriff);
        var text = string.Format(GetString("RoleHelpIndexUsage"), example);
        if (!hide) text += "\n" + GetString("RoleHelpIndexActive");
        SendMessageAutoSplit("<size=80%><line-height=1.7pic>" + text, true, $"【{GetString("RoleHelpIndexTitle")}】".Color(Color.yellow), PlayerId, "<size=80%><line-height=1.7pic>");
    }
    public static string RoleChanceCountColumns(CustomRoles role, bool alwaysChance = false)
    {
        // 役職名の長さに関係なく、% と ×N が縦にそろうよう、決まった位置から書く。
        // 出現率は数字と同じ幅の空白 (U+2007) で 3 桁にそろえ、位置の指定は 1 つで済ませる (1 通に入る行を減らさない)
        string chance = role.GetChance().ToString();
        var text = $"<pos=60%>{new string(' ', Math.Max(0, 3 - chance.Length))}{chance}%×{role.GetCount()}";
        return role.IsPairRole() ? text + GetString("Pair") : text;
    }
    public static string RoleChanceCount(CustomRoles role, bool alwaysChance = false)
    {
        int chance = role.GetChance();
        string text = chance < 100 || alwaysChance ? $"{chance}%×{role.GetCount()}" : $"×{role.GetCount()}";
        return role.IsPairRole() ? text + GetString("Pair") : text;
    }

    public static void ShowChildrenSettings(OptionItem option, ref StringBuilder sb, int deep = 0)
    {
        foreach (var opt in option.Children.Select((v, i) => new { Value = v, Index = i + 1 }))
        {
            if (opt.Value.Name == "Maximum") continue;//Maximumの項目は飛ばす
            // 設定画面で隠れている項目 (他の項目の値で出したり隠したりする物) は出さない
            if (!(opt.Value.EnabledCondition?.Invoke() ?? true)) continue;
            if (opt.Value.Parent.Name == "displayComingOut%type%" && !opt.Value.GetBool()) continue;

            if (opt.Value.Parent.Name == "AddOnBuffAssign" && !opt.Value.GetBool()) continue;
            if (opt.Value.Parent.Name == "AddOnBuffAssign%role%" && !opt.Value.GetBool()) continue;
            if (opt.Value.Parent.Name == "AddOnDebuffAssign" && !opt.Value.GetBool()) continue;
            if (opt.Value.Parent.Name == "AddOnDebuffAssign%role%" && !opt.Value.GetBool()) continue;
            if (opt.Value.Parent.Name == "SkinControle" && !opt.Value.GetBool()) continue;
            if (opt.Value.Parent.Name == "DisableTasks" && !opt.Value.GetBool()) continue;
            if (opt.Value.Parent.Name == "EvilHackerFixedRole" && !opt.Value.GetBool()) continue;

            if (deep > 0)
            {
                sb.Append(string.Concat(Enumerable.Repeat("┃", Mathf.Max(deep - 1, 0))));
                sb.Append(opt.Index == option.Children.Count ? "┗ " : "┣ ");
            }
            sb.Append(SettingLine(opt.Value.GetName(true), opt.Value.GetString())).Append('\n');
            if (opt.Value.GetBool()) ShowChildrenSettings(opt.Value, ref sb, deep + 1);
        }
    }
    /// <summary>設定の見出し (/n): 黄色の ■ と太字</summary>
    public static string SettingHead(string name) => $"<color=#ffe36e><b>■ {name}</b></color>";
    /// <summary>設定の 1 行 (/n): 名前は白、値は水色にして見分けやすくする</summary>
    public static string SettingLine(string name, string value) => $"{name}：<color=#8fd3ff>{value}</color>";
    public static void ShowVanillaSetting(byte PlayerId = byte.MaxValue)
    {
        string formatTag = $"<size=70%><line-height=1.6pic>";

        StringBuilder message = new();
        message.Append(formatTag);
        foreach (var s in GameOptionsManager.Instance.CurrentGameOptions.ToHudString(GameData.Instance ? GameData.Instance.PlayerCount : 10))
        {
            if (s == '科') break;
            message.Append(s);
        }
        SendMessageAutoSplit(message.ToString(), true, "【Vanilla Setting】", PlayerId, formatTag);
    }

    public static void ShowLastResult(byte PlayerId = byte.MaxValue)
    {
        if (AmongUsClient.Instance.IsGameStarted)
        {
            SendMessageAutoSplit(GetString("CantUse.lastresult"), true, sendTo: PlayerId);
            return;
        }
        var sb = new StringBuilder();
        var winnerColor = ((CustomRoles)CustomWinnerHolder.WinnerTeam).GetRoleInfo()?.RoleColor ?? Palette.DisabledGrey;

        sb.Append("""<align="center">""");
        sb.Append("<size=120%>").Append(GetString("LastResult")).Append("</size>").Append('\n');
        sb.Append(SetEverythingUpPatch.LastWinsText.Mark(winnerColor, false)).Append('\n');
        sb.Append("</align>");

        string formatTag = $"<size=70%><line-height=1.6pic>";

        sb.Append(formatTag);
        List<byte> cloneRoles = new(PlayerState.AllPlayerStates.Keys);
        foreach (var id in Main.winnerList)
        {            
            sb.Append($"★ ".Color(winnerColor)).Append(SummaryTexts(id, true)).Append('\n');
            cloneRoles.Remove(id);
        }
        SendMessageAutoSplitAndClearSB(PlayerId, sb, formatTag: formatTag);

        sb.Append(formatTag);
        foreach (var id in cloneRoles)
        {
            sb.Append($"　 ").Append(SummaryTexts(id, true)).Append('\n');
        }
        SendMessageAutoSplitAndClearSB(PlayerId, sb, formatTag: formatTag);
    }
    public static void ShowKillLog(byte PlayerId = byte.MaxValue)
    {
        string formatTag = $"<size=70%><line-height=1.6pic>";

        if (GameStates.IsInGame)
        {
            SendMessageAutoSplit($"{formatTag}{GetString("CantUse.killlog")}", true, "", PlayerId, formatTag);
            return;
        }
        SendMessageAutoSplit($"{formatTag}{EndGamePatch.KillLog}", true, "", PlayerId, formatTag);
    }

    public static string GetTeamMark(CustomRoles role, int sizePer, bool sizeTag = true)
    {
        string text = "　";
        if (role.IsImpostor()) text = "<color=#ff1919>Ⓘ</color>";
        else if (role.IsMadmate()) text = "<color=#ff4500>Ⓜ</color>";
        else if (role.IsCrewmate()) text = "<color=#7ee6e6>Ⓒ</color>";
        else if (role.IsNeutral()) text = "<color=#ffa500>Ⓝ</color>";

        return sizeTag ? $"<size={sizePer}%>{text}</size>": text;
    }

    private static void SendMessageAutoSplitAndClearSB(byte PlayerId, StringBuilder sb, string title = "", string formatTag = "")
    {
        bool showTitle = title != string.Empty;
        SendMessageAutoSplit(sb.ToString(), showTitle, title, PlayerId, formatTag);
        sb.Clear();
    }

    /*public static void SendMessage(string text, byte sendTo = byte.MaxValue, string title = "")
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (title == "") title = "<color=#aaaaff>" + GetString("DefaultSystemMessageTitle") + "</color>";
        else title = title.Color(Color.white);

        Logger.Info($"[MessagesToSend.Add] sendTo: {sendTo}", "SendMessage");
        Main.MessagesToSend.Add(($"<align={"left"}><size=90%>{text}</size></align>", sendTo, $"<align={"left"}>{title}</align>", false));
    }*/
    // 宛先ごとの送信待ちの上限。超えた分は送らない (返事の連打で、同じ宛先への送信が長く続かないようにする)。
    // 宛先ごとに数えるので、試合の開始に全員へ役職の説明を送る時のように宛先が多くても削られない
    private const int MaxQueuedMessagesPerTarget = 20;
    private static void EnqueueMessage((string text, byte sendTo, string title, bool custom) message)
    {
        int queued = 0;
        foreach (var x in Main.MessagesToSend)
        {
            if (x.Item2 != message.sendTo) continue;
            // 同じ宛先への同じ文が送信待ちにあれば、重ねて送らない
            if (x.Item1 == message.text) return;
            queued++;
        }
        if (queued >= MaxQueuedMessagesPerTarget)
        {
            Logger.Warn($"宛先 {message.sendTo} への送信待ちが {MaxQueuedMessagesPerTarget} 件あるので送りません", "SendMessage");
            return;
        }
        Main.MessagesToSend.Add(message);
    }
    /// <summary>
    /// 本文をそのまま 1 通送る (送り主の名前はお知らせの見出し)。装飾や分割を足さないので、短い決まった文だけに使う
    /// </summary>
    public static void SendRawChat(string text, byte sendTo = byte.MaxValue)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        EnqueueMessage((text, sendTo, $"<color=#aaaaff>{GetString("DefaultSystemMessageTitle")}</color>", false));
    }
    private const int MAX_LENGTH = 320;
    private const int MAX_LINES = 13;
    private const string FORMAT_TAG_START = "<align=left><color=#ffffff>";

    private static bool FitsOneMessage(string fullText, int maxBytes)
        => fullText.Length <= MAX_LENGTH && fullText.Split('\n').Length <= MAX_LINES
            && Encoding.UTF8.GetByteCount(fullText) <= maxBytes;

    /// <summary>
    /// まとまり (区分の見出しと役職の行など) を、1 通に入る分ずつ詰めて送る。まとまりの途中では割らない
    /// (1 つで 1 通に入らない時だけ SendMessageAutoSplit が割る)。見出しは最初の 1 通だけ。formatTag は各通の頭に付ける
    /// </summary>
    public static void SendBlocks(IReadOnlyList<string> blocks, string title, byte sendTo, string formatTag)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        int maxBytes = TownOfHostL.Core.ChatSenderName.BodyBudget(TownOfHostL.Core.ChatSenderName.Sender());
        bool showTitle = title != "";
        string titleText = showTitle ? title + "\n" : "";
        var cur = new StringBuilder();
        foreach (var block in blocks)
        {
            if (string.IsNullOrEmpty(block)) continue;
            if (cur.Length > 0)
            {
                if (FitsOneMessage($"{FORMAT_TAG_START}{titleText}{formatTag}{cur}\n{block}", maxBytes))
                {
                    cur.Append('\n').Append(block);
                    continue;
                }
                SendMessageAutoSplit(formatTag + cur, showTitle, title, sendTo, formatTag);
                showTitle = false;
                titleText = "";
                cur.Clear();
            }
            cur.Append(block);
        }
        if (cur.Length > 0) SendMessageAutoSplit(formatTag + cur, showTitle, title, sendTo, formatTag);
    }

    public static void SendMessageAutoSplit(string text, bool showTitle, string title = "", byte sendTo = byte.MaxValue, string formatTag = "<size=70%><line-height=1.6pic>")
    {
        if (!AmongUsClient.Instance.AmHost) return;

        // 1 行が長すぎると下の分割では切れないので、バイト数でも割る。予算は戻す名前やホストの生死で変わる
        if (HamoExperimental.Monochrome) { text = text.RemoveColorTags(); title = title.RemoveColorTags(); }

        int MAX_LINE_BYTES = TownOfHostL.Core.ChatSenderName.BodyBudget(TownOfHostL.Core.ChatSenderName.Sender());

        string titleText = (title == "") ? $"<color=#aaaaff>{GetString("DefaultSystemMessageTitle")}</color>" : title;

        string fullText;
        if (showTitle)
        {
            fullText = $"{FORMAT_TAG_START}{titleText}\n{text}";
        }
        else
        {
            fullText = $"{FORMAT_TAG_START}{text}";
        }

        // 一回の送信で収まる場合はそのまま送信
        if (FitsOneMessage(fullText, MAX_LINE_BYTES))
        {
            EnqueueMessage((fullText, sendTo, "", true));
            return;
        }

        var lines = new List<string>();
        foreach (var line in text.Split("\n"))
        {
            lines.AddRange(SplitLineByBytes(line, MAX_LINE_BYTES));
        }
        var chunk = new List<string>();

        bool showedTitle = false;
        bool firstSending = true;

        for (int i = 0; i < lines.Count; i++)
        {
            chunk.Add(lines[i]);

            string chunkText;
            if (showTitle && !showedTitle)
            {
                chunkText = firstSending ?
                    $"{FORMAT_TAG_START}{titleText}\n{string.Join("\n", chunk)}"
                    : $"{FORMAT_TAG_START}{titleText}\n{formatTag}{string.Join("\n", chunk)}";
            }
            else
            {
                chunkText = firstSending ? $"{FORMAT_TAG_START}{string.Join("\n", chunk)}"
                    : $"{FORMAT_TAG_START}{formatTag}{string.Join("\n", chunk)}";
            }

            bool willExceed = chunkText.Length > MAX_LENGTH || chunk.Count > MAX_LINES
                || Encoding.UTF8.GetByteCount(chunkText) > MAX_LINE_BYTES;
            if (willExceed)
            {
                string sendText = string.Empty;

                if (chunk.Count == 1)
                {
                    sendText = chunkText;
                    chunk.Clear();
                }
                else
                {
                    var sendRaw = string.Join("\n", chunk.Take(chunk.Count - 1));
                    if (showTitle && !showedTitle)
                    {
                        sendText = firstSending ? $"{FORMAT_TAG_START}{titleText}\n{sendRaw}"
                            : $"{FORMAT_TAG_START}{titleText}\n{formatTag}{sendRaw}";
                    }
                    else
                    {
                        sendText = firstSending ? $"{FORMAT_TAG_START}{sendRaw}"
                            : $"{FORMAT_TAG_START}{formatTag}{sendRaw}";
                    }
                    var last = chunk.Last();
                    chunk.Clear();
                    chunk.Add(last);
                }

                if (HasVisibleText(sendText))
                {
                    EnqueueMessage((RemoveFirstNewLine(sendText), sendTo, "", true));
                    showedTitle = true;
                    firstSending = false;
                }
            }
        }

        // 残りを送信
        if (chunk.Count > 0)
        {
            string finalText;
            var finalRaw = string.Join("\n", chunk);
            if (showTitle && !showedTitle)
            {
                finalText = firstSending ? $"{FORMAT_TAG_START}{titleText}\n{finalRaw}"
                    : $"{FORMAT_TAG_START}{titleText}\n{formatTag}{finalRaw}";
            }
            else
            {
                finalText = firstSending ? $"{FORMAT_TAG_START}{finalRaw}"
                    : $"{FORMAT_TAG_START}{formatTag}{finalRaw}";
            }

            if (HasVisibleText(finalRaw))
            {
                EnqueueMessage((RemoveFirstNewLine(finalText), sendTo, "", true));
            }
        }
    }
    private static List<string> SplitLineByBytes(string line, int maxBytes)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(line) || Encoding.UTF8.GetByteCount(line) <= maxBytes)
        {
            result.Add(line);
            return result;
        }

        var sb = new StringBuilder();
        var bytes = 0;
        var inTag = false;
        for (int i = 0; i < line.Length; i++)
        {
            var charLength = char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 2 : 1;
            var piece = line.Substring(i, charLength);
            var pieceBytes = Encoding.UTF8.GetByteCount(piece);

            if (line[i] == '<') inTag = true;
            if (!inTag && sb.Length > 0 && bytes + pieceBytes > maxBytes)
            {
                result.Add(sb.ToString());
                sb.Clear();
                bytes = 0;
            }
            sb.Append(piece);
            bytes += pieceBytes;
            if (line[i] == '>') inTag = false;

            i += charLength - 1;
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    private static bool HasVisibleText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string withoutTags = Regex.Replace(text, "<.*?>", "");
        withoutTags = withoutTags.Replace("\r", "").Replace("\n", "").Replace("\t", "");
        return !string.IsNullOrWhiteSpace(withoutTags);
    }

    private static string RemoveFirstNewLine(string text)
    {
        int tagEndIndex = 0;

        while (tagEndIndex < text.Length && text[tagEndIndex] == '<')
        {
            int closeIndex = text.IndexOf('>', tagEndIndex);

            if (closeIndex < 0)
            {
                break;
            }

            tagEndIndex = closeIndex + 1;
        }

        if (tagEndIndex < text.Length && text[tagEndIndex] == '\n')
        {
            return text.Remove(tagEndIndex, 1);
        }

        return text;
    }

    public static void ApplySuffix()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        string name = DataManager.player.Customization.Name;
        if (Main.nickName != "") name = Main.nickName;
        // 試合中のホストの名前は NotifyRoles が見る人ごとに配る。ここで素の名前を全員へ送ると、その名前 (役職表示や隠した名前) を上書きし、
        // 送った名前の覚え書きとも食い違うので、以後の NotifyRoles が送り直さなくなる。色モードの名前は試合開始時に全員分を送っている
        if (AmongUsClient.Instance.IsGameStarted) return;

        if (ModeCatalog.IsCatchCat)
            name = $"<color={Main.ModColor}>{GetString("CatchCat")}</color>\r\n" + name;
        //else if (Options.IsONMode)
        //    name = $"<color={GetRoleColorCode(CustomRoles.ONVillager)}>TOH_Y {GetString("OneNight")}</color>\r\n" + name;
        else if (AmongUsClient.Instance.IsGamePublic)
            name = $"<color={Main.ModColor}>TownOfHost_L v{Main.PluginVersion}</color>\r\n" + name;
        switch (Options.GetSuffixMode())
        {
            case SuffixModes.None:
                break;
            case SuffixModes.TOH_L:
                name += $"\r\n<color={Main.ModColor}>TOH_L v{Main.PluginVersion}</color>";
                break;
            case SuffixModes.Streaming:
                name += $"\r\n<color={Main.ModColor}>{GetString("SuffixMode.Streaming")}</color>";
                break;
            case SuffixModes.Recording:
                name += $"\r\n<color={Main.ModColor}>{GetString("SuffixMode.Recording")}</color>";
                break;
            case SuffixModes.RoomHost:
                name += $"\r\n<color={Main.ModColor}>{GetString("SuffixMode.RoomHost")}</color>";
                break;
            case SuffixModes.OriginalName:
                name += $"\r\n<color={Main.ModColor}>{DataManager.player.Customization.Name}</color>";
                break;
        }
        if (name != PlayerControl.LocalPlayer.name && PlayerControl.LocalPlayer.CurrentOutfitType == PlayerOutfitType.Default) PlayerControl.LocalPlayer.RpcSetName(name);
    }
    public static PlayerControl GetPlayerById(int playerId) => GetPlayerById((byte)playerId);
    public static PlayerControl GetPlayerById(byte playerId)
    {
        return TownOfHostL.Core.Players.ById(playerId);
    }
    public static PlayerControl GetPlayerByColorName(string colorName)
    {
        if (colorName == null) return null;
        return Main.AllPlayerControls.Where(pc =>
                GetColorTypeName(pc.Data.DefaultOutfit.ColorId).ToLower() == colorName.ToLower() ||
                Palette.GetColorName(pc.Data.DefaultOutfit.ColorId).ToLower() == colorName.ToLower()).FirstOrDefault();
    }
    public static string GetColorTypeName(int colorId)
    {
        if (colorId >= 0 && colorId < Palette.ColorNames.Length)
        {
            var name = Palette.ColorNames[colorId].ToString();
            return name[5..];
        }
        return "???";
    }

    public static NetworkedPlayerInfo GetPlayerInfoById(int PlayerId) =>
        PlayerId is >= 0 and <= byte.MaxValue ? GameData.Instance.GetPlayerById((byte)PlayerId) : null;
    private static StringBuilder SelfMark = new(20);
    private static StringBuilder SelfSuffix = new(20);
    private static StringBuilder SelfLower = new(20);
    private static StringBuilder TargetMark = new(20);
    private static StringBuilder TargetSuffix = new(20);
    /// <summary>
    /// 名前の表示を送り直す。会議以外の呼び出しはフレームの終わりにまとめて 1 回だけ組み立てる (NotifyRolesQueue)。
    /// 会議用 (isForMeeting) はその場で組み立てて送る。
    /// </summary>
    public static void NotifyRoles(bool isForMeeting = false, PlayerControl SpecifySeer = null, bool NoCache = false, bool ForceLoop = false)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!isForMeeting)
        {
            TownOfHostL.Core.NotifyRolesQueue.Enqueue(SpecifySeer, NoCache, ForceLoop);
            return;
        }
        // 先に頼まれていた会議外の分を、会議用より前に送る
        TownOfHostL.Core.NotifyRolesQueue.Flush();
        NotifyRolesNow(isForMeeting, SpecifySeer, NoCache, ForceLoop);
    }
    /// <summary>溜めずにその場で組み立てて送る。</summary>
    public static void NotifyRolesNow(bool isForMeeting = false, PlayerControl SpecifySeer = null, bool NoCache = false, bool ForceLoop = false)
    {
        try
        {
            BuildAndSendNames(isForMeeting, SpecifySeer, NoCache, ForceLoop);
        }
        finally
        {
            NameRpcBatcher.Flush();
        }
    }
    private static void BuildAndSendNames(bool isForMeeting, PlayerControl SpecifySeer, bool NoCache, bool ForceLoop)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Main.AllPlayerControls == null) return;

        //ミーティング中の呼び出しは不正
        if (GameStates.IsMeeting) return;

        if (MushroomMixupUpdateSystemPatch.InSabotage) return; //キノコカオス中は無効

        var logger = Logger.Handler("NotifyRoles");
        // 1 回の NotifyRoles で見る人 × 見られる人のぶん呼ばれるので、記録が切られている時は名前を組まない
        bool loopLog = Logger.IsTagEnabled(logger.Tag);
        // 呼び出し元を StackFrame で調べるのは重いので、ログが出る時だけにする
        if (Logger.IsTagEnabled(logger.Tag))
        {
            var callerMethod = new System.Diagnostics.StackFrame(1, false).GetMethod();
            logger.Info($"NotifyRolesが{callerMethod?.DeclaringType?.FullName}.{callerMethod?.Name}から呼び出されました");
        }
        HudManagerPatch.NowCallNotifyRolesCount++;
        HudManagerPatch.LastSetNameDesyncCount = 0;

        bool everyoneNeedsSeenNames = NoCache || ForceLoop || TownOfHostL.Core.NameParts.EveryoneNeedsSeenNames();
        // 他人の名前を隠すか (通信妨害のカモフラなど) は、この組み立ての間は変わらない
        bool hideSeenNames = TownOfHostL.Core.NameParts.HideSeenName(isForMeeting);
        var seerList = PlayerControl.AllPlayerControls;
        if (SpecifySeer != null)
        {
            seerList = new();
            seerList.Add(SpecifySeer);
        }
        //seer:ここで行われた変更を見ることができるプレイヤー
        //target:seerが見ることができる変更の対象となるプレイヤー
        foreach (var seer in seerList)
        {
            // 1 人分の例外 (役職の印や接尾の組み立て) で、後ろの見る人の名前を配り損ねない
            try
            {
            //seerが落ちているときに何もしない
            if (!TownOfHostL.Core.NativeObject.IsAlive(seer) || seer.Data.Disconnected) continue;

            //if (seer.IsModClient()) continue;
            string fontSize = isForMeeting ? "1.5" : Main.RoleTextSize.ToString();
            // 練習の人形などは客の接続を持たない
            if (isForMeeting && (seer.GetClient()?.PlatformData?.Platform is Platforms.Playstation or Platforms.Switch)) fontSize = "70%";
            if (loopLog) logger.Info($"NotifyRoles-Loop1-{seer.GetNameWithRole()}:START");

            SelfMark.Clear();
            TownOfHostL.Core.NameParts.AppendSelfMark(SelfMark, seer, isForMeeting);
            //Markとは違い、改行してから追記されます。
            SelfSuffix.Clear();
            TownOfHostL.Core.NameParts.AppendSuffix(SelfSuffix, seer, null, isForMeeting);
            SelfLower.Clear();
            TownOfHostL.Core.NameParts.AppendSelfLower(SelfLower, seer, isForMeeting);

            //RealNameを取得 なければ現在の名前をRealNamesに書き込む
            string SeerRealName = seer.GetRealName(isForMeeting);

            if (!isForMeeting && (ModeCatalog.IsCatchCat || (MeetingStates.FirstMeeting && Options.ChangeNameToRoleInfo.GetBool())))
                SeerRealName = seer.GetRoleInfo();

            //seerの役職名とSelfTaskTextとseerのプレイヤー名とSelfMarkを合成
            var (enabled, text) = GetRoleNameAndProgressTextData(isForMeeting, seer);
            string SelfRoleName = enabled ? $"<size={fontSize}>{text}</size>" : "";
            string SelfDeathReason = TownOfHostL.Core.NameParts.DeathReason(seer, seer);

            StringBuilder SelfName = new();
            bool keepsOwnNameInSyncColor = seer.GetRoleClass()?.KeepsOwnNameInSyncColorMeeting ?? false;
            if (!(Options.IsSyncColorMode && isForMeeting && seer.IsAlive())
                || keepsOwnNameInSyncColor)
            {
                SelfName.Append(SelfRoleName).Append("\r\n");
            }

            Color SelfNameColor = seer.GetRoleColor();

            string t = "";
            //trueRoleNameでColor上書きあればそれにする
            seer.GetRoleClass()?.ApplyShownMainRole(ref SelfNameColor, ref t);//colorのみ

            bool selfNameOriginal = true;
            if (TownOfHostL.Core.PlayerTraits.LearnsExiledTeam(seer))
            {
                string str = Sending.RealNameChange();
                if (str != string.Empty)
                {
                    SelfName.Append(str);
                    selfNameOriginal = false;
                }
            }
            if (Options.IsSyncColorMode && isForMeeting && seer.IsAlive() &&
                !seer.Is(CustomRoleTypes.Impostor) &&
                !keepsOwnNameInSyncColor)
            {
                SelfName.Append($"{ColorString(Color.white, SeerRealName)}{SelfDeathReason}{SelfMark}");
            }
            else if (selfNameOriginal)
            {
                SelfName.Append($"{ColorString(SelfNameColor, SeerRealName)}{SelfDeathReason}{SelfMark}");
            }

            if (SelfSuffix.Length != 0)
            {
                SelfName.Append("\r\n").Append($"<size={fontSize}>{SelfSuffix}</size>");
            }
            if (SelfLower.Length != 0)
            {
                SelfName.Append("\r\n").Append($"<size={fontSize}>{SelfLower}</size>");
            }

            if (!isForMeeting) SelfName.Append("\r\n");

            //lobby中のバニラ視点にMOD名/Versionを記載
            //if (GameStates.IsLobby)
            //    SelfName = $"{$"<color={Main.ModColor}>TownOfHost_Y</color> v{Main.PluginVersion}\n\n".Color(Color.white)}</align>"
            //     + $"<line-height=6em>\n</line-height>" + SelfName + "<line-height=6em>\n</line-height>ㅤ";

            // ミーティングテキスト
            string name = MeetingDisplayText.AddTextForVanilla(seer, SelfName.ToString(), SelfSuffix.ToString(), SelfRoleName, isForMeeting);
            //適用
            seer.RpcSetNamePrivate(name, true, force: NoCache);

            //seerが死んでいる場合など、必要なときのみ第二ループを実行する。
            // ただしホストの名前だけは全員へ必ず送る。ホストの手元の名前はホストから見た表示 (役職名入り) に書き換わっており、
            // 一度も送らない人には、その名前がチャットの名前の戻しなどから届いて役職が透けることがある
            bool fullLoop = everyoneNeedsSeenNames || TownOfHostL.Core.NameParts.NeedsSeenNames(seer);
            if (fullLoop || !seer.AmOwner)
            {
                foreach (var target in Main.AllPlayerControls)
                {
                    //targetがseer自身の場合は何もしない
                    if (TownOfHostL.Core.NativeObject.Same(target, seer)) continue;
                    if (!fullLoop && !target.AmOwner) continue;
                    // 見られる人 1 人分の例外 (その人の役職の表示や印) で、後ろの人の名前を配り損ねない
                    try
                    {
                        if (loopLog) logger.Info($"NotifyRoles-Loop2-{target.GetNameWithRole()}:START");

                        TargetMark.Clear();
                        TownOfHostL.Core.NameParts.AppendMark(TargetMark, seer, target, isForMeeting);

                        //他人の役職とタスクは幽霊が他人の役職を見れるようになっていてかつ、seerが死んでいる場合のみ表示されます。それ以外の場合は空になります。
                        var targetRoleData = GetRoleNameAndProgressTextData(isForMeeting, seer, target);
                        var TargetRoleText = targetRoleData.enabled ? $"<size={fontSize}>{targetRoleData.text}</size>\r\n" : "";

                        TargetSuffix.Clear();
                        TownOfHostL.Core.NameParts.AppendSeenSuffix(TargetSuffix, seer, target, isForMeeting);
                        // 空でなければ先頭に改行を挿入
                        if (TargetSuffix.Length > 0)
                        {
                            TargetSuffix.Insert(0, $"\r\n<size={fontSize}>");
                            TargetSuffix.Append("</size>");
                        }

                        //RealNameを取得 なければ現在の名前をRealNamesに書き込む
                        string TargetPlayerName = isForMeeting ? target.GetRealName(true): (Main.AllPlayerNames.TryGetValue(target.PlayerId, out var realName) ? realName : target.GetRealName());
                        // 変身中の人は本編が変身先の名前を出している。本名を送ると正体が見えるので、変身先の名前にする
                        if (!isForMeeting && Main.CheckShapeshift.TryGetValue(target.PlayerId, out var targetShifted) && targetShifted
                            && Main.ShapeshiftTarget.TryGetValue(target.PlayerId, out var shiftTo) && shiftTo != target.PlayerId
                            && Main.AllPlayerNames.TryGetValue(shiftTo, out var shiftName))
                            TargetPlayerName = shiftName;

                        //ターゲットのプレイヤー名の色を書き換えます。
                        TargetPlayerName = TargetPlayerName.ApplyNameColorData(seer, target, isForMeeting);

                        string TargetDeathReason = TownOfHostL.Core.NameParts.DeathReason(seer, target);

                        if (hideSeenNames || (!isForMeeting && TownOfHostL.Modules.Net.SeerCamouflage.IsActive(seer.PlayerId)))
                            TargetPlayerName = $"<size=0%>{TargetPlayerName}</size>";

                        //全てのテキストを合成します。
                        string TargetName = $"{TargetRoleText}{TargetPlayerName}{TargetDeathReason}{TargetMark}{TargetSuffix}";

                        // バニラ視点にMOD名/Versionを記載
                        TargetName = MeetingDisplayText.AddTextForVanilla(target, TargetName, TargetSuffix.ToString(), TargetRoleText, isForMeeting);

                        //適用
                        target.RpcSetNamePrivate(TargetName, true, seer, force: NoCache);

                        if (loopLog) logger.Info($"NotifyRoles-Loop2-{target.GetNameWithRole()}:END");
                    }
                    catch (Exception ex) { Logger.Exception(ex, "NotifyRoles.Target"); }
                }
            }
            if (loopLog) logger.Info($"NotifyRoles-Loop1-{seer.GetNameWithRole()}:END");
            }
            catch (Exception ex) { Logger.Exception(ex, "NotifyRoles.Seer"); }
        }
    }
    public static void MarkEveryoneDirtySettings()
    {
        PlayerGameOptionsSender.SetDirtyToAll();
    }
    public static void SyncAllSettings()
    {
        PlayerGameOptionsSender.SetDirtyToAll();
        GameOptionsSender.SendAllGameOptions();
    }
    public static void AfterMeetingTasks()
    {
        // 天秤が 2 人を選んで会議を閉じた時は、日を進めず役職の会議明けの処理もせずに天秤会議を開く
        // 会議明けの処理は 1 回の会議で 2 回来るので、天秤会議を開くまでの間に来た 2 回目も素通りさせる (天秤の状態を消さない)
        if (Balancer.Scheduled) return;
        HamoMapTask.SetPlatform();
        PersonalDoors.RequestResend();
        if (AmongUsClient.Instance.AmHost && Balancer.Id == byte.MaxValue && Balancer.target1 != byte.MaxValue && Balancer.target2 != byte.MaxValue)
        {
            foreach (var roleClass in CustomRoleManager.AllActiveRoles.Values)
                if (roleClass is Balancer balancer) balancer.BalancerAfterMeetingTasks();
            return;
        }
        // マッドマジックの変身の期限は、その日の会議が終わる時に見る (日を進める前)
        var endOfDay = MeetingStates.Day;
        if (MeetingStates.TryEndDay() && AmongUsClient.Instance.AmHost)
        {
            GameLog.OnNewDay();
            try { Roles.Madmate.MadMagic.AfterMeeting(endOfDay); }
            catch (Exception ex) { Logger.Exception(ex, "MadMagic.AfterMeeting"); }
        }
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            var state = PlayerState.GetByPlayerId(pc.PlayerId);
            state.IsBlackOut = false; //ブラックアウト解除
        }
        if (!FirstTurnMeeting.InFirst) RoleHooks.ForEachRole(roleClass => roleClass.AfterMeetingTasks(), "AfterMeetingTasks");
        // 天秤会議の途中で天秤が役職を失ったり抜けたりしても、次の天秤会議が開けるように戻す
        Balancer.AfterMeetingReset();

        Counselor.AfterMeetingTask();
        ChainShifterAddon.AfterMeetingTasks();
        VentEnterTask.AfterMeetingTasks();

        if (Options.AirShipVariableElectrical.GetBool())
            AirShipElectricalDoors.Initialize();
        DoorsReset.ResetDoors();
    }
    public static void ProtectedFirstPlayer()
    {
        var lists = Main.AllAlivePlayerControls.Where(pc => pc.GetRoleClass() is not IKiller);
        var pc = lists.ElementAtOrDefault(new System.Random().Next(lists.Count()));

        if (pc == null)
        {
            pc = Main.AllAlivePlayerControls.FirstOrDefault();
            // パリン
            Logger.Info($"nullcase:{pc.GetNameWithRole()}に強制守護天使表示", "ForceProtected");
            pc.SetKillCooldown(ForceProtect: true);
        }
        else
        {
            // パリン
            Logger.Info($"{pc.GetNameWithRole()}に強制守護天使表示", "ForceProtected");
            pc.RpcProtectedMurderPlayer();
        }
    }

    public static void SetKillFlashAfterDead(PlayerControl pc, bool isHost = false)
    {
        string name = pc.Data.PlayerName;
        if (!Main.KillFlashAfterDead.Contains(name))
        {
            // 登録
            if (isHost) Main.KillFlashAfterDeadByHost.Value = true;
            Main.KillFlashAfterDead.Add(name);
            SendMessageAutoSplit(string.Format(GetString("Message.KillFlashAfterDeadOn"), name), true, sendTo: pc.PlayerId);
            Logger.Info($"KillFlashAfterDead ON - {name}", "ChatCommand");
        }
        else
        {
            // 解除
            if (isHost) Main.KillFlashAfterDeadByHost.Value = false;
            Main.KillFlashAfterDead.Remove(name);
            SendMessageAutoSplit(string.Format(GetString("Message.KillFlashAfterDeadOff"), name), true, sendTo: pc.PlayerId);
            Logger.Info($"KillFlashAfterDead OFF - {name}", "ChatCommand");
        }
    }
    public static bool CheckKillFlashAfterDead(PlayerControl pc)
    {
        if (pc.IsAlive()) return false;
        string name = pc.Data.PlayerName;
        return Main.KillFlashAfterDead.Contains(name);
    }

    public static void ChangeInt(ref int ChangeTo, int input, int max)
    {
        var tmp = ChangeTo * 10;
        tmp += input;
        ChangeTo = Math.Clamp(tmp, 0, max);
    }
    public static void CountAlivePlayers(bool sendLog = false)
    {
        int AliveImpostorCount = Main.AllAlivePlayerControls.Count(pc => pc.Is(CustomRoleTypes.Impostor));
        if (Main.AliveImpostorCount != AliveImpostorCount)
        {
            Logger.Info("生存しているインポスター:" + AliveImpostorCount + "人", "CountAliveImpostors");
            Main.AliveImpostorCount = AliveImpostorCount;
            LastImpostor.SetSubRole();
        }

        if (sendLog)
        {
            var sb = new StringBuilder(100);
            foreach (var countTypes in EnumHelper.GetAllValues<CountTypes>())
            {
                var playersCount = PlayersCount(countTypes);
                if (playersCount == 0) continue;
                sb.Append($"{countTypes}:{AlivePlayersCount(countTypes)}/{playersCount}, ");
            }
            sb.Append($"All:{AllAlivePlayersCount}/{AllPlayersCount}");
            Logger.Info(sb.ToString(), "CountAlivePlayers");
        }
    }
    public static string PadRightV2(this object text, int num)
    {
        int bc = 0;
        var t = text.ToString();
        foreach (char c in t) bc += Encoding.GetEncoding("UTF-8").GetByteCount(c.ToString()) == 1 ? 1 : 2;
        return t?.PadRight(Mathf.Max(num - (bc - t.Length), 0));
    }
    public static DirectoryInfo GetLogFolder(bool auto = false)
    {
        var folder = Directory.CreateDirectory($"{Application.persistentDataPath}/TownOfHost_L/Logs");
        if (auto)
        {
            folder = Directory.CreateDirectory($"{folder.FullName}/AutoLogs");
        }
        return folder;
    }
    public static void DumpLog()
    {
        var logs = GetLogFolder();
        var filename = CopyLog(logs.FullName);
        OpenDirectory(filename);
        if (PlayerControl.LocalPlayer != null && HudManager.InstanceExists)
            TownOfHostL.Modules.HostNote.Post(Translator.GetString("Message.LogsSavedInLogsFolder"));
    }
    public static void SaveNowLog()
    {
        var logs = GetLogFolder(true);
        // 7日以上前のログを削除
        logs.EnumerateFiles().Where(f => f.CreationTime < DateTime.Now.AddDays(-7)).ToList().ForEach(f => f.Delete());
        CopyLog(logs.FullName);
    }
    public static string CopyLog(string path)
    {
        string t = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
        string fileName = $"{path}/TownOfHost-v{Main.PluginVersion}-{t}.log";
        LogFile.Flush();
        FileInfo file = new(LogFile.CurrentPath);
        var logFile = file.CopyTo(fileName);

        /*try
        {
            var workspaceDest = @"D:\\Among Us\\Project Files\\TownOfHost_Y";
            if (!Directory.Exists(workspaceDest)) Directory.CreateDirectory(workspaceDest);

            var destTimestamp = Path.Combine(workspaceDest, $"LogOutput-{t}.log");
            file.CopyTo(destTimestamp, true);

            var destLatest = Path.Combine(workspaceDest, "LogOutput.log");
            file.CopyTo(destLatest, true);
        }
        catch (System.Exception ex)
        {
            Logger.Warn($"LogOutput の追加コピーに失敗しました: {ex.Message}", "CopyLog");
        }*/

        return logFile.FullName;
    }
    public static void OpenLogFolder()
    {
        var logs = GetLogFolder(true);
        OpenDirectory(logs.FullName);
    }
    public static void OpenDirectory(string path)
    {
        Process.Start("Explorer.exe", $"/select,{path}");
    }
    public static string SummaryTexts(byte id, bool isForChat) => SummaryTexts(id, isForChat, null);
    /// <summary>結果の一覧の 1 行。<paramref name="progressText"/> を渡すと、手元で数えずにそれを進み具合として出す (客がホストから受けた物)</summary>
    public static string SummaryTexts(byte id, bool isForChat, string progressText)
    {
        // 全プレイヤー中最長の名前の長さからプレイヤー名の後の水平位置を計算する
        // 1em ≒ 半角2文字
        // 空白は0.5emとする
        // SJISではアルファベットは1バイト，日本語は基本的に2バイト
        var longestNameByteCount = Main.AllPlayerNames.Values.Select(name => name.GetByteCount()).OrderByDescending(byteCount => byteCount).FirstOrDefault();
        //最大11.5emとする(★+日本語10文字分+半角空白)
        var pos = Math.Min(((float)longestNameByteCount / 2) + 1.5f /* ★+末尾の半角空白 */ , 11.5f);

        var builder = new StringBuilder();
        builder.Append(ColorString(Main.PlayerColors[id], Main.AllPlayerNames[id]));
        builder.AppendFormat("<pos={0}em>", pos).Append(isForChat ? (progressText ?? GetProgressText(id)).RemoveColorTags() : progressText ?? GetProgressText(id)).Append("</pos>");
        // "(00/00) " = 4em
        pos += 4f;
        builder.AppendFormat("<pos={0}em>", pos).Append(GetVitalText(id)).Append("</pos>");
        // "Lover's Suicide " = 8em
        // "回線切断 " = 4.5em
        pos += DestroyableSingleton<TranslationController>.Instance.currentLanguage.languageID == SupportedLangs.English ? 8f : 4.5f;
        builder.AppendFormat("<pos={0}em>", pos);
        builder.Append(RoleText.GetRoleNameText(id, true));
        int kills = PlayerState.GetByPlayerId(id)?.GetKillCount(ExcludeSelfKill: true) ?? 0;
        if (kills > 0) builder.Append(' ').Append(ColorString(Palette.ImpostorRed, string.Format(GetString("SummaryKillCount"), kills)));
        builder.Append("</pos>");
        return builder.ToString();
    }
    public static string RemoveHtmlTags(this string str) => Regex.Replace(str, "<[^>]*?>", "");
    public static string RemoveColorTags(this string str) => Regex.Replace(str, "</?color(=#[0-9a-fA-F]*)?>", "");
    public static void FlashColor(Color color, float duration = 1f)
    {
        var hud = DestroyableSingleton<HudManager>.Instance;
        if (hud.FullScreen == null) return;
        var obj = hud.transform.FindChild("FlashColor_FullScreen")?.gameObject;
        if (obj == null)
        {
            obj = GameObject.Instantiate(hud.FullScreen.gameObject, hud.transform);
            obj.name = "FlashColor_FullScreen";
        }
        // Effects.Lerp に管理側のラムダを渡すと呼ぶたびに GCHandle が 1 つ残るので、自前のコルーチンで同じ動きをする
        hud.StartCoroutine(CoFlashColor(obj, color, duration).WrapToIl2Cpp());
    }
    private static System.Collections.IEnumerator CoFlashColor(GameObject obj, Color color, float duration)
    {
        var renderer = obj.GetComponent<SpriteRenderer>();
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            if (obj == null) yield break;
            float t = time / duration;
            obj.SetActive(true);
            renderer.color = new(color.r, color.g, color.b, Mathf.Clamp01((-2f * Mathf.Abs(t - 0.5f) + 1) * color.a)); //アルファ値を0→目標→0に変化させる
            yield return null;
        }
        if (obj != null) obj.SetActive(false);
    }

    // 同じ絵を開くたびに作り直すと Texture2D と Sprite が積み上がるので、作った物を使い回す
    private static readonly Dictionary<(string, float), Sprite> SpriteCache = new();
    public static Sprite LoadSprite(string path, float pixelsPerUnit = 1f)
    {
        if (SpriteCache.TryGetValue((path, pixelsPerUnit), out var cached) && cached != null) return cached;
        Sprite sprite = null;
        try
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
            var texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            using MemoryStream ms = new();
            stream.CopyTo(ms);
            ImageConversion.LoadImage(texture, ms.ToArray());
            sprite = Sprite.Create(texture, new(0, 0, texture.width, texture.height), new(0.5f, 0.5f), pixelsPerUnit);
            texture.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            SpriteCache[(path, pixelsPerUnit)] = sprite;
        }
        catch
        {
            Logger.Error($"\"{path}\"の読み込みに失敗しました。", "LoadImage");
        }
        return sprite;
    }


    public static string ColorString(Color32 color, string str) => $"<color=#{color.r:x2}{color.g:x2}{color.b:x2}{color.a:x2}>{str}</color>";
    // Color → Color32 の変換 (Unity の暗黙変換と同じ丸め) をゲーム側を呼ばずに行う
    public static string ColorString(Color color, string str) => ColorString(ToColor32(color), str);
    public static Color32 ToColor32(Color c) => new() { r = ToByte(c.r), g = ToByte(c.g), b = ToByte(c.b), a = ToByte(c.a) };
    private static byte ToByte(float v) => (byte)Math.Round(v < 0f ? 0f : v > 1f ? 255f : v * 255f);
    /// <summary>
    /// Darkness:１の比率で黒色と元の色を混ぜる。マイナスだと白色と混ぜる。
    /// </summary>
    public static Color ShadeColor(this Color color, float Darkness = 0)
    {
        bool IsDarker = Darkness >= 0; //黒と混ぜる
        if (!IsDarker) Darkness = -Darkness;
        float Weight = IsDarker ? 0 : Darkness; //黒/白の比率
        float R = (color.r + Weight) / (Darkness + 1);
        float G = (color.g + Weight) / (Darkness + 1);
        float B = (color.b + Weight) / (Darkness + 1);
        return new Color { r = R, g = G, b = B, a = color.a }; // コンストラクタはゲーム側の呼び出しになるので項目を直接入れる
    }

    /// <summary>
    /// 乱数の簡易的なヒストグラムを取得する関数
    /// <params name="nums">生成した乱数を格納したint配列</params>
    /// <params name="scale">ヒストグラムの倍率 大量の乱数を扱う場合、この値を下げることをお勧めします。</params>
    /// </summary>
    public static string WriteRandomHistgram(int[] nums, float scale = 1.0f)
    {
        int[] countData = new int[nums.Max() + 1];
        foreach (var num in nums)
        {
            if (0 <= num) countData[num]++;
        }
        StringBuilder sb = new();
        for (int i = 0; i < countData.Length; i++)
        {
            // 倍率適用
            countData[i] = (int)(countData[i] * scale);

            // 行タイトル
            sb.AppendFormat("{0:D2}", i).Append(" : ");

            // ヒストグラム部分
            for (int j = 0; j < countData[i]; j++)
                sb.Append('|');

            // 改行
            sb.Append('\n');
        }

        // その他の情報
        sb.Append("最大数 - 最小数: ").Append(countData.Max() - countData.Min());

        return sb.ToString();
    }

    public static void Safe(Action action, string tag)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error($"Safe wrapper caught exception: {ex.Message}", tag);
            Logger.Exception(ex, tag);
        }
    }

    public static bool TryCast<T>(this Il2CppObjectBase obj, out T casted)
    where T : Il2CppObjectBase
    {
        casted = obj.TryCast<T>();
        return casted != null;
    }
    public static int AllPlayersCount => PlayerState.AllPlayerStates.Values.Count(state => state.CountType != CountTypes.OutOfGame);
    public static int AllAlivePlayersCount => Main.AllAlivePlayerControls.Count(pc => !pc.Is(CountTypes.OutOfGame));
    public static bool IsAllAlive => PlayerState.AllPlayerStates.Values.All(state => state.CountType == CountTypes.OutOfGame || !state.IsDead);
    public static int PlayersCount(CountTypes countTypes) => PlayerState.AllPlayerStates.Values.Count(state => state.CountType == countTypes);
    public static int AlivePlayersCount(CountTypes countTypes) => Main.AllAlivePlayerControls.Count(pc => pc.Is(countTypes));
    private const string ActiveSettingsSize = "90%";
    /// <summary>
    /// 安全にキル可能距離を取得します。LogicOptions が null の場合はデフォルト値を返します。
    /// </summary>
    public static float SafeGetKillDistance()
    {
        try
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                Logger.Warn("GameManager.Instance が null のため SafeGetKillDistance はデフォルトを返します", "Utils");
                return 0.5f;
            }

            try
            {
                var logic = gm.LogicOptions;
                if (logic == null)
                {
                    Logger.Warn("GameManager.Instance.LogicOptions が null のため SafeGetKillDistance はデフォルトを返します", "Utils");
                    return 0.5f;
                }

                return logic.GetKillDistance();
            }
            catch (System.NullReferenceException ex)
            {
                Logger.Error($"LogicOptions.GetKillDistance で例外: {ex.Message}", "Utils");
                Logger.Exception(ex, "Utils");
                return 0.5f;
            }
            catch (System.Exception ex)
            {
                Logger.Error($"LogicOptions.GetKillDistance で予期しない例外: {ex.Message}", "Utils");
                Logger.Exception(ex, "Utils");
                return 0.5f;
            }
        }
        catch (System.Exception ex)
        {
            Logger.Error($"SafeGetKillDistance の取得で例外: {ex.Message}", "Utils");
            Logger.Exception(ex, "Utils");
            return 0.5f;
        }
    }
}