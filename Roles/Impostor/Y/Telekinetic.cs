using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class Telekinetic : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Telekinetic),
            player => new Telekinetic(player),
            CustomRoles.Telekinetic,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 2100,
            SetupOptionItem,
            "テレキネス"
        );
    public Telekinetic(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        ChangeDeathReason = OptionChangeDeathReason.GetBool();
    }
    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionChangeDeathReason;
    enum OptionName
    {
        TelekineticChangeDeathReason,
    }
    private static float KillCooldown;
    private static bool ChangeDeathReason;

    public static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionChangeDeathReason = BooleanOptionItem.Create(RoleInfo, OptionName.TelekineticChangeDeathReason, false, false);
    }

    public float CalculateKillCooldown() => KillCooldown;

    // 念力の押しはキルでないので、守りより前に振り分ける (中の自爆のキルが守りを通す)
    public bool OnCheckMurderBeforeGuards(MurderInfo info)
    {
        var (killer, target) = info.AttemptTuple;
        // 通常のキルは起こさない

        // キルモーションなし（相手の自爆）を起こす
        if (CustomRoleManager.OnCheckMurder(Player, target, target, target, true,
            deathReason: ChangeDeathReason ? CustomDeathReason.Telekinesis : CustomDeathReason.Kill))
        {
            // キラーセット
            target.SetRealKiller(killer);
        }

        // 自身は全くキルしないことになるのでキルクールセットする
        killer.SetKillCooldown();
        return false;
    }
}