using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Modules;

namespace TownOfHostL;

// ================================================================
// TownOfHost-hamo の役職が使う道具を、TOH_L の仕組みの上に用意する
// ================================================================

/// <summary>hamo の役職の元ネタ (表示用)</summary>
public enum From
{
    None,
    Love_Couple_Mod,
    TheOtherRoles,
    TOR_GM_Edition,
    TOR_GM_Haoming_Edition,
    SuperNewRoles,
    ExtremeRoles,
    NebulaontheShip,
    au_libhalt_net,
    FoolersMod,
    SheriffMod,
    Jester,
    TownOfUs,
    TownOfHost,
    TownOfHost_Y,
    TownOfHost_E,
    TownOfHost_K,
    TownOfHost_Pko,
    TownOfHost_hamo,
    RevolutionaryHostRoles,
    UchuAddon,
}

/// <summary>hamo の組み役職 (TOH_L では使わない)</summary>
public enum CombinationRoles
{
    None,
    DriverandBraid,
    AssassinandMerlin,
}

public static class PlayerCatch
{
    public static IEnumerable<PlayerControl> AllPlayerControls => Main.AllPlayerControls;
    public static IEnumerable<PlayerControl> AllAlivePlayerControls => Main.AllAlivePlayerControls;
    public static PlayerControl GetPlayerById(int playerId) => Utils.GetPlayerById(playerId);
    public static NetworkedPlayerInfo GetPlayerInfoById(int playerId) => Utils.GetPlayerInfoById(playerId);
    public static int AllAlivePlayersCount => Utils.AllAlivePlayersCount;
    public static int AliveImpostorCount => Main.AllAlivePlayerControls.Count(pc => pc.Is(CustomRoleTypes.Impostor));
    public static bool AnyModClient() => Main.AllPlayerControls.Any(pc => pc.PlayerId != 0 && pc.IsModClient());
    public static int AlivePlayersCount(CountTypes countTypes) => Utils.AlivePlayersCount(countTypes);
    public static bool IsAllAlive => Utils.IsAllAlive;
}

public static class UtilsNotifyRoles
{
    public static string ExtendedMeetingText = "";
    public static void NotifyRoles(bool isForMeeting = false, PlayerControl SpecifySeer = null, bool NoCache = false, bool ForceLoop = false, bool OnlyMeName = false, PlayerControl SpecifyTarget = null)
        => Utils.NotifyRoles(isForMeeting, SpecifySeer, NoCache, ForceLoop);
    public static void NotifyRoles(bool isForMeeting, PlayerControl[] SpecifySeer, bool NoCache = false, bool ForceLoop = false)
    {
        if (SpecifySeer == null) { Utils.NotifyRoles(isForMeeting, null, NoCache, ForceLoop); return; }
        foreach (var seer in SpecifySeer) Utils.NotifyRoles(isForMeeting, seer, NoCache, ForceLoop);
    }
    public static void NotifyMeetingRoles() => Utils.NotifyRoles(isForMeeting: true, NoCache: true);
}

public static class UtilsName
{
    public static string GetPlayerColor(this PlayerControl player, bool bold = false)
    {
        if (player == null) return "";
        var name = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(player.PlayerId, out var n) ? n : player.Data?.PlayerName;
        return Utils.ColorString(Main.PlayerColors.TryGetValue(player.PlayerId, out var c) ? (Color)c : Color.white, $"{name}");
    }
    public static string GetPlayerColor(this byte player, bool bold = false)
    {
        var pc = Utils.GetPlayerById(player);
        if (pc == null) return "";
        return pc.GetPlayerColor(bold);
    }
    public static string GetPlayerColor(this NetworkedPlayerInfo player, bool bold = false)
    {
        if (player == null) return "";
        return Utils.ColorString(Main.PlayerColors.TryGetValue(player.PlayerId, out var c) ? (Color)c : Color.white, player.PlayerName);
    }
}

public static class UtilsRoleText
{
    public static string GetTrueRoleName(byte playerId, bool showSubRoleMarks = true) => RoleText.GetRoleNameText(playerId, showSubRole: showSubRoleMarks);
    public static string GetRoleName(CustomRoles role) => Utils.GetRoleName(role);
    public static Color GetRoleColor(CustomRoles role, bool MadmateOrange = false, string fromtext = null)
        => role.IsMadmate() && MadmateOrange ? ModColors.MadMateOrenge : Utils.GetRoleColor(role);
    public static string GetRoleColorCode(CustomRoles role, bool Madcolor = false)
    {
        var code = Utils.GetRoleColorCode(role);
        return string.IsNullOrEmpty(code) ? "#cccccc" : code;
    }
    public static string GetRoleColorAndtext(CustomRoles role) => role < 0 ? "" : Utils.ColorString(Utils.GetRoleColor(role), Translator.GetString($"{role}"));
}

