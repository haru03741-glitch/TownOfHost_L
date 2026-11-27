using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Madmate;
public sealed class MadSheriff : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(MadSheriff),
            player => new MadSheriff(player),
            CustomRoles.MadSheriff,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.MadY + 300,
            SetupOptionItem,
            "マッドシェリフ",
            isDesyncImpostor: true,
            flags: RoleFlags.DirectKill | RoleFlags.DivinedAsKiller,
            introShowsImpostorCount: true
        );
    public MadSheriff(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.False
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        nowSuicideMotion = (SuicideMotionOption)OptionSuicideMotion.GetValue();
        MisfireKillsTarget = OptionMisfireKillsTarget.GetBool();
        CanVent = OptionCanVent.GetBool();
    }

    private static OptionItem OptionKillCooldown;
    public static OptionItem OptionSuicideMotion;
    private static OptionItem OptionMisfireKillsTarget;
    private static OptionItem OptionCanVent;
    public enum SuicideMotionOption
    {
        Default,
        MotionKilled
    };
    SuicideMotionOption nowSuicideMotion;

    enum OptionName
    {
        SheriffMisfireKillsTarget,
        SillySheriffSuicideMotion,
    }
    private static float KillCooldown;
    private static bool MisfireKillsTarget;
    public static bool CanVent;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionSuicideMotion = StringOptionItem.Create(RoleInfo, OptionName.SillySheriffSuicideMotion, EnumHelper.GetAllNames<SuicideMotionOption>(), 0, false);
        OptionMisfireKillsTarget = BooleanOptionItem.Create(RoleInfo, OptionName.SheriffMisfireKillsTarget, false, false);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, false, false);
        Options.SetUpAddOnOptions(RoleInfo.RoleName, RoleInfo.Tab);
    }
    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseImpostorVentButton() => CanVent;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(Options.AddOnRoleOptions[(CustomRoles.MadSheriff, CustomRoles.AddLight)].GetBool());
    }
    // 相手を殺さない誤射はキルでないので、守りより前に振り分ける (押した相手の守りを使わせない)
    public bool OnCheckMurderBeforeGuards(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.IsSuicide) return true;
        // 相手も殺す誤射はキルなので、守りを通す (本人の死は OnCheckMurderAsKiller で起こす)
        if (MisfireKillsTarget) return true;

        Misfire(info.AttemptKiller, info.AttemptTarget);
        return false;
    }
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.IsSuicide) return;
        Misfire(info.AttemptKiller, info.AttemptTarget);
    }
    void Misfire(PlayerControl killer, PlayerControl target)
    {
        //自殺処理
        PlayerState.GetByPlayerId(killer.PlayerId).DeathReason = CustomDeathReason.Misfire;
        switch (nowSuicideMotion)
        {
            case SuicideMotionOption.Default://自殺モーション起こさない＝自身が自爆
                Kill.Suicide(killer);
                break;
            case SuicideMotionOption.MotionKilled://相手に切られる
                Kill.Force(target, killer);
                break;
        }
    }

    public bool OverrideKillButtonText(out string text)
    {
        text = Translator.GetString("DeathReason.Misfire");
        return true;
    }
}