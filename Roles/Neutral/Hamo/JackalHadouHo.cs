using System.Linq;
using AmongUs.GameOptions;

using Hazel;

using TownOfHostL.Modules;

using TownOfHostL.Core.Win;

using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Core.Interfaces;

using TownOfHostL.Patches;

using UnityEngine;

using static TownOfHostL.Main;

using static TownOfHostL.Translator;

using static TownOfHostL.Modules.SelfVoteManager;



namespace TownOfHostL.Roles.Neutral;



public sealed class JackalHadouHo : RoleBase, ILNKiller, IUsePhantomButton, ISelfVoter

{

    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(JackalHadouHo),
            player => new JackalHadouHo(player),
            CustomRoles.JackalHadouHo,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 52300,
            SetUpOptionItem,
            "jhh",
            "#00b4eb",
            isDesyncImpostor: true,
            countType: CountTypes.Jackal,
            assignInfo: new RoleAssignInfo(CustomRoles.JackalHadouHo, CustomRoleTypes.Neutral)

            {

                AssignCountRule = new(1, 1, 1),

            },
            flags: RoleFlags.NeutralKiller
        );



    public JackalHadouHo(PlayerControl player)

        : base(RoleInfo, player, () => HasTask.False)

    {

        KillCooldown = OptionKillCooldown.GetFloat();

        Cooldown = OptionCoolDown.GetFloat();

        ChargeTime = OptionChargeTime.GetFloat();

        SuperChargeTime = OptionSuperChargeTime.GetFloat();

        SelfDestructOnMiss = OptionSelfDestructOnMiss.GetBool();

        KillJackal = OptionKillJackal.GetBool();

        CanVent = OptionCanVent.GetBool();

        CanSabotage = OptionCanSabotage.GetBool();

        HasImpostorVision = OptionHasImpostorVision.GetBool();

        CanSideKick = OptionCanMakeSidekick.GetBool();



        IsCharging = false;

        IsSuperCharging = false;

        chargeTimer = 0f;

        superChargeTimer = 0f;


        ShowBeamMark = false;

        HasHit = false;

        IsDead = false;

        IsFiring = false;

        IsLoaded = false;

        IsSuperBeam = false;

        _prevCharging = false;

        _prevSuperCharging = false;

        _prevBeamMark = false;



        skMode = false;

        nowcool = KillCooldown;

        LastCooltime = (int)KillCooldown;



        skCandidateId = byte.MaxValue;

        skNearTimer = 0f;

        skCooldownTimer = 0f;

        skSpawnWaitTimer = -1f;



        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);

    }



    public bool IsCharging;

    public bool IsSuperCharging;

    float chargeTimer;

    float superChargeTimer;


    public bool ShowBeamMark;

    bool HasHit;

    bool BeamFacingLeft;

    bool IsDead;

    int PlayerColor;

    bool IsFiring = false;

    public bool CanSideKick;

    public bool IsLoaded;

    public bool IsSuperBeam;

    public static bool NextNoSideKick = false;

    bool _prevCharging;

    bool _prevSuperCharging;

    bool _prevBeamMark;
    HadouHoBeam beam;



    bool skMode;

    float nowcool;

    int LastCooltime;



    byte skCandidateId;

    float skNearTimer;

    float skCooldownTimer;

    float skSpawnWaitTimer;

    bool SkCanApproach => skSpawnWaitTimer >= 3f;



    static OptionItem OptionKillCooldown;

    static float KillCooldown;

    static OptionItem OptionCoolDown;

    static float Cooldown;

    static OptionItem OptionChargeTime;

    static float ChargeTime;

    static OptionItem OptionSuperChargeTime;

    static float SuperChargeTime;

    static OptionItem OptionSelfDestructOnMiss;

    static bool SelfDestructOnMiss;

    static OptionItem OptionKillJackal;
    static OptionItem OptionBeamWarning;

    static bool KillJackal;

    static OptionItem OptionCanVent;

    static bool CanVent;

    static OptionItem OptionCanSabotage;

    static bool CanSabotage;

    static OptionItem OptionHasImpostorVision;

    static bool HasImpostorVision;

    static OptionItem OptionCanMakeSidekick;

    static OptionItem OptionSidekickCooldown;

    static float SidekickCooldown;



    static OptionItem OptionTamaLoadCooldown;

    static OptionItem OptionTamaCanLoad;

    static OptionItem OptionTamaVentCooldown;

    static OptionItem OptionTamaVentMaxTime;

    static OptionItem OptionTamaCanVentMove;



    enum OptionName

    {

        JackalHadouHoChargeTime,

        JackalHadouHoSuperChargeTime,

        JackalHadouHoSelfDestruct,

        JackalHadouHoKillJackal,

        JackalHadouHoBeamWarning,

        JackalHadouHoSidekickCooldown,

        TamaOption,

        TamaCanLoad,

        TamaLoadCooldown,

        TamaCanVentMove

    }



    public static float GetTamaLoadCooldown() => OptionTamaLoadCooldown?.GetFloat() ?? 10f;

    public static bool GetTamaCanLoad() => OptionTamaCanLoad?.GetBool() ?? true;

    public static float GetTamaVentCooldown() => OptionTamaVentCooldown?.GetFloat() ?? 0f;

    public static float GetTamaVentMaxTime() => OptionTamaVentMaxTime?.GetFloat() ?? 0f;

    public static bool GetTamaCanVentMove() => OptionTamaCanVentMove?.GetBool() ?? false;

    public static bool GetCanUseSabotageOption() => OptionCanSabotage?.GetBool() ?? false;



    public static void HideRoleOptions(CustomRoles role)

    {

        if (Options.CustomRoleSpawnChances != null && Options.CustomRoleSpawnChances.TryGetValue(role, out var sp))

            sp.SetHidden(true);

        if (Options.CustomRoleCounts != null && Options.CustomRoleCounts.TryGetValue(role, out var cp))

            cp.SetHidden(true);

    }



    static void SetUpOptionItem()

    {

        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0, 180, 0.5f), 30f, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionChargeTime = FloatOptionItem.Create(RoleInfo, OptionName.JackalHadouHoChargeTime, new(0.5f, 10f, 0.5f), 3f, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionSuperChargeTime = FloatOptionItem.Create(RoleInfo, OptionName.JackalHadouHoSuperChargeTime, new(0.5f, 15f, 0.5f), 5f, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionSelfDestructOnMiss = BooleanOptionItem.Create(RoleInfo, OptionName.JackalHadouHoSelfDestruct, false, false);

        OptionKillJackal = BooleanOptionItem.Create(RoleInfo, OptionName.JackalHadouHoKillJackal, false, false);
        OptionBeamWarning = BooleanOptionItem.Create(RoleInfo, OptionName.JackalHadouHoBeamWarning, false, false);

        OptionCanVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, true, false);

        OptionCanSabotage = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanUseSabotage, false, false);

        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, GeneralOption.ImpostorVision, true, false);

        OptionCanMakeSidekick = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanCreateSideKick, true, false);

        OptionSidekickCooldown = FloatOptionItem.Create(RoleInfo, OptionName.JackalHadouHoSidekickCooldown, new(0f, 180f, 0.5f), 30f, false, OptionCanMakeSidekick)

            .SetValueFormat(OptionFormat.Seconds);



        ObjectOptionitem.Create(RoleInfo, OptionName.TamaOption, true, "")

            .SetOptionName(() => "TAMA OPTION");

        OptionTamaLoadCooldown = FloatOptionItem.Create(RoleInfo, "TamaLoadCooldown", new(0f, 60f, 0.5f), 10f, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionTamaCanLoad = BooleanOptionItem.Create(RoleInfo, "TamaCanLoad", true, false);

        OptionTamaVentCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0f, 180f, 0.5f), 0f, false)
            .SetKeyName("TamaVentCooldown")

            .SetValueFormat(OptionFormat.Seconds);

        OptionTamaVentMaxTime = FloatOptionItem.Create(RoleInfo, GeneralOption.EngineerInVentCooldown, new(0f, 180f, 0.5f), 0f, false)

            .SetZeroNotation(OptionZeroNotation.Infinity)

            .SetValueFormat(OptionFormat.Seconds);

        OptionTamaCanVentMove = BooleanOptionItem.Create(RoleInfo, "MadmateCanMovedByVent", false, false);



        RoleAddAddons.Create(RoleInfo, NeutralKiller: true);



        HideRoleOptions(CustomRoles.Tama);

    }



    public float CalculateKillCooldown() => KillCooldown;

    public bool CanUseSabotageButton() => CanSabotage;

    public bool CanUseImpostorVentButton() => CanVent;

    public override bool CanClickUseVentButton => CanVent;

    public override bool OnEnterVent(PlayerPhysics physics, int ventId)

    {

        if (IsCharging || IsSuperCharging || ShowBeamMark) return false;

        return CanVent;

    }

    public override bool CanVentMoving(PlayerPhysics physics, int ventId) => CanVent;



    public override void Add()

    {


        PlayerColor = Player.Data.DefaultOutfit.ColorId;

        CanSideKick = NextNoSideKick ? false : OptionCanMakeSidekick.GetBool();

        NextNoSideKick = false;

        SidekickCooldown = OptionSidekickCooldown.GetFloat();

        skMode = false;

        nowcool = KillCooldown;

        LastCooltime = (int)KillCooldown;



        skCandidateId = byte.MaxValue;

        skNearTimer = 0f;

        skCooldownTimer = 0f;

        skSpawnWaitTimer = -1f;



        PetActionManager.Register(Player.PlayerId, OnPetUsed);
        beam = new HadouHoBeam(Player, drawnByClientEffects: false);
    }



    public override void OnDestroy()

    {
        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(JackalHadouHo));

        beam?.End();

        PetActionManager.Unregister(Player.PlayerId);

        CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);



        if (IsCharging || IsSuperCharging || ShowBeamMark || IsFiring)

        {

            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(JackalHadouHo));

            Player.MarkDirtySettings();

            if (AmongUsClient.Instance.AmHost)

            {

                Player.SyncSettings();

                Player.RpcSetColor((byte)PlayerColor);

            }

            IsCharging = false;

            IsSuperCharging = false;

            ShowBeamMark = false;

            IsFiring = false;

            beam?.End();

        }

    }



    private void OnPetUsed()

    {

        if (!Player.IsAlive()) return;

        if (!CanSideKick) return;



        skMode = !skMode;



        if (!IsFiring)

            Player.SetKillCooldown(Mathf.Max(LastCooltime, 0.1f), ForceProtect: true);



        SendRpc();

        Utils.NotifyRoles(SpecifySeer: Player);

    }



    public override void ApplyGameOptions(IGameOptions opt)

    {

        opt.SetVision(HasImpostorVision);

        AURoleOptions.PhantomCooldown = Cooldown;

    }



    public override bool CanUseAbilityButton() => true;

    bool IUsePhantomButton.IsPhantomRole => true;

    bool IUsePhantomButton.IsresetAfterKill => true;



    bool ISelfVoter.CanUseVoted() => CanSideKick && Player.IsAlive();



    public override bool CheckAnyVote(byte votedForId, PlayerControl voter)

    {

        if (Madmate.MadAvenger.Skill) return true;

        if (Impostor.Assassin.NowUse) return true;

        if (!Is(voter) || !CanSideKick || !Player.IsAlive()) return true;



        if (CheckSelfVoteMode(Player, votedForId, out var status))

        {

            if (status is VoteStatus.Self)

            {

                skCandidateId = byte.MaxValue;

                Utils.SendMessage(

                    "<color=#00b4eb>【SK候補選択モード】</color>\n" +

                    "誰かに投票 → 次ターン1.5秒近づいてSK\n" +

                    "スキップ → キャンセル",

                    Player.PlayerId);

                SetMode(Player, true);

                return false;

            }

            if (status is VoteStatus.Skip)

            {

                skCandidateId = byte.MaxValue;

                Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);

                SetMode(Player, false);

                return false;

            }

            if (status is VoteStatus.Vote)

            {

                var target = Utils.GetPlayerById(votedForId);

                if (target == null || !target.IsAlive() || votedForId == Player.PlayerId)

                {

                    Utils.SendMessage("<color=#00b4eb>その相手はSKにできません。</color>", Player.PlayerId);

                    SetMode(Player, false);

                    return false;

                }

                var targetRole = target.GetCustomRole();

                if (targetRole is CustomRoles.King or CustomRoles.Autocrat or CustomRoles.Jackal or CustomRoles.JackalAlien

                    or CustomRoles.Jackaldoll or CustomRoles.JackalMafia or CustomRoles.JackalHadouHo

                    or CustomRoles.Merlin)

                {

                    Utils.SendMessage("<color=#00b4eb>その役職はSKにできません。</color>", Player.PlayerId);

                    SetMode(Player, false);

                    return false;

                }

                skCandidateId = votedForId;

                Utils.SendMessage(

                    $"<color=#00b4eb>【SK候補設定】</color>\n" +

                    $"{target.ColoredName()} を候補に設定しました。\n" +

                    $"次ターン、1.5秒近づいてSK実行！",

                    Player.PlayerId);

                SetMode(Player, false);

                return false;

            }

        }

        return true;

    }



    public void OnCheckMurderAsKiller(MurderInfo info)

    {

        if (info.IsSuicide) return;



        if (skMode && CanSideKick)

        {

            info.DoKill = false;

            (_, var target) = info.AttemptTuple;

            DoSideKick(target);



            skMode = false;

            nowcool = SidekickCooldown;

            LastCooltime = (int)nowcool;

            Player.SetKillCooldown(nowcool, ForceProtect: true);

            SendRpc();

            Utils.NotifyRoles(SpecifySeer: Player);

            return;

        }



        nowcool = KillCooldown;

        LastCooltime = (int)nowcool;

        AfterKillPhantomReset();

    }



    public void AfterKillPhantomReset()

    {

        _ = new LateTask(() =>

        {

            if (!Player.IsAlive()) return;

            if (PlayerControl.LocalPlayer != null && Is(PlayerControl.LocalPlayer))

                AURoleOptions.PhantomCooldown = Cooldown;

            Player.RpcResetAbilityCooldown(sync: true);

        }, 0.2f, "JHHPhantomResetOnKill");

    }






    public bool OverrideKillButtonText(out string text)

    {

        if (skMode && CanSideKick) { text = "弾SK"; return true; }

        text = ""; return false;

    }



    public void SetLoaded(bool loaded)

    {

        IsLoaded = loaded;

        SendRpc();

        if (loaded)

        {

            Utils.SendMessage(GetString("JackalHadouHoLoaded"), Player.PlayerId);

            Utils.NotifyRoles(SpecifySeer: Player);

        }

    }



    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)

    {

        AdjustKillCooldown = false;

        ResetCooldown = false;



        if (IsFiring || ShowBeamMark || !Player.IsAlive() || IsCharging || IsSuperCharging) return;



        // 背中側に壁がある時は、その場で壁へ向き直ってもホストに届かないので、動いて向きを決めてからためる
        if (beam?.Pending == true) return;
        if (beam != null && beam.WaitForDirection(StartCharge))
        {
            Utils.NotifyRoles(SpecifySeer: Player);
            return;
        }
        StartCharge();
    }

    void StartCharge()
    {
        if (!Player.IsAlive() || MeetingHud.Instance != null) return;
        IsFiring = true;



        if (IsLoaded)

        {

            IsSuperCharging = true;
            superChargeTimer = 0f;
            beam?.BeginCharge(OptionBeamWarning.GetBool() ? HadouHoBeam.SuperSize : 0);

            Utils.AllPlayerKillFlash();

            StartSuperChargeFlashLoop();

        }

        else

        {

            IsCharging = true;
            chargeTimer = 0f;
            beam?.BeginCharge(OptionBeamWarning.GetBool() ? HadouHoBeam.NormalSize : 0);
            Utils.AllPlayerKillFlash();

        }



        TownOfHostL.Modules.PlayerSpeed.Freeze(Player.PlayerId, nameof(JackalHadouHo));

        Player.MarkDirtySettings();

        Main.AllPlayerKillCooldown[Player.PlayerId] = 60f;

        Player.SetKillCooldown(60f, ForceProtect: true);

        _ = new LateTask(() => { if (Player != null) Player.SyncSettings(); }, 0.1f, "JackalHadouHoKillTimer");

        _prevCharging = IsCharging;

        _prevSuperCharging = IsSuperCharging;

        _prevBeamMark = ShowBeamMark;

        Utils.NotifyRoles(ForceLoop: true);

        SendRpc();

    }



    void StartSuperChargeFlashLoop()

    {

        int count = (int)(SuperChargeTime / 0.1f);

        for (int i = 1; i <= count; i++)

        {

            float t = i * 0.1f;

            _ = new LateTask(() =>

            {

                if (IsDead || !Player.IsAlive() || !IsSuperCharging) return;

                Utils.AllPlayerKillFlash();

            }, t, null);

        }

    }



    private void ResetAllState()

    {

        IsCharging = false;

        IsSuperCharging = false;

        ShowBeamMark = false;

        chargeTimer = 0f;

        superChargeTimer = 0f;

        HasHit = false;

        IsFiring = false;

        IsSuperBeam = false;

        _prevCharging = false;

        _prevSuperCharging = false;

        _prevBeamMark = false;

        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(JackalHadouHo));

        Player.MarkDirtySettings();

        Player.SyncSettings();

        Player.RpcSetColor((byte)PlayerColor);

        beam?.End();

    }



    public override void OnFixedUpdate(PlayerControl player)

    {

        if (player.AmOwner && Is(player))

            AURoleOptions.PhantomCooldown = Cooldown;



        if (GameStates.IsInTask && Player.IsAlive() && CanSideKick)

        {

            if (skSpawnWaitTimer >= 0f && skSpawnWaitTimer < 3f)

                skSpawnWaitTimer += Time.fixedDeltaTime;

            if (skCooldownTimer > 0f)

                skCooldownTimer -= Time.fixedDeltaTime;

        }



        if (IsCharging) chargeTimer += Time.fixedDeltaTime;

        if (IsSuperCharging) superChargeTimer += Time.fixedDeltaTime;



        if (!AmongUsClient.Instance.AmHost) return;



        if (GameStates.IsInTask && Player.IsAlive() && CanSideKick && skCandidateId != byte.MaxValue)

        {

            if (SkCanApproach && skCooldownTimer <= 0f)

            {

                var skTarget = Utils.GetPlayerById(skCandidateId);

                if (skTarget == null || !skTarget.IsAlive())

                {

                    skCandidateId = byte.MaxValue;

                    skNearTimer = 0f;

                    SendRpc();

                }

                else

                {

                    float dist = Vector2.Distance(Player.GetTruePosition(), skTarget.GetTruePosition());

                    if (dist <= 1.5f)

                    {

                        skNearTimer += Time.fixedDeltaTime;

                        Utils.NotifyRoles();

                        if (skNearTimer >= 1.5f)

                        {

                            DoSideKick(skTarget);

                            skCandidateId = byte.MaxValue;

                            skNearTimer = 0f;

                        }

                    }

                    else

                    {

                        skNearTimer = 0f;

                    }

                }

            }

        }



        if (!IsFiring && !IsCharging && !IsSuperCharging && !ShowBeamMark && Player.IsAlive())

        {

            if (nowcool > 0) nowcool -= Time.fixedDeltaTime;

            else nowcool = 0;



            var now = (int)nowcool;

            if (now != LastCooltime)

            {

                if (now <= 0) Player.SetKillCooldown(0.5f, ForceProtect: true);

                LastCooltime = now;

                if (player != PlayerControl.LocalPlayer)

                    Utils.NotifyRoles(SpecifySeer: player);

            }

        }



        if (MeetingHud.Instance != null)

        {

            if (IsCharging || IsSuperCharging || ShowBeamMark || IsFiring)

            {

                ResetAllState();

                Utils.NotifyRoles();

                SendRpc();

            }

            return;

        }



        if (!Player.IsAlive() && (IsCharging || IsSuperCharging || ShowBeamMark))

        {

            ResetAllState();

            Utils.NotifyRoles();

            SendRpc();

            return;

        }



        bool changed = (IsCharging != _prevCharging) || (IsSuperCharging != _prevSuperCharging) || (ShowBeamMark != _prevBeamMark);

        if (changed)

        {

            _prevCharging = IsCharging;

            _prevSuperCharging = IsSuperCharging;

            _prevBeamMark = ShowBeamMark;

            Utils.NotifyRoles(ForceLoop: true);

        }



        if (ShowBeamMark && Player.IsAlive()) ApplyBeamHit();

        if (!IsCharging && !IsSuperCharging && !ShowBeamMark) beam?.TrackDirection();
        beam?.Tick();
        if (IsCharging || IsSuperCharging)
            beam?.Charging(IsSuperCharging ? SuperChargeTime - superChargeTimer : ChargeTime - chargeTimer);
        // ★が客の画面に出るまで (会議明けの待ちなど) は撃たない。見えないビームで倒さないため
        bool beamReady = beam?.Ready ?? true;
        if (IsCharging && chargeTimer >= ChargeTime && beamReady) FireBeam();

        if (IsSuperCharging && superChargeTimer >= SuperChargeTime && beamReady) FireSuperBeam();

    }



    void FireBeam()
    {
        if (IsDead || !Player.IsAlive()) return;
        BeamFacingLeft = beam?.FacingLeft ?? Player.cosmetics.FlipX;
        SendBeamDirection(BeamFacingLeft);
        beam?.Fire("#00b4eb", HadouHoBeam.NormalSize);

        Utils.AllPlayerKillFlash();

        IsCharging = false; chargeTimer = 0f;

        HasHit = false; ShowBeamMark = true; IsSuperBeam = false;


        _prevCharging = false; _prevBeamMark = true;

        Utils.NotifyRoles(ForceLoop: true);

        SendRpc();

        ApplyBeamHit();

        _ = new LateTask(() => FinishBeam(), 3f, "JHHBeamEnd");

    }



    void FireSuperBeam()
    {
        if (IsDead || !Player.IsAlive()) return;
        BeamFacingLeft = beam?.FacingLeft ?? Player.cosmetics.FlipX;
        SendBeamDirection(BeamFacingLeft);
        beam?.Fire("#FF0000", HadouHoBeam.SuperSize);

        Utils.AllPlayerKillFlash();

        IsSuperCharging = false; superChargeTimer = 0f;

        HasHit = false; ShowBeamMark = true; IsLoaded = false; IsSuperBeam = true;



        foreach (var pc in Main.AllPlayerControls)

        {

            if (pc.GetRoleClass() is Tama tama && tama.OwnerId == Player.PlayerId && pc.IsAlive())

            {

                PlayerState.GetByPlayerId(pc.PlayerId).DeathReason = CustomDeathReason.etc;

                pc.RpcExileV3();

                PlayerState.GetByPlayerId(pc.PlayerId).SetDead();

                Utils.AddGameLog("JackalHadouHo", $"<color=#ff0000>【超波動砲】</color> 弾({pc.ColoredName()})を消費しました");

                break;

            }

        }




        _prevSuperCharging = false; _prevBeamMark = true;

        Utils.NotifyRoles(ForceLoop: true);

        SendRpc();

        ApplySuperBeamHit();

        _ = new LateTask(() => FinishBeam(), 3f, "JHHSuperBeamEnd");

    }



    void FinishBeam()

    {

        if (IsDead || !Player.IsAlive())

        {

            ShowBeamMark = false; _prevBeamMark = false; IsSuperBeam = false;

            beam?.End(); IsFiring = false;

            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(JackalHadouHo));

            Player.MarkDirtySettings(); Player.RpcSetColor((byte)PlayerColor);

            Utils.NotifyRoles(); SendRpc(); return;

        }



        ShowBeamMark = false; _prevBeamMark = false; IsSuperBeam = false;

        beam?.End();

        Utils.NotifyRoles(ForceLoop: true);

        SendRpc();



        if (!HasHit && SelfDestructOnMiss)

        {

            Player.RpcSetColor((byte)PlayerColor);

            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(JackalHadouHo));

            Player.MarkDirtySettings();

            PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Suicide;

            Player.RpcMurderPlayerV2(Player);

            IsFiring = false; return;

        }



        Player.RpcSetColor((byte)PlayerColor);

        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(JackalHadouHo));

        Player.MarkDirtySettings();



        _ = new LateTask(() =>

        {

            if (!Player.IsAlive()) { IsFiring = false; return; }



            nowcool = KillCooldown;

            LastCooltime = (int)nowcool;

            Main.AllPlayerKillCooldown[Player.PlayerId] = nowcool;

            Player.SetKillCooldown(nowcool, ForceProtect: true);



            if (PlayerControl.LocalPlayer != null && Is(PlayerControl.LocalPlayer))

                AURoleOptions.PhantomCooldown = Cooldown;

            Player.RpcResetAbilityCooldown(sync: true);

            Utils.NotifyRoles();

            _ = new LateTask(() => { IsFiring = false; }, 0.3f, "JHHResetFiring", true);

        }, 0.2f, "JHHResetKillCool");

    }



    void ApplyBeamHit()

    {

        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;

        var myPos = Player.GetTruePosition();

        Vector2 dir = BeamFacingLeft ? Vector2.left : Vector2.right;

        foreach (var target in Main.AllAlivePlayerControls)

        {

            if (target.PlayerId == Player.PlayerId) continue;

            if (!KillJackal && target.GetCustomRole() is CustomRoles.Jackal or CustomRoles.JackalMafia

                or CustomRoles.JackalAlien or CustomRoles.Jackaldoll or CustomRoles.JackalHadouHo

                or CustomRoles.Tama) continue;

            var toTarget = (Vector2)target.GetTruePosition() - myPos;

            float dot = Vector2.Dot(toTarget, dir);

            if (dot <= 0) continue;

            if ((toTarget - dir * dot).magnitude > 1.3f) continue;

            CustomRoleManager.OnCheckMurder(Player, target, target, target, true, deathReason: CustomDeathReason.Evaporation);

            HasHit = true;

            Utils.AddGameLog("JackalHadouHo", $"<color=#00b4eb>【波動砲】</color> {Player.ColoredName()} ═> {target.ColoredName()}");

        }

    }



    void ApplySuperBeamHit()

    {

        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;

        var myPos = Player.GetTruePosition();

        Vector2 dir = BeamFacingLeft ? Vector2.left : Vector2.right;

        foreach (var target in Main.AllAlivePlayerControls)

        {

            if (target.PlayerId == Player.PlayerId) continue;

            if (!KillJackal && target.GetCustomRole() is CustomRoles.Jackal or CustomRoles.JackalMafia

                or CustomRoles.JackalAlien or CustomRoles.Jackaldoll or CustomRoles.JackalHadouHo

                or CustomRoles.Tama) continue;

            var toTarget = (Vector2)target.GetTruePosition() - myPos;

            float dot = Vector2.Dot(toTarget, dir);

            if (dot <= 0) continue;

            if ((toTarget - dir * dot).magnitude > 4.0f) continue;

            CustomRoleManager.OnCheckMurder(Player, target, target, target, true, deathReason: CustomDeathReason.Evaporation);

            HasHit = true;

            Utils.AddGameLog("JackalHadouHo", $"<color=#ff0000>【超波動砲】</color> {Player.ColoredName()} ═> {target.ColoredName()}");

        }

    }



    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)

    {

        ResetAllState(); Utils.NotifyRoles(ForceLoop: true); SendRpc();

    }



    public override void OnStartMeeting()

    {

        ResetAllState();

        if (skMode) { skMode = false; SendRpc(); }

        skCandidateId = byte.MaxValue;

        skNearTimer = 0f;

        skSpawnWaitTimer = -1f;

    }



    public override void AfterMeetingTasks()

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (!Player.IsAlive()) return;



        if (PlayerControl.LocalPlayer != null && Is(PlayerControl.LocalPlayer))

        {

            AURoleOptions.PhantomCooldown = Cooldown;

            Player.RpcResetAbilityCooldown();

        }



        nowcool = KillCooldown;

        LastCooltime = (int)nowcool;

        Main.AllPlayerKillCooldown[Player.PlayerId] = nowcool;

        Player.SetKillCooldown(nowcool, ForceProtect: true);



        skSpawnWaitTimer = 0f;

        skCooldownTimer = SidekickCooldown;

        skNearTimer = 0f;

    }



    private void DoSideKick(PlayerControl target)

    {

        CanSideKick = false;

        var targetRole = target.GetCustomRole();

        if (targetRole is CustomRoles.King or CustomRoles.Autocrat or CustomRoles.Jackal or CustomRoles.JackalAlien

            or CustomRoles.Jackaldoll or CustomRoles.JackalMafia or CustomRoles.JackalHadouHo

            or CustomRoles.Merlin)

        {

            Utils.SendMessage("<color=#00b4eb>この役職はSKにできません。</color>", Player.PlayerId);

            SendRpc(); return;

        }



        Player.RpcProtectedMurderPlayer(target);

        target.RpcProtectedMurderPlayer(Player);

        target.RpcProtectedMurderPlayer(target);

        target.RpcChangeRole(CustomRoles.Tama);

        if (target.GetRoleClass() is Tama tama) tama.SetOwner(Player.PlayerId);




        Utils.AddGameLog("JackalHadouHoSideKick",

            string.Format(GetString("log.Sidekick"),

            target.ColoredName() + $"({RoleText.GetRoleNameText(target.PlayerId)})",

            Player.ColoredName()));



        Utils.MarkEveryoneDirtySettings();



        if (PlayerControl.LocalPlayer != null && Is(PlayerControl.LocalPlayer))

        {

            AURoleOptions.PhantomCooldown = Cooldown;

            Player.RpcResetAbilityCooldown();

        }



        SendRpc(); Utils.NotifyRoles();

    }



    public void SendRpc()

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.Write(IsCharging);

        sender.Writer.Write(IsSuperCharging);

        sender.Writer.Write(ShowBeamMark);

        sender.Writer.Write(CanSideKick);

        sender.Writer.Write(IsLoaded);

        sender.Writer.Write(IsSuperBeam);

        sender.Writer.Write(skMode);

        sender.Writer.Write(skCandidateId);

    }



    void SendBeamDirection(bool left)

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.Write((byte)1);

        sender.Writer.Write(left);

    }



    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)

    {
        if (rpcType != CustomRPC.CustomRoleSync) return;

        if (reader.Length - reader.Position == 2)

        {

            reader.ReadByte(); BeamFacingLeft = reader.ReadBoolean(); return;

        }

        IsCharging = reader.ReadBoolean();

        IsSuperCharging = reader.ReadBoolean();

        ShowBeamMark = reader.ReadBoolean();

        CanSideKick = reader.ReadBoolean();

        IsLoaded = reader.ReadBoolean();

        IsSuperBeam = reader.ReadBoolean();

        skMode = reader.ReadBoolean();

        if (reader.BytesRemaining > 0)

            skCandidateId = reader.ReadByte();

    }



    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";

        string size = isForHud ? "" : "<size=60%>";



        if (skMode && CanSideKick)

            return $"{size}<color=#00b4eb>【SKモード】SKしたいプレイヤーにキル！ペット→キルモードへ</color>";



        if (CanSideKick && skCandidateId != byte.MaxValue && !IsCharging && !IsSuperCharging && !ShowBeamMark)

        {

            var skTarget = Utils.GetPlayerById(skCandidateId);

            string tName = skTarget != null ? skTarget.GetRealName() : "???";

            if (!SkCanApproach)

                return $"{size}<color=#00b4eb>待機中... | 候補: {tName}</color>";

            if (skCooldownTimer > 0f)

                return $"{size}<color=#00b4eb>SK CD: {Mathf.CeilToInt(skCooldownTimer)}s | 候補: {tName}</color>";

            float progress = Mathf.Min(skNearTimer, 1.5f);

            return $"{size}<color=#00b4eb>{tName}に近づき中 {progress:F1}/1.5s</color>";

        }



        if (CanSideKick && !IsCharging && !IsSuperCharging && !ShowBeamMark)

            return $"{size}<color=#ff0000>ファントム→ビーム / ペット→SKモード / 会議自投票→SK候補指定</color>";



        if (IsLoaded) return $"{size}<color=#00b4eb>【装填済】ファントムボタン → 超波動砲チャージ！</color>";

        if (beam?.Pending == true) return $"{size}<color=#ff0000>向きを決めています。撃ちたい向きへ動いてください</color>";
        if (!IsCharging && !IsSuperCharging) return $"{size}<color=#ff0000>ファントムボタン → チャージ発射</color>";

        if (IsSuperCharging)

        {

            var r = Mathf.Max(0f, SuperChargeTime - superChargeTimer);

            return $"{(isForHud ? "" : "<size=60%>")}<color=#ff0000>超チャージ中... {r:F1}s</color>";

        }

        var rem = Mathf.Max(0f, ChargeTime - chargeTimer);

        return $"{(isForHud ? "" : "<size=60%>")}<color=#ff0000>チャージ中... {rem:F1}s</color>";

    }



    public string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (seen != seer || isForMeeting || !Player.IsAlive()) return "";

        if (IsSuperCharging && seer.PlayerId != Player.PlayerId)

            return $"<color=#ff0000>超チャージ中... {(int)Mathf.Max(0f, SuperChargeTime - superChargeTimer)}s</color>";

        if (IsCharging && seer.PlayerId != Player.PlayerId)

            return $"<color=#ff0000>チャージ中... {(int)Mathf.Max(0f, ChargeTime - chargeTimer)}s</color>";

        if (ShowBeamMark && seer.PlayerId != Player.PlayerId)

            return "\n<color=#ff0000>ビーム中</color>";

        return "";

    }



    public override string GetProgressText(bool comms = false)

    {

        if (!CanSideKick && !skMode) return "";

        return skMode

            ? " <color=#ff6600>[SK]</color>"

            : " <color=#00b4eb>[キル]</color>";

    }



    public override string GetAbilityButtonText() => IsLoaded ? "超発射" : "発射";


}



