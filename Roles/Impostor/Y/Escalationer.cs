using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor;
public sealed class Escalationer : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Escalationer),
            player => new Escalationer(player),
            CustomRoles.Escalationer,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpY + 1200,
            SetupOptionItem,
            "エスカレーショナー"
        );
    public Escalationer(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        SpeedUpRate = OptionSpeedUpRate.GetFloat();
        KillCoolDecrease = OptionKillCoolDecrease.GetFloat();
        SpeedAndCoolReset = OptionSpeedAndCoolReset.GetBool();
    }
    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionSpeedUpRate;
    private static OptionItem OptionKillCoolDecrease;
    private static OptionItem OptionSpeedAndCoolReset;
    enum OptionName
    {
        EscalationerSpeedUpRate,
        EscalationerKillCoolDecrease,
        EscalationerSpeedAndCoolReset,
    }
    private static float KillCooldown;
    private static float SpeedUpRate;
    private static float KillCoolDecrease;
    private static bool SpeedAndCoolReset;
    float nowKillcool = 0f;

    public static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionKillCoolDecrease = FloatOptionItem.Create(RoleInfo, OptionName.EscalationerKillCoolDecrease, new(0f, 60f, 0.5f), 1.0f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionSpeedUpRate = FloatOptionItem.Create(RoleInfo, OptionName.EscalationerSpeedUpRate, new(0.1f, 2f, 0.05f), 0.15f, false)
            .SetValueFormat(OptionFormat.Multiplier);
        OptionSpeedAndCoolReset = BooleanOptionItem.Create(RoleInfo, OptionName.EscalationerSpeedAndCoolReset, false, false);
    }
    public override void Add()
    {
        var playerId = Player.PlayerId;
        nowKillcool = KillCooldown;
    }
    public float CalculateKillCooldown() => nowKillcool;

    public override void OnStartMeeting()
    {
    }
    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!info.IsSuicide)
        {
            (var killer, var target) = info.AttemptTuple;

            nowKillcool = System.Math.Max(0f, nowKillcool - KillCoolDecrease);
            Logger.Info($"{killer.GetNameWithRole()}:キルクール減少:{nowKillcool}", "Escalationer");
            killer.ResetKillCooldown();

            foreach (var player in Main.AllPlayerControls)
            {
                var speed = TownOfHostL.Modules.PlayerSpeed.GetAdd(player.PlayerId, SpeedKey) + SpeedUpRate;
                // 会議で戻す時は、会議の前に役職が替わっても戻るよう役職の持ち物にする。戻さない時は試合の終わりまで続く
                if (SpeedAndCoolReset) Holdings.AddSpeed(player.PlayerId, SpeedKey, speed);
                else TownOfHostL.Modules.PlayerSpeed.Add(player.PlayerId, SpeedKey, speed); // 役職が外れても残す
            }
            Utils.SyncAllSettings();
        }
    }

    private string SpeedKey => $"Escalationer{Player.PlayerId}";

    public override void AfterMeetingTasks()
    {
        if (!SpeedAndCoolReset) return;

        nowKillcool = KillCooldown;
        Player.ResetKillCooldown();

        Holdings.RemoveSpeedFromAll(SpeedKey);
        Utils.SyncAllSettings();
    }
}