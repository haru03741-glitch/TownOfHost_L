using System;
using System.Text;
using UnityEngine;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;
using static TownOfHostL.Options;

namespace TownOfHostL.Roles.Impostor;
public sealed class EvilNekomata : RoleBase, IImpostor
{
    public override bool RevengesOnExile => true;
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(EvilNekomata),
            player => new EvilNekomata(player),
            CustomRoles.EvilNekomata,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 0,
            null,
            "イビル猫又"
        );
    public EvilNekomata(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }
}