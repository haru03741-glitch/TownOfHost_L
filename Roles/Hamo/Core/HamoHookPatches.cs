using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

// ================================================================
// hamo の役職のフック (OnDead / OnSpawn / サボタージュ / ベント など) を
// TOH_L のゲームの流れへつなぐ所。
// ================================================================

namespace TownOfHostL.Roles.Hamo;

public static class HamoHookPatches
{
    [GameModuleInitializer]
    public static void Init()
    {
        Main.IsActiveSabotage = false;
        Main.SabotageType = SystemTypes.Hallway;
        Main.LastSab = byte.MaxValue;
        Main.SabotageActivetimer = 0;
        IUsePhantomButton.IPPlayerKillCooldown.Clear();
    }

    /// <summary>イントロが終わった時 (ホスト)</summary>
    public static void OnIntroEnd()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        _ = new LateTask(() =>
        {
            foreach (var role in HamoHooks.AllHamoRoles) role.StartGameTasks();
            foreach (var pc in Main.AllPlayerControls)
            {
                (pc.GetRoleClass() as IUsePhantomButton)?.Init(pc);
                if (Main.NormalOptions.MapId != 4)
                    (pc.GetRoleClass() as HamoRoleBase)?.OnSpawn(true);
            }
        }, 0.5f, "Hamo.StartGameTasks", true);
    }

    /// <summary>追放画面が終わった時 (ホスト)</summary>
    public static void OnExileWrapUp(bool airship)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!airship)
        {
            foreach (var pc in Main.AllPlayerControls)
                (pc.GetRoleClass() as HamoRoleBase)?.OnSpawn();
        }
        // 会議の後、自分の画面だけ本編の役職を変える役職
        _ = new LateTask(ApplyAfterMeetingRoles, airship ? 0.5f : 1.0f, "Hamo.AfterMeetingRole", true);
    }

    public static void ApplyAfterMeetingRoles()
    {
        if (!AmongUsClient.Instance.AmHost || GameStates.IsMeeting || !GameStates.IsInGame) return;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (!pc.IsAlive()) continue;
            if (pc.GetRoleClass() is not HamoRoleBase role) continue;
            if (role.AfterMeetingRole is not RoleTypes setrole) continue;
            if ((role as IUsePhantomButton)?.IsPhantomRole is false && setrole is RoleTypes.Phantom) setrole = RoleTypes.Impostor;
            pc.RpcSetRoleDesync(setrole, pc.GetClientId());
        }
    }

    /// <summary>エアシップで湧いた時 (ホスト)</summary>
    public static void OnAirshipSpawn(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || player == null) return;
        (player.GetRoleClass() as HamoRoleBase)?.OnSpawn(MeetingStates.FirstMeeting);
    }

    /// <summary>サボタージュが直った時</summary>
    public static void CompleteSabotage()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Main.SabotageType is SystemTypes.Hallway) return;
        Main.IsActiveSabotage = false;
        Main.SabotageActivetimer = 0;
        foreach (var role in HamoHooks.AllHamoRoles) role.AfterSabotage(Main.SabotageType);
    }

    /// <summary>サボタージュを直そうとした時</summary>
    public static void OnFixSabotage(PlayerControl player, byte amount)
    {
        if (!AmongUsClient.Instance.AmHost || player == null) return;
        foreach (var role in HamoHooks.AllHamoRoles) role.OnFixSabotage(player, Main.SabotageType, amount);
    }

    public static byte PeekByte(MessageReader msgReader)
    {
        var r = MessageReader.Get(msgReader);
        var b = r.ReadByte();
        r.Recycle();
        return b;
    }

    /// <summary>ベント移動を止める役職か (ホストが見る)</summary>
    public static bool CanVentMoving(PlayerPhysics physics, int ventId)
        => (physics?.myPlayer?.GetRoleClass() as HamoRoleBase)?.CanVentMoving(physics, ventId) ?? true;

    /// <summary>ベントに入った時、移動できない役職なら矢印を消す (入り直しで矢印が出なくなる)</summary>
    public static void OnEnterVent(PlayerPhysics physics, int id)
    {
        var user = physics?.myPlayer;
        if (user == null || CanVentMoving(physics, id)) return;
        if (user.PlayerId == PlayerControl.LocalPlayer.PlayerId) return; // ホストは TryMoveToVent で止める
        int clientId = user.GetClientId();
        _ = new LateTask(() =>
        {
            var w = AmongUsClient.Instance.StartRpcImmediately(physics.NetId, (byte)RpcCalls.BootFromVent, SendOption.None, clientId);
            w.Write(id);
            AmongUsClient.Instance.FinishRpcImmediately(w);
        }, 0.1f, "Vent- BootFromVent", true);
        _ = new LateTask(() =>
        {
            var w = AmongUsClient.Instance.StartRpcImmediately(physics.NetId, (byte)RpcCalls.EnterVent, SendOption.None, clientId);
            w.Write(id);
            AmongUsClient.Instance.FinishRpcImmediately(w);
        }, 0.25f, "Vent- EnterVent", true);
    }

    /// <summary>ファントムのボタン (ワンクリック) を他の人が押した時 (ホスト)。false でバニラの透明化を止める</summary>
    public static bool CheckVanish(PlayerControl player, IUsePhantomButton pb)
    {
        if (player.PlayerId == PlayerControl.LocalPlayer.PlayerId) return false;
        var AdjustKillCooldown = true;
        bool? ResetCooldown = true;
        pb.CheckOnClick(ref AdjustKillCooldown, ref ResetCooldown);

        float cooldown = IUsePhantomButton.GetRemainingKillCooldown(player);
        if (pb.SyncAbilityCooldownWithKillCooldown) pb.SetSyncedAbilityCooldown(cooldown);
        Logger.Info($"Use:{player.GetNameWithRole()}", "PhantomButton");
        if (AdjustKillCooldown)
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = cooldown < 10 ? cooldown : cooldown * 2;
            player.SyncSettings();
        }
        var writer = CustomRpcSender.Create("Phantom OneClick", SendOption.None);
        writer.StartMessage(player.GetClientId());
        if (ResetCooldown is not null)
        {
            writer.StartRpc(player.NetId, (byte)RpcCalls.SetRole)
                .Write((ushort)RoleTypes.Impostor)
                .Write(true)
                .EndRpc();
            if (pb.IsPhantomRole)
            {
                writer.StartRpc(player.NetId, (byte)RpcCalls.SetRole)
                    .Write((ushort)RoleTypes.Phantom)
                    .Write(true)
                    .EndRpc();
            }
        }
        if (AdjustKillCooldown && 10 < cooldown)
        {
            writer.StartRpc(player.NetId, (byte)RpcCalls.MurderPlayer)
                .WriteNetObject(player)
                .Write((int)MurderResultFlags.FailedProtected)
                .EndRpc();
        }
        if (ResetCooldown is true && pb.IsPhantomRole)
        {
            writer.StartRpc(player.NetId, (byte)RpcCalls.ProtectPlayer)
                .WriteNetObject(player)
                .Write(0)
                .EndRpc();
        }
        writer.EndMessage();
        writer.SendMessage();
        player.ResetKillCooldown();
        if (AdjustKillCooldown)
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = cooldown;
            IUsePhantomButton.IPPlayerKillCooldown[player.PlayerId] = 0f;
        }
        return false;
    }

    /// <summary>ホスト自身がファントムのボタンを押した時</summary>
    public static void HandlePhantomClick(PlayerControl player, IUsePhantomButton pb)
    {
        bool AdjustKillCooldown = true;
        bool? ResetCooldown = true;
        pb.CheckOnClick(ref AdjustKillCooldown, ref ResetCooldown);
        float cooldown = IUsePhantomButton.GetRemainingKillCooldown(player);
        if (pb.SyncAbilityCooldownWithKillCooldown) pb.SetSyncedAbilityCooldown(cooldown);
        if (AdjustKillCooldown)
        {
            Main.AllPlayerKillCooldown[player.PlayerId] = cooldown;
            IUsePhantomButton.IPPlayerKillCooldown[player.PlayerId] = 0f;
            player.SetKillTimer(cooldown);
            player.SyncSettings();
        }
        if (ResetCooldown == true) player.Data.Role.SetCooldown();
    }
}

