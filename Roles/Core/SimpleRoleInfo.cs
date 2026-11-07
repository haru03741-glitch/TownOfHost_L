using System;
using System.Linq;
using UnityEngine;
using AmongUs.GameOptions;

using static TownOfHostL.Options;

namespace TownOfHostL.Roles.Core;

public partial class SimpleRoleInfo
{
    public Type ClassType;
    public Func<PlayerControl, RoleBase> CreateInstance;
    public CustomRoles RoleName;
    public Func<RoleTypes> BaseRoleType;
    public CustomRoleTypes CustomRoleType;
    public CountTypes CountType;
    public Color RoleColor;
    public string RoleColorCode;
    public int ConfigId;
    public TabGroup Tab;
    public OptionItem RoleOption => CustomRoleSpawnChances[RoleName];
    public bool IsEnable = false;
    public OptionCreatorDelegate OptionCreator;
    public string ChatCommand;
    /// <summary>本人視点のみインポスターに見える役職</summary>
    public bool IsDesyncImpostor;
    /// <summary>役職の分類 (判定は CustomRolesHelper の IsXxx から引く)</summary>
    public RoleFlags Flags;
    private Func<AudioClip> introSound;
    public AudioClip IntroSound => introSound?.Invoke();
    private Func<bool> canMakeMadmate;
    public bool CanMakeMadmate => canMakeMadmate?.Invoke() == true;
    public RoleAssignInfo AssignInfo { get; }
    /// <summary>
    /// イントロで、この役職が属す組として見せる役職 (ジャッカルの組・妖狐の組など)。
    /// 既定のイントロではその役職の名前と色の帯と「Team役職名」の文、ChangeIntro では第三陣営の文を「Team役職名」にする。null なら陣営の既定の表示
    /// </summary>
    public CustomRoles? IntroTeam { get; private set; }
    /// <summary>既定のイントロで、インポスターの人数の文を出す (インポスター側に見えないデシンクのキル役など)</summary>
    public bool IntroShowsImpostorCount { get; private set; }
    /// <summary>
    /// 配るタスクの数を役職が決める。呼ぶ時点の数は、設定の上書きと属性 (Workhorse) を通した後の物。null を返すとそのまま。
    /// タスクを配る時は役職クラスがまだ無いことがあるので、クラスでなく登録で持つ
    /// </summary>
    public Func<PlayerControl, (bool hasCommonTasks, int numLong, int numShort)?> TaskCount { get; private set; }
    /// <summary>短いタスクを掃除 (VentCleaning) だけにし、共通タスクを配らない</summary>
    public bool OnlyVentCleaningTask { get; private set; }