/// <summary>hamo のゲームログ。TOH_L ではログに書くだけ</summary>
public static class UtilsGameLog
{
    public static int day = 1;
    public static void AddGameLog(string type, string text) => Logger.Info(text.RemoveHtmlTags(), $"GameLog.{type}");
    public static void AddGameLogsub(string text) => Logger.Info(text.RemoveHtmlTags(), "GameLog");
    [Attributes.GameModuleInitializer]
    public static void Init() => day = 1;
}

public static class UtilsOption
{
    public static void MarkEveryoneDirtySettings() => Utils.MarkEveryoneDirtySettings();
}

public static class ModColors
{
    public static readonly Color Red = Color.red;
    public static readonly Color Orange = new Color32(255, 165, 0, 255);
    public static readonly Color MadMateOrenge = new Color32(255, 136, 0, 255);
    public static readonly Color Gray = Color.gray;
}

/// <summary>hamo の実績。TOH_L には実績が無いので何もしない</summary>
public class Achievement
{
    public Achievement() { }
    public Achievement(SimpleRoleInfo info, int id, int maxcount, int playcount, int difficulty, bool hide = false, bool roleachievement = true) { }
    public static Achievement Create(SimpleRoleInfo info, int id, int maxcount, int playcount, int difficulty, bool hide = false, bool roleachievement = true) => new();
}
public static class Achievements
{
    public static void RpcCompleteAchievement(byte playerid, int type, Achievement achievement, int addcount = 1) { }
}

/// <summary>hamo の突然死モード。TOH_L には無いので常にオフ</summary>
public static class SuddenDeathMode
{
    public static bool NowSuddenDeathMode => false;
    public static bool NowSuddenDeathTemeMode => false;
    public static bool IsSameteam(byte a, byte b) => false;
}

public static class HamoUtilsExtensions
{
    public static PlayerState GetPlayerState(this PlayerControl player) => PlayerState.GetByPlayerId(player.PlayerId);
    public static PlayerState GetPlayerState(this byte player) => PlayerState.GetByPlayerId(player);
    public static PlayerControl GetPlayerControl(this byte playerId) => Utils.GetPlayerById(playerId);
    public static string GetLogPlayerName(this NetworkedPlayerInfo info) => info?.PlayerName ?? "";
    public static string Color(this string str, Color color) => Utils.ColorString(color, str);
    public static void RpcSnapToForced(this PlayerControl pc, Vector2 position) => pc.RpcSnapTo(position);
}

public static partial class Utils
{
    public static void SendMessage(string text, byte sendTo = byte.MaxValue, string title = "", bool checkl = true, bool isTowSend = false, bool setsize = false)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (text.RemoveHtmlTags() == "") return;
        if (title == "NonTitle") title = "";
        SendMessageAutoSplit(text, title != "", title, sendTo);
    }
    public static List<byte> RoleSendList = new();
    public const string AdditionalAliveWinnerMark = "<#349121>★</color>";
    public static bool IsRestriction() => false;
    public static void AllPlayerKillFlash()
    {
        foreach (var pc in Main.AllPlayerControls) pc.KillFlash();
    }
    public static string GetVitalTextHamo(byte playerId, bool? RealKillerColor = false, bool alivecolor = true)
    {
        var state = PlayerState.GetByPlayerId(playerId);
        if (state == null) return Translator.GetString("DeathReason.Disconnected");
        string deathReason = state.IsDead ? Translator.GetString("DeathReason." + state.DeathReason) : Translator.GetString("Alive");
        switch (RealKillerColor)
        {
            case true:
                {
                    var killerId = state.GetRealKiller();
                    if (alivecolor is false && killerId == byte.MaxValue) return deathReason;
                    Color color = killerId != byte.MaxValue && Main.PlayerColors.TryGetValue(killerId, out var c) ? c
                        : (state.DeathReason == CustomDeathReason.etc ? new Color32(97, 128, 163, 255) : new Color32(120, 120, 120, 255));
                    deathReason = ColorString(color, deathReason);
                }
                break;
            case null:
                deathReason = $"<#80ffdd>{deathReason}</color>";
                break;
        }
        return deathReason;
    }
}
