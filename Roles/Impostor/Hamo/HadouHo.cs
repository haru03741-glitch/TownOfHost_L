using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Crewmate;
using UnityEngine;

namespace TownOfHostL.Roles.Impostor;

public sealed class HadouHo : RoleBase, IImpostor, IUsePhantomButton
{
    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(HadouHo),
            player => new HadouHo(player),
            CustomRoles.HadouHo,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 4900,
            SetUpOptionItem,
            "hh"
        );

    public HadouHo(PlayerControl player)
        : base(RoleInfo, player)
    {
        KillCooldown = OptionKillCoolDown.GetFloat();
        Cooldown = OptionCoolDown.GetFloat();
        ChargeTime = OptionChargeTime.GetFloat();
        BeamTime = OptionBeamTime.GetFloat();
        SelfDestructOnMiss = OptionSelfDestructOnMiss.GetBool();
        KillImpostor = OptionKillImpostor.GetBool();
        IsCharging = false;
        chargeTimer = 0f;
        ShowBeamMark = false;
        HasHit = false;
        IsDead = false;
        IsFiring = false;
        _prevCharging = false;
        _prevBeamMark = false;
        Charging = false;
        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
    }

    public bool IsCharging;
    float chargeTimer;
    public bool ShowBeamMark;
    bool HasHit;
    bool BeamFacingLeft;
    bool IsDead;
    bool IsFiring = false;
    bool spawnCooldownStarted = false;
    bool _prevCharging;
    bool _prevBeamMark;
    HadouHoBeam beam;
    public static bool Charging;
    static OptionItem OptionCoolDown;
    static float Cooldown;
    public static float CooldownValue => Cooldown;
    static OptionItem OptionKillCoolDown;
    static float KillCooldown;
    static OptionItem OptionChargeTime;
    static float ChargeTime;
    static OptionItem OptionBeamTime;
    static float BeamTime;
    static OptionItem OptionSelfDestructOnMiss;
    static bool SelfDestructOnMiss;
    static OptionItem OptionKillImpostor;
    static OptionItem OptionBeamWarning;
    static bool KillImpostor;

    enum OptionName { HadouHoChargeTime, HadouHoSelfDestruct, HadouHoKillImpostor, HadouHoBeamTime, HadouHoBeamWarning }

    static void SetUpOptionItem()
    {
        OptionKillCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(0, 180, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionCoolDown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(0, 180, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionChargeTime = FloatOptionItem.Create(RoleInfo, OptionName.HadouHoChargeTime, new(0.5f, 10f, 0.5f), 3f, false).SetValueFormat(OptionFormat.Seconds);
        OptionBeamTime = FloatOptionItem.Create(RoleInfo, OptionName.HadouHoBeamTime, new(0.5f, 10f, 0.5f), 3f, false).SetValueFormat(OptionFormat.Seconds);
        OptionSelfDestructOnMiss = BooleanOptionItem.Create(RoleInfo, OptionName.HadouHoSelfDestruct, false, false);
        OptionKillImpostor = BooleanOptionItem.Create(RoleInfo, OptionName.HadouHoKillImpostor, false, false);
        OptionBeamWarning = BooleanOptionItem.Create(RoleInfo, OptionName.HadouHoBeamWarning, false, false);
    }

    public override void Add()
    {
        spawnCooldownStarted = false;
        // MOD を入れている人とホストは ClientEffects の演出でためとビームを見るので、CNO を隠す
        beam = new HadouHoBeam(Player, drawnByClientEffects: true);
    }

    public override void OnDestroy()
    {
        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(HadouHo));
        beam?.End();
        CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);

        if (IsCharging || ShowBeamMark || IsFiring)
        {
            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(HadouHo));
            Player.MarkDirtySettings();
            if (AmongUsClient.Instance.AmHost)
            {
                Player.SyncSettings();
            }
            IsCharging = false;
            Charging = false;
            ShowBeamMark = false;
            IsFiring = false;
            beam?.End();
        }
    }

    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = Cooldown;
    public float CalculateKillCooldown() => KillCooldown;

    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (IsCharging || ShowBeamMark) return false;
        return true;
    }
    bool IUsePhantomButton.IsPhantomRole => true;

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;
        if (IsFiring || ShowBeamMark || !Player.IsAlive() || IsCharging) return;

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
        Charging = true;
        chargeTimer = 0f;
        TownOfHostL.Modules.ClientEffects.Charge(Player, ChargeTime, new Color(1f, 0.55f, 0.2f));
        beam?.BeginCharge(OptionBeamWarning.GetBool() ? HadouHoBeam.SmallWarningSize : 0);
        TownOfHostL.Modules.PlayerSpeed.Freeze(Player.PlayerId, nameof(HadouHo));
        Player.MarkDirtySettings();
        Utils.AllPlayerKillFlash();
        Main.AllPlayerKillCooldown[Player.PlayerId] = 60f;
        Player.SetKillCooldown(60f, ForceProtect: true);
        // 設定は 1 回送れば足りる(続けて 3 回送っていて、何人も同時に押すと通信が多すぎてキックされていた)
        _ = new LateTask(() => { if (Player != null) Player.SyncSettings(); }, 0.1f, "HadouHoKillTimer");
        Utils.NotifyRoles(ForceLoop: true);
        _prevCharging = true;
        _prevBeamMark = false;
        SendRpc();
    }

    /// <summary>撃つたび・状態を戻すたびに進める。会議で戻した後に、前の発射の後処理 (当たらなかった時の自爆など) を走らせない</summary>
    int fireGeneration;

    private void ResetState()
    {
        // 撃っている途中の後処理 (LateTask) を捨てる
        fireGeneration++;
        TownOfHostL.Modules.ClientEffects.ChargeStop(Player);
        IsCharging = false;
        ShowBeamMark = false;
        Charging = false;
        IsFiring = false;
        chargeTimer = 0f;
        HasHit = false;
        _prevCharging = false;
        _prevBeamMark = false;
        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(HadouHo));
        Player.MarkDirtySettings();
        beam?.End();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        if (!spawnCooldownStarted && Player.IsAlive() && !GameStates.Intro && GameStates.IsInTask && !GameStates.IsMeeting)
        {
            spawnCooldownStarted = true;
            Player.RpcResetAbilityCooldown(sync: true);
        }

        if (MeetingHud.Instance != null)
        {
            if (IsCharging || ShowBeamMark || IsFiring)
            {
                ResetState();
                Utils.NotifyRoles();
                SendRpc();
            }
            return;
        }

        if (!Player.IsAlive() && (IsCharging || ShowBeamMark))
        {
            ResetState();
            Utils.NotifyRoles();
            SendRpc();
            return;
        }

        bool changed = (IsCharging != _prevCharging) || (ShowBeamMark != _prevBeamMark);
        if (changed)
        {
            _prevCharging = IsCharging;
            _prevBeamMark = ShowBeamMark;
            Utils.NotifyRoles(ForceLoop: true);
        }

        if (!IsCharging && !ShowBeamMark) beam?.TrackDirection();
        beam?.Tick();
        if (ShowBeamMark && Player.IsAlive()) ApplyBeamHit();
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
        if (IsDead || !Player.IsAlive()) return;
        BeamFacingLeft = beam?.FacingLeft ?? Player.cosmetics.FlipX;
        SendBeamDirection(BeamFacingLeft);
        TownOfHostL.Modules.ClientEffects.ChargeStop(Player);
        var beamFrom = Player.GetTruePosition() + (BeamFacingLeft ? Vector2.left : Vector2.right) * 0.5f;
        TownOfHostL.Modules.ClientEffects.NovaBeam(Player, beamFrom, BeamFacingLeft ? 180f : 0f, TownOfHostL.Modules.ClientEffects.MapReach(beamFrom, BeamFacingLeft ? Vector2.left : Vector2.right), 2.2f, BeamTime);
        TownOfHostL.Modules.ClientEffects.Sound(TownOfHostL.Modules.ClientEffects.SoundKind.NovaFire);
        beam?.Fire("#FF6600", HadouHoBeam.NormalSize);
        Utils.AllPlayerKillFlash();
        IsCharging = false; chargeTimer = 0f; HasHit = false; ShowBeamMark = true;
        _prevCharging = false;
        _prevBeamMark = true;
        Utils.NotifyRoles(ForceLoop: true);
        SendRpc();
        ApplyBeamHit();

        var generation = ++fireGeneration;
        _ = new LateTask(() =>
        {
            if (generation != fireGeneration) return;
            if (IsDead || !Player.IsAlive())
            {
                Charging = false;
                ShowBeamMark = false; _prevBeamMark = false;
                beam?.End(); IsFiring = false;
                TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(HadouHo));
                Player.MarkDirtySettings();
                Utils.NotifyRoles(); SendRpc(); return;
            }
            Charging = false;

            ShowBeamMark = false; _prevBeamMark = false;
            beam?.End();
            Utils.NotifyRoles(ForceLoop: true); SendRpc();

            if (!HasHit && SelfDestructOnMiss)
            {
                TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(HadouHo)); Player.MarkDirtySettings();
                PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Suicide;
                Player.RpcMurderPlayerV2(Player); IsFiring = false; return;
            }
            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(HadouHo)); Player.MarkDirtySettings();
            _ = new LateTask(() =>
            {
                if (!Player.IsAlive()) { IsFiring = false; return; }
                Main.AllPlayerKillCooldown[Player.PlayerId] = KillCooldown;
                Player.SetKillCooldown(KillCooldown, ForceProtect: true);
                Player.RpcResetAbilityCooldown(sync: true);
                Utils.NotifyRoles();
                _ = new LateTask(() => { IsFiring = false; }, 0.3f, "HadouHoResetFiring", true);
            }, 0.2f, "HadouHoResetKillCool");
        }, BeamTime);
    }

    void ApplyBeamHit()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        bool facingLeft = BeamFacingLeft;
        var myPos = Player.GetTruePosition();
        Vector2 dir = facingLeft ? Vector2.left : Vector2.right;
        // ループ中にOnCheckMurderで対象が死亡すると、AllAlivePlayerControlsの中身が
        // その場で変化してしまう(シェリフのキルモード解除RPC等、死亡時の副作用が絡む場合に
        // 顕著)。開始前にスナップショットを取り、ループ中の死亡は都度チェックすることで
        // 以降の対象判定がおかしくなるのを防ぐ。
        foreach (var target in Main.AllAlivePlayerControls.ToArray())
        {
            if (!Player.IsAlive()) break;
            if (target.PlayerId == Player.PlayerId) continue;
            if (!target.IsAlive()) continue;
            if (!KillImpostor && target.GetCustomRole().IsImpostor()) continue;
            var toTarget = target.GetTruePosition() - myPos;
            float dot = Vector2.Dot(toTarget, dir);
            if (dot <= 0) continue;
            var proj = dir * dot;
            var perp = toTarget - proj;
            if (perp.magnitude > 1.3f) continue;
            CustomRoleManager.OnCheckMurder(Player, target, target, target, true, deathReason: CustomDeathReason.Evaporation);
            HasHit = true;
        }
    }
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
    }
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        ResetState();
        Player.SyncSettings();
        Utils.NotifyRoles(ForceLoop: true); SendRpc();
    }

    public override void OnStartMeeting()
    {
        ResetState();
        Player.SyncSettings();
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        AURoleOptions.PhantomCooldown = Cooldown;
        Player.RpcResetAbilityCooldown();
    }

    void SendRpc()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.Write(IsCharging);
        sender.Writer.Write(ShowBeamMark);
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
        if (reader.Length - reader.Position == 2) { reader.ReadByte(); BeamFacingLeft = reader.ReadBoolean(); return; }
        bool oldCharging = IsCharging;
        bool oldBeamMark = ShowBeamMark;
        IsCharging = reader.ReadBoolean();
        ShowBeamMark = reader.ReadBoolean();
        if (oldCharging != IsCharging || oldBeamMark != ShowBeamMark)
            Utils.NotifyRoles(ForceLoop: true);
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";
        if (beam?.Pending == true) return $"{(isForHud ? "" : "<size=60%>")}<color=#ff0000>向きを決めています。撃ちたい向きへ動いてください</color>";
        if (!IsCharging) return $"{(isForHud ? "" : "<size=60%>")}<color=#ff0000>ファントムボタン → チャージ発射</color>";
        return $"{(isForHud ? "" : "<size=100%>")}<color=#ff0000>チャージ中... {Mathf.Max(0f, ChargeTime - chargeTimer):F1}s</color>";
    }

    public string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen != seer || isForMeeting || !Player.IsAlive()) return "";
        if (IsCharging && seer.PlayerId != Player.PlayerId) return $"\n<size=100%><color=#ff0000>チャージ中... {(int)Mathf.Max(0f, ChargeTime - chargeTimer)}s</color></size>";
        if (ShowBeamMark && seer.PlayerId != Player.PlayerId) return "\n<size=100%><color=#ff0000>ビーム中</color></size>";
        return "";
    }

    public override string GetAbilityButtonText() => "発射";

    public void cancelbeam()
    {
        if (IsDead || !Player.IsAlive())
        {
            Charging = false;
            ShowBeamMark = false; _prevBeamMark = false;
            beam?.End(); IsFiring = false;
            TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(HadouHo));
            Player.MarkDirtySettings();
            Utils.NotifyRoles(); SendRpc(); return;
        }
        Charging = false;
        IsCharging = false;
        _prevCharging = false;
        _prevBeamMark = false;
        ShowBeamMark = false; _prevBeamMark = false;
        beam?.End();
        Utils.NotifyRoles(ForceLoop: true); SendRpc();
        TownOfHostL.Modules.PlayerSpeed.Remove(Player.PlayerId, nameof(HadouHo)); Player.MarkDirtySettings();
        var charge = OptionChargeTime.GetFloat() - chargeTimer;
        var waittime = OptionBeamTime.GetFloat() + charge;
        _ = new LateTask(() =>
        {
            IsFiring = false;
        }, waittime);
    }
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.ClimbLadder))]
    class LadderPatch
    {
        public static Dictionary<byte, Vector2> Ladder = new();
        public static void Postfix(PlayerPhysics __instance, Ladder source, byte climbLadderSid)
        {
            var sourcePos = source.transform.position;
            var targetPos = source.Destination.transform.position;
            if (sourcePos.y > targetPos.y)
                Ladder[__instance.myPlayer.PlayerId] = targetPos;
            else
                Ladder[__instance.myPlayer.PlayerId] = sourcePos;

            if (__instance.myPlayer.GetRoleClass() is HadouHo { ShowBeamMark: true } hadouho)
            {
                hadouho.cancelbeam();
            }
            else if (__instance.myPlayer.GetRoleClass() is HadouHo { IsCharging: true } hadouhou)
            {
                hadouhou.cancelbeam();
            }
        }
    }
}