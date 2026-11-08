using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

using TownOfHostL.Modules;
using TownOfHostL.Roles;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.Unit;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.AddOns.Impostor;
using TownOfHostL.Roles.AddOns.Crewmate;
using TownOfHostL.Roles.AddOns;

namespace TownOfHostL;

/// <summary>値は GameMode の設定の選択肢の並びと揃える。どの設定にも付けない値は後ろに置く。</summary>
public enum CustomGameMode
{
    Standard,
    HideAndSeek,
    CatchCat,
    //OneNight,
    HideMenu,
    /// <summary>本編のかくれんぼ。設定では選ばず、ロビーの切り替え (/mode) で入る。</summary>
    VanillaHideAndSeek,
    All = int.MaxValue
}
[HarmonyPatch]
public static partial class Options
{
    //static Task taskOptionsLoad;
    [HarmonyPatch(typeof(TranslationController), nameof(TranslationController.Initialize)), HarmonyPostfix]
    public static void OptionsLoadStart()
    {
        // 2 回目に呼ばれると設定を二重に作り、保存待ちの変更もファイルの値で上書きしてしまう
        if (IsLoaded) return;
        Logger.Info("Options.Load Start", "Options");
        //taskOptionsLoad = Task.Run(Load);
        Load();
    }
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix]
    public static void WaitOptionsLoad()
    {
        //taskOptionsLoad.Wait();
        Logger.Info("Options.Load End", "Options");
    }

    // ゲームモード
    public static OptionItem GameMode;
    public static CustomGameMode CurrentGameMode => (CustomGameMode)GameMode.GetValue();

    public static readonly string[] gameModes =
    {
         "Standard", "HideAndSeek", /*"CatchCat", "OneNight",*/
    };

    // MapActive
    public static bool IsActiveSkeld => AddedTheSkeld.GetValue() == 1 || Main.NormalOptions.MapId == 0;
    public static bool IsActiveMiraHQ => AddedMiraHQ.GetValue() == 1 || Main.NormalOptions.MapId == 1;
    public static bool IsActivePolus => AddedPolus.GetValue() == 1 || Main.NormalOptions.MapId == 2;
    public static bool IsActiveAirship => AddedTheAirship.GetValue() == 1 || Main.NormalOptions.MapId == 4;
    public static bool IsActiveFungle => AddedTheFungle.GetValue() == 1 || Main.NormalOptions.MapId == 5;

    // 役職数・確率
    public static Dictionary<CustomRoles, OptionItem> CustomRoleCounts;
    public static Dictionary<CustomRoles, IntegerOptionItem> CustomRoleSpawnChances;
    public static readonly string[] rates =
    {
        "Rate0",  "Rate5",  "Rate10", "Rate20", "Rate30", "Rate40",
        "Rate50", "Rate60", "Rate70", "Rate80", "Rate90", "Rate100",
    };

    //役職直接属性付与
    public static Dictionary<(CustomRoles, CustomRoles), OptionItem> AddOnRoleOptions = new();
    public static Dictionary<CustomRoles, OptionItem> AddOnBuffAssign = new();
    public static Dictionary<CustomRoles, OptionItem> AddOnDebuffAssign = new();

    // 各役職の詳細設定
    public static OptionItem EnableGM;
    public static float DefaultKillCooldown = Main.NormalOptions?.KillCooldown ?? 20;
    public static OptionItem DefaultShapeshiftCooldown;
    public static OptionItem ImpostorOperateVisibility;
    public static OptionItem CanMakeMadmateCount;
    public static OptionItem MadmateCanFixLightsOut;
    public static OptionItem MadmateCanFixComms;
    public static OptionItem MadmateHasImpostorVision;
    public static OptionItem MadmateCanSeeKillFlash;
    public static OptionItem MadmateCanSeeOtherVotes;
    public static OptionItem MadmateCanSeeDeathReason;
    public static OptionItem MadmateRevengeCrewmate;
    public static OptionItem MadmateVentCooldown;
    public static OptionItem MadmateVentMaxTime;

    public static OptionItem KillFlashDuration;

    // HideAndSeek
    /// <summary>0 = かくれんぼ専用の役職、1 = 通常モードの役職</summary>
    public static OptionItem HideAndSeekRolePool;
    public static OptionItem AllowCloseDoors;
    public static OptionItem KillDelay;
    // public static OptionItem IgnoreCosmetics;
    public static OptionItem IgnoreVent;
    public static float HideAndSeekKillDelayTimer = 0f;

    // タスク無効化
    public static OptionItem DisableTasks;

    // マップ設定
    public static OptionItem MapOption_Skeld;
    public static OptionItem MapOption_MiraHQ;
    public static OptionItem MapOption_Polus;
    public static OptionItem MapOption_Airship;
    public static OptionItem MapOption_Fungle;

    //デバイスブロック
    public static OptionItem DisableDevices_Skeld;
    public static OptionItem DisableAdmin_Skeld;
    public static OptionItem DisableCamera_Skeld;
    public static OptionItem DisableDevices_MiraHQ;
    public static OptionItem DisableAdmin_MiraHQ;
    public static OptionItem DisableDoorLog_MiraHQ;
    public static OptionItem DisableDevices_Polus;
    public static OptionItem DisableAdmin_Polus;
    public static OptionItem DisableCamera_Polus;
    public static OptionItem DisableVital_Polus;
    public static OptionItem DisableDevices_Airship;
    public static OptionItem DisableCockpitAdmin_Airship;
    public static OptionItem DisableRecordsAdmin_Airship;
    public static OptionItem DisableCamera_Airship;
    public static OptionItem DisableVital_Airship;
    public static OptionItem DisableDevices_Fungle;
    public static OptionItem DisableVital_Fungle;
    public static OptionItem DisableDevicesIgnoreConditions;
    public static OptionItem DisableDevicesIgnoreImpostors;
    public static OptionItem DisableDevicesIgnoreMadmates;
    public static OptionItem DisableDevicesIgnoreNeutrals;
    public static OptionItem DisableDevicesIgnoreCrewmates;
    public static OptionItem DisableDevicesIgnoreAfterAnyoneDied;

    // ランダムマップ
    public static OptionItem RandomMapsMode;
    public static OptionItem AddedTheSkeld;
    public static OptionItem AddedMiraHQ;
    public static OptionItem AddedPolus;
    public static OptionItem AddedTheAirship;
    public static OptionItem AddedTheFungle;

    // ランダムスポーン
    public static OptionItem RandomSpawn_Skeld;
    public static OptionItem RandomSpawn_MiraHQ;
    public static OptionItem RandomSpawn_Polus;
    public static OptionItem RandomSpawn_Airship;
    public static OptionItem RandomSpawn_Fungle;
    public static OptionItem AdditionalSpawn_Skeld;
    public static OptionItem AdditionalSpawn_MiraHQ;
    public static OptionItem AdditionalSpawn_Polus;
    public static OptionItem AdditionalSpawn_Airship;
    public static OptionItem AdditionalSpawn_AirshipTAKADA;
    public static OptionItem AdditionalSpawn_Fungle;
    public static OptionItem DisableNearButton_Skeld;
    public static OptionItem DisableNearButton_MiraHQ;
    public static OptionItem DisableNearButton_Polus;
    public static OptionItem DisableNearButton_Airship;
    public static OptionItem DisableNearButton_Fungle;
    public static OptionItem FirstFixedSpawn_Skeld;
    public static OptionItem FirstFixedSpawn_MiraHQ;
    public static OptionItem FirstFixedSpawn_Polus;
    public static OptionItem FirstFixedSpawn_Fungle;

    // 投票モード
    public static OptionItem VoteMode;
    public static OptionItem WhenSkipVote;
    public static OptionItem WhenSkipVoteIgnoreFirstMeeting;
    public static OptionItem WhenSkipVoteIgnoreNoDeadBody;
    public static OptionItem WhenSkipVoteIgnoreEmergency;
    public static OptionItem WhenNonVote;
    public static OptionItem WhenTie;
    public static readonly string[] voteModes =
    {
        "Default", "Suicide", "SelfVote", "Skip"
    };
    public static readonly string[] tieModes =
    {
        "TieMode.Default", "TieMode.All", "TieMode.Random"
    };
    public static VoteMode GetWhenSkipVote() => (VoteMode)WhenSkipVote.GetValue();
    public static VoteMode GetWhenNonVote() => (VoteMode)WhenNonVote.GetValue();

    // ボタン回数
    public static OptionItem SyncButtonMode;
    public static OptionItem SyncedButtonCount;
    public static int UsedButtonCount = 0;

    // 全員生存時の会議時間
    public static OptionItem AllAliveMeeting;
    public static OptionItem AllAliveMeetingTime;

    // 追加の緊急ボタンクールダウン
    public static OptionItem AdditionalEmergencyCooldown;
    public static OptionItem AdditionalEmergencyCooldownThreshold;
    public static OptionItem AdditionalEmergencyCooldownTime;

    //転落死
    public static OptionItem LadderDeath;
    public static OptionItem LadderDeathChance;


    // リアクターの時間制御
    public static OptionItem SabotageTimeControl_Skeld;
    public static OptionItem SkeldReactorTimeLimit;
    public static OptionItem SkeldO2TimeLimit;
    public static OptionItem SabotageTimeControl_MiraHQ;
    public static OptionItem MiraHQReactorTimeLimit;
    public static OptionItem MiraHQO2TimeLimit;
    public static OptionItem SabotageTimeControl_Polus;
    public static OptionItem SabotageTimeControl_Airship;
    public static OptionItem SabotageTimeControl_Fungle;
    public static OptionItem PolusReactorTimeLimit;
    public static OptionItem AirshipReactorTimeLimit;
    public static OptionItem FungleReactorTimeLimit;
    public static OptionItem FungleMushroomMixupDuration;

    // サボタージュのクールダウン変更
    public static OptionItem ModifySabotageCooldown;
    public static OptionItem SabotageCooldown;

    // 停電の特殊設定
    public static OptionItem LightsOutSpecialSettings_Airship;
    public static OptionItem DisableAirshipViewingDeckLightsPanel;
    public static OptionItem DisableAirshipGapRoomLightsPanel;
    public static OptionItem DisableAirshipCargoLightsPanel;
    public static OptionItem BlockDisturbancesToSwitches;
    // キノコカオスサボ時のボタン無効
    public static OptionItem DisableButtonInMushroomMixup;

    // マップ改造
    public static OptionItem ResetDoorsEveryTurns_Polus;
    public static OptionItem ResetDoorsEveryTurns_Airship;
    public static OptionItem ResetDoorsEveryTurns_Fungle;
    public static OptionItem DoorsResetMode_Polus;
    public static OptionItem DoorsResetMode_Airship;
    public static OptionItem DoorsResetMode_Fungle;

    public static OptionItem AirShipVariableElectrical;
    public static OptionItem DisableAirshipMovingPlatform;
    public static OptionItem FungleCanUseZipline;
    public static OptionItem FungleCanUseZiplineFromTop;
    public static OptionItem FungleCanUseZiplineFromUnder;
    public static OptionItem FungleCanSporeTrigger;

    // その他
    public static OptionItem FixFirstKillCooldown;
    public static OptionItem DisableTaskWin;
    public static OptionItem GhostCantSeeOtherRoles;
    public static OptionItem GhostCantSeeOtherTasks;
    public static OptionItem GhostCantSeeOtherVotes;
    public static OptionItem GhostCanSeeOtherTeams;
    public static OptionItem GhostCanSeeDeathReason;
    public static OptionItem GhostIgnoreTasks;
    public static OptionItem CommsCamouflage;
    public static OptionItem DisableSabotage;
    public static OptionItem DisableSabotageReactor;
    public static OptionItem DisableSabotageO2;
    public static OptionItem DisableSabotageLights;
    public static OptionItem DisableSabotageComms;
    public static OptionItem DisableSabotageMushroom;
    public static OptionItem DisableSabotageDoors;
    public static OptionItem DeadImpostorCannotSabotage;

    //public static OptionItem SkinControle;
    //public static OptionItem NoHat;
    //public static OptionItem NoFullFaceHat;
    //public static OptionItem NoSkin;
    //public static OptionItem NoVisor;
    //public static OptionItem NoPet;
    //public static OptionItem NoDuplicateHat;
    //public static OptionItem NoDuplicateSkin;

    // プリセット対象外
    public static OptionItem NoGameEnd;
    public static OptionItem NoGameEndByCount;
    public static OptionItem NoGameEndByTask;
    public static OptionItem NoGameEndBySabotage;
    public static OptionItem NoGameEndByRole;
    public static OptionItem AutoDisplayLastResult;
    public static OptionItem AutoDisplayKillLog;
    public static OptionItem SuffixMode;
    public static OptionItem HideGameSettings;
    public static OptionItem NameChangeMode;
    public static OptionItem ChangeNameToRoleInfo;
    public static OptionItem RoleAssigningAlgorithm;

    public static OptionItem ApplyDenyNameList;
    public static OptionItem KickPlayerFriendCodeNotExist;
    public static OptionItem ApplyBanList;
    public static OptionItem AntiCheat;
    public static OptionItem CheaterAutoBan;
    public static OptionItem CheatLobbyKill;

    // ModGameMode

    //public static bool IsONMode => CurrentGameMode == CustomGameMode.OneNight;

    // TOH_Y機能
    // 会議収集理由表示
    public static OptionItem ShowReportReason;
    // 道連れ対象表示
    public static OptionItem ShowRevengeTarget;
    // 初手会議に役職説明表示
    public static OptionItem ShowRoleInfoAtFirstMeeting;
    // 道連れ設定
    public static OptionItem RevengeNeutral;
    public static OptionItem RevengeMadByImpostor;
    public static OptionItem RevengeImpostorByImpostor;

    public static OptionItem HostGhostIgnoreTasks;
    public static OptionItem ChangeIntro;
    public static OptionItem DisplayTeamMark;
    public static OptionItem AddonShow;
    public static readonly string[] addonShowModes =
    {
        "addonShowModes.Default", "addonShowModes.All", "addonShowModes.TOH"
    };
    public static AddonShowMode GetAddonShowModes() => (AddonShowMode)AddonShow.GetValue();
    public static readonly string[] nameChangeModes =
    {
        "nameChangeMode.None", "nameChangeMode.Crew", "nameChangeMode.Color"
    };
    public static NameChange GetNameChangeModes() => (NameChange)NameChangeMode.GetValue();

    public static readonly string[] suffixModes =
    {
        "SuffixMode.None",
        //"SuffixMode.Version",
        //"SuffixMode.Streaming",
        //"SuffixMode.Recording",
        //"SuffixMode.RoomHost",
        //"SuffixMode.OriginalName"
    };
    public static readonly string[] RoleAssigningAlgorithms =
    {
        "RoleAssigningAlgorithm.Default",
        "RoleAssigningAlgorithm.NetRandom",
        "RoleAssigningAlgorithm.HashRandom",
        "RoleAssigningAlgorithm.Xorshift",
        "RoleAssigningAlgorithm.MersenneTwister",
    };
    public static SuffixModes GetSuffixMode()
    {
        return (SuffixModes)SuffixMode.GetValue();
    }

    // シンクロカラーモード
    public static OptionItem SyncColorModeSelect;
    public static readonly string[] SelectSyncColorMode =
    {
        "None", "Clone", "fif_fif", "ThreeCornered", "Twin"
    };
    public static OptionItem SCM_NothingMeetingNameColor;
    public static OptionItem SCM_RestoredDeadPlayer;

    // モード読み取り
    public static bool IsSyncColorMode => GetSyncColorMode() != SyncColorMode.None;
    public static SyncColorMode GetSyncColorMode() => (SyncColorMode)SyncColorModeSelect.GetValue();

    // ======================
    public static bool IsLoaded = false;
    public static int GetRoleCount(CustomRoles role)
    {
        return GetRoleChance(role) == 0 ? 0 : CustomRoleCounts.TryGetValue(role, out var option) ? option.GetInt() : 0;
    }

    public static int GetRoleChance(CustomRoles role)
    {
        return CustomRoleSpawnChances.TryGetValue(role, out var option) ? option.GetInt() : 0;
    }
    public static void Load()
    {

        // ゲームモード
        GameMode = StringOptionItem.Create("GameMode", gameModes, 0, TabGroup.ModMainSettings, false)
            .SetColor(new Color32(204, 204, 0, 255))
            .SetHeader(true)
            .SetGameMode(CustomGameMode.All);

        OptionItem.CreatingFeatures = true;
        HideGameSettings = StringOptionItem.Create("HideGameSettings", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Color.gray);
        OptionItem.CreatingFeatures = false;

        #region 役職・詳細設定
        CustomRoleCounts = new();
        CustomRoleSpawnChances = new();

        var sortedRoleInfo = CustomRoleManager.AllRolesInfo.Values.OrderBy(role => role.ConfigId);
        // GM
        EnableGM = StringOptionItem.Create("EnableGM", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(new Color32(255, 91, 112, 255))
            .SetHeader(true, Translator.GetString("GM"))
            .SetGameMode(CustomGameMode.All);

        // SpecialEvent
        //if (Main.IsChristmas)
        //{
        //    Potentialist.SetupRoleOptions();
        //    Potentialist.RoleInfo.OptionCreator?.Invoke();
        //}
        if (Main.IsHalloween)
        {
            JackOLantern.SetupRoleOptions();
            JackOLantern.RoleInfo.OptionCreator?.Invoke();
        }

        // 常設 新役職
        sortedRoleInfo.Where(role => role.RoleName.IsNewRole()).Do(info =>
        {
            SetupRoleOptions(info);
            info.OptionCreator?.Invoke();
        });
        // 常設
        sortedRoleInfo.Where(role => !role.RoleName.IsDontShowOptionRole() && !role.RoleName.IsNewRole()).Do(info =>
        {
            SetupRoleOptions(info);
            info.OptionCreator?.Invoke();
        });

        TextOptionItem.Create("Head.CommonImpostor", TabGroup.ImpostorRoles);
        DefaultShapeshiftCooldown = FloatOptionItem.Create("DefaultShapeshiftCooldown", new(5f, 999f, 5f), 15f, TabGroup.ImpostorRoles, false)
            .SetValueFormat(OptionFormat.Seconds);
        ImpostorOperateVisibility = StringOptionItem.Create("ImpostorOperateVisibility", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ImpostorRoles, false);

        // Madmate
        CanMakeMadmateCount = IntegerOptionItem.Create("CanMakeMadmateCount", new(0, 15, 1), 0, TabGroup.MadmateRoles, false)
            .SetColor(Palette.ImpostorRed)
            .SetHeader(true, Translator.GetString("SKMadmateInfo"))
            .SetValueFormat(OptionFormat.Players);
        MadmateCanFixLightsOut = StringOptionItem.Create("MadmateCanFixLightsOut", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.MadmateRoles, false).SetParent(CanMakeMadmateCount).SetGameMode(CustomGameMode.Standard);
        MadmateCanFixComms = StringOptionItem.Create("MadmateCanFixComms", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.MadmateRoles, false).SetParent(CanMakeMadmateCount).SetGameMode(CustomGameMode.Standard);
        MadmateHasImpostorVision = StringOptionItem.Create("MadmateHasImpostorVision", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.MadmateRoles, false).SetParent(CanMakeMadmateCount).SetGameMode(CustomGameMode.Standard);
        MadmateCanSeeKillFlash = StringOptionItem.Create("MadmateCanSeeKillFlash", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.MadmateRoles, false).SetParent(CanMakeMadmateCount).SetGameMode(CustomGameMode.Standard);
        MadmateCanSeeOtherVotes = StringOptionItem.Create("MadmateCanSeeOtherVotes", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.MadmateRoles, false).SetParent(CanMakeMadmateCount).SetGameMode(CustomGameMode.Standard);
        MadmateCanSeeDeathReason = StringOptionItem.Create("MadmateCanSeeDeathReason", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.MadmateRoles, false).SetParent(CanMakeMadmateCount).SetGameMode(CustomGameMode.Standard);
        MadmateRevengeCrewmate = StringOptionItem.Create("MadmateExileCrewmate", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.MadmateRoles, false).SetParent(CanMakeMadmateCount).SetGameMode(CustomGameMode.Standard);

        TextOptionItem.Create("Head.CommonMadmate", TabGroup.MadmateRoles);
        MadmateVentCooldown = FloatOptionItem.Create("MadmateVentCooldown", new(0f, 180f, 5f), 0f, TabGroup.MadmateRoles, false)
            .SetValueFormat(OptionFormat.Seconds);
        MadmateVentMaxTime = FloatOptionItem.Create("MadmateVentMaxTime", new(0f, 180f, 5f), 0f, TabGroup.MadmateRoles, false)
            .SetValueFormat(OptionFormat.Seconds);

        // Add-Ons
        //TextOptionItem.Create("Head.NewAddOn", TabGroup.Addons).SetColor(Color.green);

        TextOptionItem.Create("Head.ImpostorAddOn", TabGroup.Addons).SetColor(Palette.ImpostorRed);
        AddOnList.SetupOptions(AddOnGroup.Impostor);

        TextOptionItem.Create("Head.CrewmateAddOn", TabGroup.Addons).SetColor(Palette.CrewmateBlue);
        AddOnList.SetupOptions(AddOnGroup.Crewmate);

        TextOptionItem.Create("Head.NeutralAddOn", TabGroup.Addons).SetColor(Palette.Orange);
        AddOnList.SetupOptions(AddOnGroup.Neutral);

        TextOptionItem.Create("Head.BuffAddOn", TabGroup.Addons).SetColor(Color.yellow);
        AddOnList.SetupOptions(AddOnGroup.Buff);

        TextOptionItem.Create("Head.DebuffAddOn", TabGroup.Addons).SetColor(Palette.Purple);
        AddOnList.SetupOptions(AddOnGroup.Debuff);
        #endregion

        TextOptionItem.Create("Head.RoleAssign", TabGroup.ModMainSettings).SetColor(Palette.LightBlue);
        // ここから hamo の共通設定までは機能の設定 (設定一覧の「機能」の欄に出す)
        OptionItem.CreatingFeatures = true;
        RoleAssigningAlgorithm = StringOptionItem.Create("RoleAssigningAlgorithm", RoleAssigningAlgorithms, 0, TabGroup.ModMainSettings, true)
            .RegisterUpdateValueEvent((object obj, OptionItem.UpdateValueEventArgs args) => IRandom.SetInstanceById(args.CurrentValue))
            .SetGameMode(CustomGameMode.All)
            .SetColor(Palette.LightBlue);
        RoleAssignManager.SetupOptionItem();

        // HideAndSeek
        HideAndSeekRolePool = StringOptionItem.Create("HideAndSeekRolePool", new string[] { "HideAndSeekRolePool.Own", "HideAndSeekRolePool.Standard" }, 0, TabGroup.ModMainSettings, false)
            .SetHeader(true)
            .SetColor(Color.yellow)
            .SetGameMode(CustomGameMode.HideAndSeek);
        SetupRoleOptions(TabGroup.ModMainSettings, CustomRoles.HASFox, customGameMode: CustomGameMode.HideAndSeek);
        SetupRoleOptions(TabGroup.ModMainSettings, CustomRoles.HASTroll, customGameMode: CustomGameMode.HideAndSeek);
        // 本編のかくれんぼ (/mode hns) でだけ配る
        SetupRoleOptions(TabGroup.ModMainSettings, CustomRoles.HASPortalMaker, customGameMode: CustomGameMode.HideAndSeek);

        AllowCloseDoors = StringOptionItem.Create("AllowCloseDoors", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetHeader(true)
            .SetGameMode(CustomGameMode.HideAndSeek);
        KillDelay = FloatOptionItem.Create("HideAndSeekWaitingTime", new(0f, 180f, 5f), 10f, TabGroup.ModMainSettings, false)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.HideAndSeek);
        IgnoreVent = StringOptionItem.Create("IgnoreVent", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.HideAndSeek);

        // CC: 今の試合開始の流れ (StandardIntroHelper) に載っておらず、客にイントロが届かず暗転するので選べないままにする
        //CatchCat.Option.SetupCustomOption();

        TextOptionItem.Create("Head.Map", TabGroup.ModMainSettings).SetColor(Palette.Orange).SetGameMode(CustomGameMode.All);
        /**************** SKELD ****************/
        MapOption_Skeld = StringOptionItem.Create("MapOption_Skeld", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.Orange)
            .SetGameMode(CustomGameMode.All);
        // ランダムスポーン
        RandomSpawn_Skeld = StringOptionItem.Create("RandomSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Skeld)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        AdditionalSpawn_Skeld = StringOptionItem.Create("AdditionalSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Skeld)
            .SetGameMode(CustomGameMode.All);
        DisableNearButton_Skeld = StringOptionItem.Create("DisableNearButton", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Skeld)
            .SetGameMode(CustomGameMode.All);
        FirstFixedSpawn_Skeld = StringOptionItem.Create("FirstFixedSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Skeld)
            .SetGameMode(CustomGameMode.All);
        // デバイス無効化
        DisableDevices_Skeld = StringOptionItem.Create("DisableDevices", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Skeld)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        DisableAdmin_Skeld = StringOptionItem.Create("DisableAdmin", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Skeld).SetGameMode(CustomGameMode.All);
        DisableCamera_Skeld = StringOptionItem.Create("DisableCamera", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Skeld).SetGameMode(CustomGameMode.All);
        // サボタージュの時間制御
        SabotageTimeControl_Skeld = StringOptionItem.Create("SabotageTimeControl", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Skeld)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        SkeldReactorTimeLimit = FloatOptionItem.Create("ReactorTimeLimit", new(1f, 60f, 1f), 30f, TabGroup.ModMainSettings, false).SetParent(SabotageTimeControl_Skeld)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);
        SkeldO2TimeLimit = FloatOptionItem.Create("O2TimeLimit", new(1f, 60f, 1f), 30f, TabGroup.ModMainSettings, false).SetParent(SabotageTimeControl_Skeld)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);

        /**************** MIRA HQ ****************/
        MapOption_MiraHQ = StringOptionItem.Create("MapOption_MiraHQ", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.Orange)
            .SetGameMode(CustomGameMode.All);
        // ランダムスポーン
        RandomSpawn_MiraHQ = StringOptionItem.Create("RandomSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_MiraHQ)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        AdditionalSpawn_MiraHQ = StringOptionItem.Create("AdditionalSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_MiraHQ)
            .SetGameMode(CustomGameMode.All);
        DisableNearButton_MiraHQ = StringOptionItem.Create("DisableNearButton", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_MiraHQ)
            .SetGameMode(CustomGameMode.All);
        FirstFixedSpawn_MiraHQ = StringOptionItem.Create("FirstFixedSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_MiraHQ)
            .SetGameMode(CustomGameMode.All);
        // デバイス無効化
        DisableDevices_MiraHQ = StringOptionItem.Create("DisableDevices", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_MiraHQ)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        DisableAdmin_MiraHQ = StringOptionItem.Create("DisableAdmin", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_MiraHQ).SetGameMode(CustomGameMode.All);
        DisableDoorLog_MiraHQ = StringOptionItem.Create("DisableMiraHQDoorLog", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_MiraHQ).SetGameMode(CustomGameMode.All);
        // サボタージュの時間制御
        SabotageTimeControl_MiraHQ = StringOptionItem.Create("SabotageTimeControl", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_MiraHQ)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        MiraHQReactorTimeLimit = FloatOptionItem.Create("ReactorTimeLimit", new(1f, 60f, 1f), 45f, TabGroup.ModMainSettings, false).SetParent(SabotageTimeControl_MiraHQ)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);
        MiraHQO2TimeLimit = FloatOptionItem.Create("O2TimeLimit", new(1f, 60f, 1f), 45f, TabGroup.ModMainSettings, false).SetParent(SabotageTimeControl_MiraHQ)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);

        /**************** POLUS ****************/
        MapOption_Polus = StringOptionItem.Create("MapOption_Polus", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.Orange)
            .SetGameMode(CustomGameMode.All);
        // ランダムスポーン
        RandomSpawn_Polus = StringOptionItem.Create("RandomSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Polus)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        AdditionalSpawn_Polus = StringOptionItem.Create("AdditionalSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Polus)
            .SetGameMode(CustomGameMode.All);
        DisableNearButton_Polus = StringOptionItem.Create("DisableNearButton", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Polus)
            .SetGameMode(CustomGameMode.All);
        FirstFixedSpawn_Polus = StringOptionItem.Create("FirstFixedSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Polus)
            .SetGameMode(CustomGameMode.All);
        // デバイス無効化
        DisableDevices_Polus = StringOptionItem.Create("DisableDevices", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Polus)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        DisableAdmin_Polus = StringOptionItem.Create("DisableAdmin", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Polus).SetGameMode(CustomGameMode.All);
        DisableCamera_Polus = StringOptionItem.Create("DisableCamera", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Polus).SetGameMode(CustomGameMode.All);
        DisableVital_Polus = StringOptionItem.Create("DisableVital", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Polus).SetGameMode(CustomGameMode.All);
        // ドアリセット
        ResetDoorsEveryTurns_Polus = StringOptionItem.Create("ResetDoorsEveryTurns", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Polus)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        DoorsResetMode_Polus = StringOptionItem.Create("DoorsResetMode", EnumHelper.GetAllNames<DoorsReset.ResetMode>(), 0, TabGroup.ModMainSettings, false).SetParent(ResetDoorsEveryTurns_Polus).SetGameMode(CustomGameMode.All);
        // リアクターの時間制御
        SabotageTimeControl_Polus = StringOptionItem.Create("SabotageTimeControl", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Polus)
            .SetColor(Color.magenta)
            .SetGameMode(CustomGameMode.All);
        PolusReactorTimeLimit = FloatOptionItem.Create("ReactorTimeLimit", new(1f, 60f, 1f), 30f, TabGroup.ModMainSettings, false).SetParent(SabotageTimeControl_Polus)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);

        /**************** AIRSHIP ****************/
        MapOption_Airship = StringOptionItem.Create("MapOption_Airship", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.Orange)
            .SetGameMode(CustomGameMode.All);
        // ランダムスポーン
        RandomSpawn_Airship = StringOptionItem.Create("RandomSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Airship)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        AdditionalSpawn_AirshipTAKADA = StringOptionItem.Create("AdditionalSpawn_AirshipTAKADA", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Airship)
            .SetGameMode(CustomGameMode.All);
        AdditionalSpawn_Airship = StringOptionItem.Create("AdditionalSpawn_Airship", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Airship)
            .SetGameMode(CustomGameMode.All);
        DisableNearButton_Airship = StringOptionItem.Create("DisableNearButton", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Airship)
            .SetGameMode(CustomGameMode.All);
        // デバイス無効化
        DisableDevices_Airship = StringOptionItem.Create("DisableDevices", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Airship)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        DisableCockpitAdmin_Airship = StringOptionItem.Create("DisableAirshipCockpitAdmin", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Airship).SetGameMode(CustomGameMode.All);
        DisableRecordsAdmin_Airship = StringOptionItem.Create("DisableAirshipRecordsAdmin", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Airship).SetGameMode(CustomGameMode.All);
        DisableCamera_Airship = StringOptionItem.Create("DisableCamera", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Airship).SetGameMode(CustomGameMode.All);
        DisableVital_Airship = StringOptionItem.Create("DisableVital", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Airship).SetGameMode(CustomGameMode.All);
        // ドアリセット
        ResetDoorsEveryTurns_Airship = StringOptionItem.Create("ResetDoorsEveryTurns", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Airship)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        DoorsResetMode_Airship = StringOptionItem.Create("DoorsResetMode", EnumHelper.GetAllNames<DoorsReset.ResetMode>(), 0, TabGroup.ModMainSettings, false).SetParent(ResetDoorsEveryTurns_Airship).SetGameMode(CustomGameMode.All);
        // エレキ構造の変化
        AirShipVariableElectrical = StringOptionItem.Create("AirShipVariableElectrical", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Airship)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        // 昇降機使用制限
        DisableAirshipMovingPlatform = StringOptionItem.Create("DisableAirshipMovingPlatform", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Airship)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        // リアクターの時間制御
        SabotageTimeControl_Airship = StringOptionItem.Create("SabotageTimeControl", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Airship)
            .SetColor(Color.magenta)
            .SetGameMode(CustomGameMode.All);
        AirshipReactorTimeLimit = FloatOptionItem.Create("ReactorTimeLimit", new(1f, 90f, 1f), 60f, TabGroup.ModMainSettings, false).SetParent(SabotageTimeControl_Airship)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);
        // 停電解除場所の制限
        LightsOutSpecialSettings_Airship = StringOptionItem.Create("LightsOutSpecialSettings_Airship", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Airship)
            .SetColor(Color.magenta).SetGameMode(CustomGameMode.All);
        DisableAirshipViewingDeckLightsPanel = StringOptionItem.Create("DisableAirshipViewingDeckLightsPanel", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(LightsOutSpecialSettings_Airship).SetGameMode(CustomGameMode.All);
        DisableAirshipGapRoomLightsPanel = StringOptionItem.Create("DisableAirshipGapRoomLightsPanel", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(LightsOutSpecialSettings_Airship).SetGameMode(CustomGameMode.All);
        DisableAirshipCargoLightsPanel = StringOptionItem.Create("DisableAirshipCargoLightsPanel", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(LightsOutSpecialSettings_Airship).SetGameMode(CustomGameMode.All);

        /**************** FUNGLE ****************/
        MapOption_Fungle = StringOptionItem.Create("MapOption_Fungle", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.Orange)
            .SetGameMode(CustomGameMode.All);
        // ランダムスポーン
        RandomSpawn_Fungle = StringOptionItem.Create("RandomSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Fungle)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        AdditionalSpawn_Fungle = StringOptionItem.Create("AdditionalSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Fungle)
            .SetGameMode(CustomGameMode.All);
        DisableNearButton_Fungle = StringOptionItem.Create("DisableNearButton", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Fungle)
            .SetGameMode(CustomGameMode.All);
        FirstFixedSpawn_Fungle = StringOptionItem.Create("FirstFixedSpawn", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomSpawn_Fungle)
            .SetGameMode(CustomGameMode.All);
        // デバイス無効化
        DisableDevices_Fungle = StringOptionItem.Create("DisableDevices", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Fungle)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        DisableVital_Fungle = StringOptionItem.Create("DisableVital", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevices_Fungle).SetGameMode(CustomGameMode.All);
        // ドアリセット
        ResetDoorsEveryTurns_Fungle = StringOptionItem.Create("ResetDoorsEveryTurns", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Fungle)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        DoorsResetMode_Fungle = StringOptionItem.Create("DoorsResetMode", EnumHelper.GetAllNames<DoorsReset.ResetMode>(), 0, TabGroup.ModMainSettings, false).SetParent(ResetDoorsEveryTurns_Fungle).SetGameMode(CustomGameMode.All);
        // ジップラインの方向制限
        FungleCanUseZipline = StringOptionItem.Create("FungleCanUseZipline", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Fungle)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        FungleCanUseZiplineFromTop = StringOptionItem.Create("FungleCanUseZiplineFromTop", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(FungleCanUseZipline).SetGameMode(CustomGameMode.All);
        FungleCanUseZiplineFromUnder = StringOptionItem.Create("FungleCanUseZiplineFromUnder", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(FungleCanUseZipline).SetGameMode(CustomGameMode.All);
        // キノコ胞子モヤの無効
        FungleCanSporeTrigger = StringOptionItem.Create("FungleCanSporeTrigger", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Fungle)
            .SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        // リアクターの時間制御
        SabotageTimeControl_Fungle = StringOptionItem.Create("SabotageTimeControl", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Fungle)
            .SetColor(Color.magenta)
            .SetGameMode(CustomGameMode.All);
        FungleReactorTimeLimit = FloatOptionItem.Create("ReactorTimeLimit", new(1f, 60f, 1f), 50f, TabGroup.ModMainSettings, false).SetParent(SabotageTimeControl_Fungle)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);
        FungleMushroomMixupDuration = FloatOptionItem.Create("FungleMushroomMixupDuration", new(1f, 20f, 1f), 10f, TabGroup.ModMainSettings, false).SetParent(SabotageTimeControl_Fungle)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);
        // キノコカオスサボ時のボタン無効
        DisableButtonInMushroomMixup = StringOptionItem.Create("DisableButtonInMushroomMixup", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(MapOption_Fungle)
            .SetColor(Color.magenta).SetGameMode(CustomGameMode.All);

        // マップ設定：マップ共通
        TextOptionItem.Create("Head.MapCommon", TabGroup.ModMainSettings).SetColor(Color.yellow).SetGameMode(CustomGameMode.All);
        // デバイス無効 マップ共通除外条件
        DisableDevicesIgnoreConditions = StringOptionItem.Create("IgnoreConditions", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.yellow);
        DisableDevicesIgnoreImpostors = StringOptionItem.Create("IgnoreImpostors", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevicesIgnoreConditions);
        DisableDevicesIgnoreMadmates = StringOptionItem.Create("IgnoreMadmates", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevicesIgnoreConditions);
        DisableDevicesIgnoreNeutrals = StringOptionItem.Create("IgnoreNeutrals", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevicesIgnoreConditions);
        DisableDevicesIgnoreCrewmates = StringOptionItem.Create("IgnoreCrewmates", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevicesIgnoreConditions);
        DisableDevicesIgnoreAfterAnyoneDied = StringOptionItem.Create("IgnoreAfterAnyoneDied", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableDevicesIgnoreConditions);
        // ランダムマップ
        RandomMapsMode = StringOptionItem.Create("RandomMapsMode", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.yellow)
            .SetGameMode(CustomGameMode.All);
        AddedTheSkeld = StringOptionItem.Create("AddedTheSkeld", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomMapsMode).SetGameMode(CustomGameMode.All);
        AddedMiraHQ = StringOptionItem.Create("AddedMIRAHQ", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomMapsMode).SetGameMode(CustomGameMode.All);
        AddedPolus = StringOptionItem.Create("AddedPolus", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomMapsMode).SetGameMode(CustomGameMode.All);
        AddedTheAirship = StringOptionItem.Create("AddedTheAirship", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomMapsMode).SetGameMode(CustomGameMode.All);
        AddedTheFungle = StringOptionItem.Create("AddedTheFungle", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(RandomMapsMode).SetGameMode(CustomGameMode.All);

        // サボタージュ：マップ共通
        TextOptionItem.Create("Head.Sabotage", TabGroup.ModMainSettings).SetColor(Color.magenta).SetGameMode(CustomGameMode.All);
        // サボタージュのクールダウン変更
        ModifySabotageCooldown = StringOptionItem.Create("ModifySabotageCooldown", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.magenta).SetGameMode(CustomGameMode.All);
        SabotageCooldown = FloatOptionItem.Create("SabotageCooldown", new(1f, 60f, 1f), 30f, TabGroup.ModMainSettings, false).SetParent(ModifySabotageCooldown)
            .SetValueFormat(OptionFormat.Seconds).SetGameMode(CustomGameMode.All);
        // 停電の配電盤妨害を無効化
        BlockDisturbancesToSwitches = StringOptionItem.Create("BlockDisturbancesToSwitches", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.magenta).SetGameMode(CustomGameMode.All);
        // コミュサボカモフラージュ
        CommsCamouflage = StringOptionItem.Create("CommsCamouflage", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.magenta)
            .SetGameMode(CustomGameMode.All);
        // サボタージュを種類ごとに無効化 (ホストが受け付けない)
        DisableSabotage = StringOptionItem.Create("DisableSabotage", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.magenta).SetGameMode(CustomGameMode.All);
        DisableSabotageReactor = StringOptionItem.Create("DisableSabotage.Reactor", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableSabotage).SetGameMode(CustomGameMode.All);
        DisableSabotageO2 = StringOptionItem.Create("DisableSabotage.O2", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableSabotage).SetGameMode(CustomGameMode.All);
        DisableSabotageLights = StringOptionItem.Create("DisableSabotage.Lights", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableSabotage).SetGameMode(CustomGameMode.All);
        DisableSabotageComms = StringOptionItem.Create("DisableSabotage.Comms", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableSabotage).SetGameMode(CustomGameMode.All);
        DisableSabotageMushroom = StringOptionItem.Create("DisableSabotage.Mushroom", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableSabotage).SetGameMode(CustomGameMode.All);
        DisableSabotageDoors = StringOptionItem.Create("DisableSabotage.Doors", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(DisableSabotage).SetGameMode(CustomGameMode.All);
        // 死んだインポスターのサボタージュを受け付けない
        DeadImpostorCannotSabotage = StringOptionItem.Create("DeadImpostorCannotSabotage", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.magenta).SetGameMode(CustomGameMode.All);

        TextOptionItem.Create("Head.Meeting", TabGroup.ModMainSettings).SetColor(Color.cyan).SetGameMode(CustomGameMode.All);
        // 会議収集理由表示
        ShowReportReason = StringOptionItem.Create("ShowReportReason", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.All);

        // 初手会議に役職名表示
        ShowRoleInfoAtFirstMeeting = StringOptionItem.Create("ShowRoleInfoAtFirstMeeting", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Color.cyan);

        // ボタン回数同期
        SyncButtonMode = StringOptionItem.Create("SyncButtonMode", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan);
        SyncedButtonCount = IntegerOptionItem.Create("SyncedButtonCount", new(0, 100, 1), 10, TabGroup.ModMainSettings, false).SetParent(SyncButtonMode)
            .SetValueFormat(OptionFormat.Times);

        // 投票モード
        VoteMode = StringOptionItem.Create("VoteMode", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.All);
        WhenSkipVote = StringOptionItem.Create("WhenSkipVote", voteModes[0..3], 0, TabGroup.ModMainSettings, false).SetParent(VoteMode);
        WhenSkipVoteIgnoreFirstMeeting = StringOptionItem.Create("WhenSkipVoteIgnoreFirstMeeting", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(WhenSkipVote);
        WhenSkipVoteIgnoreNoDeadBody = StringOptionItem.Create("WhenSkipVoteIgnoreNoDeadBody", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(WhenSkipVote);
        WhenSkipVoteIgnoreEmergency = StringOptionItem.Create("WhenSkipVoteIgnoreEmergency", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(WhenSkipVote);
        WhenNonVote = StringOptionItem.Create("WhenNonVote", voteModes, 0, TabGroup.ModMainSettings, false).SetParent(VoteMode)
            .SetGameMode(CustomGameMode.All);
        WhenTie = StringOptionItem.Create("WhenTie", tieModes, 0, TabGroup.ModMainSettings, false).SetParent(VoteMode);

        // 全員生存時の会議時間
        AllAliveMeeting = StringOptionItem.Create("AllAliveMeeting", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan);
        AllAliveMeetingTime = FloatOptionItem.Create("AllAliveMeetingTime", new(1f, 300f, 1f), 10f, TabGroup.ModMainSettings, false).SetParent(AllAliveMeeting)
            .SetValueFormat(OptionFormat.Seconds);

        // 生存人数ごとの緊急会議
        AdditionalEmergencyCooldown = StringOptionItem.Create("AdditionalEmergencyCooldown", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan);
        AdditionalEmergencyCooldownThreshold = IntegerOptionItem.Create("AdditionalEmergencyCooldownThreshold", new(1, 15, 1), 1, TabGroup.ModMainSettings, false).SetParent(AdditionalEmergencyCooldown)
            .SetValueFormat(OptionFormat.Players);
        AdditionalEmergencyCooldownTime = FloatOptionItem.Create("AdditionalEmergencyCooldownTime", new(1f, 60f, 1f), 1f, TabGroup.ModMainSettings, false).SetParent(AdditionalEmergencyCooldown)
            .SetValueFormat(OptionFormat.Seconds);

        TextOptionItem.Create("Head.Revenge", TabGroup.ModMainSettings).SetColor(Palette.Orange).SetGameMode(CustomGameMode.Standard);
        // 道連れ人表記
        ShowRevengeTarget = StringOptionItem.Create("ShowRevengeTarget", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Color.cyan);
        RevengeImpostorByImpostor = StringOptionItem.Create("RevengeImpostorByImpostor", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.ImpostorRed);
        RevengeMadByImpostor = StringOptionItem.Create("RevengeMadByImpostor", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.ImpostorRed);
        RevengeNeutral = StringOptionItem.Create("RevengeNeutral", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.Orange);

        TextOptionItem.Create("Head.Task", TabGroup.ModMainSettings).SetColor(Color.green).SetGameMode(CustomGameMode.All);
        // タスク無効化
        DisableTasks = StringOptionItem.Create("DisableTasks", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.green)
            .SetGameMode(CustomGameMode.All);
        TaskDisableOptions.Create(DisableTasks);

        // タスク勝利無効化
        DisableTaskWin = StringOptionItem.Create("DisableTaskWin", new string[] { "ColoredOff", "ColoredOn", "DisableTaskWin.WhenCrewDead" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.green);
        //ホストの死後タスク免除
        HostGhostIgnoreTasks = StringOptionItem.Create("HostGhostIgnoreTasks", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Color.green);
        //タスク免除
        GhostIgnoreTasks = StringOptionItem.Create("GhostIgnoreTasks", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Color.green);

        TextOptionItem.Create("Head.Ghost", TabGroup.ModMainSettings).SetColor(Palette.LightBlue).SetGameMode(CustomGameMode.All);
        // 幽霊
        GhostCantSeeOtherRoles = StringOptionItem.Create("GhostCantSeeOtherRoles", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.LightBlue)
            .SetGameMode(CustomGameMode.All);
        GhostCantSeeOtherTasks = StringOptionItem.Create("GhostCantSeeOtherTasks", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.LightBlue)
            .SetGameMode(CustomGameMode.All);
        GhostCantSeeOtherVotes = StringOptionItem.Create("GhostCantSeeOtherVotes", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.LightBlue)
            .SetGameMode(CustomGameMode.All);
        GhostCanSeeOtherTeams = StringOptionItem.Create("GhostCanSeeOtherTeams", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.LightBlue)
            .SetGameMode(CustomGameMode.All);
        GhostCanSeeDeathReason = StringOptionItem.Create("GhostCanSeeDeathReason", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.LightBlue)
            .SetGameMode(CustomGameMode.All);

        TextOptionItem.Create("Head.Other", TabGroup.ModMainSettings).SetColor(Palette.CrewmateBlue).SetGameMode(CustomGameMode.All);
        KillFlashDuration = FloatOptionItem.Create("KillFlashDuration", new(0.1f, 0.45f, 0.05f), 0.3f, TabGroup.ModMainSettings, true)
            .SetColor(Palette.ImpostorRed)
            .SetValueFormat(OptionFormat.Seconds);
        // CO可否表示(id+499まで使用)
        DisplayComingOut.SetupCustomOption();
        // 陣営マーク表示
        DisplayTeamMark = StringOptionItem.Create("DisplayTeamMark", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetColor(Palette.CrewmateBlue);
        // 初手キルクール調整
        FixFirstKillCooldown = StringOptionItem.Create("FixFirstKillCooldown", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.CrewmateBlue);

        // 転落死
        LadderDeath = StringOptionItem.Create("LadderDeath", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Palette.CrewmateBlue)
            .SetGameMode(CustomGameMode.All);
        LadderDeathChance = StringOptionItem.Create("LadderDeathChance", rates[1..], 0, TabGroup.ModMainSettings, false).SetParent(LadderDeath).SetGameMode(CustomGameMode.All);

        // スキン設定
        //SkinControle = StringOptionItem.Create("SkinControle", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
        //    .SetColor(Palette.CrewmateBlue)
        //    .SetGameMode(CustomGameMode.All);
        //NoHat = StringOptionItem.Create("NoHat", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(SkinControle).SetGameMode(CustomGameMode.All);
        //NoFullFaceHat = StringOptionItem.Create("NoFullFaceHat", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(SkinControle).SetGameMode(CustomGameMode.All);
        //NoSkin = StringOptionItem.Create("NoSkin", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(SkinControle).SetGameMode(CustomGameMode.All);
        //NoVisor = StringOptionItem.Create("NoVisor", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(SkinControle).SetGameMode(CustomGameMode.All);
        //NoPet = StringOptionItem.Create("NoPet", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(SkinControle).SetGameMode(CustomGameMode.All);
        //NoDuplicateHat = StringOptionItem.Create("NoDuplicateHat", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(SkinControle).SetGameMode(CustomGameMode.All);
        //NoDuplicateSkin = StringOptionItem.Create("NoDuplicateSkin", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(SkinControle).SetGameMode(CustomGameMode.All);
        VoiceReader.SetupCustomOption();
        // 生存者の切断で自動廃村
        Modules.AutoAbort.SetupCustomOption();
        // 耳打ち・荒らしの書き込みのキック
        Modules.Whisper.SetupCustomOption();
        Modules.ChatGuard.SetupCustomOption();

        TextOptionItem.Create("Head.GameMode", TabGroup.ModMainSettings).SetColor(Color.yellow).SetGameMode(CustomGameMode.Standard);
        // シンクロカラーモード
        SyncColorModeSelect = StringOptionItem.Create("SyncColorMode", SelectSyncColorMode, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.yellow)
            .SetGameMode(CustomGameMode.Standard);
        SCM_NothingMeetingNameColor = StringOptionItem.Create("SCM_NothingMeetingNameColor", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(SyncColorModeSelect)
            .SetGameMode(CustomGameMode.Standard);
        SCM_RestoredDeadPlayer = StringOptionItem.Create("SCM_RestoredDeadPlayer", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false).SetParent(SyncColorModeSelect)
            .SetGameMode(CustomGameMode.Standard);


        // その他
        TextOptionItem.Create("Head.System", TabGroup.ModMainSettings).SetColor(Color.blue).SetGameMode(CustomGameMode.All);
        NoGameEnd = StringOptionItem.Create("NoGameEnd", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetGameMode(CustomGameMode.All);
        // 終わり方ごとに止める。初期値は全部 ON (親だけ ON にすれば、どの終わり方でも終わらない)
        NoGameEndByCount = StringOptionItem.Create("NoGameEnd.ByCount", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false).SetParent(NoGameEnd)
            .SetGameMode(CustomGameMode.All);
        NoGameEndByTask = StringOptionItem.Create("NoGameEnd.ByTask", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false).SetParent(NoGameEnd)
            .SetGameMode(CustomGameMode.All);
        NoGameEndBySabotage = StringOptionItem.Create("NoGameEnd.BySabotage", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false).SetParent(NoGameEnd)
            .SetGameMode(CustomGameMode.All);
        NoGameEndByRole = StringOptionItem.Create("NoGameEnd.ByRole", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false).SetParent(NoGameEnd)
            .SetGameMode(CustomGameMode.All);
        AutoDisplayLastResult = StringOptionItem.Create("AutoDisplayLastResult", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        AutoDisplayKillLog = StringOptionItem.Create("AutoDisplayKillLog", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        SuffixMode = StringOptionItem.Create("SuffixMode", suffixModes, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        NameChangeMode = StringOptionItem.Create("NameChangeMode", nameChangeModes, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        ChangeNameToRoleInfo = StringOptionItem.Create("ChangeNameToRoleInfo", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        AddonShow = StringOptionItem.Create("AddonShowMode", addonShowModes, 0, TabGroup.ModMainSettings, true);
        ChangeIntro = StringOptionItem.Create("ChangeIntro", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true);

        TextOptionItem.Create("Head.Participation", TabGroup.ModMainSettings).SetColor(Palette.Purple).SetGameMode(CustomGameMode.All);
        ApplyDenyNameList = StringOptionItem.Create("ApplyDenyNameList", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        KickPlayerFriendCodeNotExist = StringOptionItem.Create("KickPlayerFriendCodeNotExist", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        ApplyBanList = StringOptionItem.Create("ApplyBanList", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        AntiCheat = StringOptionItem.Create("AntiCheat", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true)
            .SetGameMode(CustomGameMode.All);
        CheaterAutoBan = StringOptionItem.Create("CheaterAutoBan", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(AntiCheat)
            .SetGameMode(CustomGameMode.All);
        CheatLobbyKill = StringOptionItem.Create("CheatLobbyKill", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, true).SetParent(AntiCheat)
            .SetGameMode(CustomGameMode.All);

        // hamo から移植した役職の共通設定
        LoadHamoOptions();
        OptionItem.CreatingFeatures = false;

        DebugModeManager.SetupCustomOption();

        OptionSaver.Load();

        IsLoaded = true;
    }

    public static bool NotShowOption(string optionName)
    {
        return optionName is "KillFlashDuration"
                        or "AssignMode"
                        or "SuffixMode"
                        or "HideGameSettings"
                        or "AutoDisplayLastResult"
                        or "AutoDisplayKillLog"
                        or "RoleAssigningAlgorithm"
                        or "ShowReportReason"
                        or "ShowRoleInfoAtFirstMeeting"
                        or "HostGhostIgnoreTasks"
                        or "RevengeNeutral"
                        or "ChangeNameToRoleInfo"
                        or "ApplyDenyNameList"
                        or "KickPlayerFriendCodeNotExist"
                        or "ApplyBanList"
                        or "AntiCheat"
                        or "CheaterAutoBan"
                        or "CheatLobbyKill"
                        or "ChangeIntro"
                        or "AddonShowMode";
    }
    public static void SetupRoleOptions(SimpleRoleInfo info) =>
    SetupRoleOptions(info.Tab, info.RoleName, info.AssignInfo.AssignCountRule);
    public static void SetupRoleOptions(TabGroup tab, CustomRoles role, IntegerValueRule assignCountRule = null, CustomGameMode customGameMode = CustomGameMode.Standard)
    {
        if (role.IsVanilla()) return;
        assignCountRule ??= new(1, 15, 1);

        string roleInfo = "";
        if (role.IsNormalVanillaRole())
        {
            roleInfo = Translator.GetString(Enum.GetName(typeof(CustomRoles), role.VanillaRoleConversion()) + "Blurb");
        }
        else
        {
            roleInfo = Translator.GetString(role.ToString() + "Info");
        }

        var spawnOption = IntegerOptionItem.Create(role.ToString(), new(0, 100, 10), 0, tab, false)
            .SetColor(Utils.GetRoleColor(role))
            .SetValueFormat(OptionFormat.Percent)
            .SetHeader(true, roleInfo)
            .SetGameMode(customGameMode) as IntegerOptionItem;
        var countOption = IntegerOptionItem.Create("Maximum", assignCountRule, assignCountRule.Step, tab, false)
            .SetParent(spawnOption)
            .SetValueFormat(role.IsPairRole() ? OptionFormat.Pair : OptionFormat.Players)
            .SetFixValue(role.IsFixedCountRole())
            .SetGameMode(customGameMode);

        CustomRoleSpawnChances.Add(role, spawnOption);
        CustomRoleCounts.Add(role, countOption);
    }

    //AddOn
    public static void SetUpAddOnOptions(CustomRoles PlayerRole, TabGroup tab, CustomRoles parentRole = CustomRoles.NotAssigned, bool addRoleName = false)
    {
        if (parentRole == CustomRoles.NotAssigned) parentRole = PlayerRole;

        if (!addRoleName)
        {
            AddOnBuffAssign[PlayerRole] = StringOptionItem.Create("AddOnBuffAssign", new string[] { "ColoredOff", "ColoredOn" }, 0, tab, false).SetParent(CustomRoleSpawnChances[parentRole]);
        }
        else
        {
            var roleName = Utils.GetRoleName(PlayerRole);
            Dictionary<string, string> replacementDic = new() { { "%role%", Utils.ColorString(Utils.GetRoleColor(PlayerRole), roleName) } };
            AddOnBuffAssign[PlayerRole] = StringOptionItem.Create("AddOnBuffAssign%role%", new string[] { "ColoredOff", "ColoredOn" }, 0, tab, false).SetParent(CustomRoleSpawnChances[parentRole]);
            AddOnBuffAssign[PlayerRole].ReplacementDictionary = replacementDic;
        }
        foreach (var Addon in Enum.GetValues(typeof(CustomRoles)).Cast<CustomRoles>().Where(x => x.IsBuffAddOn()))
        {
            if (Addon == CustomRoles.Loyalty && PlayerRole.IsDontSelectLoyaltyRole()) continue;
            if (Addon == CustomRoles.Revenger && PlayerRole is CustomRoles.MadNimrod) continue;

            SetUpAddOnRoleOption(PlayerRole, tab, Addon, false, AddOnBuffAssign[PlayerRole]);
        }

        if (!addRoleName)
        {
            AddOnDebuffAssign[PlayerRole] = StringOptionItem.Create("AddOnDebuffAssign", new string[] { "ColoredOff", "ColoredOn" }, 0, tab, false).SetParent(CustomRoleSpawnChances[parentRole]);
        }
        else
        {
            var roleName = Utils.GetRoleName(PlayerRole);
            Dictionary<string, string> replacementDic = new() { { "%role%", Utils.ColorString(Utils.GetRoleColor(PlayerRole), roleName) } };
            AddOnDebuffAssign[PlayerRole] = StringOptionItem.Create("AddOnDebuffAssign%role%", new string[] { "ColoredOff", "ColoredOn" }, 0, tab, false).SetParent(CustomRoleSpawnChances[parentRole]);
            AddOnDebuffAssign[PlayerRole].ReplacementDictionary = replacementDic;
        }
        foreach (var Addon in Enum.GetValues(typeof(CustomRoles)).Cast<CustomRoles>().Where(x => x.IsDebuffAddOn()))
        {
            SetUpAddOnRoleOption(PlayerRole, tab, Addon, false, AddOnDebuffAssign[PlayerRole]);
        }
    }
    public static void SetUpAddOnRoleOption(CustomRoles PlayerRole, TabGroup tab, CustomRoles role, bool defaultValue = false, OptionItem parent = null)
    {
        // 親オプションが指定されていなければデフォルトの親を使用
        if (parent == null && CustomRoleSpawnChances.ContainsKey(PlayerRole))
        {
            parent = CustomRoleSpawnChances[PlayerRole];
        }

        string roleNameKey = Translator.GetString(role.ToString());

        string colorAddonName = Utils.ColorString(Utils.GetRoleColor(role), Utils.GetRoleName(role));
        string addonAbilityInfo = Utils.GetAddonAbilityInfo(role);

        Dictionary<string, string> replacementDic = new()
        {
            { roleNameKey,  $"{colorAddonName} {addonAbilityInfo}" }
        };

        var key = (PlayerRole, role);

        if (!AddOnRoleOptions.ContainsKey(key))
        {
            var newOption = StringOptionItem.Create(role.ToString(), new[] { "ColoredOff", "ColoredOn" }, defaultValue ? 1 : 0, tab, true);
            if (parent != null)
            {
                newOption.SetParent(parent);
            }

            AddOnRoleOptions[key] = newOption;
        }

        AddOnRoleOptions[key].ReplacementDictionary = replacementDic;
    }

    public class OverrideTasksData
    {
        public static Dictionary<CustomRoles, OverrideTasksData> AllData = new();
        public CustomRoles Role { get; private set; }
        public OptionItem doOverride;
        public OptionItem assignCommonTasks;
        public OptionItem numLongTasks;
        public OptionItem numShortTasks;

        public OverrideTasksData(TabGroup tab, CustomRoles role, OptionItem option = null, bool addRoleName = false, (bool defo, int common, int Long, int Short)? tasks = null)
        {
            this.Role = role;

            if (option == null) option = CustomRoleSpawnChances[role];
            if (!addRoleName)
            {
                doOverride = StringOptionItem.Create("doOverride", new string[] { "ColoredOff", "ColoredOn" }, tasks?.defo == true ? 1 : 0, tab, false).SetParent(option)
                    .SetValueFormat(OptionFormat.None);
            }
            else
            {
                var roleName = Utils.GetRoleName(role);
                Dictionary<string, string> replacementDic = new() { { "%role%", Utils.ColorString(Utils.GetRoleColor(role), roleName) } };
                doOverride = StringOptionItem.Create("doOverride%role%", new string[] { "ColoredOff", "ColoredOn" }, tasks?.defo == true ? 1 : 0, tab, false).SetParent(option);
                doOverride.ReplacementDictionary = replacementDic;
            }

            assignCommonTasks = IntegerOptionItem.Create("assignCommonTasks", new(0, 99, 1), tasks?.common ?? 0, tab, false).SetParent(doOverride)
                .SetValueFormat(OptionFormat.Pieces);
            numLongTasks = IntegerOptionItem.Create("roleLongTasksNum", new(0, 99, 1), tasks?.Long ?? 3, tab, false).SetParent(doOverride)
                .SetValueFormat(OptionFormat.Pieces);
            numShortTasks = IntegerOptionItem.Create("roleShortTasksNum", new(0, 99, 1), tasks?.Short ?? 3, tab, false).SetParent(doOverride)
                .SetValueFormat(OptionFormat.Pieces);

            if (!AllData.ContainsKey(role)) AllData.Add(role, this);
            else Logger.Warn("重複したCustomRolesを対象とするOverrideTasksDataが作成されました", "OverrideTasksData");
        }
        public static OverrideTasksData Create(TabGroup tab, CustomRoles role)
        {
            return new OverrideTasksData(tab, role);
        }
        public static OverrideTasksData Create(SimpleRoleInfo roleInfo, OptionItem option = null, CustomRoles setRole = CustomRoles.NotAssigned)
        {
            bool addRoleName = false;
            if (setRole == CustomRoles.NotAssigned) setRole = roleInfo.RoleName;
            else addRoleName = true;

            return new OverrideTasksData(roleInfo.Tab, setRole, option, addRoleName);
        }
        /// <summary>hamo 形式 (rolename: 別名の役職, tasks: 初期値)</summary>
        public static OverrideTasksData Create(SimpleRoleInfo roleInfo, CustomRoles rolename, (bool defo, int common, int Long, int Short)? tasks = null, OptionItem parent = null)
        {
            var setRole = rolename == CustomRoles.NotAssigned ? roleInfo.RoleName : rolename;
            return new OverrideTasksData(roleInfo.Tab, setRole, parent ?? CustomRoleSpawnChances[roleInfo.RoleName], setRole != roleInfo.RoleName, tasks);
        }
        public OptionItem numCommonTasks => assignCommonTasks;
    }

    /// <summary>役職の帯 (SimpleRoleInfo の configId)。設定画面と設定一覧での役職の並び順にだけ使う</summary>
    public enum offsetId
    {
        // Unit
        UnitSpecial = 1000,
        UnitImp = 2000,
        UnitCrew = 4000,
        UnitMix = 6000,

        // Impostor
        ImpSpecial = 10000,
        ImpDefault = 11000,
        ImpTOH = 12000,
        ImpY = 20000,

        // Madmate
        MadTOH = 28500,
        MadY = 29000,

        // Crewmate
        CrewSpecial = 30000,
        CrewDefault = 31000,
        CrewSheriff = 32000,
        CrewTOH = 33000,
        CrewY = 40000,

        // Neutral
        NeuJackal = 51000,
        NeuFox = 51800,
        NeuTOH = 52000,
        NeuY = 60000,
    }
}