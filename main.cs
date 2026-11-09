using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using AmongUs.GameOptions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using UnityEngine;

[assembly: AssemblyFileVersionAttribute(TownOfHostL.Main.PluginVersion)]
[assembly: AssemblyInformationalVersionAttribute(TownOfHostL.Main.PluginVersion)]
namespace TownOfHostL;

[BepInPlugin(PluginGuid, "Town Of Host_L", PluginVersion)]
[BepInIncompatibility("com.yumenopai.townofhosty")]
[BepInIncompatibility("jp.ykundesu.supernewroles")]
[BepInIncompatibility("com.emptybottle.townofhost")]
[BepInIncompatibility("com.AsumuAkaguma.townofhostfore")]
[BepInIncompatibility("com.kymario.townofhost-k")]
[BepInProcess("Among Us.exe")]
public partial class Main : BasePlugin
{
    // == プログラム設定 / Program Config ==
    // modの名前 / Mod Name (Default: Town Of Host)
    public static readonly string ModName = "Town Of Host_L";
    // modの色 / Mod Color (Default: #00bfff)
    public static readonly string ModColor = "#ffff00";
    // 公開ルームを許可する / Allow Public Room (Default: true)
    public static readonly bool AllowPublicRoom = true;
    // フォークID / ForkId (Default: OriginalTOH)
    public static readonly string ForkId = "TOH_L";
    // Discordボタンを表示するか / Show Discord Button (Default: true)
    public static readonly bool ShowDiscordButton = true;
    // Discordサーバーの招待リンク / Discord Server Invite URL (Default: https://discord.gg/W5ug6hXB9V)
    public static readonly string DiscordInviteUrl = "https://discord.gg/Bv8XRBvCXW";
    // ==========
    public const string OriginalForkId = "OriginalTOH"; // Don't Change The Value. / この値を変更しないでください。
    // == 認証設定 / Authentication Config ==
    // デバッグキーの認証インスタンス
    public static HashAuth DebugKeyAuth { get; private set; }
    // デバッグキーのハッシュ値
    public const string DebugKeyHash = "c0fd562955ba56af3ae20d7ec9e64c664f0facecef4b3e366e109306adeae29d";
    // デバッグキーのソルト
    public const string DebugKeySalt = "59687b";
    // デバッグキーのコンフィグ入力
    public static ConfigEntry<string> DebugKeyInput { get; private set; }

    // ==========
    //Sorry for many Japanese comments.
    public const string PluginGuid = "6bc9fc19-1629-412a-989d-acf4e12140ff";
    public const string PluginVersion = "2026.11.2";
    // サポートされている最低のAmongUsバージョン
    public static readonly string LowestSupportedVersion = "2026.9.29";
    // このバージョンのみで公開ルームを無効にする場合
    public static readonly bool IsPublicAvailableOnThisVersion = true;
    // プレリリースかどうか
    public static bool IsPrerelease { get; } = true;
    public const string PluginSubVersion = "β";
    public const string PluginVersionName = "";

    public Harmony Harmony { get; } = new Harmony(PluginGuid);
    public static Version version = Version.Parse(PluginVersion);
    public static BepInEx.Logging.ManualLogSource Logger;
    public static bool hasArgumentException = false;
    public static string ExceptionMessage;
    public static bool ExceptionMessageIsShown = false;
    public static string credentialsText;    
    public static NormalGameOptionsV12 NormalOptions => GameOptionsManager.Instance.currentNormalGameOptions;
    public static HideNSeekGameOptionsV12 HideNSeekOptions => GameOptionsManager.Instance.currentHideNSeekGameOptions;

    //Client Options
    public static ConfigEntry<string> HideName { get; private set; }
    public static ConfigEntry<string> HideColor { get; private set; }
    public static ConfigEntry<bool> ForceJapanese { get; private set; }
    public static ConfigEntry<bool> JapaneseRoleName { get; private set; }
    public static ConfigEntry<bool> IsMuteLobbyBGM { get; private set; }
    public static ConfigEntry<bool> SleepAccountMenu { get; private set; }
    public static ConfigEntry<bool> OnlyTohlLobbies { get; private set; }
    public static ConfigEntry<float> MessageWait { get; private set; }
    public static ConfigEntry<bool> KillFlashAfterDeadByHost { get; private set; }

