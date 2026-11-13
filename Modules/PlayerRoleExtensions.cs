using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL;

public partial class PlayerState
{
    /// <summary>このゲームで一度でもスポーンしたか (会議後のスポーンも含む)</summary>
    public bool HasSpawned { get; set; } = false;
    /// <summary>残りの緊急ボタンの数</summary>
    public float NumberOfRemainingButtons = Main.NormalOptions?.NumEmergencyMeetings ?? 1;
    /// <summary>開始直後の 10 秒キルボタンか</summary>
    public bool Is10secKillButton = true;
}

public static partial class StringHelper
{
    public static string Color(this string str, UnityEngine.Color color) => Utils.ColorString(color, str);
    public static Color32 CodeColor(string code)
    {
        if (!code.StartsWith("#")) code = "#" + code;
        return ColorUtility.TryParseHtmlString(code, out var c) ? c : UnityEngine.Color.white;
    }
    static readonly System.Text.RegularExpressions.Regex SizeTagRegex = new("</?size[^>]*>", System.Text.RegularExpressions.RegexOptions.Compiled);
    public static string RemoveSizeTags(this string str) => str.IndexOf('<') < 0 ? str : SizeTagRegex.Replace(str, "");
}

public static partial class Utils
{
    /// <summary>チャットに文を送る (ホストのみ)。sendTo を省くと全員へ。title が空なら題を付けない ("NonTitle" も空と同じ)</summary>
    public static void SendMessage(string text, byte sendTo = byte.MaxValue, string title = "")
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (text.RemoveHtmlTags() == "") return;
        if (title == "NonTitle") title = "";
        SendMessageAutoSplit(text, title != "", title, sendTo);
    }
    /// <summary>役職の色を付けた役職名</summary>
    public static string GetRoleColorAndName(CustomRoles role) => role < 0 ? "" : ColorString(GetRoleColor(role), GetString($"{role}"));
    /// <summary>試合の記録 (/l で見られるゲームログ) に 1 件足す。type はログの見出し</summary>
    public static void AddGameLog(string type, string text) => Modules.GameLog.Add(type, text);
    /// <summary>ヴァイパーにキルされた死体が溶けるまでの秒数 (本編の設定)</summary>
    public static float ViperDissolveTime
    {
        get
        {
            try { return Main.NormalOptions.GetFloat(FloatOptionNames.ViperDissolveTime); }
            catch { return 15f; }
        }
    }
    /// <summary>全員の画面をキルの閃光で光らせる</summary>
    public static void AllPlayerKillFlash()
    {
        foreach (var pc in Main.AllPlayerControls) pc.KillFlash();
    }
}

/// <summary>役職の能力から使うプレイヤーの道具 (役職の変更・部屋・キルの相手・色付きの名前など)</summary>
public static class PlayerRoleExtensions
{
    /// <summary>プレイヤーの色で塗った名前</summary>
    public static string ColoredName(this PlayerControl player)
    {
        if (player == null) return "";
        var name = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(player.PlayerId, out var n) ? n : player.Data?.PlayerName;
        return Utils.ColorString(Main.PlayerColors.TryGetValue(player.PlayerId, out var c) ? (Color)c : Color.white, $"{name}");
    }
    public static string ColoredName(this byte playerId) => Utils.GetPlayerById(playerId)?.ColoredName() ?? "";
    public static string ColoredName(this NetworkedPlayerInfo player)
    {
        if (player == null) return "";
        return Utils.ColorString(Main.PlayerColors.TryGetValue(player.PlayerId, out var c) ? (Color)c : Color.white, player.PlayerName);
    }

