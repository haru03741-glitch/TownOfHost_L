using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Vanilla;

public sealed class Shapeshifter : RoleBase, IImpostor, IKiller, ISidekickable
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.CreateForVanilla(
            typeof(Shapeshifter),
            player => new Shapeshifter(player),
            RoleTypes.Shapeshifter,
            canMakeMadmate: true,
            flags: RoleFlags.NoComingOutOption
        );
    public Shapeshifter(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }
}