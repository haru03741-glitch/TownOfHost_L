using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;

using static TownOfHostL.Translator;
using static TownOfHostL.Utils;

namespace TownOfHostL.Roles.Crewmate;
public sealed class SeeingOff : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(SeeingOff),
            player => new SeeingOff(player),
            CustomRoles.SeeingOff,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewY + 500,
            null,
            "見送り人",
            "#883fd1",
            flags: RoleFlags.LearnsExiledTeam
        );
    public SeeingOff(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }
    //sendingに記載
}