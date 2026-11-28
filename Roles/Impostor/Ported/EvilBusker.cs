using AmongUs.GameOptions;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Crewmate;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

// イビルバスカー: ファントムのボタンで、決めた時間だけ幽霊になる。時間が来るか会議が始まると元に戻る。キルで使える回数が増える。
// 役職の元: End K not (Endless Host Roles のアストラル (バスカー) をインポスターにした物。案は SuperNewRoles のバスカー)
public sealed class EvilBusker : RoleBase, IImpostor, IUsePhantomButton, IUsePetButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilBusker),
            player => new EvilBusker(player),
            CustomRoles.EvilBusker,
            () => OptionUsePet?.GetBool() == true ? RoleTypes.Impostor : RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpPorted + 0,
            SetupOptionItem,
            "ebk"
        );

    public EvilBusker(PlayerControl player)
    : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        cooldown = OptionCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
        killsPerUse = OptionKillsPerUse.GetInt();
        uses = OptionUseCount.GetInt();
        if (OptionUsePet.GetBool()) pet = new BuskerPet(cooldown, TryBecomeGhost);
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionUseCount;
    private static OptionItem OptionUsePet;
    private static OptionItem OptionKillsPerUse;
    private static float killCooldown;
    private static float cooldown;
    private static float duration;
    private static int killsPerUse;

    private int uses;
    private int killsTowardUse;
    private readonly BuskerPet pet;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 25f, false)
            .SetValueFormat(OptionFormat.Seconds);
        Busker.CreateGhostOptions(RoleInfo, out OptionCooldown, out OptionDuration, out OptionUseCount, out OptionUsePet, 2);
        OptionKillsPerUse = IntegerOptionItem.Create(RoleInfo, Busker.OptionName.BuskerKillsPerUse, new(0, 10, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
    }

    public float CalculateKillCooldown() => killCooldown;
    public override void ApplyGameOptions(IGameOptions opt)
    {
        // 使い切った後は押せるボタンを残さない (キルで回数が戻ったら元のクールへ)
        AURoleOptions.PhantomCooldown = uses > 0 ? cooldown : 200f;
        AURoleOptions.GuardianAngelCooldown = TempGhost.LockedGuardianAngelCooldown;
    }

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
        => Busker.CountdownText(Player, seer, seen, isForMeeting);
}
