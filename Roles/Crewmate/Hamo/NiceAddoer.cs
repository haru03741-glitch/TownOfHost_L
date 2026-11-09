using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Crewmate
{
    public sealed class NiceAddoer : RoleBase
    {
        // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
            typeof(NiceAddoer),
            player => new NiceAddoer(player),
            CustomRoles.NiceAddoer,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 33200,
            SetupOptionItem,
            "NA",
            "#87cefa"
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