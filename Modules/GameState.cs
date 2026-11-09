using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;

using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

public partial class PlayerState
{
    byte PlayerId;
    public List<CustomRoles> MainRoleList;
    public List<CustomRoles> SubRoles;
    public CountTypes CountType { get; private set; }
    public bool IsDead { get; set; }
    public CustomDeathReason DeathReason { get; set; }
    public TaskState taskState;
    public bool IsBlackOut { get; set; }
    private bool _canUseMovingPlatform = true;
    public bool CanUseMovingPlatform
    {
        get => _canUseMovingPlatform;
        set
        {
            Logger.Info($"ID: {PlayerId} の昇降機可用性を {value} に設定", nameof(PlayerState));
            _canUseMovingPlatform = value;
        }
    }
    public (DateTime, byte) RealKiller;
    public PlainShipRoom LastRoom;
    public Dictionary<byte, string> TargetColorData;
    public PlayerState(byte playerId)
    {
        MainRoleList = new();
        SubRoles = new();
        CountType = CountTypes.OutOfGame;
        PlayerId = playerId;
        IsDead = false;
        DeathReason = CustomDeathReason.etc;
        taskState = new();
        IsBlackOut = false;
        RealKiller = (DateTime.MinValue, byte.MaxValue);
        LastRoom = null;
        TargetColorData = new();
    }
    public CustomRoles GetNowMainRole()
    {
        if (MainRoleList.Count == 0) return CustomRoles.NotAssigned;

        return MainRoleList.Last();
    }
    public CustomRoles GetCustomRole()
    {
        var RoleInfo = Utils.GetPlayerInfoById(PlayerId);
        return RoleInfo.Role == null
            ? GetNowMainRole()
            : RoleInfo.Role.Role switch
            {
                RoleTypes.Crewmate => CustomRoles.Crewmate,
                RoleTypes.Engineer => CustomRoles.Engineer,
                RoleTypes.Scientist => CustomRoles.Scientist,
                RoleTypes.Tracker => CustomRoles.Tracker,
                RoleTypes.Noisemaker => CustomRoles.Noisemaker,
                RoleTypes.Judge => CustomRoles.Judge,
                RoleTypes.GuardianAngel => CustomRoles.GuardianAngel,
                RoleTypes.Impostor => CustomRoles.Impostor,
                RoleTypes.Shapeshifter => CustomRoles.Shapeshifter,
                RoleTypes.Phantom => CustomRoles.Phantom,
                RoleTypes.Detective => CustomRoles.Detective,
                RoleTypes.Viper => CustomRoles.Viper,
                _ => CustomRoles.Crewmate,
            };
    }
    public void SetMainRole(CustomRoles role, bool resetList = false)
    {
        // 初手のアサインロール時には初期登録されているバニラロールを上書きする
        if (resetList)
        {
            MainRoleList.Clear();
        }
        MainRoleList.Add(role);
        // 前の役職での見え方は使わない (替えた側が送り直せば、また覚える)
        if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost) Modules.RoleView.Forget(PlayerId);

