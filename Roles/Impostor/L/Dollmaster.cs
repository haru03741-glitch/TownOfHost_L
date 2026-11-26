using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

// 人形使い: 変身のボタンで選んだ人を、決めた時間だけ操る。操っている間、相手は人形使いが歩いたのと同じ向き・同じ距離を歩く
// (本人の操作は効かない)。歩かせる仕組みは PlayerWalker。壁や机があれば相手はその手前で止まる。
// 本編の客が自分で動けないのは歩かされている最中だけで、着いた後は操作が戻る。操っている間は相手の速さを止めておく
// (歩かされる速さは本編の決まった値で、速さの設定は掛からない)。
// 操っている間は変身のボタンをすぐ押せる。メニューで別の人を選ぶと、届けば相手にその人をキルさせ、
// 相手自身を選ぶと、近くの死体を相手に通報させる。どちらかをすると操るのをやめる。
// 操っている間、人形使いはほかの人の画面でだけ幽霊になり (TempGhost の hideFromSelf)、相手の上へ移る。
// 本編の客のカメラはホストから替えられないので、本人の体を相手に重ねて、相手の視点で操る形にする。
// 本人の画面では生きたままなので、キル・通報・ベント・サボタージュを押すと、それはせずに操るのをやめる。
// 終わると元の場所へ戻る。
public sealed class Dollmaster : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Dollmaster),
            player => new Dollmaster(player),
            CustomRoles.Dollmaster,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.ImpL + 300,
            SetupOptionItem,
            "dlm"
        );

    public Dollmaster(PlayerControl player)
    : base(RoleInfo, player)
    {
        cooldown = OptionCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
        canMakeKill = OptionCanMakeKill.GetBool();
        canMakeReport = OptionCanMakeReport.GetBool();
    }

    private static OptionItem OptionCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionCanMakeKill;
    private static OptionItem OptionCanMakeReport;
    private static float cooldown;
    private static float duration;
    private static bool canMakeKill;
    private static bool canMakeReport;

    private enum OptionName
    {
        DollmasterDuration,
        DollmasterCanMakeKill,
        DollmasterCanMakeReport,
    }

    // 操っている間の変身のボタンの待ち時間 (キル・通報をさせるために押し直す)
    private const float ActCooldown = 1f;

    // 行き先を出し直す間隔 (PlayerWalker の送る間隔の下限と同じ)
    private const float StepInterval = 0.25f;
    // 人形使いがこれより動いたら、相手の行き先を出し直す
    private const float ResendDistance = 0.3f;
    // 1 回の見直しでこれより動いたら、歩きではなく飛び (ベント・テレポート) として相手には写さない
    private const float JumpDistance = 2f;
    // 人形使いを相手の上へ移した後、移す前の位置の電文が届き終わるまで、動きを相手に写さない
    private const float SnapGrace = 0.6f;
    // ほかの人の画面で幽霊になってから、相手の上へ移るまでの間。同時に送ると、移った姿が消える前に 1 コマ見える
    private const float GhostLead = 0.25f;
    // 人形使いと相手がこれより離れたまま DriftHold 秒たったら、人形使いを相手の上へ移し直す
    // (相手が机の角などで止まった時。1.5 より短い移しは届かないことがある)
    private const float DriftDistance = 1.5f;
    private const float DriftHold = 0.5f;
    // 移し直しの間隔の下限
    private const float ResyncInterval = 1.5f;
    // 幽霊の時間は人形使いの側で終わらせるので、TempGhost の時間切れは操る時間より後に置く
    private const float GhostSlack = 5f;
    // ベントから押し出してから元の場所へ戻すまで (押し出された客がベントに入りきって出終わるまで)
    private const float VentReleaseDelay = 2f;
    // ベントから出る動きが戻しを上書きした時のために、もう一度戻すまで
    private const float VentResnapDelay = 1f;
    // 相手の速さを止める時の名前 (操る相手は人形使いごとに 1 人で、同じ人を 2 人では操れない)
    private const string SpeedKey = nameof(Dollmaster);

    private PlayerControl puppet;
    private byte puppetId;
    private Vector2 masterOrigin, puppetOrigin, lastMasterPos, lastTarget;
    // 操り始めた時の人形使いの位置 (終わったら戻す)
    private Vector2 home;
    private float endAt, nextStepAt, graceUntil, driftSince, nextResyncAt, firstSnapAt;
    private bool ventReleasePending;
    private int shownSeconds;
    private float failNoticeUntil;
    private string failNoticeKey;

    private static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.Cooldown, new(5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, OptionName.DollmasterDuration, new(2f, 20f, 1f), 6f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCanMakeKill = BooleanOptionItem.Create(RoleInfo, OptionName.DollmasterCanMakeKill, true, false);
        OptionCanMakeReport = BooleanOptionItem.Create(RoleInfo, OptionName.DollmasterCanMakeReport, true, false);
    }

    private bool CanAct => canMakeKill || canMakeReport;

    public override void Add() => CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);

    public override void OnDestroy()
    {
        CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);
        // 試合の終わりの後始末では送らない (相手の歩きは次の試合に持ち越されず、相手が先に壊れていることもある)
        Release(send: GameStates.IsInTask && !GameEndChecker.EndingStarted);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = puppet != null && CanAct ? ActCooldown : cooldown;
        AURoleOptions.ShapeshifterDuration = 1f;
    }

    public override string GetAbilityButtonText() => GetString(puppet != null && CanAct ? "DollmasterActButtonText" : "DollmasterButtonText");

    public override bool OnCheckShapeshift(PlayerControl target, ref bool animate)
    {
        animate = false;
        if (!Player.IsAlive() || Is(target) || !target.IsAlive()) return false;
        if (puppet != null)
        {
            if (CanAct) Act(target);
            return false;
        }
        // ほかの人形使いが操っている人は選べない (後から終わった方の止めが、もう片方の歩きまで止めるため)
        if ((PlayerWalker.IsWalking(target.PlayerId) ? "walking" : PlayerWalker.BlockReason(target)) is { } reason)
        {
            Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: 操れない ({reason})", nameof(Dollmaster));
            Fail("DollmasterFailed", reason);
            return false;
        }

        home = Player.transform.position;
        if (!TempGhost.Begin(Player, duration + GhostSlack, hideFromSelf: true, onInterrupt: OnInterrupted))
        {
            Fail("DollmasterFailed", "cannot hide");
            return false;
        }
        puppet = target;
        puppetId = target.PlayerId;
        PlayerSpeed.Freeze(puppetId, SpeedKey);
        target.MarkDirtySettings();
        endAt = Time.time + duration;
        nextStepAt = 0f;
        nextResyncAt = 0f;
        shownSeconds = -1;
        ventReleasePending = false;
        Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: {duration} 秒操る", nameof(Dollmaster));
        firstSnapAt = Time.time + GhostLead;
        graceUntil = firstSnapAt + SnapGrace;
        // 操っている間は押し直せるよう、待ち時間を短くしてから測り直す。
        // 本編の客は設定より先に測り直しが届くと古い待ち時間で数えるので、設定が届いてから測り直す
        Player.RpcResetAbilityCooldown(sync: CanAct);
        Utils.NotifyRoles(SpecifySeer: target);
        return false;
    }

    // 人形使いを相手の上へ移す。移した後しばらくは動きを写さず、落ち着いてから基準の位置を取り直す
    private bool SnapOntoPuppet(bool force)
    {
        if (!Player.RpcSnapTo(puppet.transform.position, force)) return false;
        graceUntil = Time.time + SnapGrace;
        driftSince = 0f;
        return true;
    }

    // 本人の画面で押せてしまうキル・通報・サボタージュ (TempGhost が止めて呼ぶ)
    private void OnInterrupted()
    {
        if (puppet == null) return;
        Logger.Info($"{Player.GetNameWithRole()}: 自分の行動を押したので操るのをやめる", nameof(Dollmaster));
        Release(send: true);
    }

    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (puppet == null) return true;
        // インポスターのベントは役職が断っても押し出されないので、ここで押し出す。
        // 出る動きの途中で元の場所へ戻すと、客がベントの中にいるまま残るので、出終わってから戻す
        if (!ventReleasePending)
        {
            ventReleasePending = true;
            Logger.Info($"{Player.GetNameWithRole()}: ベントを押したので操るのをやめる", nameof(Dollmaster));
            physics.RpcBootFromVent(ventId);
            var target = puppet;
            _ = new LateTask(() =>
            {
                if (puppet != target) return;
                var back = home;
                Release(send: true);
                _ = new LateTask(() =>
                {
                    if (Player != null && Player.IsAlive() && GameStates.IsInTask && puppet == null) Player.RpcSnapTo(back, force: true);
                }, VentResnapDelay, "Dollmaster.VentResnap");
            }, VentReleaseDelay, "Dollmaster.VentRelease");
        }
        return false;
    }

    public override bool OnInvokeSabotage(SystemTypes systemType)
    {
        if (puppet == null) return true;
        OnInterrupted();
        return false;
    }

    private void Fail(string key, string reason)
    {
        Logger.Info($"{Player.GetNameWithRole()}: できない ({reason})", nameof(Dollmaster));
        var showing = failNoticeUntil > 0f;
        failNoticeUntil = Time.time + 3f;
        failNoticeKey = key;
        if (!showing) Utils.NotifyRoles(SpecifySeer: Player);
    }

    // 操っている間に押し直した時: 相手自身を選べば死体を通報させ、別の人を選べばその人をキルさせる
    private void Act(PlayerControl chosen)
    {
        if (!GameManager.Instance || puppet.inVent || puppet.onLadder || puppet.inMovingPlat) { Fail("DollmasterActFailed", "puppet moving"); return; }
        if (chosen == puppet)
        {
            if (!canMakeReport) { Fail("DollmasterActFailed", "report off"); return; }
            var from = puppet.GetTruePosition();
            float range = puppet.MaxReportDistance;
            var body = Object.FindObjectsOfType<DeadBody>()
                .Where(b => b != null && !b.Reported && Vector2.Distance(from, b.TruePosition) <= range)
                .OrderBy(b => Vector2.Distance(from, b.TruePosition))
                .FirstOrDefault();
            var info = body == null ? null : GameData.Instance.GetPlayerById(body.ParentId);
            if (info == null) { Fail("DollmasterNoBody", "no body"); return; }
            var reporter = puppet;
            Logger.Info($"{Player.GetNameWithRole()} → {reporter.GetNameWithRole()} に {info.PlayerName} の死体を通報させる", nameof(Dollmaster));
            Release(send: true);
            reporter.ReportDeadBody(info);
            return;
        }

        if (!canMakeKill) { Fail("DollmasterActFailed", "kill off"); return; }
        // 仲間のインポスターはキルさせない
        if (chosen.Is(CountTypes.Impostor)) { Fail("DollmasterActFailed", "impostor"); return; }
        float killRange = GameManager.Instance.LogicOptions.GetKillDistance();
        float dist = Vector2.Distance(puppet.GetTruePosition(), chosen.GetTruePosition());
        if (dist > killRange || chosen.inVent || !chosen.CanMove)
        {
            Fail("DollmasterTooFar", $"dist={dist:0.00} range={killRange:0.00} inVent={chosen.inVent} canMove={chosen.CanMove}");
            return;
        }
        var killer = puppet;
        Logger.Info($"{Player.GetNameWithRole()} → {killer.GetNameWithRole()} に {chosen.GetNameWithRole()} をキルさせる", nameof(Dollmaster));
        Release(send: true);
        RPC.PlaySound(Player.PlayerId, Sounds.KillSound);
        chosen.SetRealKiller(Player);
        killer.RpcMurderPlayer(chosen);
        Utils.MarkEveryoneDirtySettings();
        Utils.NotifyRoles();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (failNoticeUntil > 0f && Time.time >= failNoticeUntil)
        {
            failNoticeUntil = 0f;
            Utils.NotifyRoles(SpecifySeer: Player);
        }
        if (puppet == null) return;
        PlayerWalker.Tick(puppet);
        // ベントから押し出している間は、戻すのを押し出した側 (OnEnterVent の予約) に任せる
        if (ventReleasePending) return;

        // 会議の前に幽霊から戻された時 (会議が開くまで通報を預かる間) も、ほかの人に見えたまま操らないようやめる
        if (Time.time >= endAt || !Player.IsAlive() || !puppet.IsAlive() || puppet.Data == null || puppet.Data.Disconnected
            || !TempGhost.IsGhost(Player))
        {
            Release(send: true);
            return;
        }

        int seconds = Mathf.CeilToInt(endAt - Time.time);
        if (seconds != shownSeconds)
        {
            shownSeconds = seconds;
            Utils.NotifyRoles(SpecifySeer: Player);
        }

        if (firstSnapAt > 0f && Time.time >= firstSnapAt)
        {
            firstSnapAt = 0f;
            SnapOntoPuppet(force: true);
        }
        // 移した直後は、移す前の位置の電文が混ざるので写さない。落ち着いたら基準を取り直す
        if (graceUntil > 0f)
        {
            if (Time.time < graceUntil) return;
            graceUntil = 0f;
            masterOrigin = lastMasterPos = Player.GetTruePosition();
            puppetOrigin = lastTarget = puppet.GetTruePosition();
            nextStepAt = 0f;
        }

        if (Time.time < nextStepAt) return;
        nextStepAt = Time.time + StepInterval;

        var masterPos = Player.GetTruePosition();
        // 相手が机の角などで止まって離れたら、人形使いを相手の上へ移し直す
        if ((masterPos - puppet.GetTruePosition()).sqrMagnitude > DriftDistance * DriftDistance)
        {
            if (driftSince == 0f) driftSince = Time.time;
            else if (Time.time - driftSince >= DriftHold && Time.time >= nextResyncAt && SnapOntoPuppet(force: false))
            {
                nextResyncAt = Time.time + ResyncInterval;
                Logger.Info($"{Player.GetNameWithRole()}: 相手から離れたので重ね直す", nameof(Dollmaster));
                return;
            }
        }
        else driftSince = 0f;
        // ベントやテレポートで飛んだ分は写さない (相手が壁を越えて一直線に歩こうとしないように)
        if ((masterPos - lastMasterPos).sqrMagnitude > JumpDistance * JumpDistance)
            masterOrigin += masterPos - lastMasterPos;
        lastMasterPos = masterPos;

        var target = puppetOrigin + (masterPos - masterOrigin);
        if ((target - lastTarget).sqrMagnitude < ResendDistance * ResendDistance) return;
        if (PlayerWalker.WalkTo(puppet, target)) lastTarget = target;
    }

    // 操っている途中に人形使いが抜けると、OnFixedUpdate の見張りが止まり相手の速さが止まったまま残る。抜けた時に放す
    // (相手が抜けた時は OnFixedUpdate が切断を見てやめる)
    public override void OnPlayerLeft(byte playerId)
    {
        if (!AmongUsClient.Instance.AmHost || puppet == null || playerId != Player.PlayerId) return;
        try { Release(send: false); }
        catch (System.Exception ex) { Logger.Exception(ex, nameof(Dollmaster)); }
    }

    public override void OnStartMeeting()
    {
        failNoticeUntil = 0f;
        // 会議の始めに本編が相手の歩きを止めるので、電文は送らない
        Release(send: false);
    }

    private void Release(bool send)
    {
        if (puppet == null) return;
        // 試合の終わり際は送らない
        if (!GameStates.IsInTask || GameEndChecker.EndingStarted || AmongUsClient.Instance.IsGameOver) send = false;
        var target = puppet;
        puppet = null;
        graceUntil = 0f;
        firstSnapAt = 0f;
        ventReleasePending = false;
        if (send) PlayerWalker.Stop(target);
        else PlayerWalker.Forget(target);
        PlayerSpeed.Remove(puppetId, SpeedKey);
        // 試合の終わりの後始末では、相手が先に壊れていることがある
        if (target != null && target.Data != null && !target.Data.Disconnected) target.MarkDirtySettings();
        // 元の場所へ戻してから、ほかの人の画面に姿を戻す (会議の時は TempGhost が会議の前に戻し、会議で全員の位置も替わる)
        if (send && Player != null && Player.IsAlive())
        {
            Player.RpcSnapTo(home, force: true);
            TempGhost.End(Player);
        }
        // 送らない時も、ホストの画面で隠した姿は戻す (会議の時は TempGhost が会議の前か会議明けに戻す)
        else if (!send && !MeetingHud.Instance && !MeetingStates.MeetingCalled) TempGhost.Forget(Player);
        // 押し直しのための短い待ち時間を元に戻し、ここから待ち時間を数える
        if (send && CanAct && Player != null && Player.IsAlive())
            Player.RpcResetAbilityCooldown(sync: true);
        Logger.Info($"{Player?.GetNameWithRole()} → {target.GetNameWithRole()}: 操るのをやめた", nameof(Dollmaster));
        if (send)
        {
            Utils.NotifyRoles(SpecifySeer: Player);
            Utils.NotifyRoles(SpecifySeer: target);
        }
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (isForMeeting || puppet == null || seer != Player || seen != puppet) return "";
        return Utils.ColorString(RoleInfo.RoleColor, "◈");
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer != Player || seen != Player) return "";
        if (puppet != null) return Utils.ColorString(RoleInfo.RoleColor, string.Format(GetString("DollmasterControlling"), Mathf.Max(0, shownSeconds)));
        if (failNoticeUntil > 0f) return Utils.ColorString(RoleInfo.RoleColor, GetString(failNoticeKey ?? "DollmasterFailed"));
        return "";
    }

    // 操られている人の画面に、自分が操られていることを出す
    private string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || puppet == null || seer != puppet || seen != puppet) return "";
        return Utils.ColorString(RoleInfo.RoleColor, GetString("DollmasterControlled"));
    }
}
