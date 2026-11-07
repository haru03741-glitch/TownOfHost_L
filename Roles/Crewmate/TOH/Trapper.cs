using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Crewmate;
public sealed class Trapper : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Trapper),
            player => new Trapper(player),
            CustomRoles.Trapper,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewTOH + 800,
            SetupOptionItem,
            "トラッパー",
            "#5a8fd0"
        );
    public Trapper(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        BlockMoveTime = OptionBlockMoveTime.GetFloat();
    }

    private static OptionItem OptionBlockMoveTime;
    enum OptionName
    {
        TrapperBlockMoveTime
    }

    private static float BlockMoveTime;

    private static void SetupOptionItem()
    {
        OptionBlockMoveTime = FloatOptionItem.Create(RoleInfo, 10, OptionName.TrapperBlockMoveTime, new(1f, 180f, 1f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }
    public override void OnMurderPlayerAsTarget(MurderInfo info)
    {
        if (info.IsSuicide) return;
        if (info.IsMeeting) return;

        var killer = info.AttemptKiller;
        var speedKey = $"Trapper{Player.PlayerId}";
        TownOfHostL.Modules.PlayerSpeed.Freeze(killer.PlayerId, speedKey);
        ReportDeadBodyPatch.CannotReportList.Add(killer.PlayerId);
        killer.MarkDirtySettings();
        _ = new LateTask(() =>
        {
            TownOfHostL.Modules.PlayerSpeed.Remove(killer.PlayerId, speedKey);
            ReportDeadBodyPatch.CannotReportList.Remove(killer.PlayerId);
            killer.MarkDirtySettings();
            RPC.PlaySoundRPC(killer.PlayerId, Sounds.TaskComplete);
        }, BlockMoveTime, "Trapper BlockMove");
    }
}