using AmongUs.GameOptions;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Neutral;

// ジャッカルバスカー: ジャッカル陣営のキラー。ファントムのボタンで、決めた時間だけ幽霊になる。時間が来るか会議が始まると元に戻る。
// キルで使える回数が増える。
// 役職の元: End K not (Endless Host Roles のアストラル (バスカー) をジャッカルにした物。案は SuperNewRoles のバスカー)
public sealed class JackalBusker : RoleBase, ILNKiller, IUsePhantomButton, ISchrodingerCatOwner, IUsePetButton
{
    public override bool SeesNamesInMushroomMixup => Jackal.CanSeeNameMushroomMixup;
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(JackalBusker),
            player => new JackalBusker(player),
            CustomRoles.JackalBusker,
            () => OptionUsePet?.GetBool() == true ? RoleTypes.Impostor : RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuJackal + 300,
            SetupOptionItem,
            "jbk",
            "#00b4eb",
            isDesyncImpostor: true,
            countType: CountTypes.Jackal,
            assignInfo: new RoleAssignInfo(CustomRoles.JackalBusker, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1)
            },
            flags: RoleFlags.NeutralKiller | RoleFlags.CountedAsKiller,
            introTeam: CustomRoles.Jackal
        );

    public JackalBusker(PlayerControl player)
    : base(RoleInfo, player, () => HasTask.False)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        canVent = OptionCanVent.GetBool();
        canUseSabotage = OptionCanUseSabotage.GetBool();
        hasImpostorVision = OptionHasImpostorVision.GetBool();
        cooldown = OptionCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
        killsPerUse = OptionKillsPerUse.GetInt();
        uses = OptionUseCount.GetInt();
        if (OptionUsePet.GetBool()) pet = new Crewmate.BuskerPet(cooldown, TryBecomeGhost);
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionCanVent;
    private static OptionItem OptionCanUseSabotage;
    private static OptionItem OptionHasImpostorVision;
    private static OptionItem OptionCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionUseCount;
    private static OptionItem OptionUsePet;
    private static OptionItem OptionKillsPerUse;
    private static float killCooldown;
    private static bool canVent;
    private static bool canUseSabotage;
    private static bool hasImpostorVision;
    private static float cooldown;
    private static float duration;
    private static int killsPerUse;

    private int uses;
    private int killsTowardUse;
    private readonly Crewmate.BuskerPet pet;

    public SchrodingerCat.TeamType SchrodingerCatChangeTo => SchrodingerCat.TeamType.Jackal;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 22.5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, true, false);
        OptionCanUseSabotage = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanUseSabotage, true, false);
        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, GeneralOption.ImpostorVision, true, false);
        Crewmate.Busker.CreateGhostOptions(RoleInfo, out OptionCooldown, out OptionDuration, out OptionUseCount, out OptionUsePet, 2);
        OptionKillsPerUse = IntegerOptionItem.Create(RoleInfo, Crewmate.Busker.OptionName.BuskerKillsPerUse, new(0, 10, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
    }

    public float CalculateKillCooldown() => killCooldown;
    public bool CanUseSabotageButton() => canUseSabotage;
    public bool CanUseImpostorVentButton() => canVent;
    public override bool OnInvokeSabotage(SystemTypes systemType) => canUseSabotage;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(hasImpostorVision);
        // 使い切った後は押せるボタンを残さない (キルで回数が戻ったら元のクールへ)
        AURoleOptions.PhantomCooldown = uses > 0 ? cooldown : 200f;
        AURoleOptions.GuardianAngelCooldown = TempGhost.LockedGuardianAngelCooldown;
    }
    public void ApplySchrodingerCatOptions(IGameOptions option) => option.SetVision(hasImpostorVision);

    public override bool CanUseAbilityButton() => true;

    public override void Add() => pet?.Restart();
    bool IUsePetButton.UsesPet => pet != null;
    void IUsePetButton.OnPet() => pet?.OnPet();
    public override void AfterMeetingTasks() => pet?.Restart();

    private bool TryBecomeGhost(System.Action<PlayerControl> onReturned)
    {
        if (uses <= 0 || !TempGhost.Begin(Player, duration, onReturned)) return false;
        uses--;
        return true;
    }

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        // 回数切れやペットで使う設定の時は何も起きないので、クールも使わせない
        if (pet != null || !TryBecomeGhost(null))
        {
            ResetCooldown = false;
            return;
        }
        // 戻った時にキルと能力のクールを立て直すので、ここでは役職の送り直しもクールの調整もしない
        AdjustKillCooldown = false;
        ResetCooldown = null;
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (killsPerUse <= 0 || info.IsSuicide) return;
        if (++killsTowardUse < killsPerUse) return;
        killsTowardUse = 0;
        uses++;
        if (uses == 1) Player.MarkDirtySettings();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override int? UsesRemaining => pet == null ? uses : null;
    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(uses > 0 ? RoleInfo.RoleColor : UnityEngine.Color.gray, $"[{uses}]");
    public override string GetAbilityButtonText() => GetString("BuskerGhostButtonText");
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
        => Crewmate.Busker.CountdownText(Player, seer, seen, isForMeeting);
}
