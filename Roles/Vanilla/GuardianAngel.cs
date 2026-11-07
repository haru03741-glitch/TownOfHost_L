using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Vanilla;

public sealed class GuardianAngel : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.CreateForVanilla(
            typeof(GuardianAngel),
            player => new GuardianAngel(player),
            RoleTypes.GuardianAngel,
            assignInfo: new RoleAssignInfo(CustomRoles.GuardianAngel, CustomRoleTypes.Crewmate)
            {
                IsInitiallyAssignableCallBack = () => false
            },
            flags: RoleFlags.NoComingOutOption
        );
    public GuardianAngel(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }
}