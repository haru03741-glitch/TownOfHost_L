using AmongUs.GameOptions;
using TownOfHostL.Modules.Net;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;

// ペースメーカー: キルした相手を、決めた時間 (0 は次の会議まで) バイタルで生きている (OK) ように見せる。
// 嘘はバイタルの台の近くにいる人の画面にだけ出す (VitalsMask)。
public sealed class Pacemaker : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Pacemaker),
            player => new Pacemaker(player),
            CustomRoles.Pacemaker,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpL + 400,
            SetupOptionItem,
            "pcm"
        );

    public Pacemaker(PlayerControl player)
    : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionDuration;
    private static float killCooldown;
    private static float duration;

    private enum OptionName
    {
        PacemakerDuration,
    }

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, OptionName.PacemakerDuration, new(0f, 180f, 5f), 0f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public float CalculateKillCooldown() => killCooldown;

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        var target = info.AttemptTarget;
        // ここは死の記録より前に呼ばれるので、相手の生死はまだ見ない (生き返った人は VitalsMask が外す)
        if (!AmongUsClient.Instance.AmHost || target == null || Is(target)) return;
        VitalsMask.Mask(target.PlayerId, duration);
    }
}
