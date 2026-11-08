using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Hamo.Crewmate
{
    public sealed class NiceAddoer : HamoRoleBase
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.CreateHamo(
                typeof(NiceAddoer),
                player => new NiceAddoer(player),
                CustomRoles.NiceAddoer,
                () => RoleTypes.Crewmate,
                CustomRoleTypes.Crewmate,
                33200,
                SetupOptionItem,
                "NA",
                "#87cefa",
                (1, 1),
                from: From.TownOfHost_K
            );
        public NiceAddoer(PlayerControl player)
        : base(
            RoleInfo,
            player
        )
        {
        }
        private static void SetupOptionItem()
        {
            RoleAddAddons.Create(RoleInfo, DefaaultOn: true);
        }
    }
}