    public static Dictionary<byte, PlayerVersion> playerVersion = new();
    //Preset Name Options
    public static ConfigEntry<string> Preset1 { get; private set; }
    public static ConfigEntry<string> Preset2 { get; private set; }
    public static ConfigEntry<string> Preset3 { get; private set; }
    public static ConfigEntry<string> Preset4 { get; private set; }
    public static ConfigEntry<string> Preset5 { get; private set; }
    //Other Configs
    public static ConfigEntry<string> WebhookURL { get; private set; }
    public static ConfigEntry<string> BetaBuildURL { get; private set; }
    public static ConfigEntry<float> LastKillCooldown { get; private set; }
    public static ConfigEntry<float> LastShapeshifterCooldown { get; private set; }
    public static OptionBackupData RealOptionsData;
    public static Dictionary<byte, string> AllPlayerNames;
    public static Dictionary<(byte, byte), string> LastNotifyNames;
    public static Dictionary<byte, Color32> PlayerColors = new();
    public static Dictionary<byte, CustomDeathReason> AfterMeetingDeathPlayers = new();
    public static List<byte> winnerList;
    public static List<int> clientIdList;
    public static List<(string, byte, string, bool)> MessagesToSend;
    public static bool isChatCommand = false;
    public static Dictionary<byte, float> AllPlayerKillCooldown = new();
    public static HashSet<string> KillFlashAfterDead = new();
    public const float MinSpeed = 0.0001f;
    public static int AliveImpostorCount;
    public static int SKMadmateNowCount;
    public static Dictionary<byte, bool> CheckShapeshift = new();
    public static Dictionary<byte, byte> ShapeshiftTarget = new();
    public static bool VisibleTasksCount;
    public static string nickName = "";
    public static bool introDestroyed = false;
    // イントロが終わった時刻。キル履歴を試合の経過時間で出すのに使う
    public static DateTime IntroEndTime = DateTime.MinValue;  
    public static bool SetRoleOverride = true;   
    public static bool IsroleAssigned
        => !SetRoleOverride || SelectRolesPatch.roleAssigned;
    public static float DefaultCrewmateVision;
    public static float DefaultImpostorVision;
    public const float RoleTextSize = 2f;
    public static HashSet<byte> ShowRoleInfoAtMeeting = new();
    public static HashSet<byte> ShowChangeMainRole = new();
    public static bool isFirstTurn = false;
    public static bool IsSetRoleFinished = false;//廃村条件用

    // 期間限定 月13は無効
    public static bool IsValentine = DateTime.Now.Month == 13 && DateTime.Now.Day is 9 or 10 or 11 or 12 or 13 or 14 or 15;
    public static bool IsWhiteDay = DateTime.Now.Month == 13 && (DateTime.Now.Day >= 13 && DateTime.Now.Day <= 16);
    public static bool IsAprilFool = DateTime.Now.Month == 13 && DateTime.Now.Day == 1;
    public static bool IsHalloween = DateTime.Now.Month == 11 && DateTime.Now.Day <= 8;
    public static bool IsInitialRelease = DateTime.Now.Month == 11 && DateTime.Now.Day >= 1 && DateTime.Now.Day <= 8;
    public static bool IsChristmas = DateTime.Now.Month == 13 && DateTime.Now.Day <= 25;

    public static IEnumerable<PlayerControl> AllPlayerControls => TownOfHostL.Core.Players.All;
    public static IEnumerable<PlayerControl> AllAlivePlayerControls => TownOfHostL.Core.Players.Alive;
    public static IEnumerable<PlayerControl> AllDeadPlayerControls => TownOfHostL.Core.Players.Dead;

    public static Main Instance;