public sealed class Tama : RoleBase, IKiller

{
    public override bool NeedsSeenNames => true;

    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(Tama),
            player => new Tama(player),
            CustomRoles.Tama,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 54900,
            SetupOptionItem,
            "tm",
            "#00b4eb",
            isDesyncImpostor: true,
            countType: CountTypes.Crew
        );



    private static void SetupOptionItem()

    {

        JackalHadouHo.HideRoleOptions(CustomRoles.Tama);

    }

    /// <summary>ジャッカルの組が全滅していても、ジャッカル系の役職が配られていてたまが生きていれば、ジャッカル側に 1 人数える (何人いても 1 人)</summary>
    private static void AdjustGameEndCounts(GameEndCounts counts)
    {
        if (counts[CountTypes.Jackal] != 0) return;
        if (!Main.AllPlayerControls.Any(p => p.Is(CustomRoles.Jackal) || p.Is(CustomRoles.JackalHadouHo) || p.Is(CustomRoles.JackalWolf))) return;
        if (!Main.AllAlivePlayerControls.Any(p => p.Is(CustomRoles.Tama))) return;
        counts[CountTypes.Jackal]++;
        counts[CountTypes.Crew]--;
    }



    public Tama(PlayerControl player)

        : base(RoleInfo, player, () => HasTask.False)

    {
        // 仲間 (ジャッカルの陣営) の画面にたまの役職名を出す。見る側の役職は他人の名前を組み直さないことがある
        CustomRoleManager.EveryoneNeedsSeenNamesOthers.Add(() => true);
        CustomRoleManager.GameEndCountAdjustersOthers.Add(AdjustGameEndCounts);

        OwnerId = byte.MaxValue;

        hasLoaded = false;

        isLoading = false;

    }



    public byte OwnerId;

    public bool hasLoaded;

    bool isLoading;

    int snapFrame = 0;



    public void SetOwner(byte ownerId)

    {

        OwnerId = ownerId;

        SendRPC();

    }



    public override void ApplyGameOptions(IGameOptions opt)

    {

        opt.SetVision(true);

        // 設定を送るたびに呼ばれるので、生きていて、まだエンジニアにしていない時だけ送る
        if (!JackalHadouHo.GetTamaCanLoad() && Player.IsAlive()
            && !(Modules.RoleView.TryGet(Player, Player, out var tamaRole) && tamaRole == RoleTypes.Engineer))

            Player.RpcSetRoleDesync(RoleTypes.Engineer, Player.GetClientId());

        AURoleOptions.EngineerCooldown = JackalHadouHo.GetTamaVentCooldown();

        AURoleOptions.EngineerInVentMaxTime = JackalHadouHo.GetTamaVentMaxTime();

    }



    public float CalculateKillCooldown() => JackalHadouHo.GetTamaLoadCooldown();



    public bool CanUseKillButton()

    {

        if (!JackalHadouHo.GetTamaCanLoad()) return false;

        return Player.IsAlive() && !hasLoaded && !isLoading && IsOwnerAlive();

    }



    public bool CanUseSabotageButton() => false;

    public bool CanUseImpostorVentButton() => true;

    public override bool CanVentMoving(PlayerPhysics physics, int ventId) => JackalHadouHo.GetTamaCanVentMove();



    private bool IsOwnerAlive()

    {

        if (OwnerId == byte.MaxValue) return false;

        var owner = Utils.GetPlayerById(OwnerId);

        return owner != null && owner.IsAlive();

    }



    public void OnCheckMurderAsKiller(MurderInfo info)

    {

        info.DoKill = false;

        if (!JackalHadouHo.GetTamaCanLoad()) return;



        var (killer, target) = info.AttemptTuple;

        if (hasLoaded || isLoading) return;

        if (target.PlayerId != OwnerId) return;



        isLoading = true;

        hasLoaded = true;



        var owner = Utils.GetPlayerById(OwnerId);

        if (owner?.GetRoleClass() is JackalHadouHo jhh)

            jhh.SetLoaded(true);



        SendRPC();

        Utils.NotifyRoles(SpecifySeer: Player);

        Utils.SendMessage(GetString("TamaLoaded"), Player.PlayerId);

    }



    public override string GetProgressText(bool comms = false)

    {

        if (!JackalHadouHo.GetTamaCanLoad()) return "<color=#5e5e5e>【装填不可】</color>";

        if (hasLoaded) return $"<color=#00b4eb>【装填済】</color>";

        return $"<color=#5e5e5e>【未装填】</color>";

    }



    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (!Is(seer) || seer.PlayerId != seen.PlayerId || isForMeeting || !Player.IsAlive()) return "";



        if (!JackalHadouHo.GetTamaCanLoad())

            return $"{(isForHud ? "" : "<size=60%>")}<color=#5e5e5e>装填機能は無効化されています</color>";

        if (hasLoaded)

            return $"{(isForHud ? "" : "<size=60%>")}<color=#00b4eb>装填済み！波動砲ジャッカルが超波動砲を撃てる</color>";

        if (!IsOwnerAlive())

            return $"{(isForHud ? "" : "<size=60%>")}<color=#5e5e5e>波動砲ジャッカルが死亡しています</color>";

        return $"{(isForHud ? "" : "<size=60%>")}<color=#00b4eb>波動砲ジャッカルにキルボタンで装填</color>";

    }



    public override void OnFixedUpdate(PlayerControl player)

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (!GameStates.IsInTask) return;

        if (OwnerId == byte.MaxValue) return;



        var owner = Utils.GetPlayerById(OwnerId);



        if (!player.IsAlive() && hasLoaded)

        {

            hasLoaded = false;

            isLoading = false;

            if (owner?.GetRoleClass() is JackalHadouHo jhh)

                jhh.SetLoaded(false);

            SendRPC();

            return;

        }



        if (player.IsAlive() && (owner == null || !owner.IsAlive() || owner.GetCustomRole() != CustomRoles.JackalHadouHo))

        {

            OwnerId = byte.MaxValue;

            MyState.SetCountType(CountTypes.Jackal);


            JackalHadouHo.NextNoSideKick = true;

            Player.RpcChangeRole(CustomRoles.JackalHadouHo);

            SendRPC();

            Utils.NotifyRoles();

            return;

        }



        if (!hasLoaded) return;

        if (owner == null || !owner.IsAlive() || !player.IsAlive()) return;



        snapFrame++;

        if (snapFrame % 3 == 0)

        {

            var targetPos = owner.transform.position;

            player.NetTransform.SnapTo(targetPos);



            ushort sid = (ushort)(player.NetTransform.lastSequenceId + 2U);

            var writer = AmongUsClient.Instance.StartRpcImmediately(

                player.NetTransform.NetId, (byte)RpcCalls.SnapTo, SendOption.None);

            NetHelpers.WriteVector2(targetPos, writer);

            writer.Write(sid);

            AmongUsClient.Instance.FinishRpcImmediately(writer);

        }

    }



    public override void OnStartMeeting()

    {

        if (!AmongUsClient.Instance.AmHost) return;

        if (hasLoaded || isLoading)

        {

            hasLoaded = false;

            isLoading = false;

            var owner = Utils.GetPlayerById(OwnerId);

            if (owner?.GetRoleClass() is JackalHadouHo jhh)

                jhh.SetLoaded(false);

            SendRPC();

            Utils.NotifyRoles();

        }

    }



    public override bool KnowsRoleOf(PlayerControl seen, bool isMeeting) => seen.Is(CountTypes.Jackal);



    public override bool ShowsRoleTo(PlayerControl seer, bool isMeeting) => seer.Is(CountTypes.Jackal);



    public void SendRPC()

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.Write(OwnerId);

        sender.Writer.Write(hasLoaded);

    }



    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)

    {
        if (rpcType != CustomRPC.CustomRoleSync) return;

        OwnerId = reader.ReadByte();

        hasLoaded = reader.ReadBoolean();

    }



    public bool OverrideKillButtonText(out string text)

    {

        text = GetString("TamaLoadButtonText");

        return true;

    }




}

