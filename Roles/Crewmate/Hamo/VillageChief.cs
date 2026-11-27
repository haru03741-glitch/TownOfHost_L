using System;

using AmongUs.GameOptions;


using UnityEngine;

using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;

using TownOfHostL.Patches;

using static TownOfHostL.Main;

using static TownOfHostL.Utils;

using static TownOfHostL.Translator;

using static TownOfHostL.Modules.SelfVoteManager;



namespace TownOfHostL.Roles.Crewmate;



public sealed class VillageChief : RoleBase, IKiller, ISelfVoter, IUsePetButton

{

    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(VillageChief),
            player => new VillageChief(player),
            CustomRoles.VillageChief,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 36700,
            SetupOptionItem,
            "vc",
            "#f5a623",
            isDesyncImpostor: true
        );



    public VillageChief(PlayerControl player)

        : base(RoleInfo, player, () => HasTask.True)

    {

        appointMode = false;

        hasAppointed = false;

        nowcool = AppointCooldown.GetFloat();

        LastCooltime = -1;



        NextAppointCandidate = byte.MaxValue;

        nearTimer = 0f;

        spawnWaitTimer = -1f;

    }



    private static OptionItem NotifyTarget;

    private static OptionItem AppointCooldown;

    private static OptionItem CanSeeCreatedSheriff;

    public static OptionItem SheriffKillCooldown;

    public static OptionItem SheriffMisfireKillsTarget;

    public static OptionItem SheriffShotLimit;

    public static OptionItem SheriffCanKillAllAlive;



    private static readonly string[] NotifyTargetOptions =

        ["None", "Everyone", "VillageChiefOnly", "SheriffOnly", "VillageChiefAndSheriff"];



    enum OptionName

    {

        VillageChiefSheriffShotLimit,

    }



    bool appointMode;

    bool hasAppointed;

    float nowcool;

    int LastCooltime;



    public byte NextAppointCandidate;

    private float nearTimer;

    private float spawnWaitTimer;

    private bool CanApproach => spawnWaitTimer >= 3f;



    private static void SetupOptionItem()

    {

        NotifyTarget = StringOptionItem.Create(

            RoleInfo, "VillageChiefNotifyTarget",

            NotifyTargetOptions, 0, false

        );

        AppointCooldown = FloatOptionItem.Create(

            RoleInfo, "AppointCooldown",

            new(0f, 120f, 2.5f), 30f, false

        ).SetValueFormat(OptionFormat.Seconds);

        CanSeeCreatedSheriff = BooleanOptionItem.Create(

            RoleInfo, "VillageChiefCanSeeCreatedSheriff", false, false

        );

        SheriffMisfireKillsTarget = BooleanOptionItem.Create(

            RoleInfo, "VillageChiefSheriffMisfireKillsTarget", false, false

        );

        SheriffKillCooldown = FloatOptionItem.Create(

            RoleInfo, "VillageChiefSheriffKillCooldown",

            new(0f, 990f, 0.5f), 30f, false

        ).SetValueFormat(OptionFormat.Seconds);

        SheriffShotLimit = IntegerOptionItem.Create(

            RoleInfo, OptionName.VillageChiefSheriffShotLimit,

            new(1, 15, 1), 15, false

        ).SetValueFormat(OptionFormat.Times);

        SheriffCanKillAllAlive = BooleanOptionItem.Create(

            RoleInfo, "VillageChiefSheriffCanKillAllAlive", true, false

        );

    }



    public float CalculateKillCooldown() => CanUseKillButton() ? AppointCooldown.GetFloat() : 999f;

    public bool CanUseKillButton() => Player.IsAlive() && appointMode && !hasAppointed;

    public bool CanUseSabotageButton() => false;

    public bool CanUseImpostorVentButton() => false;

    public override bool CanUseAbilityButton() => false;



    public override bool CanClickUseVentButton => !appointMode;

    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;



    bool ISelfVoter.CanUseVoted() => Player.IsAlive() && !hasAppointed;



    public override bool CheckAnyVote(byte votedForId, PlayerControl voter)

    {

        if (!Is(voter) || !Player.IsAlive() || hasAppointed) return true;



        if (CheckSelfVoteMode(Player, votedForId, out var status))

        {

            if (status is VoteStatus.Self)

            {

                SendMessage(

                    GetString("VillageChiefSelectModeMessage"),

                    

                    

                    Player.PlayerId);

                SetMode(Player, true);

                return false;

            }

            if (status is VoteStatus.Vote)

            {

                if (votedForId == Player.PlayerId || votedForId == SkipId)

                {

                    SendMessage("<color=#f5a623>" + GetString("VillageChiefInvalidTarget") + "</color>", Player.PlayerId);

                    SetMode(Player, false);

                    return false;

                }

                NextAppointCandidate = votedForId;

                SendMessage(

                    GetString("VillageChiefCandidateSetMessage"),

                    

                    

                    Player.PlayerId);

                SetMode(Player, false);

                return false;

            }

            if (status is VoteStatus.Skip)

            {

                NextAppointCandidate = byte.MaxValue;

                SendMessage("<color=#f5a623>" + GetString("VillageChiefAppointCancelled") + "</color>", Player.PlayerId);

                SetMode(Player, false);

                return false;

            }

        }

        return true;

    }



    public override void ApplyGameOptions(IGameOptions opt)

    {

        opt.SetVision(false);

        if (!appointMode)

        {

            AURoleOptions.EngineerCooldown = Mathf.Max(nowcool, 0.1f);

            AURoleOptions.EngineerInVentMaxTime = 0f;

        }

    }



    public override RoleTypes? AfterMeetingRole

        => (appointMode && !hasAppointed) ? RoleTypes.Impostor : RoleTypes.Engineer;



    public override void Add()

    {

        nowcool = AppointCooldown.GetFloat();

        LastCooltime = -1;


    }




    void IUsePetButton.OnPet() => OnPetUsed();
    private void OnPetUsed()

    {

        if (!Player.IsAlive() || hasAppointed) return;

        appointMode = !appointMode;

        ApplyModeDesync(appointMode);


        Utils.NotifyRoles(SpecifySeer: Player);

    }



    // 任命の押しはキルでないので、守りより前に振り分ける (押した相手の守りを使わせない)
    public bool OnCheckMurderBeforeGuards(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.IsSuicide) return true;

        if (!appointMode || hasAppointed || nowcool > 0f) return false;

        (_, var target) = info.AttemptTuple;

        DoAppoint(target);
        return false;
    }



    public override void OnFixedUpdate(PlayerControl player)

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (GameStates.CalledMeeting || GameStates.Intro) return;



        if (!Player.IsAlive() && appointMode)

        {

            appointMode = false;

            ApplyModeDesync(false);


            return;

        }



        if (!Player.IsAlive()) return;



        if (nowcool > 0f) nowcool -= Time.fixedDeltaTime;

        else nowcool = 0f;



        var now = Mathf.FloorToInt(nowcool);

        if (now != LastCooltime)

        {

            LastCooltime = now;

            if (!appointMode && !hasAppointed)

            {

                Player.MarkDirtySettings();

                _ = new LateTask(() =>

                {

                    if (Player.IsAlive() && !appointMode)

                        Player.RpcResetAbilityCooldown(sync: true);

                }, 0.1f, "VillageChief.VentCDSync");

            }

            if (player != PlayerControl.LocalPlayer)

                Utils.NotifyRoles(SpecifySeer: player);

        }



        if (spawnWaitTimer >= 0f && spawnWaitTimer < 3f)

            spawnWaitTimer += Time.fixedDeltaTime;



        if (!hasAppointed && NextAppointCandidate != byte.MaxValue && CanApproach)

        {

            var candidate = GetPlayerById(NextAppointCandidate);



            if (candidate == null || !candidate.IsAlive())

            {

                NextAppointCandidate = byte.MaxValue;

                nearTimer = 0f;


                return;

            }



            float dist = Vector2.Distance(Player.GetTruePosition(), candidate.GetTruePosition());

            if (dist <= 1.5f)

            {

                nearTimer += Time.fixedDeltaTime;

                if (nearTimer >= 1.5f && nowcool <= 0f)

                {

                    DoAppoint(candidate);

                    NextAppointCandidate = byte.MaxValue;

                    nearTimer = 0f;

                }

            }

            else

            {

                nearTimer = 0f;

            }

        }

    }



    private void DoAppoint(PlayerControl target)

    {

        hasAppointed = true;

        appointMode = false;

        ApplyModeDesync(false);



        if (target.GetCustomRole().IsImpostor())

        {

            PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Suicide;

            Kill.Suicide(Player);


            return;

        }



        if (Walkure.TryRejectRoleChange(Player, target, Walkure.RoleChangeSource.Crewmate))

        {


            return;

        }









        var previousRole = target.GetCustomRole();
        Sheriff.AppointedPlayerIds.Add(target.PlayerId);

        target.RpcChangeRole(CustomRoles.Sheriff);



        target.ResetKillCooldown();

        target.SetKillCooldown(ForceProtect: true);

        target.RpcResetAbilityCooldown();



        if (CanSeeCreatedSheriff.GetBool())

            NameColorManager.Add(Player.PlayerId, target.PlayerId, "#f5a623");



        Utils.AddGameLog(

            "VillageChief",

            string.Format(GetString("VillageChiefAppointLog"),

            Player.ColoredName(), target.ColoredName(), Utils.GetRoleName(previousRole))

        );

        SendAppointNotice(target);


        Utils.NotifyRoles();

    }



    // 任命を設定の送信先へチャットで知らせる (前の役職は書かない)
    private void SendAppointNotice(PlayerControl target)
    {
        var to = NotifyTargetOptions[NotifyTarget.GetValue()];
        if (to == "None") return;
        var text = "<color=#f5a623>" + string.Format(GetString("VillageChiefAppointNotice"), Player.ColoredName(), target.ColoredName()) + "</color>";
        if (to == "Everyone")
        {
            SendMessage(text);
            return;
        }
        if (to is "VillageChiefOnly" or "VillageChiefAndSheriff") SendMessage(text, Player.PlayerId);
        if (to is "SheriffOnly" or "VillageChiefAndSheriff") SendMessage(text, target.PlayerId);
    }

    private void ApplyModeDesync(bool toAppointMode)

    {

        if (!Player.IsAlive()) return;



        foreach (var pc in Main.AllAlivePlayerControls)

        {

            var role = pc.GetCustomRole();

            if (role.IsImpostor())

                pc.RpcSetRoleDesync(

                    toAppointMode ? RoleTypes.Scientist : role.GetRoleTypes(),

                    Player.GetClientId());

            if (Is(pc))

                pc.RpcSetRoleDesync(

                    toAppointMode ? RoleTypes.Impostor : RoleTypes.Engineer,

                    Player.GetClientId());

        }



        if (toAppointMode)

        {

            Player.SetKillCooldown(Mathf.Max(nowcool, 0.1f), ForceProtect: true);

        }

        else

        {

            Player.MarkDirtySettings();

            _ = new LateTask(() =>

            {

                if (!Player.IsAlive() || appointMode) return;

                Player.RpcResetAbilityCooldown(sync: true);

            }, 0.1f, "VillageChief.EngineerReset");

        }

    }



    public override void OnStartMeeting()

    {

        spawnWaitTimer = -1f;

        nearTimer = 0f;

    }



    public override void AfterMeetingTasks()

    {

        if (!Player.IsAlive()) return;

        _ = new LateTask(() =>

        {

            nowcool = AppointCooldown.GetFloat();

            LastCooltime = -1;

            spawnWaitTimer = 0f;

            // 暗転対策の身代わりを戻すまでは送らない (先に送ると、客の追放の締めの頭数が崩れて暗転しうる)
            AntiBlackout.RunAfterRevert(() =>
            {
                if (Player.GetRoleClass() == this) ApplyModeDesync(appointMode);
            });

        }, Main.LagTime, "Reset-VillageChief");

    }





    public override string GetProgressText(bool comms = false)

    {

        if (hasAppointed) return "<color=#f5a623>" + GetString("VillageChiefAppointedProgress") + "</color>";

        if (!GameStates.CalledMeeting)

            return ColorString(Color.yellow, " " + GetString(appointMode ? "VillageChiefModeAppoint" : "VillageChiefModeTask"));

        return "<color=#808080>" + GetString("VillageChiefNotAppointedProgress") + "</color>";

    }



    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,

        bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (!Is(seer) || seer.PlayerId != seen.PlayerId || isForMeeting || !Player.IsAlive()) return "";

        if (hasAppointed) return "";



        string prefix = isForHud ? "" : "<size=60%>";

        string c = "#f5a623";



        if (appointMode)

            return $"{prefix}<color={c}>{GetString("VillageChiefLowerAppointMode")}</color>";



        if (NextAppointCandidate != byte.MaxValue)

        {

            var cand = GetPlayerById(NextAppointCandidate);

            string name = cand != null ? cand.GetRealName() : "???";

            if (!CanApproach)

                return $"{prefix}<color={c}>{string.Format(GetString("VillageChiefLowerCandidateWaiting"), name)}</color>";

            if (nowcool > 0f)

                return $"{prefix}<color={c}>{string.Format(GetString("VillageChiefLowerCandidateCooldown"), name, Mathf.CeilToInt(nowcool))}</color>";

            return $"{prefix}<color={c}>{string.Format(GetString("VillageChiefLowerCandidateApproach"), name)}</color>";

        }



        return $"{prefix}<color={c}>{GetString("VillageChiefLowerIdle")}</color>";

    }



    public override bool CanTask() => true;




}