    public override void Load()
    {
        Instance = this;
        TownOfHostL.Modules.IncrementalGcGate.Apply();
        TownOfHostL.Modules.OptionSaver.Initialize(); // オプション保存用ディレクトリ・ファイルを起動時に作成

        //Client Options
        HideName = Config.Bind("Client Options", "Hide Game Code Name", "TOH_L");
        HideColor = Config.Bind("Client Options", "Hide Game Code Color", $"{ModColor}");
        ForceJapanese = Config.Bind("Client Options", "Force Japanese", false);
        JapaneseRoleName = Config.Bind("Client Options", "Japanese Role Name", true);
        IsMuteLobbyBGM = Config.Bind("Client Options", "Mute Lobby BGM", false);
        SleepAccountMenu = Config.Bind("Client Options", "Sleep Account Menu", true,
            "Hide the account panel on the main menu until login finishes, so the menu can be used right after start-up.");
        OnlyTohlLobbies = Config.Bind("Client Options", "Only TOHL Lobbies", true,
            "Find Game lists only lobbies hosted with TOHL (official servers only). When off, TOHL lobbies are not listed.");
        DebugKeyInput = Config.Bind("Authentication", "Debug Key", "");

        Logger = BepInEx.Logging.Logger.CreateLogSource("TOH_L");
        TownOfHostL.Logger.Enable();
        TownOfHostL.Modules.ConsoleQuickEdit.Disable();
        // 止めるタグは設定ファイルに持たせ、/log で切り替えた分も次の起動に残す
        TownOfHostL.Logger.LoadDisabledTags(Config.Bind("Logging", "Disabled Tags", TownOfHostL.Logger.DefaultDisabledTags,
            "Log tags that are not written. Comma separated. Developers can change this in game with /log."));

        // 認証関連-初期化
        DebugKeyAuth = new HashAuth(DebugKeyHash, DebugKeySalt);

        // 認証関連-認証
        DebugModeManager.Auth(DebugKeyAuth, DebugKeyInput.Value);

        winnerList = new();
        VisibleTasksCount = false;
        MessagesToSend = new List<(string, byte, string, bool)>();

        Preset1 = Config.Bind("Preset Name Options", "Preset1", "Preset_1");
        Preset2 = Config.Bind("Preset Name Options", "Preset2", "Preset_2");
        Preset3 = Config.Bind("Preset Name Options", "Preset3", "Preset_3");
        Preset4 = Config.Bind("Preset Name Options", "Preset4", "Preset_4");
        Preset5 = Config.Bind("Preset Name Options", "Preset5", "Preset_5");
        WebhookURL = Config.Bind("Other", "WebhookURL", "none");
        BetaBuildURL = Config.Bind("Other", "BetaBuildURL", "");
        MessageWait = Config.Bind("Other", "MessageWait", 0.5f);
        KillFlashAfterDeadByHost = Config.Bind("Other", "KillFlashAfterDeadByHost", false);
        LastKillCooldown = Config.Bind("Other", "LastKillCooldown", (float)30);
        LastShapeshifterCooldown = Config.Bind("Other", "LastShapeshifterCooldown", (float)30);

        PluginModuleInitializerAttribute.InitializeAll();

        IRandom.SetInstance(new NetRandomWrapper());

        hasArgumentException = false;
        ExceptionMessage = "";
        try
        {
            var type = typeof(RoleBase);
            var roleClassArray =
            CustomRoleManager.AllRolesClassType = Assembly.GetAssembly(type)
                .GetTypes()
                .Where(x => x.IsSubclassOf(type)).ToArray();

            foreach (var roleClassType in roleClassArray)
                roleClassType.GetField("RoleInfo")?.GetValue(type);
        }
        catch (ArgumentException ex)
        {
            TownOfHostL.Logger.Error("エラー:Dictionaryの値の重複を検出しました", "LoadDictionary");
            TownOfHostL.Logger.Exception(ex, "LoadDictionary");
            hasArgumentException = true;
            ExceptionMessage = ex.Message;
            ExceptionMessageIsShown = false;
        }
        // 役職の分類の表は、全役職クラスの RoleInfo が揃ってから組む (登録で重複が出ても組む)
        TownOfHostL.Core.RoleCatalog.Build();
        TownOfHostL.Logger.Info($"{Application.version}", "AmongUs Version");

        var handler = TownOfHostL.Logger.Handler("GitVersion");
        handler.Info($"{nameof(ThisAssembly.Git.Branch)}: {ThisAssembly.Git.Branch}");
        handler.Info($"{nameof(ThisAssembly.Git.BaseTag)}: {ThisAssembly.Git.BaseTag}");
        handler.Info($"{nameof(ThisAssembly.Git.Commit)}: {ThisAssembly.Git.Commit}");
        handler.Info($"{nameof(ThisAssembly.Git.Commits)}: {ThisAssembly.Git.Commits}");
        handler.Info($"{nameof(ThisAssembly.Git.IsDirty)}: {ThisAssembly.Git.IsDirty}");
        handler.Info($"{nameof(ThisAssembly.Git.Sha)}: {ThisAssembly.Git.Sha}");
        handler.Info($"{nameof(ThisAssembly.Git.Tag)}: {ThisAssembly.Git.Tag}");

        ClassInjector.RegisterTypeInIl2Cpp<ErrorText>();
        ClassInjector.RegisterTypeInIl2Cpp<BooleanOptionItem.BooleanGameSetting>();

        // かくれんぼの間に休ませる仕組みと delegate 型のキャッシュは、TOHL の Harmony を外しても残る別の Harmony に置く
        TownOfHostL.Core.VanillaHideAndSeek.Init(this);
        TownOfHostL.Core.Modes.PatchBundleSet.Init(Harmony);
        TownOfHostL.Core.Modes.PatchBundleSet.Apply(TownOfHostL.Core.Modes.ModeCatalog.Standard.LiveBundles);
        Application.quitting += new Action(Utils.SaveNowLog);
        // まとめて保存する設定を、終了前に書き切る
        Application.quitting += new Action(TownOfHostL.Modules.OptionSaver.Flush);
    }
}
public enum CustomDeathReason
{
    Kill,
    Vote,
    Suicide,
    Spell,
    FollowingSuicide,
    Bite,
    Bombed,
    Misfire,
    Torched,
    Sniped,
    Shot,
    Revenge,
    Execution,
    Disconnected,
    Fall,
    Poisoning,
    Clean,
    NotGather,
    Senility,
    CounterAttack,
    Telekinesis,
    IgnitionBombed,

