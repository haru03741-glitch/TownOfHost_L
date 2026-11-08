using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Crewmate;
public sealed class NormalNoisemaker : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(NormalNoisemaker),
            player => new NormalNoisemaker(player),
            CustomRoles.NormalNoisemaker,
            () => RoleTypes.Noisemaker,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewDefault + 300,
            SetupOptionItem,
            "ノイズメーカー",
            "#8cffff"
        );
    public NormalNoisemaker(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        impostorAlert = OptionImpostorAlert.GetBool();
        alertDuration = OptionAlertDuration.GetFloat();
    }

    private static OptionItem OptionImpostorAlert;
    private static OptionItem OptionAlertDuration;

    enum OptionName
    {
        NoisemakerImpostorAlert,
        NoisemakerAlertDuration,
    }
    public static bool impostorAlert;
    public static float alertDuration;
    private static void SetupOptionItem()
    {
        OptionImpostorAlert = BooleanOptionItem.Create(RoleInfo, OptionName.NoisemakerImpostorAlert, true, false);
        OptionAlertDuration = FloatOptionItem.Create(RoleInfo, OptionName.NoisemakerAlertDuration, new(1f, 30f, 1f), 10f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.NoisemakerImpostorAlert = impostorAlert;
        AURoleOptions.NoisemakerAlertDuration = alertDuration;
    }
}