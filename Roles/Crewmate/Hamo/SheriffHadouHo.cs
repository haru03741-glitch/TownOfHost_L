using AmongUs.GameOptions;
using Hazel;
using TownOfHostL.Modules;
using TownOfHostL.Patches;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHostL.Roles.Crewmate;

public sealed class SheriffHadouHo : RoleBase, IUsePhantomButton
{
    // 役職の元: TownOfHost_Pko (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(SheriffHadouHo),
            player => new SheriffHadouHo(player),
            CustomRoles.SheriffHadouHo,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 260200,
            SetupOptionItem,
            "shh",
            "#f8cd46",
            isDesyncImpostor: true,
            countType: CountTypes.Crew
        );

    public SheriffHadouHo(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.True)
    {
        Cooldown = OptionCooldown.GetFloat();
        ChargeTime = OptionChargeTime.GetFloat();
        ShotLimit = OptionShotLimit.GetInt();
        SelfDestructOnMiss = OptionSelfDestructOnMiss.GetBool();
        BeamColorModeValue = OptionBeamColorMode.GetValue();
        IsCharging = false;
        chargeTimer = 0f;
        ShowBeamMark = false;
        HasHit = false;
        IsFiring = false;
        _prevCharging = false;
        _prevBeamMark = false;
        BeamFacingLeft = false;
        beamMode = false;
        nowcool = Cooldown;
        LastCooltime = (int)Cooldown;
    }

    static OptionItem OptionCooldown;
    static float Cooldown;
    static OptionItem OptionChargeTime;
    static float ChargeTime;
    static OptionItem OptionShotLimit;
    static OptionItem OptionSelfDestructOnMiss;
    static OptionItem OptionBeamWarning;
    static bool SelfDestructOnMiss;
    static OptionItem OptionBeamColorMode;
    static int BeamColorModeValue;
    static OptionItem OptionBeamUnlockTask;
    static int BeamUnlockTaskCount;

    int ShotLimit;
    public bool IsCharging;
    float chargeTimer;
    public bool ShowBeamMark;
    bool HasHit;
    bool IsFiring;
    bool spawnCooldownStarted;
    bool _prevCharging;
    bool _prevBeamMark;
    HadouHoBeam beam;
    bool BeamFacingLeft;
    bool beamMode;
    float nowcool;
    int LastCooltime;

    bool IsBeamUnlocked =>
        BeamUnlockTaskCount <= 0 ||
        (MyTaskState != null && MyTaskState.CompletedTasksCount >= BeamUnlockTaskCount);

    enum BeamColorMode { Cyan, Yellow }

    enum OptionName
    {
        SheriffHadouHoChargeTime,
        SheriffHadouHoShotLimit,
        SheriffHadouHoBeamColorMode,
        SheriffHadouHoBeamUnlockTask,
        SheriffHadouHoSelfDestruct,
        SheriffHadouHoBeamWarning,
    }

    static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown,
            new(0f, 60f, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionChargeTime = FloatOptionItem.Create(RoleInfo, OptionName.SheriffHadouHoChargeTime,
            new(0.5f, 10f, 0.5f), 3f, false).SetValueFormat(OptionFormat.Seconds);
        OptionShotLimit = IntegerOptionItem.Create(RoleInfo, OptionName.SheriffHadouHoShotLimit,
            new(1, 15, 1), 3, false).SetValueFormat(OptionFormat.Times);
        OptionSelfDestructOnMiss = BooleanOptionItem.Create(RoleInfo,
            OptionName.SheriffHadouHoSelfDestruct, true, false);
        OptionBeamWarning = BooleanOptionItem.Create(RoleInfo,
            OptionName.SheriffHadouHoBeamWarning, false, false);
        OptionBeamColorMode = StringOptionItem.Create(RoleInfo,
            OptionName.SheriffHadouHoBeamColorMode,
            new string[] { "Cyan", "Yellow" }, 1, false);
        OptionBeamUnlockTask = IntegerOptionItem.Create(RoleInfo,
            OptionName.SheriffHadouHoBeamUnlockTask,
            new(0, 10, 1), 0, false).SetValueFormat(OptionFormat.Times);
    }

    public override void Add()
    {
        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
        BeamColorModeValue = OptionBeamColorMode.GetValue();
        BeamUnlockTaskCount = OptionBeamUnlockTask.GetInt();
        SelfDestructOnMiss = OptionSelfDestructOnMiss.GetBool();
        spawnCooldownStarted = false;
        beamMode = false;
        nowcool = Cooldown;
        LastCooltime = (int)Cooldown;
        PetActionManager.Register(Player.PlayerId, OnPetUsed);
        beam = new HadouHoBeam(Player, drawnByClientEffects: false);
    }

    public override void OnDestroy()
    {
        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(SheriffHadouHo));
        beam?.End();
        PetActionManager.Unregister(Player.PlayerId);
        CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        float cd = Mathf.Max(nowcool, 0.1f);
        if (!beamMode)
        {
            AURoleOptions.EngineerCooldown = cd;
            AURoleOptions.EngineerInVentMaxTime = 0f;
        }
        else
        {
            AURoleOptions.PhantomCooldown = cd;
        }
    }

    bool IUsePhantomButton.SyncAbilityCooldownWithKillCooldown => false;
    public override bool CanClickUseVentButton => !beamMode;

    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (IsCharging || ShowBeamMark) return false;
        return !beamMode;
    }

    bool IUsePhantomButton.IsPhantomRole => beamMode && !IsFiring;
    bool IUsePhantomButton.IsresetAfterKill => false;

    public override bool CanUseAbilityButton()
        => beamMode && !IsFiring && ShotLimit > 0 && nowcool <= 0f && IsBeamUnlocked;

    public override RoleTypes? AfterMeetingRole
        => (beamMode && ShotLimit > 0 && IsBeamUnlocked) ? RoleTypes.Phantom : RoleTypes.Engineer;

    void OnPetUsed()
    {
        if (!Player.IsAlive()) return;
        if (ShotLimit <= 0) return;
        if (IsFiring || IsCharging || ShowBeamMark) return;
        if (!beamMode && !IsBeamUnlocked) return;

        beamMode = !beamMode;
        ApplyModeDesync(beamMode);
        SendRpc();
        Utils.NotifyRoles(SpecifySeer: Player);
    }

    void ApplyModeDesync(bool toBeamMode)
    {
        if (!Player.IsAlive()) return;
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            var role = pc.GetCustomRole();
            if (role.IsImpostor())
                pc.RpcSetRoleDesync(
                    toBeamMode ? RoleTypes.Scientist : role.GetRoleTypes(),
                    Player.GetClientId());
            if (Is(pc))
                pc.RpcSetRoleDesync(
                    toBeamMode ? RoleTypes.Phantom : RoleTypes.Engineer,
                    Player.GetClientId());
        }
        _ = new LateTask(() =>
        {
            if (Player.IsAlive()) Player.RpcResetAbilityCooldown(sync: true);
        }, 0.1f, "SHH.ModeDesync");
    }

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;
        if (IsFiring || ShowBeamMark || !Player.IsAlive() || IsCharging || !beamMode) return;
        if (ShotLimit <= 0) return;
        if (nowcool > 0f) return;
        if (!IsBeamUnlocked) return;

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
        IsCharging = true;
        chargeTimer = 0f;
        TownOfHostL.Modules.PlayerSpeed.Freeze(Player.PlayerId, nameof(SheriffHadouHo));
        Player.MarkDirtySettings();
        Utils.AllPlayerKillFlash();
        Player.SyncSettings();
        beam?.BeginCharge(OptionBeamWarning.GetBool() ? HadouHoBeam.NormalSize : 0);
        _prevCharging = true;
        _prevBeamMark = false;
        Utils.NotifyRoles(ForceLoop: true);
        SendRpc();
    }

    /// <summary>撃つたび・状態を戻すたびに進める。会議で戻した後に、前の発射の後処理 (当たらなかった時の自爆など) を走らせない</summary>
    int fireGeneration;

    void ResetBeamState()
    {
        // 撃っている途中の後処理 (LateTask) を捨てる
        fireGeneration++;
        IsCharging = false;
        ShowBeamMark = false;
        IsFiring = false;
        HasHit = false;
        chargeTimer = 0f;
        _prevCharging = false;
        _prevBeamMark = false;
        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(SheriffHadouHo));
        Player.MarkDirtySettings();
        beam?.End();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        if (!spawnCooldownStarted && Player.IsAlive()
            && !GameStates.Intro && GameStates.IsInTask && !GameStates.IsMeeting)
        {
            spawnCooldownStarted = true;
            Player.RpcResetAbilityCooldown(sync: true);
        }

        if (MeetingHud.Instance != null)
        {
            if (IsCharging || ShowBeamMark || IsFiring)
            { ResetBeamState(); Utils.NotifyRoles(); SendRpc(); }
            return;
        }

        if (!Player.IsAlive() && (IsCharging || ShowBeamMark))
        {
            ResetBeamState();
            Utils.NotifyRoles(); SendRpc();
            return;
        }

        if (!IsFiring && Player.IsAlive() && GameStates.IsInTask)
        {
            if (nowcool > 0) nowcool -= Time.fixedDeltaTime;
            else nowcool = 0;
            var now = (int)nowcool;
            if (now != LastCooltime)
            {
                LastCooltime = now;
                Player.MarkDirtySettings();
                _ = new LateTask(() =>
                {
                    if (Player.IsAlive()) Player.RpcResetAbilityCooldown(sync: true);
                }, 0.1f, "SHH.CDSync");
                if (player != PlayerControl.LocalPlayer)
                    Utils.NotifyRoles(SpecifySeer: player);
            }
        }

        bool changed = (IsCharging != _prevCharging) || (ShowBeamMark != _prevBeamMark);
        if (changed)
        {
            _prevCharging = IsCharging;
            _prevBeamMark = ShowBeamMark;
            Utils.NotifyRoles(ForceLoop: true);
        }

        if (ShowBeamMark && Player.IsAlive()) ApplyBeamHit();
        if (!IsCharging && !ShowBeamMark) beam?.TrackDirection();
        beam?.Tick();
        if (IsCharging)
        {
            chargeTimer += Time.fixedDeltaTime;
            beam?.Charging(ChargeTime - chargeTimer);
            // ★が客の画面に出るまで (会議明けの待ちなど) は撃たない。見えないビームで倒さないため
            if (chargeTimer >= ChargeTime && (beam?.Ready ?? true)) FireBeam();
        }
    }

    void FireBeam()
    {
        if (!Player.IsAlive()) return;

        BeamFacingLeft = beam?.FacingLeft ?? Player.cosmetics.FlipX;
        SendBeamDirection(BeamFacingLeft);
        beam?.Fire(BeamColor, HadouHoBeam.NormalSize);
        Utils.AllPlayerKillFlash();

        IsCharging = false;
        chargeTimer = 0f;
        HasHit = false;
        ShowBeamMark = true;
        _prevCharging = false;
        _prevBeamMark = true;
        ShotLimit--;
        Utils.NotifyRoles(ForceLoop: true);
        SendRpc();
        ApplyBeamHit();

        var generation = ++fireGeneration;
        _ = new LateTask(() =>
        {
            if (generation != fireGeneration) return;
            if (!Player.IsAlive())
            {
                ShowBeamMark = false; _prevBeamMark = false;
                beam?.End(); IsFiring = false;
                TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(SheriffHadouHo));
                Player.MarkDirtySettings();
                Utils.NotifyRoles(); SendRpc(); return;
            }

            ShowBeamMark = false; _prevBeamMark = false;
            beam?.End();
            Utils.NotifyRoles(ForceLoop: true);
            SendRpc();

            if (!HasHit && SelfDestructOnMiss)
            {
                TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(SheriffHadouHo));
                Player.MarkDirtySettings();
                PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Suicide;
                Player.RpcMurderPlayerV2(Player);
                IsFiring = false;
                Utils.AddGameLog("SheriffHadouHo",
                    $"{Player.ColoredName()} はビームで誰も倒せず自爆した");
                return;
            }

            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(SheriffHadouHo));
            Player.MarkDirtySettings();
            nowcool = Cooldown;
            LastCooltime = (int)Cooldown;
            _ = new LateTask(() =>
            {
                if (!Player.IsAlive()) { IsFiring = false; return; }
                Player.MarkDirtySettings();
                Player.RpcResetAbilityCooldown(sync: true);
                Utils.NotifyRoles();
                _ = new LateTask(() => { IsFiring = false; }, 0.3f, "SHHResetFiring", true);
            }, 0.2f, "SHHResetCooldown", true);
        }, 3f, "SHHBeamEnd");
    }

    void ApplyBeamHit()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;

        bool facingLeft = BeamFacingLeft;
        var myPos = Player.GetTruePosition();
        Vector2 dir = facingLeft ? Vector2.left : Vector2.right;

        foreach (var target in Main.AllAlivePlayerControls)
        {
            if (target.PlayerId == Player.PlayerId) continue;
            var toTarget = target.GetTruePosition() - myPos;
            float dot = Vector2.Dot(toTarget, dir);
            if (dot <= 0) continue;
            var proj = dir * dot;
            var perp = toTarget - proj;
            if (perp.magnitude > 1.3f) continue;

            CustomRoleManager.OnCheckMurder(Player, target, target, target,
                true, deathReason: CustomDeathReason.Hit);
            HasHit = true;
        }
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        ResetBeamState();
        Utils.NotifyRoles(ForceLoop: true);
        SendRpc();
    }

    public override void OnStartMeeting()
    {
        ResetBeamState();
        if (beamMode) { beamMode = false; SendRpc(); }
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        nowcool = Cooldown;
        LastCooltime = (int)Cooldown;
        ApplyModeDesync(beamMode);
    }

    string BeamColor => (BeamColorMode)BeamColorModeValue == BeamColorMode.Yellow ? "#f8cd46" : "#00CFFF";

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";

        string size = isForHud ? "" : "<size=60%>";

        if (ShotLimit <= 0)
            return $"{size}<color=#888888>弾切れ</color>";

        if (!IsBeamUnlocked)
        {
            int done = MyTaskState?.CompletedTasksCount ?? 0;
            int remaining = BeamUnlockTaskCount - done;
            return $"{size}<color=#888888>波動砲ロック中 (あと {remaining} タスク)</color>";
        }

        if (!beamMode)
        {
            string cdText = LastCooltime > 0
                ? $" (CD: {LastCooltime}s)"
                : " <color=#00ff88>(準備完了)</color>";
            return $"{size}<color=#f8cd46>ペット → 波動砲モードへ{cdText}</color>";
        }

        if (beam?.Pending == true)
            return $"{size}<color=#ff0000>向きを決めています。撃ちたい向きへ動いてください</color>";
        if (IsCharging)
            return $"{size}<color=#ff0000>チャージ中... {Mathf.Max(0f, ChargeTime - chargeTimer):F1}s</color>";

        string ready = nowcool <= 0f
            ? " <color=#00ff88>(発射可能)</color>"
            : $" (CD: {LastCooltime}s)";
        return $"{size}<color=#ff0000>ファントムボタン → チャージ発射{ready}</color>";
    }

    public string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen != seer || isForMeeting || !Player.IsAlive()) return "";
        if (IsCharging && seer.PlayerId != Player.PlayerId)
            return $"\n<color=#ff0000>チャージ中... {(int)Mathf.Max(0f, ChargeTime - chargeTimer)}s</color>";
        if (ShowBeamMark && seer.PlayerId != Player.PlayerId)
            return "\n<color=#ff0000>ビーム中</color>";
        return "";
    }

    public override string GetProgressText(bool comms = false)
    {
        if (!Player.IsAlive()) return "";
        if (!IsBeamUnlocked)
        {
            int done = MyTaskState?.CompletedTasksCount ?? 0;
            return $"<color=#888888>[{done}/{BeamUnlockTaskCount}]</color>";
        }
        string shots = Utils.ColorString(ShotLimit > 0 ? Color.yellow : Color.gray, $"({ShotLimit})");
        string mode = beamMode
            ? "<color=#ff4444>[波動砲]</color>"
            : "<color=#f8cd46>[タスク]</color>";
        string cd = $"<color=#ffffff>({LastCooltime})</color>";
        return $"{shots}{mode}{cd}";
    }

    public override string GetAbilityButtonText() => "発射";


    void SendRpc()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write((byte)0);
        sender.Writer.Write(IsCharging);
        sender.Writer.Write(ShowBeamMark);
        sender.Writer.Write(ShotLimit);
        sender.Writer.Write(beamMode);
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
        byte tag = reader.ReadByte();
        if (tag == 1) { BeamFacingLeft = reader.ReadBoolean(); return; }
        IsCharging = reader.ReadBoolean();
        ShowBeamMark = reader.ReadBoolean();
        ShotLimit = reader.ReadInt32();
        beamMode = reader.ReadBoolean();
    }
}
