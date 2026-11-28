using AmongUs.GameOptions;


using System.Linq;

using UnityEngine;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;

using static TownOfHostL.Main;

using static TownOfHostL.Utils;
using static TownOfHostL.Translator;



namespace TownOfHostL.Roles.Neutral;



public sealed class Freeter : RoleBase, IKiller, IAdditionalWinner

{
    public override bool NeedsSeenNames => OptSeeEmployerRole.GetBool();

    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(Freeter),
            player => new Freeter(player),
            CustomRoles.Freeter,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 51600,
            SetupOptionItem,
            "tt",
            "#32cd32",
            isDesyncImpostor: true
        );



    public Freeter(PlayerControl player)

        : base(RoleInfo, player)

    {

        BetTargetId = byte.MaxValue;

        hasBeenEmployed = false;

        unemployedTurns = 0;

        lastBetTargetRole = CustomRoles.NotAssigned;

        initialBetTargetRole = CustomRoles.NotAssigned;

    }



    static OptionItem OptUnemployedDeathTurns;

    static OptionItem OptInitialCooldown;

    static OptionItem OptFinalCooldown;

    static OptionItem OptSeeEmployerRole;

    static OptionItem OptSurvivalRequiredForWin;



    byte BetTargetId;

    public byte GetBetTargetId => BetTargetId;

    CustomRoles lastBetTargetRole;

    CustomRoles initialBetTargetRole;

    bool hasBeenEmployed;

    int unemployedTurns;
    int countedDay = -1;



    enum OptionName

    {

        FreeterUnemployedDeathTurns,

        FreeterInitialCooldown,

        FreeterFinalCooldown,

        FreeterSeeEmployerRole,

        FreeterSurvivalRequiredForWin,

    }



    private static void SetupOptionItem()

    {

        // 無職(BetTargetId未設定)のままこのターン数が経過すると死亡する。0で無効化(死なない)

        OptUnemployedDeathTurns = IntegerOptionItem.Create(

                RoleInfo, OptionName.FreeterUnemployedDeathTurns,

                new(0, 15, 1), 3, false)

            .SetValueFormat(OptionFormat.Times)

            .SetZeroNotation(OptionZeroNotation.Infinity);



        // 一度も就職したことがない状態で最初の就職を試みるときのクールタイム

        OptInitialCooldown = FloatOptionItem.Create(

                RoleInfo, OptionName.FreeterInitialCooldown,

                new(0f, 60f, 2.5f), 20f, false)

            .SetValueFormat(OptionFormat.Seconds);



        // 一度就職した後、雇用主が死亡するなどして再就職が必要になったときのクールタイム

        OptFinalCooldown = FloatOptionItem.Create(

                RoleInfo, OptionName.FreeterFinalCooldown,

                new(0f, 120f, 2.5f), 90f, false)

            .SetValueFormat(OptionFormat.Seconds);



        OptSeeEmployerRole = BooleanOptionItem.Create(

            RoleInfo, OptionName.FreeterSeeEmployerRole, true, false);



        OptSurvivalRequiredForWin = BooleanOptionItem.Create(

            RoleInfo, OptionName.FreeterSurvivalRequiredForWin, false, false);

    }



    public override void ApplyGameOptions(IGameOptions opt)

    {

        opt.SetVision(false);

    }



    public bool CanUseKillButton() => Player.IsAlive() && BetTargetId == byte.MaxValue;

    public bool CanUseImpostorVentButton() => false;

    public bool CanUseSabotageButton() => false;



    // 初回就職はInitial、雇用主を失った後の再就職はFinalのクールタイムを使う

    public float CalculateKillCooldown() =>

        hasBeenEmployed ? OptFinalCooldown.GetFloat() : OptInitialCooldown.GetFloat();



    public bool OverrideKillButtonText(out string text)

    {

        text = BetTargetId == byte.MaxValue ? "就職" : "就職済み";

        return true;

    }




    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)

    {

        if (!OptSeeEmployerRole.GetBool()) return;

        if (BetTargetId == byte.MaxValue) return;

        if (seen.PlayerId != BetTargetId) return;



        var role = seen.GetCustomRole();

        view.Enabled = true;

        view.SetMainRole(ColorUtility.TryParseHtmlString(Utils.GetRoleColorCode(role), out var color) ? color : Utils.GetRoleColor(role), Utils.GetRoleName(role), showAddons: true);


    }



    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)

    {

        if (BetTargetId == byte.MaxValue) return;

        if (seer.PlayerId != BetTargetId) return;



        view.Enabled = true;

        view.SetMainRole(ColorUtility.TryParseHtmlString(RoleInfo.RoleColorCode, out var c) ? c : Utils.GetRoleColor(CustomRoles.Freeter), Utils.GetRoleName(CustomRoles.Freeter));


    }



    // 賭けの押しはキルでないので、守りより前に振り分ける (押した相手の守りを使わせない)
    public bool OnCheckMurderBeforeGuards(MurderInfo info)

    {

        var (freeter, target) = info.AttemptTuple;




        if (BetTargetId != byte.MaxValue) return false;

        if (target.PlayerId == freeter.PlayerId) return false;



        var closest = GetClosestPlayerInRange();

        closest ??= target;



        BetTargetId = closest.PlayerId;

        lastBetTargetRole = closest.GetCustomRole();

        initialBetTargetRole = lastBetTargetRole;

        hasBeenEmployed = true;

        unemployedTurns = 0;



        Holdings.NameColor(closest.PlayerId, Player.PlayerId, "#32cd32");



        Utils.NotifyRoles(ForceLoop: true);

        Utils.MarkEveryoneDirtySettings();




        _ = After(() => Utils.NotifyRoles(SpecifySeer: Player), 0.2f, "Freeter Bet");



        freeter.ResetKillCooldown();

        freeter.SetKillCooldown(ForceProtect: true);
        return false;

    }



    public override void OnFixedUpdate(PlayerControl player)

    {

        if (BetTargetId == byte.MaxValue) return;

        if (!AmongUsClient.Instance.AmHost) return;



        var target = GetPlayerById(BetTargetId);



        if (target == null || !target.IsAlive())

        {

            Holdings.RemoveNameColor(BetTargetId, Player.PlayerId);

            BetTargetId = byte.MaxValue;

            lastBetTargetRole = CustomRoles.NotAssigned;

            initialBetTargetRole = CustomRoles.NotAssigned;


            SendMessage(GetString("Freeter_BetTargetDead"), Player.PlayerId);

            Player.ResetKillCooldown();

            Player.SetKillCooldown(ForceProtect: true);

            _ = After(() => Utils.NotifyRoles(SpecifySeer: Player), 0.2f, "Freeter Reset");

            return;

        }



        var currentRole = target.GetCustomRole();

        if (currentRole != lastBetTargetRole)

        {

            lastBetTargetRole = currentRole;

            Holdings.NameColor(BetTargetId, Player.PlayerId, "#32cd32");

            _ = After(() => Utils.NotifyRoles(SpecifySeer: Player), 0.2f, "Freeter ColorFix");

        }

    }



    public override void OnStartMeeting()

    {

        if (BetTargetId == byte.MaxValue) return;

        if (!AmongUsClient.Instance.AmHost) return;



        Holdings.NameColor(BetTargetId, Player.PlayerId, "#32cd32");

        Utils.NotifyRoles(isForMeeting: true, SpecifySeer: Player, NoCache: true);

    }



    public override void AfterMeetingTasks()

    {

        if (!AmongUsClient.Instance.AmHost) return;



        if (BetTargetId == byte.MaxValue)

        {

            CheckUnemployedDeath();

            return;

        }



        _ = After(() =>

        {

            if (BetTargetId == byte.MaxValue) return;

            Holdings.NameColor(BetTargetId, Player.PlayerId, "#32cd32");

            Utils.NotifyRoles(SpecifySeer: Player);

        }, 0.3f, "Freeter.ColorRefresh");

    }



    // 無職のまま設定ターン数が経過したら死亡させる(0なら無効)

    private void CheckUnemployedDeath()

    {

        if (!Player.IsAlive()) return;



        int deathTurns = OptUnemployedDeathTurns.GetInt();

        if (deathTurns <= 0) return;



        if (!MeetingStates.OncePerDay(ref countedDay)) return;
        unemployedTurns++;




        if (unemployedTurns < deathTurns) return;



        var state = PlayerState.GetByPlayerId(Player.PlayerId);

        state.DeathReason = CustomDeathReason.Suicide;

        // 客の追放の演出の締めより前に死を送ると、暗転対策で合わせた頭数が崩れて暗転しうる
        AntiBlackout.RunAfterRevert(() =>
        {
            if (Player == null || !Player.IsAlive()) return;
            // 死の確定はキルの後の処理に任せる (先に死を記録すると、キルの入口が死んでいるとみなして送らない)
            state.DeathReason = CustomDeathReason.Suicide;
            Kill.Suicide(Player);
            SendMessage(GetString("Freeter_UnemployedDeath"), Player.PlayerId);
        });

    }



    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,

        bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        if (BetTargetId != byte.MaxValue) return "";



        int deathTurns = OptUnemployedDeathTurns.GetInt();

        if (deathTurns <= 0) return "";



        string sz = isForHud ? "" : "<size=60%>";

        int remain = Mathf.Max(0, deathTurns - unemployedTurns);

        return $"{sz}<color=#ff8800>無職状態: あと{remain}ターンで死亡</color>";

    }



    public override void OnAddedMidGame()

    {

        base.OnAddedMidGame();

        if (BetTargetId == byte.MaxValue) return;



        var target = GetPlayerById(BetTargetId);

        if (target == null) return;



        var role = target.GetCustomRole();

        if (role.IsImpostor())

            target.RpcSetRoleDesync(RoleTypes.Impostor, target.GetClientId());

        else

            target.RpcSetRoleDesync(RoleTypes.Crewmate, target.GetClientId());

    }



    public bool CheckWin(ref CustomRoles winnerRole)

    {

        if (OptSurvivalRequiredForWin.GetBool() && !Player.IsAlive()) return false;

        if (BetTargetId == byte.MaxValue) return false;



        var target = GetPlayerById(BetTargetId);

        if (target == null) return false;



        if (target.Is(CustomRoles.Freeter)) return false;



        return CustomWinnerHolder.WinnerIds.Contains(BetTargetId)

            || CustomWinnerHolder.WinnerRoles.Any(role =>

                target.Is(role)

                || role == initialBetTargetRole

                || role == lastBetTargetRole);

    }



    public override void CheckWinner(GameOverReason reason)

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (OptSurvivalRequiredForWin.GetBool() && !Player.IsAlive()) return;

        if (BetTargetId == byte.MaxValue) return;

        if (CustomWinnerHolder.WinnerIds.Contains(Player.PlayerId)) return;



        var target = GetPlayerById(BetTargetId);

        if (target == null || target.Is(CustomRoles.Freeter)) return;



        bool employerWon = CustomWinnerHolder.WinnerIds.Contains(BetTargetId)

                        || CustomWinnerHolder.WinnerRoles.Any(role =>

                            target.Is(role)

                            || role == initialBetTargetRole

                            || role == lastBetTargetRole);

        if (!employerWon) return;



        CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);

        if (!CustomWinnerHolder.AdditionalWinnerRoles.Contains(CustomRoles.Freeter))

            CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Freeter);

    }



    private PlayerControl GetClosestPlayerInRange()

    {

        const float maxDist = 1f;

        PlayerControl closest = null;

        float minDist = float.MaxValue;



        foreach (var pc in AllAlivePlayerControls)

        {

            if (pc.PlayerId == Player.PlayerId) continue;

            float dist = Vector2.Distance(Player.GetTruePosition(), pc.GetTruePosition());

            if (dist < maxDist && dist < minDist)

            {

                minDist = dist;

                closest = pc;

            }

        }

        return closest;

    }
}