using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHostL.Attributes;
using TownOfHostL.Modules.SpawnEditor;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.Modules;

public static class FreePlayRole
{
    private static bool ready;

    public static bool Available =>
        GameStates.IsFreePlay && PlayerControl.LocalPlayer && ShipStatus.Instance && !MeetingHud.Instance;

    private static void EnsureInit()
    {
        if (ready) return;
        Main.AllPlayerKillCooldown = new Dictionary<byte, float>();
        Main.AfterMeetingDeathPlayers = new();
        Main.clientIdList = new();
        Main.CheckShapeshift = new();
        Main.ShapeshiftTarget = new();
        Main.ShowRoleInfoAtMeeting = new();
        ReportDeadBodyPatch.CannotReportList = new();
        ReportDeadBodyPatch.CannotReportByDeadBodyList = new();
        ReportDeadBodyPatch.DontReportMarkList = new();
        MeetingHudPatch.RevengeTargetPlayer = new();
        Main.RealOptionsData = new OptionBackupData(GameOptionsManager.Instance.CurrentGameOptions);
        Main.DefaultCrewmateVision = Main.RealOptionsData.GetFloat(FloatOptionNames.CrewLightMod);
        Main.DefaultImpostorVision = Main.RealOptionsData.GetFloat(FloatOptionNames.ImpostorLightMod);
        Main.LastNotifyNames = new();
        Main.PlayerColors = new();
        Main.AllPlayerNames = new();
        RandomSpawn.CustomNetworkTransformPatch.FirstTP = new();

        GameModuleInitializerAttribute.InitializeAll();

        foreach (var pc in Main.AllPlayerControls)
        {
            int colorId = pc.Data.DefaultOutfit.ColorId;
            PlayerState.Create(pc.PlayerId);
            Main.AllPlayerNames[pc.PlayerId] = pc.Data.PlayerName;
            Main.PlayerColors[pc.PlayerId] = Palette.PlayerColors[Mathf.Clamp(colorId, 0, Palette.PlayerColors.Length - 1)];
            ReportDeadBodyPatch.WaitReport[pc.PlayerId] = new();
            RandomSpawn.CustomNetworkTransformPatch.FirstTP[pc.PlayerId] = false;
            var outfit = pc.Data.DefaultOutfit;
            Camouflage.PlayerSkins[pc.PlayerId] = new NetworkedPlayerInfo.PlayerOutfit().Set(outfit.PlayerName, outfit.ColorId, outfit.HatId, outfit.SkinId, outfit.VisorId, outfit.PetId, outfit.NamePlateId);
            Main.clientIdList.Add(pc.GetClientId());
            bool imp = pc.Data.Role && pc.Data.Role.IsImpostor;
            PlayerState.GetByPlayerId(pc.PlayerId).SetMainRole(imp ? CustomRoles.Impostor : CustomRoles.Crewmate);
        }

        MeetingStates.FirstMeeting = true;
        GameStates.AlreadyDied = false;
        Main.introDestroyed = true;
        Main.IsSetRoleFinished = true;
        GameStates.InGame = true;
        CustomRoleManager.CreateInstance();
        foreach (var pc in Main.AllPlayerControls) pc.ResetKillCooldown();
        ready = true;
    }

    public static void Become(CustomRoles role)
    {
        if (!Available || role is CustomRoles.NotAssigned || role.GetRoleInfo() == null && role is not (CustomRoles.Crewmate or CustomRoles.Impostor)) return;
        try
        {
            EnsureInit();
            var pc = PlayerControl.LocalPlayer;
            pc.GetRoleClass()?.Dispose();
            PlayerState.GetByPlayerId(pc.PlayerId).SetMainRole(role);
            RoleManager.Instance.SetRole(pc, role.GetRoleTypes());
            CustomRoleManager.CreateInstance(role, pc);
            pc.ResetKillCooldown();
            pc.SetKillCooldown(1f);
            Utils.SyncAllSettings();
            Utils.NotifyRoles(ForceLoop: true);
            RPC.PlaySound(pc.PlayerId, Sounds.TaskComplete);
            Logger.Info($"フリープレイの役職: {role}", "FreePlayRole");
        }
        catch (Exception ex)
        {
            Logger.Error($"フリープレイで役職を付けられませんでした: {ex}", "FreePlayRole");
        }
    }

    public static void Reset()
    {
        if (!ready) return;
        ready = false;
        GameStates.InGame = false;
    }

    [HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.DisconnectInternal))]
    private static class DisconnectPatch
    {
        public static void Prefix()
        {
            if (GameStates.IsFreePlay) Reset();
        }
    }
}