    // TownOfHost-hamo から移植
    Counter,
    Infected,
    Grim,
    Magic,
    Guess,
    TeleportKill,
    Trap,
    Hit,
    Suffocation,
    Swallowed,
    Poisoned,
    Launch,
    Compression,
    Evaporation,
    Retaliation,
    RuleViolation,
    MagicC,

    Win,
    etc = -1
}
//WinData
public enum CustomWinner
{
    Draw = -1,
    Default = -2,
    None = -3,
    Impostor = CustomRoles.Impostor,
    Crewmate = CustomRoles.Crewmate,
    Jester = CustomRoles.Jester,
    Terrorist = CustomRoles.Terrorist,
    Lovers = CustomRoles.Lovers,
    Executioner = CustomRoles.Executioner,
    Arsonist = CustomRoles.Arsonist,
    Egoist = CustomRoles.Egoist,
    Jackal = CustomRoles.Jackal,

    AntiComplete = CustomRoles.AntiComplete,
    DarkHide = CustomRoles.DarkHide,
    NBakery = CustomRoles.Bakery,
    Workaholic = CustomRoles.Workaholic,
    LoveCutter = CustomRoles.LoveCutter,
    Lawyer = CustomRoles.Lawyer,
    God = CustomRoles.God,
    FoxSpirit = CustomRoles.FoxSpirit,
    Pirate = CustomRoles.Pirate,

    //CC
    RedL = CustomRoles.CCRedLeader,
    BlueL = CustomRoles.CCBlueLeader,
    YellowL = CustomRoles.CCYellowLeader,

    HASTroll = CustomRoles.HASTroll,

    // TownOfHost-hamo から移植
    Pavlov = CustomRoles.PavlovDog,
    StandMaster = CustomRoles.StandMaster,
    Eater = CustomRoles.Eater,
    Fox = CustomRoles.Fox,
    Spelunker = CustomRoles.Spelunker,
    Tuna = CustomRoles.Tuna,
    Vulture = CustomRoles.Vulture,
    Onmyoji = CustomRoles.Onmyoji,
    AntiHero = CustomRoles.AntiHero,
    Zombie = CustomRoles.Zombie,
    PlagueDoctor = CustomRoles.PlagueDoctor,
    CountKiller = CustomRoles.CountKiller,
}
public enum SuffixModes
{
    None = 0,
    TOH_L,
    Streaming,
    Recording,
    RoomHost,
    OriginalName
}
public enum VoteMode
{
    Default,
    Suicide,
    SelfVote,
    Skip
}

public enum TieMode
{
    Default,
    All,
    Random
}
public enum AddonShowMode
{
    Default,
    All,
    TOH
}
public enum NameChange
{
    None,
    Crew,
    Color
}
public enum SyncColorMode
{
    None,
    Clone,
    fif_fif,
    ThreeCornered,
    Twin,
}