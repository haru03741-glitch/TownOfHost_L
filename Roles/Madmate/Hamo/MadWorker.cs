using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Madmate;

// 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
public sealed class MadWorker : RoleBase, IKillFlashSeeable, IDeathReasonSeeable
{
    public override bool NeedsSeenNames => canSeeDeathReason;
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadWorker),
            player => new MadWorker(player),
            CustomRoles.MadWorker,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.Hamo + 21300,
            SetupOptionItem,
            "mw",
            introSound: () => GetIntroSound(RoleTypes.Impostor)
        );
    public MadWorker(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => (CannotWinAtDeath && player.Data.IsDead) ? HasTask.False : HasTask.ForRecompute
    )
    {
        canSeeKillFlash = Options.MadmateCanSeeKillFlash.GetBool();
        canSeeDeathReason = Options.MadmateCanSeeDeathReason.GetBool();

        ventCooldown = OptionVentCooldown.GetFloat();
        CannotWinAtDeath = true;
    }
    private static OptionItem OptionCanVent;
    private static OptionItem OptionVentCooldown;
    enum OptionName
    {
        VentCooldown
    }
    private static bool canSeeKillFlash;
    private static bool canSeeDeathReason;
    private static bool CannotWinAtDeath;
    private static float ventCooldown;
    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, false, false);
        OptionVentCooldown = FloatOptionItem.Create(RoleInfo, OptionName.VentCooldown, new(0f, 180f, 0.5f), 0f, false, OptionCanVent)
                .SetValueFormat(OptionFormat.Seconds);
        Options.OverrideTasksData.Create(RoleInfo);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = ventCooldown;
        AURoleOptions.EngineerInVentMaxTime = 0;
    }
    // タスクを全部終えたら (死んでいなければ) インポスター陣営の勝ち
    public override bool OnCompleteTask()
    {
        if (IsTaskFinished && !(CannotWinAtDeath && !Player.IsAlive()))
        {
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Impostor, Player.PlayerId, hantrole: CustomRoles.MadWorker);
        }
        return true;
    }
    public bool? CheckKillFlash(MurderInfo info) => canSeeKillFlash;
    public bool CheckSeeDeathReason(PlayerControl seen) => canSeeDeathReason;
    public override CustomRoles TellResults(PlayerControl player) => Options.MadTellOpt();
}
