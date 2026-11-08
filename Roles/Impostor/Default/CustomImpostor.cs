using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class CustomImpostor : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(CustomImpostor),
            player => new CustomImpostor(player),
            CustomRoles.CustomImpostor,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpDefault + 900,
            SetupOptionItem,
            "カスタムインポスター",
            flags: RoleFlags.DontSelectLoyalty | RoleFlags.AddAddOn
        );
    public CustomImpostor(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        CanVent = OptionCanVent.GetBool();
        CanUseSabotage = OptionCanUseSabotage.GetBool();
        HasImpostorVision = OptionHasImpostorVision.GetBool();
    }
    private static OptionItem OptionKillCooldown;
    public static OptionItem OptionCanVent;
    public static OptionItem OptionCanUseSabotage;
    private static OptionItem OptionHasImpostorVision;
    private static float KillCooldown;
    public static bool CanVent;
    public static bool CanUseSabotage;
    private static bool HasImpostorVision;
    public static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCanVent = StringOptionItem.Create(RoleInfo, GeneralOption.CanVent, new[] { "ColoredOff", "ColoredOn" }, 1, false);
        OptionCanUseSabotage = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanUseSabotage, true, false);
        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, GeneralOption.ImpostorVision, true, false);
        Options.SetUpAddOnOptions(RoleInfo.RoleName, RoleInfo.Tab);
    }
    public float CalculateKillCooldown() => KillCooldown;
    public override void ApplyGameOptions(IGameOptions opt) => opt.SetVision(HasImpostorVision);
    public static void SetHudActive(HudManager __instance, bool isActive)
    {
        __instance.SabotageButton.ToggleVisible(isActive && CanUseSabotage);
    }
    public override bool OnInvokeSabotage(SystemTypes systemType) => CanUseSabotage;
    public bool CanUseImpostorVentButton() => CanVent;
}