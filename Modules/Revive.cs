using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 本当に死んだプレイヤーを生き返らせる (ホストのみ)。
///
/// 死ぬと全員の画面でその人の役職が幽霊の役職に替わるので、見る人ごとに生きている時の役職を SetRole で送り直し、
/// PlayerInfo の IsDead を戻して送る。他の人の画面とホストでは、その場で生きている体に戻る。
/// 生き返った本人の画面は、インポスター系の役職ならその場で戻るが、クルー系の役職は次の会議まで
/// 幽霊のボタン (憑依) と「死亡した」の見出しが残り、通報のボタンも出ない (本編の客は SetRole で画面を組み直さない。
/// 2026-11-09 実機で確認。エンジニアを挟む・SetRole の送り直し・PlayerInfo をすぐ送る、のどれでも戻らなかった)。
///
/// 会議・追放の演出の間と会議明けの直後は行わず、予約しておいて安全になった所で行う
/// (会議中の SetRole は会議明けの配り直しに上書きされ、PlayerInfo も会議中は送らない)。
/// 同じ宛先へ SetRole を続けて送らないよう、1 回に 1 人ずつ、間を空けて行う。
/// 復活の直後 (SetRole の処理が落ち着く前) に会議を開くと本編の客の会議の画面が壊れるので、
/// その間の通報は 1 件だけ預かり、落ち着いてから通す (ReportDeadBodyPatch)。
///
/// 死体は本編に消す RPC が無いので、ホストの手元で消して客の画面は床の色で覆う (DeadBodyEraser.HideLeftover)。
/// 戻さない物: 死んだ時に終えたことにしたホストのタスク。役職の「一度きり」の旗は、本人の役職は RoleBase.OnRevived、
/// 他の人の死を見る側は CustomRoleManager.OnPlayerRevivedOthers で戻す。
/// </summary>
public static class Revive
{
    // 会議・追放が終わってから、復活を行うまでに空ける時間 (会議明けの役職の配り直しが 0.5 秒後に走る)
    private const float AfterMeetingGap = 3f;
    // 続けて復活させる時に空ける時間 (同じ宛先へ同じ種類の SetRole を続けて送らない)
    private const float BetweenRevives = 1f;
    /// <summary>SetRole を送ってから会議を開くまでに空ける時間。本編の客は SetRole の直後に会議が来ると会議の画面が壊れる</summary>
    public const float SettleTime = 2.5f;

    private static readonly List<byte> pending = new();
    private static readonly HashSet<byte> revived = new();
    private static float lastMeetingAt = float.NegativeInfinity;
    private static float nextReviveAt;
    private static float settlingUntil;
    // 試合ごとに増やす。遅れて走る処理が前の試合の人に触らないように比べる
    private static int generation;
    // 落ち着くまで預かっている通報 (1 件だけ。後から来た物は捨てる)
    private static PlayerControl heldReporter;
    private static NetworkedPlayerInfo heldTarget;
    private static bool hasHeldReport;
    private static bool heldForce;

    [GameModuleInitializer]
    public static void Init()
    {
        pending.Clear();
        revived.Clear();
        lastMeetingAt = float.NegativeInfinity;
        nextReviveAt = 0f;
        settlingUntil = 0f;
        generation++;
        ClearHeldReport();
    }

    /// <summary>復活を予約する。安全な時になったら、予約した順に 1 人ずつ行う。</summary>
    public static void Request(byte playerId)
    {
        if (!AmongUsClient.Instance.AmHost || pending.Contains(playerId)) return;
        pending.Add(playerId);
        Logger.Info($"復活を予約: {Utils.GetPlayerById(playerId)?.GetNameWithRole()}", "Revive");
    }

    public static void Cancel(byte playerId) => pending.Remove(playerId);
    public static bool IsPending(byte playerId) => pending.Contains(playerId);
    /// <summary>この試合で一度でも生き返った人か (残った死体の通報を止めるのに使う)。</summary>
    public static bool WasRevived(byte playerId) => revived.Contains(playerId);
    /// <summary>復活の SetRole が落ち着くまでの間か。この間に会議を開かない。</summary>
    public static bool IsSettling => Time.realtimeSinceStartup < settlingUntil;

