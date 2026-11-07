using System.Linq;
using AmongUs.GameOptions;

using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Neutral;

public sealed class FoxSpirit : RoleBase, ISystemTypeUpdateHook, ISoloWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(FoxSpirit),
            player => new FoxSpirit(player),
            CustomRoles.FoxSpirit,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuFox + 0,
            SetupOptionItem,
            "妖狐",
            "#ad6ce0",
            countType: CountTypes.None,
            introSound: () => GetIntroSound(RoleTypes.Shapeshifter),
            assignInfo: new(CustomRoles.FoxSpirit, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1),
                IsInitiallyAssignableCallBack = () => (MapNames)Main.NormalOptions.MapId is not MapNames.Polus and not MapNames.Fungle
            },
            flags: RoleFlags.FixedCount | RoleFlags.CannotReport,
            introTeam: CustomRoles.FoxSpirit,
            // ワークホースになった後は、ワークホースの数のまま
            taskCount: pc => pc.Is(CustomRoles.Workhorse) ? null : TaskData,
            onlyVentCleaningTask: true
        );
    public FoxSpirit(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.ForRecompute
    )
    {
        KilledAfterFinishTask = OptionKilledAfterFinishTask.GetBool();
        IgnoreGhostTask = OptionIgnoreGhostTask.GetBool();
        CustomRoleManager.TaskExemptionsOthers.Add(IsGhostTaskIgnored);
    }
    private static OptionItem OptionTaskCount;
    private static OptionItem OptionKilledAfterFinishTask;
    private static OptionItem OptionIgnoreGhostTask;
    private bool KilledAfterFinishTask;
    private static bool IgnoreGhostTask;
    /// <summary>妖狐が配られた試合で、死後のタスクを免除する設定なら、死んだ人のタスクを免除する</summary>
    private static bool IsGhostTaskIgnored(NetworkedPlayerInfo p)
        => IgnoreGhostTask && p.IsDead && CustomRoles.FoxSpirit.IsPresent() && !TownOfHostL.Core.PlayerTraits.KeepsTasksWhenDead(p.Object);
    private enum OptionName
    {
        FoxSpiritTaskCount,
        FoxSpiritKilledAfterFinishTask,
        FoxSpiritIgnoreGhostTask,
    }

    private static void SetupOptionItem()
    {
        OptionTaskCount = IntegerOptionItem.Create(RoleInfo, 10, OptionName.FoxSpiritTaskCount, new(1, 50, 1), 10, false)
                .SetValueFormat(OptionFormat.Pieces);
        OptionKilledAfterFinishTask = BooleanOptionItem.Create(RoleInfo, 11, OptionName.FoxSpiritKilledAfterFinishTask, true, false);
        OptionIgnoreGhostTask = BooleanOptionItem.Create(RoleInfo, 12, OptionName.FoxSpiritIgnoreGhostTask, true, false);
    }
    public static (bool, int, int) TaskData => (false, 0, OptionTaskCount.GetInt());
    public SoloWinTier SoloWinTier => SoloWinTier.Tasks;
    /// <summary>妖狐の誰かがタスクを終えて生きていれば、生きている妖狐が全員勝つ。妨害の勝ちは横取りしない</summary>
    public bool CheckSoloWin(WinnerResolver.Context ctx, out CustomWinner winner)
    {
        winner = CustomWinner.FoxSpirit;
        return Player.IsAlive() && ctx.Reason != GameOverReason.ImpostorsBySabotage
            && Main.AllAlivePlayerControls.Any(p => p.Is(CustomRoles.FoxSpirit) && p.GetPlayerTaskState().IsTaskFinished);
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        (var killer, var target) = info.AttemptTuple;
        // 直接キル出来る役職チェック
        if (killer.GetCustomRole().IsDirectKillRole()) return true;
        // タスク完了していたらキルされる
        if (KilledAfterFinishTask && IsTaskFinished) return true;

        killer.RpcProtectedMurderPlayer(target); //常にバリアする、互いに分かる
        target.RpcProtectedMurderPlayer(target);
        Logger.Info($"{target.GetNameWithRole()}：ガード", "FoxSpirit");

        info.CanKill = false;
        return true;
    }

    // O2
    bool ISystemTypeUpdateHook.UpdateLifeSuppSystem(LifeSuppSystemType switchSystem, byte amount)
    {
        return false;
    }
    // リアクター
    bool ISystemTypeUpdateHook.UpdateReactorSystem(ReactorSystemType switchSystem, byte amount)
    {
        return false;
    }
    bool ISystemTypeUpdateHook.UpdateHeliSabotageSystem(HeliSabotageSystem switchSystem, byte amount)
    {
        return false;
    }
}
