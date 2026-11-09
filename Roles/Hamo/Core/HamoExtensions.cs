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

// ================================================================
// hamo の役職が使うプレイヤーの道具
// ================================================================

public partial class PlayerState
{
    /// <summary>false の間は動けない (速さを最低にする)</summary>
    public bool CanMove = true;
    /// <summary>このゲームで一度でもスポーンしたか (会議後のスポーンも含む)</summary>
    public bool HasSpawned { get; set; } = false;
    /// <summary>残りの緊急ボタンの数</summary>
    public float NumberOfRemainingButtons = Main.NormalOptions?.NumEmergencyMeetings ?? 1;
    /// <summary>死んだ部屋 (hamo のゲームログ用)</summary>
    public string KillRoom = "";
    /// <summary>開始直後の 10 秒キルボタンか</summary>
    public bool Is10secKillButton = true;
    /// <summary>今のメイン役職</summary>
    public CustomRoles MainRole => GetNowMainRole();
}

public partial class TaskState
{
    /// <summary>必要なタスク数。-1 なら全部</summary>
    public int NeedTaskCount = -1;
    public int GetNeedCountOrAll() => NeedTaskCount < 0 ? AllTasksCount : NeedTaskCount;
}

public static partial class StringHelper
{
    public static Color32 CodeColor(string code)
    {
        if (!code.StartsWith("#")) code = "#" + code;
        return ColorUtility.TryParseHtmlString(code, out var c) ? c : Color.white;
    }
    static readonly System.Text.RegularExpressions.Regex SizeTagRegex = new("</?size[^>]*>", System.Text.RegularExpressions.RegexOptions.Compiled);
    public static string RemoveSizeTags(this string str) => str.IndexOf('<') < 0 ? str : SizeTagRegex.Replace(str, "");
}

public static class HamoRoleHelper
{
    public static bool IsWhiteCrew(this CustomRoles roles) => roles is CustomRoles.UltraStar;
    /// <summary>TOH_L には幽霊役職が無い</summary>
    public static bool IsGhostRole(this CustomRoles role) => false;
    public static bool IsGhostRole(this PlayerControl pc) => false;
    public static bool IsLovers(this PlayerControl pc) => pc.Is(CustomRoles.Lovers);
    public static bool IsHamoNeutralKiller(this CustomRoles role)
        => role is CustomRoles.Egoist
            or CustomRoles.Jackal
            or CustomRoles.JackalHadouHo
            or CustomRoles.JackalWolf
            or CustomRoles.CountKiller
            or CustomRoles.Eater
            or CustomRoles.Obolus
            or CustomRoles.DarkHide;
}

public static class HamoPlayerExtensions
{
    // ===== キルクール =====
    /// <summary>hamo の SetKillCooldown。target に守護を見せてクールを始める</summary>
    public static void SetKillCooldownHamo(this PlayerControl player, float time = -1f, PlayerControl target = null, bool force = true, bool delay = true)
    {
        if (player == null) return;
        if (!player.CanUseKillButton() && !force) return;
        if (target == null) target = player;
        if (IUsePhantomButton.IPPlayerKillCooldown.ContainsKey(player.PlayerId))
            IUsePhantomButton.IPPlayerKillCooldown[player.PlayerId] = 0;
        if (!Main.AllPlayerKillCooldown.ContainsKey(player.PlayerId)) player.ResetKillCooldown();
        if (0f <= time)
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = time * 2;
            if (!force) player.GetPlayerState().Is10secKillButton = false;
        }
        else
        {
            Main.AllPlayerKillCooldown[player.PlayerId] *= 2;
        }
        player.SyncSettings();
        void Do()
        {
            if (player == null) return;
            player.RpcProtectedMurderPlayer(target);
            _ = new LateTask(() =>
            {
                if (player == null) return;
                player.ResetKillCooldown();
                player.SyncSettings();
            }, 1f, "SetKillCooldownHamo.Reset");
        }
        if (delay) _ = new LateTask(Do, 0.2f, "SetKillCooldownHamo");
        else Do();
    }

    public static void RpcResetAbilityCooldownHamo(this PlayerControl target, bool log = true, bool Sync = false)
    {
        if (target == null || !AmongUsClient.Instance.AmHost) return;
        if (CustomWinnerHolder.WinnerTeam is not CustomWinner.Default) return;
        if (Sync)
        {
            target.SyncSettings();
            _ = new LateTask(() => target?.RpcResetAbilityCooldown(), 0.2f, "RpcResetAbilityCooldownHamo", true);
        }
        else target.RpcResetAbilityCooldown();
    }

    // ===== 役職の変更 =====
    /// <summary>
    /// hamo の RpcSetCustomRole。setRole が true なら本編の役職 (インポスター・エンジニア等) も変える。
    /// log は TOH_L ではログに書くだけ。
    /// </summary>
    public static void RpcSetCustomRoleHamo(this PlayerControl player, CustomRoles role, bool setRole = true, bool? log = false)
    {
        if (player == null) return;
        if (player.GetCustomRole() == role) return;
        var isMain = role < CustomRoles.StartAddon && role > CustomRoles.NotAssigned;
        var oldClass = player.GetRoleClass();
        player.RpcSetCustomRole(role);
        if (!isMain) return;
        Logger.Info($"{player.GetNameWithRole().RemoveHtmlTags()} => {role}", "RpcSetCustomRoleHamo");
        if (!AmongUsClient.Instance.AmHost) return;
        if (oldClass != null && oldClass == player.GetRoleClass()) return;

        NameColorManager.RemoveAll(player.PlayerId);
        if (setRole)
        {
            if (GameStates.IsInTask && !GameStates.CalledMeeting) SetVanillaRole(player, role);
            else _ = new LateTask(() => { if (GameStates.IsInTask) SetVanillaRole(player, role); }, 10f, "ExSetRole", true);
        }
        player.ResetKillCooldown();
        player.SetKillCooldownHamo(delay: true, force: true);
        player.RpcResetAbilityCooldown();
        (player.GetRoleClass() as IUsePhantomButton)?.Init(player);
        foreach (var roleclass in CustomRoleManager.AllActiveRoles.Values.OfType<HamoRoleBase>())
            roleclass.ChangeColor();
        Utils.MarkEveryoneDirtySettings();
        Utils.NotifyRoles(ForceLoop: true);
    }

    /// <summary>役職が変わった人の本編の役職を、見る人ごとに配り直す (規則は Modules.RoleView)</summary>
    public static void SetVanillaRole(PlayerControl player, CustomRoles role)
    {
        if (player == null || !player.IsAlive()) return;
        // デシンクの役職は、本人の画面ではインポスターにする (基底が変身・消失でも)
        Modules.RoleView.ApplyBasisChange(player, role.GetRoleInfo()?.IsDesyncImpostor == true ? RoleTypes.Impostor : null);
    }

    // ===== 占い =====
    public static CustomRoles GetTellResults(this PlayerControl target, PlayerControl pc)
    {
        var role = CustomRoles.NotAssigned;
        if (target.GetRoleClass() is HamoRoleBase roleclass) role = roleclass.TellResults(pc);
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
        if (plrole.IsImpostor() || plrole is CustomRoles.Egoist)
            return tgrole.IsImpostor() || tgrole is CustomRoles.Egoist;
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
        if (player.AmOwner && GameStates.IsInTask && !GameStates.Intro && !(playerrole.IsImpostor() || playerrole is CustomRoles.Egoist)
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

    public static void RpcSnapToForced(this PlayerControl pc, Vector2 position, SendOption sendOption) => pc.RpcSnapTo(position);
}