    /// <summary>
    /// 試合の途中で役職を替える (ホストのみ)。setRole が true なら本編の役職も見る人ごとに配り直す (規則は Modules.RoleView)。
    /// 替えた後、名前の色・キルクール・アビリティクールを戻し、全員の設定と名前を送り直す
    /// </summary>
    public static void RpcChangeRole(this PlayerControl player, CustomRoles role, bool setRole = true)
    {
        if (player == null) return;
        if (player.GetCustomRole() == role) return;
        var isMain = role < CustomRoles.StartAddon && role > CustomRoles.NotAssigned;
        var oldClass = player.GetRoleClass();
        player.RpcSetCustomRole(role);
        if (!isMain) return;
        Logger.Info($"{player.GetNameWithRole().RemoveHtmlTags()} => {role}", "RpcChangeRole");
        if (!AmongUsClient.Instance.AmHost) return;
        if (oldClass != null && oldClass == player.GetRoleClass()) return;

        NameColorManager.RemoveAll(player.PlayerId);
        if (setRole)
        {
            ApplyVanillaRoleWhenInTask(player, role);
        }
        player.ResetKillCooldown();
        player.SetKillCooldown(ForceProtect: true);
        player.RpcResetAbilityCooldown();
        (player.GetRoleClass() as IUsePhantomButton)?.Init(player);
        Utils.MarkEveryoneDirtySettings();
        Utils.NotifyRoles(ForceLoop: true);
    }

    /// <summary>会議や追放の演出の間に替えた時は、タスクの時間に戻るまで待ってから配り直す</summary>
    static void ApplyVanillaRoleWhenInTask(PlayerControl player, CustomRoles role)
    {
        if (player == null || !GameStates.IsInGame || player.GetCustomRole() != role) return;
        if (GameStates.IsInTask && !GameStates.CalledMeeting && !GameStates.ExiledAnimate) { ApplyVanillaRole(player, role); return; }
        _ = new LateTask(() => ApplyVanillaRoleWhenInTask(player, role), 1f, "");
    }

    /// <summary>役職が変わった人の本編の役職を、見る人ごとに配り直す (規則は Modules.RoleView)</summary>
    static void ApplyVanillaRole(PlayerControl player, CustomRoles role)
    {
        if (player == null || !player.IsAlive()) return;
        // デシンクの役職は、本人の画面ではインポスターにする (基底が変身・消失でも)
        Modules.RoleView.ApplyBasisChange(player, role.GetRoleInfo()?.IsDesyncImpostor == true ? RoleTypes.Impostor : null);
    }

    // ===== 占い =====
    public static CustomRoles GetTellResults(this PlayerControl target, PlayerControl pc)
    {
        var role = CustomRoles.NotAssigned;
        if (target.GetRoleClass() is RoleBase roleclass) role = roleclass.TellResults(pc);
        if (role is CustomRoles.NotAssigned) role = target.GetCustomRole();
        return role;
    }

    // ===== 部屋 =====
    public static PlainShipRoom GetPlainShipRoom(this PlayerControl pc, bool IsDead)
    {
        if (ShipStatus.Instance == null) return null;
        var Rooms = ShipStatus.Instance.AllRooms;
        if (Rooms == null) return null;
        if (!pc.IsAlive()) return IsDead ? GetPlainShipRoom((Vector2)pc.transform.position) : null;
        foreach (var room in Rooms)
        {
            if (!room.roomArea) continue;
            if (pc.Collider.IsTouching(room.roomArea)) return room;
        }
        return null;
    }
    public static PlainShipRoom GetPlainShipRoom(this Vector2 pos)
    {
        if (ShipStatus.Instance == null) return null;
        var Rooms = ShipStatus.Instance.AllRooms;
        if (Rooms == null) return null;
        PlainShipRoom room = null;
        foreach (var psr in Rooms)
        {
            if (psr.roomArea == null) continue;
            if (psr.roomArea.OverlapPoint(pos)) room = psr;
        }
        return room;
    }
    public static string GetShipRoomName(this Vector2 pos, PlainShipRoom deforoom = null)
    {
        var Room = deforoom ??= pos.GetPlainShipRoom();
        var RoomName = Room == null ? "" : GetString($"{Room.RoomId}");
        if (Room == null || Room.RoomId is SystemTypes.Hallway)
        {
            var Distance = new Dictionary<byte, float>();
            var AllRooms = ShipStatus.Instance?.AllRooms;
            if (AllRooms != null)
                foreach (var room in AllRooms)
                {
                    if (room.RoomId == SystemTypes.Hallway) continue;
                    Distance[(byte)room.RoomId] = Vector2.Distance(pos, room.transform.position);
                }
            if (Distance.Count == 0) return RoomName;
            var nearest = Distance.OrderBy(x => x.Value).First().Key;
            var Nearestroom = GetString($"{(SystemTypes)nearest}");
            RoomName = Room == null ? string.Format(GetString("Nearroom"), Nearestroom) : Nearestroom + RoomName;
        }
        return RoomName;
    }
    public static string GetShipRoomName(this PlayerControl pc)
    {
        if (ShipStatus.Instance == null || pc == null) return "";
        var Room = pc.GetPlainShipRoom(true);
        return GetShipRoomName((Vector2)pc.transform.position, Room);
    }

