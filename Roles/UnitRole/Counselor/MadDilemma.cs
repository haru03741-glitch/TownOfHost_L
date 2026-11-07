using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Madmate;
public sealed class MadDilemma : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(MadDilemma),
            player => new MadDilemma(player),
            CustomRoles.MadDilemma,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.UnitMix + 0,//使用しない
            null,
            "マッドジレンマ",
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            flags: RoleFlags.DontShowOption | RoleFlags.DontSelectLoyalty | RoleFlags.LearnsExiledTeam
        );
    public MadDilemma(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {}
}