        CountType = CustomRoleManager.GetRoleInfo(role) is SimpleRoleInfo roleInfo ?
            roleInfo.CountType :
            role.IsSpectator() ? CountTypes.OutOfGame :
            role.HasRoleFlag(RoleFlags.NotCounted) ? CountTypes.None :
            role.IsImpostor() ? CountTypes.Impostor : CountTypes.Crew;
    }
    public void SetSubRole(CustomRoles role, bool AllReplace = false)
    {
        if (AllReplace)
            SubRoles.ToArray().Do(role => SubRoles.Remove(role));

        if (!SubRoles.Contains(role))
        {
            SubRoles.Add(role);
            CustomRoleManager.SubRoleAdd(PlayerId, role);
        }
    }
    public void RemoveSubRole(CustomRoles role)
    {
        if (SubRoles.Contains(role))
            SubRoles.Remove(role);
    }

    public void SetDead()
    {
        var wasDead = IsDead;
        IsDead = true;
        if (AmongUsClient.Instance.AmHost)
        {
            RPC.SendDeathReason(PlayerId, DeathReason);
            var host = PlayerControl.LocalPlayer;
            if (host != null && host.PlayerId == PlayerId) CompleteHostTasksOnDeath(host);
            // 死に方によらない「誰かが死んだ」の知らせ。同じ人に 2 度呼ばれても 1 度だけ
            if (!wasDead) CustomRoleManager.NotifyPlayerDead(PlayerId, DeathReason);
        }
    }
    /// <summary>
    /// 死んだ状態を戻す (復活は Modules.Revive から呼ぶ)。死んだ時に終えたことにしたホストのタスクは戻さない
    /// </summary>
    public void SetAlive()
    {
        IsDead = false;
        DeathReason = CustomDeathReason.etc;
        RealKiller = (DateTime.MinValue, byte.MaxValue);
        if (AmongUsClient.Instance.AmHost) RPC.SendDeathReason(PlayerId, DeathReason, isDead: false);
    }
    /// <summary>
    /// ホストの死後のタスク免除。残りのタスクを終えたことにする (タスクの合計からは外さない)。
    /// 死に方 (キル・追放・はしご・自爆など) によらず、死んだ所で 1 度効かせる
    /// </summary>
    private static void CompleteHostTasksOnDeath(PlayerControl host)
    {
        if (!Options.HostGhostIgnoreTasks.GetBool()) return;
        if (TownOfHostL.Core.PlayerTraits.KeepsTasksWhenDead(host) || !Utils.HasTasks(host.Data)) return;

        var taskState = host.GetPlayerTaskState();
        taskState.CompletedTasksCount = taskState.AllTasksCount;
        Logger.Info($"{host.GetNameWithRole()}: TaskCounts = {taskState.CompletedTasksCount}/{taskState.AllTasksCount}", "HostGhostIgnoreTasks");

        foreach (var task in host.Data.Tasks)
        {
            if (!task.Complete)
            {
                task.Complete = true;
                GameData.Instance.CompletedTasks++;
            }
        }
        Logger.Info($"{host.GetNameWithRole()}.Dead TotalTaskCounts = {GameData.Instance.CompletedTasks}/{GameData.Instance.TotalTasks}", "HostGhostIgnoreTasks");
    }
    public bool IsSuicide() { return DeathReason == CustomDeathReason.Suicide; }
    public TaskState GetTaskState() { return taskState; }
    public void InitTask(PlayerControl player)
    {
        taskState.Init(player);
    }
    public void UpdateTask(PlayerControl player)
    {
        taskState.Update(player);
    }

    public byte GetRealKiller()
        => IsDead && RealKiller.Item1 != DateTime.MinValue ? RealKiller.Item2 : byte.MaxValue;
    public int GetKillCount(bool ExcludeSelfKill = false)
    {
        int count = 0;
        foreach (var state in AllPlayerStates.Values)
            if (!(ExcludeSelfKill && state.PlayerId == PlayerId) && state.GetRealKiller() == PlayerId)
                count++;
        return count;
    }
    public void SetCountType(CountTypes countType) => CountType = countType;

    private static Dictionary<byte, PlayerState> allPlayerStates = new(15);
    public static IReadOnlyDictionary<byte, PlayerState> AllPlayerStates => allPlayerStates;

    public static PlayerState GetByPlayerId(byte playerId) => AllPlayerStates.TryGetValue(playerId, out var state) ? state : null;
    [GameModuleInitializer]
    public static void Clear() => allPlayerStates.Clear();
    public static void Create(byte playerId)
    {
        if (allPlayerStates.ContainsKey(playerId))
        {
            Logger.Warn($"重複したIDのPlayerStateが作成されました: {playerId}", nameof(PlayerState));
            return;
        }
        allPlayerStates[playerId] = new(playerId);
    }
}
public partial class TaskState
{
    public static int InitialTotalTasks;
    public int AllTasksCount;
    public int CompletedTasksCount;
    public bool hasTasks;
    public int RemainingTasksCount => AllTasksCount - CompletedTasksCount;
    public bool IsTaskFinished => RemainingTasksCount <= 0 && hasTasks;
    public TaskState()
    {
        this.AllTasksCount = -1;
        this.CompletedTasksCount = 0;
        this.hasTasks = false;
    }

