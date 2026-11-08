using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Madmate;
public sealed class MadScientist : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(MadScientist),
            player => new MadScientist(player),
            CustomRoles.MadScientist,
            () => RoleTypes.Scientist,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.MadY + 500,
            SetupOptionItem,
            "マッド科学者",
            introSound: () => GetIntroSound(RoleTypes.Impostor)
        );
    public MadScientist(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        vitalCooldown = OptionVitalCooldown.GetFloat();
        vitalBatteryDuration = OptionVitalBatteryDuration.GetFloat();
    }
    private static OptionItem OptionVitalCooldown;
    private static OptionItem OptionVitalBatteryDuration;
    enum OptionName
    {
        VitalCooldown,
        VitalBatteryDuration
    }
    private static float vitalCooldown;
    private static float vitalBatteryDuration;

    private static void SetupOptionItem()
    {
        OptionVitalCooldown = FloatOptionItem.Create(RoleInfo, OptionName.VitalCooldown, new(0f, 180f, 5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionVitalBatteryDuration = FloatOptionItem.Create(RoleInfo, OptionName.VitalBatteryDuration, new(5f, 180f, 5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        Options.SetUpAddOnOptions(RoleInfo.RoleName, RoleInfo.Tab);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ScientistCooldown = vitalCooldown;
        AURoleOptions.ScientistBatteryCharge = vitalBatteryDuration;
    }
}