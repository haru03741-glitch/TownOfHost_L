using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Madmate;
public sealed class MadJester : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadJester),
            player => new MadJester(player),
            CustomRoles.MadJester,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.MadY + 600,
            SetupOptionItem,
            "マッドジェスター",
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            flags: RoleFlags.NoTeamWin
        );
    public MadJester(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => TaskCompWin ? HasTask.ForRecompute : HasTask.False
    )
    {
        TaskCompWin = OptionTaskCompWin.GetBool();
    }

    private static OptionItem OptionCanVent;
    private static OptionItem OptionTaskCompWin;
    private static Options.OverrideTasksData Tasks;

    static bool TaskCompWin;
    enum OptionName
    {
        MadJesterTaskCompWin,
    }

    private static void SetupOptionItem()
    {
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 10, GeneralOption.CanVent, false, false);
        OptionTaskCompWin = BooleanOptionItem.Create(RoleInfo, 11, OptionName.MadJesterTaskCompWin, true, false);
        Tasks = Options.OverrideTasksData.Create(RoleInfo, 20, OptionTaskCompWin);

        Options.SetUpAddOnOptions(RoleInfo.ConfigId + 30, RoleInfo.RoleName, RoleInfo.Tab);
    }

    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (!AmongUsClient.Instance.AmHost || Player.PlayerId != exiled.PlayerId) return;
        if (TaskCompWin && !MyTaskState.IsTaskFinished) return;

        CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
        CustomWinnerHolder.WinnerIds.Add(exiled.PlayerId);
        DecidedWinner = true;
    }
}