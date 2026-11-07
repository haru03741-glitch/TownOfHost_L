using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Vanilla;

public sealed class Scientist : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.CreateForVanilla(
            typeof(Scientist),
            player => new Scientist(player),
            RoleTypes.Scientist,
            "#8cffff",
            flags: RoleFlags.NoComingOutOption
        );
    public Scientist(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }
}