    public void Init(PlayerControl player)
    {
        Logger.Info($"{player.GetNameWithRole()}: InitTask", "TaskState.Init");
        if (player == null || player.Data == null || player.Data.Tasks == null) return;
        if (!Utils.HasTasks(player.Data, false))
        {
            AllTasksCount = 0;
            return;
        }
        hasTasks = true;
        AllTasksCount = player.Data.Tasks.Count;
        Logger.Info($"{player.GetNameWithRole()}: TaskCounts = {CompletedTasksCount}/{AllTasksCount}", "TaskState.Init");
    }
    public void Update(PlayerControl player)
    {
        Logger.Info($"{player.GetNameWithRole()}: UpdateTask", "TaskState.Update");

        //初期化出来ていなかったら初期化
        if (AllTasksCount == -1) Init(player);

        if (!hasTasks) return;

        //クリアしてたらカウントしない
        if (CompletedTasksCount >= AllTasksCount) return;

        CompletedTasksCount++;

        //調整後のタスク量までしか表示しない
        CompletedTasksCount = Math.Min(AllTasksCount, CompletedTasksCount);
        Logger.Info($"{player.GetNameWithRole()}: TaskCounts = {CompletedTasksCount}/{AllTasksCount}", "TaskState.Update");
    }
    public bool HasCompletedEnoughCountOfTasks(int count) =>
        IsTaskFinished || CompletedTasksCount >= count;
}
public class PlayerVersion
{
    public readonly Version version;
    public readonly string tag;
    public readonly string forkId;
    public PlayerVersion(string ver, string tag_str, string forkId) : this(Version.Parse(ver), tag_str, forkId) { }
    public PlayerVersion(Version ver, string tag_str, string forkId)
    {
        version = ver;
        tag = tag_str;
        this.forkId = forkId;
    }
    public bool IsEqual(PlayerVersion pv)
    {
        return pv.version == version && pv.tag == tag;
    }
}
public static partial class GameStates
{
    public static bool InGame = false;
    public static bool AlreadyDied = false;
    public static bool IsModHost => TownOfHostL.Core.Players.ById(0) is { } host && host.IsModClient();
    public static bool IsLobby => AmongUsClient.Instance.GameState == AmongUsClient.GameStates.Joined;
    public static bool IsInGame => InGame;
    public static bool IsEnded => AmongUsClient.Instance.GameState == AmongUsClient.GameStates.Ended;
    public static bool IsNotJoined => AmongUsClient.Instance.GameState == AmongUsClient.GameStates.NotJoined;
    public static bool IsOnlineGame => AmongUsClient.Instance.NetworkMode == NetworkModes.OnlineGame;
    public static bool IsLocalGame => AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame;
    public static bool IsFreePlay => AmongUsClient.Instance.NetworkMode == NetworkModes.FreePlay;
    public static bool IsInTask => InGame && !MeetingHud.Instance;
    public static bool IsMeeting => InGame && MeetingHud.Instance;
    public static bool IsCountDown => GameStartManager.InstanceExists && GameStartManager.Instance.startState == GameStartManager.StartingStates.Countdown;
    public static bool IsShip => ShipStatus.Instance != null;
    public static bool IsCanMove => PlayerControl.LocalPlayer?.CanMove is true;
    public static bool IsDead => PlayerControl.LocalPlayer?.Data?.IsDead is true;
}
public static partial class MeetingStates
{
    public static DeadBody[] DeadBodies = null;
    public static NetworkedPlayerInfo ReportTarget = null;
    public static bool IsEmergencyMeeting => ReportTarget == null;
    public static bool IsExistDeadBody => DeadBodies.Length > 0;
    public static bool MeetingCalled = false;
    public static bool FirstMeeting = true;
    /// <summary>今が何日目か。試合の始めが 1 日目で、会議が終わるたびに 1 つ進む (ホストのみ)</summary>
    public static int Day { get; private set; } = 1;
    static bool dayEnded;
    [Attributes.GameModuleInitializer]
    public static void InitDay() { Day = 1; dayEnded = false; }
    /// <summary>会議の始めに呼ぶ (その日の終わりの処理をまた受け付ける)</summary>
    public static void BeginMeeting() => dayEnded = false;
    /// <summary>その日の会議の終わり。会議の後の処理は 1 回の会議で 2 度呼ばれるので、最初の 1 回だけ true を返して日を進める</summary>
    public static bool TryEndDay()
    {
        if (dayEnded) return false;
        dayEnded = true;
        Day++;
        return true;
    }
}