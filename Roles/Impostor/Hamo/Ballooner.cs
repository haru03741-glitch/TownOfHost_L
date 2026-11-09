
using AmongUs.GameOptions;
using Hazel;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

public sealed class Ballooner : RoleBase, IImpostor, IUsePhantomButton
{
    // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Ballooner),
            player => new Ballooner(player),
            CustomRoles.Ballooner,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 2500,
            SetupOptionItem,
            "Ba"
        );
    public Ballooner(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        MinBoomDis = OptionMinBoomDis.GetFloat();
        MaxBoomDis = OptionMaxBoomDis.GetFloat();
        chargewakl = OptionChargeWalk.GetFloat();
        chargestep = OptionChargeStep.GetFloat();

        n1flug = false;
        l1move = 0;
        ResetBalloon();
    }
    static OptionItem OptionKillCoolDown;
    static OptionItem OptionAbilityCoolDown;
    static OptionItem OptionMinBoomDis;//最小の爆破範囲
    static float MinBoomDis;
    static OptionItem OptionMaxBoomDis;//最大の爆破範囲
    static float MaxBoomDis;
    static OptionItem OptionChargeWalk;//どれくらい歩けば1たまるか
    static float chargewakl;
    static OptionItem OptionChargeStep;//どれくらいチャージされるか
    static float chargestep;
    static OptionItem OptionAfterMeetingRemoveCharge;//会議後どれだけ空気が抜けるか
    static OptionItem OptionSuicide;//爆破時、自殺するか
    static OptionItem OptionTargetImpostor;//爆破時、味方を巻き込むか

    float NowBoomDis;//現在の爆破範囲 0以下なら使用不可
    float NowWalkCount;//歩数計
    Vector2 OldPosition;//前チェックの位置

    enum OptionName
    {
        BalloonerMinBoomDis, BalloonerMaxBoomDis,
        BalloonerChargeWalk, BalloonerChargeStep,
        BalloonerSuicide, BalloonerAfterMeetingRemoveCharge,
        BalloonerTargetImpostor
    }

    private static void SetupOptionItem()
    {
        OptionKillCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0, 180, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionAbilityCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0, 180, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionMinBoomDis = FloatOptionItem.Create(RoleInfo, OptionName.BalloonerMinBoomDis, new(-5, 10, 0.25f), 0, false).SetValueFormat(OptionFormat.Multiplier);
        OptionMaxBoomDis = FloatOptionItem.Create(RoleInfo, OptionName.BalloonerMaxBoomDis, new(0.25f, 15, 0.25f), 3, false).SetValueFormat(OptionFormat.Multiplier);
        OptionChargeWalk = FloatOptionItem.Create(RoleInfo, OptionName.BalloonerChargeWalk, new(1, 300, 1), 60, false);
        OptionChargeStep = FloatOptionItem.Create(RoleInfo, OptionName.BalloonerChargeStep, new(0.1f, 5, 0.05f), 0.5f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionAfterMeetingRemoveCharge = FloatOptionItem.Create(RoleInfo, OptionName.BalloonerAfterMeetingRemoveCharge, new(0, 30, 0.5f), 0.5f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionSuicide = BooleanOptionItem.Create(RoleInfo, OptionName.BalloonerSuicide, false, false);
        OptionTargetImpostor = BooleanOptionItem.Create(RoleInfo, OptionName.BalloonerTargetImpostor, true, false);
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (player.IsAlive() is false) return;

        if (OldPosition == new Vector2(50f, 50f) || MaxBoomDis <= NowBoomDis
        || Player.inVent || Player.MyPhysics.Animations.IsPlayingEnterVentAnimation()
        || Player.MyPhysics.Animations.IsPlayingAnyLadderAnimation() || Player.inMovingPlat
        || !Player.CanMove)//歩行以外は除外
        {
            OldPosition = player.GetTruePosition();
            return;
        }

        var distance = Vector2.Distance(OldPosition, player.GetTruePosition());
        l1move += distance;
        OldPosition = player.GetTruePosition();
        //歩数計の確認
        NowWalkCount += distance;
        if (chargewakl <= NowWalkCount)
        {
            NowWalkCount = 0;
            NowBoomDis = Mathf.Clamp(NowBoomDis + chargestep, MinBoomDis, MaxBoomDis);
            NowBoomDis = Mathf.Round(NowBoomDis * 100) / 100;
            Utils.NotifyRoles(SpecifySeer: player);
            if (MaxBoomDis <= NowBoomDis) n1flug = true;
        }
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = OptionAbilityCoolDown.GetFloat();
    }
    public float CalculateKillCooldown() => OptionKillCoolDown.GetFloat();

    public override string GetProgressText(bool comms = false)
        => Utils.ColorString(NowBoomDis <= 0 ? Color.gray : (MaxBoomDis <= NowBoomDis ? Color.red : new Color32(255, 165, 0, 255)), $" ({NowBoomDis})");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting) return "";
        string text = "";

        if (MaxBoomDis > NowBoomDis)
            text = GetString("BalloonerLowerText1");
        if (NowBoomDis > 0)
        {
            if (text is not "") text += "\n";
            text += GetString("BalloonerLowerText2");
        }
        if (isForHud is false) text = $"<size=60%>{text}</size>";

        return Utils.ColorString(new Color32(255, 136, 0, 255), text);
    }
    void ResetBalloon()
    {
        NowBoomDis = OptionMinBoomDis.GetFloat();
        NowWalkCount = 0;
    }

    internal static bool IsInExplosionRange(PlayerControl source, PlayerControl target, float radius)
        => source != null && target != null
            && Vector2.Distance(target.GetTruePosition(), source.GetTruePosition()) <= radius;

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        if (NowBoomDis <= 0)
        {
            AdjustKillCooldown = true;
            ResetCooldown = false;
            return;
        }
        AdjustKillCooldown = false;
        ResetCooldown = true;

        var count = 0;
        foreach (var target in Main.AllAlivePlayerControls)
        {
            if (target.PlayerId == Player.PlayerId && !OptionSuicide.GetBool()) continue;//自殺が無効なら除外
            if (target.PlayerId != Player.PlayerId && target.IsTeammate(Player) && !OptionTargetImpostor.GetBool()) continue;

            if (IsInExplosionRange(Player, target, NowBoomDis))
            {
                count++;
                CustomRoleManager.OnCheckMurder(Player, target, target, target, true, false, 2, CustomDeathReason.Bombed);
            }
        }
        if (OptionSuicide.GetBool() is false)
        {
            ResetBalloon();
            SendRPC();
            Utils.NotifyRoles();
        }
        Player.SetKillCooldown(ForceProtect: true);
    }

    public override void AfterMeetingTasks()
    {
        NowBoomDis = Mathf.Clamp(NowBoomDis - OptionAfterMeetingRemoveCharge.GetFloat(), MinBoomDis, MaxBoomDis);
        NowBoomDis = Mathf.Round(NowBoomDis * 100) / 100;
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    public override string GetAbilityButtonText() => GetString("BalloonerAbility");

    public void SendRPC()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        ResetBalloon();
    }
    public override void CheckWinner(GameOverReason reason)
    {
    }
    bool n1flug;
    float l1move;
}