    /// <summary>
    /// 落ち着くまでの間に来た通報を預かる。預かれた (この通報は今は通さない) なら true。
    /// 既に 1 件預かっている時は、後の通報は捨てる (同じ死体への重ねた通報で会議が 2 度開かないように)。
    /// </summary>
    public static bool HoldReport(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!IsSettling) return false;
        // 役職の能力で確認なしに開く会議の旗は、預かった通報と一緒に預かる (立てたままにすると次の通報が素通りする)
        bool force = ReportDeadBodyPatch.HamoForceReport;
        ReportDeadBodyPatch.HamoForceReport = false;
        if (!hasHeldReport)
        {
            heldReporter = reporter;
            heldTarget = target;
            heldForce = force;
            hasHeldReport = true;
            Logger.Info($"復活の直後なので通報を預かる: {reporter?.GetNameWithRole()} => {target?.PlayerName ?? "ボタン"}", "Revive");
        }
        return true;
    }

    private static void ClearHeldReport()
    {
        heldReporter = null;
        heldTarget = null;
        hasHeldReport = false;
        heldForce = false;
    }

    private static void FlushHeldReport()
    {
        var reporter = heldReporter;
        var target = heldTarget;
        bool force = heldForce;
        ClearHeldReport();
        if (reporter == null || reporter.Data == null || reporter.Data.IsDead) return;
        // 預かっている間に死体の主が生き返った・抜けた時は通さない
        // (自分自身を対象にする会議は、生きているので外さない)
        if (target != null && target.PlayerId != reporter.PlayerId && (target.Disconnected || !(PlayerState.GetByPlayerId(target.PlayerId)?.IsDead ?? false))) return;
        Logger.Info($"預かった通報を通す: {reporter.GetNameWithRole()}", "Revive");
        ReportDeadBodyPatch.HamoForceReport = force;
        reporter.ReportDeadBody(target);
    }

    private static bool CanRunNow(float now)
    {
        if (!GameStates.IsInTask || MeetingHud.Instance || ExileController.Instance || AntiBlackout.IsCached) return false;
        return now - lastMeetingAt >= AfterMeetingGap;
    }

    private static void Update()
    {
        var client = AmongUsClient.Instance;
        if (!client || !client.AmHost || !GameStates.IsInGame) return;
        float now = Time.realtimeSinceStartup;
        if (MeetingHud.Instance || ExileController.Instance)
        {
            lastMeetingAt = now;
            // 預かっている間に別の経路で会議が開いたら、預かった通報は捨てる
            if (hasHeldReport) ClearHeldReport();
        }
        if (hasHeldReport && !IsSettling && GameStates.IsInTask && !GameStates.IsMeeting) FlushHeldReport();

        if (pending.Count == 0 || now < nextReviveAt || !CanRunNow(now)) return;
        while (pending.Count > 0)
        {
            byte id = pending[0];
            pending.RemoveAt(0);
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
            var state = PlayerState.GetByPlayerId(id);
            // MOD の上で死んでいる人だけ (梯子の途中など、PlayerInfo だけ先に死んだ人は戻さない)
            if (state == null || !state.IsDead) continue;
            nextReviveAt = now + BetweenRevives;
            try { Execute(pc, state); }
            catch (System.Exception ex) { Logger.Exception(ex, "Revive"); }
            return;
        }
    }

    private static void Execute(PlayerControl pc, PlayerState state)
    {
        Logger.Info($"復活: {pc.GetNameWithRole()}", "Revive");
        state.SetAlive();
        Main.AfterMeetingDeathPlayers.Remove(pc.PlayerId);
        revived.Add(pc.PlayerId);

        // 見る人ごとに、死ぬ前の見え方を送る (宛先ごとに 1 本ずつ)
        RoleView.Replay(pc);
        settlingUntil = Time.realtimeSinceStartup + SettleTime;
        // 手元の IsDead はすぐ戻す (この後すぐに会議が始まっても、AntiBlackout が生きている人として覚えるように)
        pc.Data.IsDead = false;
        int gen = generation;

        // 残った死体はホストの手元で消す (死体を探す役職・アドミン・矢印に拾われないように)。
        // 客の画面は SetRole が落ち着いてから床の色で覆う (覆いは CNO の spawn なので、SetRole と重ねない)
        Vector2? bodyPos = null;
        try
        {
            TargetDeadArrow.RemoveBodiesOf(pc.PlayerId);
            bodyPos = DeadBodyEraser.HideLeftover(pc.PlayerId);
        }
        catch (System.Exception ex) { Logger.Exception(ex, "Revive"); }
        if (bodyPos.HasValue)
        {
            var pos = bodyPos.Value;
            _ = new LateTask(() =>
            {
                if (gen != generation || !GameStates.IsInTask || MeetingHud.Instance || state.IsDead) return;
                DeadBodyEraser.CoverLeftover(pc.PlayerId, pos);
            }, SettleTime, "Revive_CoverBody", true);
        }

        // PlayerInfo は SetRole の後に届ける (宛先指定の RPC と全員宛ての PlayerInfo は順が保たれない)
        _ = new LateTask(() =>
        {
            if (gen != generation || pc == null || pc.Data == null || state.IsDead || !GameStates.IsInGame) return;
            pc.Data.IsDead = false;
            // 会議・追放の間は送らない (会議明けに AntiBlackout が全員の PlayerInfo を送り直す)
            if (!MeetingHud.Instance && !ExileController.Instance && !AntiBlackout.IsCached) pc.Data.MarkDirty();
            // ホストの手元の体を生きている見た目へ戻す (客は SetRole で戻る)
            // ここで失敗しても、この後の役職の戻しと知らせは行う
            try
            {
                pc.Revive();
                pc.ResetKillCooldown();
                pc.SetKillCooldown();
                pc.RpcResetAbilityCooldown();
                pc.MarkDirtySettings();
            }
            catch (System.Exception ex) { Logger.Exception(ex, "Revive"); }
            try { pc.GetRoleClass()?.OnRevived(); }
            catch (System.Exception ex) { Logger.Exception(ex, "Revive"); }
            CustomRoleManager.NotifyPlayerRevived(pc.PlayerId);
            Utils.NotifyRoles();
        }, 0.2f, "Revive_Data", true);
    }

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    private static class DrivePatch
    {
        public static void Postfix() => Update();
    }
}
