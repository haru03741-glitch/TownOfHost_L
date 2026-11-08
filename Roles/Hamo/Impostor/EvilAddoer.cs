using AmongUs.GameOptions;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;



namespace TownOfHostL.Roles.Hamo.Impostor

{

    public sealed class EvilAddoer : HamoRoleBase, IImpostor

    {

        public static readonly SimpleRoleInfo RoleInfo =

            SimpleRoleInfo.CreateHamo(

                typeof(EvilAddoer),

                player => new EvilAddoer(player),

                CustomRoles.EvilAddoer,

                () => RoleTypes.Impostor,

                CustomRoleTypes.Impostor,

                3700,

                SetupOptionItem,

                "evad",

                OptionSort: (2, 4),

                from: From.TownOfHost_K

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