// ===== サボタージュ =====
[HarmonyPatch(typeof(SabotageSystemType), nameof(SabotageSystemType.UpdateSystem))]
static class HamoSabotageStartPatch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix([HarmonyArgument(1)] MessageReader msgReader, out byte __state)
        => __state = HamoHookPatches.PeekByte(msgReader);
    public static void Postfix([HarmonyArgument(0)] PlayerControl player, byte __state, bool __runOriginal)
    {
        if (!__runOriginal || !AmongUsClient.Instance.AmHost || player == null) return;
        Main.SabotageType = (SystemTypes)__state;
        Main.IsActiveSabotage = true;
        Main.LastSab = player.PlayerId;
        Main.SabotageActivetimer = 0;
    }
}
[HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.FixedUpdate))]
static class HamoSabotageTimerPatch
{
    public static void Postfix()
    {
        if (Main.IsActiveSabotage && GameStates.IsInTask) Main.SabotageActivetimer += Time.fixedDeltaTime;
    }
}
[HarmonyPatch]
static class HamoFixSabotagePatch
{
    static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ReactorSystemType), nameof(ReactorSystemType.UpdateSystem));
        yield return AccessTools.Method(typeof(SwitchSystem), nameof(SwitchSystem.UpdateSystem));
        yield return AccessTools.Method(typeof(HqHudSystemType), nameof(HqHudSystemType.UpdateSystem));
        yield return AccessTools.Method(typeof(HudOverrideSystemType), nameof(HudOverrideSystemType.UpdateSystem));
        yield return AccessTools.Method(typeof(LifeSuppSystemType), nameof(LifeSuppSystemType.UpdateSystem));
        yield return AccessTools.Method(typeof(HeliSabotageSystem), nameof(HeliSabotageSystem.UpdateSystem));
    }
    [HarmonyPriority(Priority.First)]
    public static void Prefix([HarmonyArgument(1)] MessageReader msgReader, out byte __state)
        => __state = HamoHookPatches.PeekByte(msgReader);
    public static void Postfix([HarmonyArgument(0)] PlayerControl player, byte __state, bool __runOriginal)
    {
        if (!__runOriginal) return;
        HamoHookPatches.OnFixSabotage(player, __state);
    }
}
[HarmonyPatch]
static class HamoSabotageCompletePatch
{
    static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ElectricTask), nameof(ElectricTask.Complete));
        yield return AccessTools.Method(typeof(HeliCharlesTask), nameof(HeliCharlesTask.Complete));
        yield return AccessTools.Method(typeof(HqHudOverrideTask), nameof(HqHudOverrideTask.Complete));
        yield return AccessTools.Method(typeof(MushroomMixupSabotageTask), nameof(MushroomMixupSabotageTask.Complete));
        yield return AccessTools.Method(typeof(NoOxyTask), nameof(NoOxyTask.Complete));
        yield return AccessTools.Method(typeof(ReactorTask), nameof(ReactorTask.Complete));
        yield return AccessTools.Method(typeof(HudOverrideTask), nameof(HudOverrideTask.Complete));
    }
    public static void Postfix() => HamoHookPatches.CompleteSabotage();
}

