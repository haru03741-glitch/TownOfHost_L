using AmongUs.GameOptions;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;



namespace TownOfHostL.Roles.Impostor

{

    public sealed class EvilAddoer : RoleBase, IImpostor

    {

        // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
        public static readonly SimpleRoleInfo RoleInfo =

            SimpleRoleInfo.Create(
            typeof(EvilAddoer),
            player => new EvilAddoer(player),
            CustomRoles.EvilAddoer,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 3700,
            SetupOptionItem,
            "evad"
        );

        public EvilAddoer(PlayerControl player)

        : base(

            RoleInfo,

            player

        )

        {

        }

        static OptionItem KillCoolDown;

        private static void SetupOptionItem()

        {

            KillCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0, 180, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);

            OverrideKilldistance.Create(RoleInfo);

            RoleAddAddons.Create(RoleInfo, DefaaultOn: true);

        }

        public float CalculateKillCooldown() => KillCoolDown.GetFloat();

    }

}