    private SimpleRoleInfo(
        Type classType,
        Func<PlayerControl, RoleBase> createInstance,
        CustomRoles roleName,
        Func<RoleTypes> baseRoleType,
        CustomRoleTypes customRoleType,
        CountTypes countType,
        int configId,
        OptionCreatorDelegate optionCreator,
        string chatCommand,
        string colorCode,
        bool isDesyncImpostor,
        TabGroup tab,
        Func<AudioClip> introSound,
        Func<bool> canMakeMadmate,
        RoleAssignInfo assignInfo,
        RoleFlags flags
    )
    {
        ClassType = classType;
        CreateInstance = createInstance;
        RoleName = roleName;
        BaseRoleType = baseRoleType;
        CustomRoleType = customRoleType;
        CountType = countType;
        ConfigId = configId;
        OptionCreator = optionCreator;
        IsDesyncImpostor = isDesyncImpostor;
        this.introSound = introSound;
        this.canMakeMadmate = canMakeMadmate;
        ChatCommand = chatCommand;
        AssignInfo = assignInfo;
        Flags = flags;

        if (colorCode == "")
            colorCode = customRoleType switch
            {
                CustomRoleTypes.Impostor or CustomRoleTypes.Madmate => "#ff1919",
                CustomRoleTypes.Crewmate => "#8cffff",
                _ => "#ffffff"
            };
        RoleColorCode = colorCode;

        _ = ColorUtility.TryParseHtmlString(colorCode, out RoleColor);

        if (tab == TabGroup.ModMainSettings)
            tab = CustomRoleType switch
            {
                CustomRoleTypes.Impostor => TabGroup.ImpostorRoles,
                CustomRoleTypes.Madmate => TabGroup.MadmateRoles,
                CustomRoleTypes.Crewmate => TabGroup.CrewmateRoles,
                CustomRoleTypes.Neutral => TabGroup.NeutralRoles,
                CustomRoleTypes.Unit => TabGroup.UnitRoles,
                _ => tab
            };
        Tab = tab;

        CustomRoleManager.AllRolesInfo.Add(roleName, this);
    }
    public static SimpleRoleInfo Create(
        Type classType,
        Func<PlayerControl, RoleBase> createInstance,
        CustomRoles roleName,
        Func<RoleTypes> baseRoleType,
        CustomRoleTypes customRoleType,
        int configId,
        OptionCreatorDelegate optionCreator,
        string chatCommand,
        string colorCode = "",
        bool isDesyncImpostor = false,
        TabGroup tab = TabGroup.ModMainSettings,
        Func<AudioClip> introSound = null,
        Func<bool> canMakeMadmate = null,
        CountTypes? countType = null,
        RoleAssignInfo assignInfo = null,
        RoleFlags flags = RoleFlags.None,
        CustomRoles? introTeam = null,
        bool introShowsImpostorCount = false,
        Func<PlayerControl, (bool hasCommonTasks, int numLong, int numShort)?> taskCount = null,
        bool onlyVentCleaningTask = false
    )
    {
        countType ??= customRoleType == CustomRoleTypes.Impostor ? CountTypes.Impostor : CountTypes.Crew;
        assignInfo ??= new RoleAssignInfo(roleName, customRoleType);

        SimpleRoleInfo info =
            new(
                classType,
                createInstance,
                roleName,
                baseRoleType,
                customRoleType,
                countType.Value,
                configId,
                optionCreator,
                chatCommand,
                colorCode,
                isDesyncImpostor,
                tab,
                introSound,
                canMakeMadmate,
                assignInfo,
                flags
            );
        info.IntroTeam = introTeam;
        info.IntroShowsImpostorCount = introShowsImpostorCount;
        info.TaskCount = taskCount;
        info.OnlyVentCleaningTask = onlyVentCleaningTask;
        return info;
    }
    public static SimpleRoleInfo CreateForVanilla(
        Type classType,
        Func<PlayerControl, RoleBase> createInstance,
        RoleTypes baseRoleType,
        string colorCode = "",
        bool canMakeMadmate = false,
        RoleAssignInfo assignInfo = null,
        RoleFlags flags = RoleFlags.None
    )
    {
        CustomRoles roleName;
        CustomRoleTypes customRoleType;
        CountTypes countType = CountTypes.Crew;

        switch (baseRoleType)
        {
            case RoleTypes.Engineer:
                roleName = CustomRoles.Engineer;
                customRoleType = CustomRoleTypes.Crewmate;
                break;
            case RoleTypes.Scientist:
                roleName = CustomRoles.Scientist;
                customRoleType = CustomRoleTypes.Crewmate;
                break;
            case RoleTypes.Tracker:
                roleName = CustomRoles.Tracker;
                customRoleType = CustomRoleTypes.Crewmate;
                break;
            case RoleTypes.Noisemaker:
                roleName = CustomRoles.Noisemaker;
                customRoleType = CustomRoleTypes.Crewmate;
                break;
            case RoleTypes.Detective:
                roleName = CustomRoles.Detective;
                customRoleType = CustomRoleTypes.Crewmate;
                break;
            case RoleTypes.Judge:
                roleName = CustomRoles.Judge;
                customRoleType = CustomRoleTypes.Crewmate;
                break;
            case RoleTypes.GuardianAngel:
                roleName = CustomRoles.GuardianAngel;
                customRoleType = CustomRoleTypes.Crewmate;
                break;
            case RoleTypes.Impostor:
                roleName = CustomRoles.Impostor;
                customRoleType = CustomRoleTypes.Impostor;
                countType = CountTypes.Impostor;
                break;
            case RoleTypes.Shapeshifter:
                roleName = CustomRoles.Shapeshifter;
                customRoleType = CustomRoleTypes.Impostor;
                countType = CountTypes.Impostor;
                break;
            case RoleTypes.Phantom:
                roleName = CustomRoles.Phantom;
                customRoleType = CustomRoleTypes.Impostor;
                countType = CountTypes.Impostor;
                break;
            case RoleTypes.Viper:
                roleName = CustomRoles.Viper;
                customRoleType = CustomRoleTypes.Impostor;
                countType = CountTypes.Impostor;
                break;
            default:
                roleName = CustomRoles.Crewmate;
                customRoleType = CustomRoleTypes.Crewmate;
                break;
        }
        return
            new(
                classType,
                createInstance,
                roleName,
                () => baseRoleType,
                customRoleType,
                countType,
                -1,
                null,
                null,
                colorCode,
                false,
                TabGroup.ModMainSettings,
                null,
                () => canMakeMadmate,
                assignInfo ?? new(roleName, customRoleType),
                flags | RoleFlags.Vanilla
            );
    }
    public delegate void OptionCreatorDelegate();
}