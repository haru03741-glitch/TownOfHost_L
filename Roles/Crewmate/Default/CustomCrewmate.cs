using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Crewmate;
public sealed class CustomCrewmate : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(CustomCrewmate),
            player => new CustomCrewmate(player),
            CustomRoles.CustomCrewmate,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewDefault + 900,
            SetupOptionItem,
            "カスタムクルーメイト",
            "#8cffff",
            flags: RoleFlags.DontSelectLoyalty | RoleFlags.AddAddOn
        );
    public CustomCrewmate(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }

    public static void SetupOptionItem()
    {
        Options.SetUpAddOnOptions(RoleInfo.RoleName, RoleInfo.Tab);
        Options.OverrideTasksData.Create(RoleInfo);
    }
}