// ===== ベント =====
[HarmonyPatch(typeof(VentilationSystem), nameof(VentilationSystem.UpdateSystem))]
static class HamoVentilationSystemPatch
{
    public static void Prefix([HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] MessageReader msgReader)
    {
        if (!AmongUsClient.Instance.AmHost || player == null) return;
        VentilationSystem.Operation op;
        byte ventId;
        {
            var r = MessageReader.Get(msgReader);
            r.ReadUInt16();
            op = (VentilationSystem.Operation)r.ReadByte();
            ventId = r.ReadByte();
            r.Recycle();
        }
        VentilationSystemUpdateSystemPatch.NowVentId[player.PlayerId] = ventId;
        foreach (var role in HamoHooks.AllHamoRoles) role.OnVentilationSystemUpdate(player, op, ventId);
    }
}
[HarmonyPatch(typeof(Vent), nameof(Vent.TryMoveToVent))]
static class HamoTryMoveToVentPatch
{
    public static bool Prefix(Vent __instance, ref bool __result)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        var pc = PlayerControl.LocalPlayer;
        if (pc == null || HamoHookPatches.CanVentMoving(pc.MyPhysics, __instance.Id)) return true;
        __result = false;
        return false;
    }
}

// ===== 死亡・切断 =====
[HarmonyPatch(typeof(PlayerState), nameof(PlayerState.SetDead))]
static class HamoSetDeadPatch
{
    public static void Prefix(PlayerState __instance, out bool __state) => __state = __instance.IsDead;
    public static void Postfix(PlayerState __instance, bool __state, byte ___PlayerId)
    {
        if (__state || !AmongUsClient.Instance.AmHost || !GameStates.IsInGame) return;
        var player = Utils.GetPlayerById(___PlayerId);
        if (player == null) return;
        foreach (var role in HamoHooks.AllHamoRoles)
        {
            try { role.OnDead(player); }
            catch (System.Exception ex) { Logger.Exception(ex, "Hamo.OnDead"); }
        }
    }
}
[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
static class HamoPlayerLeftPatch
{
    public static void Prefix([HarmonyArgument(0)] InnerNet.ClientData data, out PlayerControl __state) => __state = data?.Character;
    public static void Postfix(PlayerControl __state)
    {
        if (__state == null || !AmongUsClient.Instance.AmHost || !GameStates.IsInGame) return;
        foreach (var role in HamoHooks.AllHamoRoles)
        {
            try { role.OnLeftPlayer(__state); }
            catch (System.Exception ex) { Logger.Exception(ex, "Hamo.OnLeftPlayer"); }
        }
    }
}

// ===== タスク =====
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CompleteTask))]
static class HamoCompleteTaskIdPatch
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix([HarmonyArgument(0)] uint idx) => HamoHooks.CurrentTaskId = idx;
}

// ===== ファントムのボタン (ホスト自身) =====
[HarmonyPatch(typeof(AbilityButton), nameof(AbilityButton.DoClick))]
static class HamoAbilityButtonDoClickPatch
{
    public static bool Prefix(AbilityButton __instance)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        var player = PlayerControl.LocalPlayer;
        if (player == null || HudManager._instance.AbilityButton.isCoolingDown
            || !player.CanMove || !player.IsAlive()) return true;
        if (player.GetRoleClass() is not IUsePhantomButton pb || !pb.UseOneclickButton) return true;

        // 押したのが分かるように少し暗くする
        __instance.OverrideColor(Palette.DisabledGrey);
        _ = new LateTask(() => __instance.OverrideColor(Palette.EnabledColor), 0.07f, "", true);
        HamoHookPatches.HandlePhantomClick(player, pb);
        return false;
    }
}