    // ===== 陣営 =====
    public static bool IsTeammate(this PlayerControl player, PlayerControl target)
    {
        var plrole = player.GetCustomRole();
        var tgrole = target.GetCustomRole();
        if (plrole.IsImpostor() || plrole.JoinsImpostorTeam())
            return tgrole.IsImpostor() || tgrole.JoinsImpostorTeam();
        if (player.Is(CountTypes.Jackal)) return target.Is(CountTypes.Jackal);
        return false;
    }
    public static bool IsWinner(this PlayerControl player, CustomWinner? team = null)
    {
        if (CustomWinnerHolder.WinnerIds.Contains(player.PlayerId) && !CustomWinnerHolder.CantWinPlayerIds.Contains(player.PlayerId))
            return !team.HasValue || CustomWinnerHolder.winners.Contains(team.Value) || CustomWinnerHolder.AdditionalWinnerRoles.Contains((CustomRoles)team.Value);
        return false;
    }

    // ===== キルの相手 =====
    public static PlayerControl TryGetKilltarget(this PlayerControl pc, bool IsOneclick = false)
    {
        float killdis = NormalGameOptionsV12.KillDistances[Mathf.Clamp(GameManager.Instance.LogicOptions.currentGameOptions.GetInt(Int32OptionNames.KillDistance), 0, 2)];
        if (pc.Data.IsDead || pc.inVent) return null;
        var roletype = pc.GetCustomRole().GetCustomRoleTypes();
        Vector2 psi = pc.GetTruePosition();
        var ta = pc;
        foreach (var playerInfo in GameData.Instance.AllPlayers)
        {
            if (playerInfo.Disconnected || playerInfo.PlayerId == pc.PlayerId || playerInfo.IsDead) continue;
            var tage = playerInfo.Object;
            if (tage == null || tage.inVent || !tage.IsAlive()) continue;
            if (IsOneclick && tage.GetCustomRole().GetCustomRoleTypes() == roletype && roletype is CustomRoleTypes.Impostor) continue;
            var vector = tage.GetTruePosition() - psi;
            float dis = vector.magnitude;
            if (IsOneclick) dis = Mathf.Clamp(dis - 2f, 0.01f, 99);
            if (dis > killdis || PhysicsHelpers.AnyNonTriggersBetween(psi, vector.normalized, dis, Constants.ShipAndObjectsMask)) continue;
            killdis = dis;
            ta = tage;
        }
        return ta == pc ? null : ta;
    }
    public static PlayerControl GetKillTarget(this PlayerControl player, bool IsOneclick)
    {
        var playerrole = player.GetCustomRole();
        if (IsOneclick && !player.AmOwner) return player.TryGetKilltarget(true);
        if (player.AmOwner && GameStates.IsInTask && !GameStates.Intro && !(playerrole.IsImpostor() || playerrole.JoinsImpostorTeam())
            && (playerrole.GetRoleInfo()?.IsDesyncImpostor ?? false) && !player.Data.IsDead)
            return player.TryGetKilltarget();
        var players = player.GetPlayersInAbilityRangeSorted(false);
        return players.Count <= 0 ? null : players[0];
    }

    // ===== 会議中キル =====
    public static void RpcMeetingKill(this PlayerControl killer, PlayerControl target = null, bool SendtoClient = false)
    {
        if (!GameStates.InGame) return;
        if (target == null) target = killer;
        if (target.IsModClient() && !SendtoClient) return;
        var messageWriter = AmongUsClient.Instance.StartRpcImmediately(killer.NetId, (byte)RpcCalls.MurderPlayer, SendOption.None, target.GetClientId());
        messageWriter.WriteNetObject(target);
        messageWriter.Write((int)MurderResultFlags.Succeeded);
        AmongUsClient.Instance.FinishRpcImmediately(messageWriter);
    }
}
