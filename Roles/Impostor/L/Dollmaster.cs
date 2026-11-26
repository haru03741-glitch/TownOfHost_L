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
// 操っている間は変身のボタンをすぐ押せる。メニューで別の人を選ぶと、届けば相手にその人をキルさせ、
// 相手自身を選ぶと、近くの死体を相手に通報させる。どちらかをすると操るのをやめる。
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

    private PlayerControl puppet;
    private Vector2 masterOrigin, puppetOrigin, lastMasterPos, lastTarget;
    private float endAt, nextStepAt;
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

        puppet = target;
        masterOrigin = lastMasterPos = Player.GetTruePosition();
        puppetOrigin = lastTarget = target.GetTruePosition();
        endAt = Time.time + duration;
        nextStepAt = 0f;
        shownSeconds = -1;
        Logger.Info($"{Player.GetNameWithRole()} → {target.GetNameWithRole()}: {duration} 秒操る", nameof(Dollmaster));
        // 操っている間は押し直せるよう、待ち時間を短くしてから測り直す
        if (CanAct) Player.SyncSettings();
        Player.RpcResetAbilityCooldown();
        Utils.NotifyRoles(SpecifySeer: target);
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

        if (Time.time >= endAt || !Player.IsAlive() || !puppet.IsAlive() || puppet.Data == null || puppet.Data.Disconnected)
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

        if (Time.time < nextStepAt) return;
        nextStepAt = Time.time + StepInterval;

        var masterPos = Player.GetTruePosition();
        // ベントやテレポートで飛んだ分は写さない (相手が壁を越えて一直線に歩こうとしないように)
        if ((masterPos - lastMasterPos).sqrMagnitude > JumpDistance * JumpDistance)
            masterOrigin += masterPos - lastMasterPos;
        lastMasterPos = masterPos;

        var target = puppetOrigin + (masterPos - masterOrigin);
        if ((target - lastTarget).sqrMagnitude < ResendDistance * ResendDistance) return;
        if (PlayerWalker.WalkTo(puppet, target)) lastTarget = target;
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
        var target = puppet;
        puppet = null;
        if (send) PlayerWalker.Stop(target);
        else PlayerWalker.Forget(target);
        // 押し直しのための短い待ち時間を元に戻し、ここから待ち時間を数える
        if (send && CanAct && Player != null && Player.IsAlive())
        {
            Player.SyncSettings();
            Player.RpcResetAbilityCooldown();
        }
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
