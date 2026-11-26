using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using InnerNet;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Core;
using TownOfHostL.Modules.Net;

namespace TownOfHostL.Modules;

/// <summary>
/// 本編の客の画面に残っている死体を、ホストの手元の死体に合わせ直す (ホストだけが動かす)。
/// <para>
/// 本編には死体を消す電文が無く、客が手元の死体を壊すのは会議の始まりだけ。そこで、その客にだけ会議の始まりを送って
/// 死体を全部壊させ、同じ包みの中で、会議の始まりが変えた物を元へ戻し、残す死体を作り直す。包みの中身は順に、
/// 会議の画面の spawn → 会議の始まり → 会議の画面の despawn → 生きている全員と CNO の位置の戻し →
/// 本人の役職の送り直し → 残す死体の作り直し。客は 1 つの包みを同じフレームで処理するので、消す死体が消える所だけが映る
/// (2026-11-27 公式サーバー・本編の客で確認)。
/// </para>
/// <para>
/// 会議の始まりは「緊急会議」の演出を積むが、通報者のプレイヤーの情報が無いと積む前で止まる。出したばかりで可視化の前の
/// CNO は、客の手元でプレイヤーの情報を持たないので、これを通報者にする。会議の始まりは HUD と地図のボタンを隠し、全員を
/// 会議の席へ動かす。HUD とボタンは、本人の役職の送り直しで本編が出し直す。位置は同じ包みの SnapTo で戻す。
/// </para>
/// <para>
/// 残す死体は、死体の持ち主を死体の所へ SnapTo してから、殺し手の CNO に殺させて作り直す (<see cref="DeadBodyMover"/> と同じ)。
/// 作り直した死体は倒れる動きをやり直すので、残す死体が画面に入っている客には送らず、入らなくなってから送る。
/// 殺し手は死体 1 つに 1 体。同じフレームに 1 体の殺し手が 2 人を殺すと、本編のキルの演出が終わらず、死体が通報できないまま残る。
/// </para>
/// <para>
/// 送るのは、本人の画面に悪い変化が出ない客だけ: 生きている・基底がクルーメイト (役職の送り直しでクールが始まり直すボタンを
/// 持たない)・ベントやはしごの途中でない。合わない客は合うまで待ち、その間は覆い (<see cref="DeadBodyEraser"/>) のまま。
/// 送った客からは覆いを外す。会議の始まりが開いている地図とタスクの窓を閉じる点は残る。
/// </para>
/// </summary>
public static class ClientBodySync
{
    private const string Tag = nameof(ClientBodySync);

    // 合わせ直しの条件を見る間隔と、頼まれてから最初に見るまでの間 (覆いの CNO を先に出させる)
    private const float CheckInterval = 0.5f;
    private const float StartDelay = 0.2f;
    // 送り終えてから次の回までの間
    private const float RoundGap = 1f;
    // 通報者の CNO を spawn してから送るまでの間。客の手元の体の生成は spawn の電文より遅れるので、すぐ送ると会議の始まりが
    // 宛先なしで捨てられる (0.3 秒で 13 回中 13 回届いた)。可視化はその後まで待たせる
    private const float ReporterSendDelay = 0.4f;
    private const float ReporterVisibilityHold = 0.6f;
    // 殺し手の CNO を消すまでの間 (DeadBodyMover と同じ)
    private const float KillerHold = 0.6f;
    // 1 回に作り直す死体の上限 (殺し手の CNO を 1 体ずつ出すので、多いと時間がかかる)
    private const int MaxKeep = 4;
    // 殺し手・通報者の CNO が出るまでの待ちの上限
    private const float RoundTimeout = 30f;
    // 後始末で CNO が出し終わるのを待つ上限
    private const float RetireTimeout = 40f;
    // CNO を置く所 (地図の外)
    private static readonly Vector2 KillerPark = new(40f, 42f);
    private static readonly Vector2 ReporterPark = new(45f, 45f);
    private const string BlankSprite = "<size=1> </size>";

    // 位置を戻す SnapTo の通し番号の進め幅。
    // 本人: ExtendedPlayerControl.SendSnapTo と同じ (本人の歩きの電文がホストに届く前でも勝つ)。
    // 他の人と CNO: 小さく進める (大きいと、その人の本物の位置の更新がその数だけ捨てられて止まって見える)。
    // どれもホストの手元の番号をそろえる。
    private const ushort SelfSidStep = 32;
    private const ushort OtherSidStep = 6;
    private const ushort ParentSidStep = 32;

    // SnapTo の財布に残しておく本数 (役職の TP の分)
    private const float BudgetReserve = 30f;
    // CNO を 1 体出す時の SnapTo の本数
    private const int CnoSnapCost = 2;
    // 包みの長さの上限 (分割されない長さ) と、中身の長さの見積もり
    private const int MaxPacketBytes = 1000;
    private const int BaseBytes = 64;
    private const int SnapBytes = 13;
    private const int RebuildBytes = 28;

    // 残す死体が客の画面に入っているかを見る範囲 (画面の半分 + 動いている分の余り)
    private const float ViewHalfWidth = 8f;
    private const float ViewHalfHeight = 4.6f;
    // これより近い死体は、壁越しでも見えるものとして扱う
    private const float AlwaysSeenRange = 2.5f;
    // 死体の絵の端まで見る時の、真ん中からの幅
    private const float BodyHalfWidth = 0.5f;
    // キルの演出の途中の死体がある間と、その後しばらくは送らない (演出の途中の死体を壊すと、客の手元の演出が例外で止まる)
    private const float BodySettle = 1.2f;
    // ジップラインを使い始めてから、乗り終わったとみなすまで
    private const float ZiplineTime = 8f;

    private readonly struct Keep
    {
        public readonly PlayerControl Parent;
        public readonly Vector2 Stand;
        public Keep(PlayerControl parent, Vector2 stand) { Parent = parent; Stand = stand; }
    }

    private sealed class Round
    {
        public readonly List<CustomNetObject> Killers = new();
        public CustomNetObject Reporter;
        public float SendAt = float.PositiveInfinity;
        public float Deadline;
        public bool Sent;
        public float KillersUntil;
    }

    // 合わせ直しを待っている客 (clientId)
    private static readonly HashSet<int> pending = new();
    // 出し終えたら消す CNO と、待ちの期限
    private static readonly List<(CustomNetObject obj, float deadline)> retiring = new();
    private static readonly Dictionary<byte, float> ziplineUntil = new();
    private static Round round;
    private static float nextCheckAt;
    private static float bodyBusyAt = float.NegativeInfinity;
    private static bool ticking;
    private static string lastWhy;

    [GameModuleInitializer]
    public static void Init()
    {
        pending.Clear();
        retiring.Clear();
        ziplineUntil.Clear();
        round = null;
        nextCheckAt = 0f;
        bodyBusyAt = float.NegativeInfinity;
        ticking = false;
        lastWhy = null;
    }

    private static bool Enabled
        => AmongUsClient.Instance && AmongUsClient.Instance.AmHost
           && (Options.ErasedBodySyncVanilla?.GetBool() ?? true)
           && GameOptionsManager.Instance != null && GameOptionsManager.Instance.currentGameMode is GameModes.Normal or GameModes.NormalFools;

    /// <summary>
    /// ホストの手元の死体を消した・動かした後に呼ぶ。本編の客を、画面の死体の合わせ直し待ちに入れる。
    /// 合わせ直せた客には <see cref="DeadBodyEraser.HideCoversFrom"/> と <see cref="DeadBodyMover.HideCoversFrom"/> で覆いを外す。
    /// </summary>
    public static void Request()
    {
        if (!Enabled) return;
        foreach (var pc in Main.AllPlayerControls)
            if (IsVanillaClient(pc)) pending.Add(pc.OwnerId);
        if (pending.Count == 0) return;
        nextCheckAt = Mathf.Max(nextCheckAt, Time.realtimeSinceStartup + StartDelay);
        if (ticking) return;
        ticking = true;
        ScheduleTick();
    }

    /// <summary>会議が決まった時。客の手元の死体は会議で全部消えるので、待ちを捨てる</summary>
    public static void OnReportDeadBody()
    {
        pending.Clear();
        if (round != null) EndRound(Time.realtimeSinceStartup, RoundGap);
    }

    /// <summary>ジップラインを使い始めた人を覚える (乗っている間は、その人の動きを会議の始まりで止めない)</summary>
    public static void OnZiplineUsed(PlayerControl pc)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return;
        ziplineUntil[pc.PlayerId] = Time.realtimeSinceStartup + ZiplineTime;
    }

    private static bool IsVanillaClient(PlayerControl pc)
        => pc && !pc.AmOwner && !CustomNetObject.Is(pc) && pc.Data != null && !pc.Data.Disconnected
           && pc.OwnerId >= 0 && !ModClientSync.IsPeer(pc.OwnerId);

    private static void ScheduleTick()
        => new LateTask(() =>
        {
            if (!ticking) return;
            try { Step(Time.realtimeSinceStartup); }
            catch (System.Exception ex) { Logger.Exception(ex, Tag); }
            if (pending.Count == 0 && round == null && retiring.Count == 0)
            {
                ticking = false;
                return;
            }
            ScheduleTick();
        }, 0.1f, "");

    private static void Step(float now)
    {
        StepRetiring(now);
        if (round != null)
        {
            StepRound(now);
            return;
        }
        if (pending.Count == 0 || now < nextCheckAt) return;
        nextCheckAt = now + CheckInterval;
        if (!Enabled) { pending.Clear(); return; }
        string why = GlobalBlock(now);
        List<Keep> keep = null;
        if (why == null)
        {
            keep = KeepList(now, out bool blocked);
            if (blocked || keep.Count > MaxKeep) why = "bodies";
        }
        // SnapTo の財布が足りない間は、CNO も出さずに待つ (この回に出す殺し手と通報者の分も見込む)
        if (why == null && !SnapToBudget.HasHeadroom(SnapCount(keep.Count) + (keep.Count + 1) * CnoSnapCost + BudgetReserve)) why = "budget";
        if (why == null)
        {
            var sb = new System.Text.StringBuilder();
            bool any = false;
            foreach (var pc in Targets())
            {
                string block = ClientBlock(pc, keep);
                if (block == null) any = true;
                else sb.Append(pc.PlayerId).Append(':').Append(block).Append(' ');
            }
            if (any)
            {
                lastWhy = null;
                StartRound(keep, now);
                return;
            }
            why = sb.ToString();
        }
        // 待っている理由は、変わった時だけ記録する
        if (why == lastWhy) return;
        lastWhy = why;
        Logger.Info($"死体の合わせ直しを待っています ({why})", Tag);
    }

    // ---- 送ってよいか ----

    // 今は誰にも送れない理由 (送れるなら null)
    private static string GlobalBlock(float now)
    {
        if (ShipStatus.Instance == null || !GameStates.IsInTask || !Main.IsSetRoleFinished) return "not in task";
        if (AmongUsClient.Instance.IsGameOver || GameEndChecker.EndingStarted) return "game over";
        // 本物の会議の画面が客に在る時に届くと、本物の会議を壊す
        if (MeetingHud.Instance || ExileController.Instance || MeetingStates.MeetingCalled || AntiBlackout.IsCached || AntiBlackout.IsRevertPending) return "meeting";
        // 役職の送り直しが落ち着く前は、本人の役職の送り直しを重ねない
        if (Revive.IsSettling) return "revive";
        // 動かしている途中の死体は、作り直しが 2 つ重なる
        if (DeadBodyMover.IsBusy) return "moving body";
        if (HudManager.Instance == null || HudManager.Instance.MeetingPrefab == null) return "no hud";
        // はしご・昇降機・ジップラインの途中の人は、会議の始まりを受けた客の画面で動きが止まる
        foreach (var pc in Main.AllAlivePlayerControls)
            if (pc && !CustomNetObject.Is(pc) && IsRiding(pc, now)) return "riding";
        return null;
    }

    private static bool IsRiding(PlayerControl pc, float now)
        => pc.onLadder || pc.inMovingPlat || pc.MyPhysics.Animations.IsPlayingAnyLadderAnimation()
           || (ziplineUntil.TryGetValue(pc.PlayerId, out float until) && now < until);

    // 残す死体 (ホストの手元にある死体) の一覧。作り直せない死体があるなら blocked
    private static List<Keep> KeepList(float now, out bool blocked)
    {
        blocked = false;
        var list = new List<Keep>();
        Vector3 offset = PlayerControl.LocalPlayer.KillAnimations[0].BodyOffset;
        foreach (var body in Object.FindObjectsOfType<DeadBody>())
        {
            if (body == null || !body.gameObject.activeSelf) continue;
            if (!body.enabled) bodyBusyAt = now;
            var viper = body.TryCast<ViperDeadBody>();
            if (viper != null && viper.victimDissolving)
            {
                // 溶けている途中の死体は、作り直すと溶けない死体になる。溶け切った死体は客の画面にも無いので残さない
                if (viper.dissolveCurrentTime > 0f) blocked = true;
                continue;
            }
            var parent = Utils.GetPlayerById(body.ParentId);
            // 作り直しは客の手元で持ち主を殺すので、持ち主が死んでいて、客の手元にも体がある死体だけ
            if (parent == null || parent.Data == null || !parent.Data.IsDead || parent.Data.Disconnected)
            {
                blocked = true;
                continue;
            }
            list.Add(new Keep(parent, (Vector2)(body.transform.position - offset)));
        }
        if (now - bodyBusyAt < BodySettle) blocked = true;
        return list;
    }

    private static List<PlayerControl> Targets()
    {
        var list = new List<PlayerControl>();
        var still = new HashSet<int>();
        foreach (var pc in Main.AllPlayerControls)
        {
            if (!IsVanillaClient(pc) || !pending.Contains(pc.OwnerId)) continue;
            still.Add(pc.OwnerId);
            list.Add(pc);
        }
        // 抜けた客・握手した客は待ちから外す
        pending.IntersectWith(still);
        return list;
    }

    // この客には今は送れない理由 (送れるなら null)
    private static string ClientBlock(PlayerControl pc, List<Keep> keep)
    {
        // 死者は、役職の送り直しで地図のボタンが戻らない。生き返った人も、本人の画面では幽霊のまま
        if (!pc.IsAlive() || Revive.WasRevived(pc.PlayerId) || TempGhost.IsGhost(pc) || SeekerBody.IsMasked(pc.PlayerId)) return "dead";
        // 役職の送り直しは、キルと能力のボタンのクールを始め直す。ボタンを持たない基底だけ
        if (RoleView.Living(pc, pc) != RoleTypes.Crewmate) return "role";
        // ベントの中の本人に送ると、本人の画面でだけ外に出る
        if (pc.inVent || pc.walkingToVent || VentCage.IsCaged(pc) || pc.MyPhysics.Animations.IsPlayingEnterVentAnimation()) return "in vent";
        if (PlayerWalker.IsWalking(pc.PlayerId)) return "walking";
        if (SeesAny(pc, keep)) return "body in view";
        if (EstimateBytes(keep.Count) > MaxPacketBytes) return "too long";
        return null;
    }

    // 残す死体のどれかが、この客の画面に入っているか
    private static bool SeesAny(PlayerControl pc, List<Keep> keep)
    {
        Vector2 from = pc.GetTruePosition();
        foreach (var k in keep)
        {
            Vector2 d = k.Stand - from;
            if (Mathf.Abs(d.x) > ViewHalfWidth || Mathf.Abs(d.y) > ViewHalfHeight) continue;
            if (d.sqrMagnitude <= AlwaysSeenRange * AlwaysSeenRange) return true;
            // 壁の影に入っている死体は映らない。真ん中と左右の端のどれかが見通せれば、映るものとして扱う
            for (int i = -1; i <= 1; i++)
            {
                Vector2 to = k.Stand + new Vector2(i * BodyHalfWidth, 0f);
                if (!PhysicsHelpers.AnythingBetween(from, to, Constants.ShadowMask, false)) return true;
            }
        }
        return false;
    }

    private static int SnapCount(int keepCount)
    {
        int n = keepCount;
        foreach (var pc in Main.AllAlivePlayerControls)
            if (pc && !CustomNetObject.Is(pc)) n++;
        foreach (var _ in CustomNetObject.Bodies) n++;
        return n;
    }

    private static int EstimateBytes(int keepCount)
        => BaseBytes + SnapCount(keepCount) * SnapBytes + keepCount * RebuildBytes;

    // ---- 1 回の合わせ直し ----

    private static void StartRound(List<Keep> keep, float now)
    {
        var r = new Round { Deadline = now + RoundTimeout };
        for (int i = 0; i < keep.Count; i++)
        {
            // 殺し手は演出の終わりに死体の位置へ寄せられるので、絵はその死体の所の床の色にしておく
            var killer = CustomNetObject.Create(DeadBodyEraser.CoverSpriteAt(keep[i].Stand), KillerPark + new Vector2(i, 0f));
            if (killer == null)
            {
                foreach (var k in r.Killers) k.Despawn();
                return;
            }
            r.Killers.Add(killer);
        }
        round = r;
    }

    private static void StepRound(float now)
    {
        var r = round;
        if (r.Sent)
        {
            if (now >= r.KillersUntil) EndRound(now, RoundGap);
            return;
        }
        if (now > r.Deadline)
        {
            Logger.Warn("死体の合わせ直しの CNO が出ないので、やり直します", Tag);
            EndRound(now, RoundGap * 2f);
            return;
        }
        if (r.Reporter == null)
        {
            // 殺し手は、通報者を出す前に出し終えておく
            foreach (var k in r.Killers)
            {
                if (k.IsGone) { EndRound(now, RoundGap); return; }
                if (!k.IsLive) return;
            }
            // 会議が決まった後に、プレイヤーの情報を持たない体を客へ出さない
            if (GlobalBlock(now) != null) return;
            var reporter = CustomNetObject.Create(BlankSprite, ReporterPark);
            if (reporter == null) { EndRound(now, RoundGap); return; }
            reporter.DelayVisibility(ReporterVisibilityHold);
            reporter.Spawned = c =>
            {
                if (round != null && round.Reporter == c) round.SendAt = Time.realtimeSinceStartup + ReporterSendDelay;
            };
            r.Reporter = reporter;
            return;
        }
        if (r.Reporter.IsGone) { EndRound(now, RoundGap); return; }
        if (now < r.SendAt) return;
        if (Send(r, now) == 0)
        {
            EndRound(now, RoundGap * 3f);
            return;
        }
        r.Sent = true;
        r.KillersUntil = now + KillerHold;
    }

    // 殺し手を消し、通報者は出し終えてから消す (可視化の前に止めると、客の手元でプレイヤーの情報の待ちが終わらない)
    private static void EndRound(float now, float gap)
    {
        var r = round;
        round = null;
        nextCheckAt = now + gap;
        if (r == null) return;
        foreach (var k in r.Killers) Retire(k, now);
        if (r.Reporter != null) Retire(r.Reporter, now);
    }

    private static void Retire(CustomNetObject obj, float now)
    {
        if (obj == null || obj.IsGone) return;
        if (obj.IsLive || obj.IsWaiting) { obj.Despawn(); return; }
        retiring.Add((obj, now + RetireTimeout));
    }

    private static void StepRetiring(float now)
    {
        for (int i = retiring.Count - 1; i >= 0; i--)
        {
            var (obj, deadline) = retiring[i];
            if (!obj.IsGone && !obj.IsLive && now < deadline) continue;
            obj.Despawn();
            retiring.RemoveAt(i);
        }
    }

    // 送れる客へ送り、送った客の数を返す
    private static int Send(Round r, float now)
    {
        if (GlobalBlock(now) != null || r.Reporter.NetId == 0) return 0;
        var keep = KeepList(now, out bool blocked);
        // 殺し手を出している間に死体が増えた時は、次の回に回す
        if (blocked || keep.Count > r.Killers.Count) return 0;
        foreach (var k in r.Killers)
            if (!k.IsLive || k.NetId == 0) return 0;

        var client = AmongUsClient.Instance;
        // 位置を戻す SnapTo と、死体の持ち主に送る通し番号。この回の全員に同じ番号を送り、手元は 1 回だけ進める
        var sids = new Dictionary<uint, ushort>();
        ushort[] parentSids = null;
        int sent = 0;
        foreach (var pc in Targets())
        {
            if (ClientBlock(pc, keep) != null) continue;
            int cost = SnapCount(keep.Count);
            if (!SnapToBudget.HasHeadroom(cost + BudgetReserve)) break;
            parentSids ??= ParentSids(keep);

            var m = MessageWriter.Get(SendOption.Reliable);
            m.StartMessage(6);
            m.Write(client.GameId);
            m.WritePacked(pc.OwnerId);
            WriteFakeMeeting(m, r.Reporter.NetId);
            int snaps = WritePositions(m, pc, r.Reporter, sids);
            WriteRpc(m, pc.NetId, RpcCalls.SetRole);
            m.Write((ushort)RoleView.Living(pc, pc));
            m.Write(true);
            m.EndMessage();
            for (int i = 0; i < keep.Count; i++)
                DeadBodyMover.WriteRecreate(m, keep[i].Parent, keep[i].Stand, parentSids[i], r.Killers[i].NetId);
            m.EndMessage();
            int length = m.Length;
            if (length > MaxPacketBytes)
            {
                // 分割されると順番が崩れる。見積もりより長くなった包みは送らない
                Logger.Warn($"{pc.PlayerId} への死体の合わせ直しの包みが長すぎるので送りません ({length} バイト)", Tag);
                m.Recycle();
                continue;
            }
            client.SendOrDisconnect(m);
            m.Recycle();
            for (int i = 0; i < snaps + keep.Count; i++) SnapToBudget.Record();

            pending.Remove(pc.OwnerId);
            DeadBodyEraser.HideCoversFrom(pc.OwnerId);
            DeadBodyMover.HideCoversFrom(pc.OwnerId);
            sent++;
            Logger.Info($"{pc.PlayerId} の画面の死体を合わせ直しました (残す死体 {keep.Count}・位置 {snaps}・{length} バイト)", Tag);
        }
        if (sent == 0) Logger.Info("死体の合わせ直しは、送れる客がいなかったので次へ回します", Tag);
        return sent;
    }

    // 死体の持ち主へ送る通し番号。同じ人の死体が 2 つあれば、2 つ目は 1 つ進める
    private static ushort[] ParentSids(List<Keep> keep)
    {
        var sids = new ushort[keep.Count];
        for (int i = 0; i < keep.Count; i++)
        {
            var nt = keep[i].Parent.NetTransform;
            ushort sid = (ushort)(nt.lastSequenceId + (i == 0 || keep[i - 1].Parent != keep[i].Parent ? ParentSidStep : 1));
            // ホストの手元の番号もそろえる (そろえないと、次にこの人へ送る SnapTo の番号が客の番号より古くなり捨てられる)
            nt.lastSequenceId = sid;
            sids[i] = sid;
        }
        return sids;
    }

    // 会議の画面を出し、プレイヤーの情報を持たない通報者に会議を始めさせ、すぐ会議の画面を消す。
    // 会議の画面を残すと、次の本物の会議の画面が客の手元で壊される
    private static void WriteFakeMeeting(MessageWriter m, uint reporterNetId)
    {
        var client = AmongUsClient.Instance;
        uint hudId = client.NetIdCnt;
        client.NetIdCnt = hudId + 1U;
        m.StartMessage(4);
        m.WritePacked(HudManager.Instance.MeetingPrefab.SpawnId);
        m.WritePacked(-2);
        m.Write((byte)SpawnFlags.None);
        m.WritePacked(1);
        m.WritePacked(hudId);
        m.StartMessage(1);
        m.WritePacked(0);
        m.EndMessage();
        m.EndMessage();
        WriteRpc(m, reporterNetId, RpcCalls.StartMeeting);
        m.Write(byte.MaxValue);
        m.EndMessage();
        m.StartMessage(5);
        m.WritePacked(hudId);
        m.EndMessage();
    }

    // 会議の始まりは、客の手元の体を全部 (CNO も) 会議の席へ動かす。今の位置へ戻す SnapTo を書き、書いた本数を返す。
    // 死者は生きている客の画面に映らないので戻さない
    private static int WritePositions(MessageWriter m, PlayerControl self, CustomNetObject reporter, Dictionary<uint, ushort> sids)
    {
        int n = 0;
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (!pc || CustomNetObject.Is(pc)) continue;
            var nt = pc.NetTransform;
            Vector2 pos = pc.transform.position;
            ushort sid;
            if (pc == self)
            {
                sid = (ushort)(nt.lastSequenceId + SelfSidStep);
                nt.lastSequenceId = sid;
            }
            else sid = RoundSid(nt, sids);
            WriteSnapTo(m, nt, pos, sid);
            n++;
        }
        int clientId = self.OwnerId;
        foreach (var obj in CustomNetObject.Bodies)
        {
            // 通報者は地図の外のまま。この客から消した CNO は、客の手元に無い
            if (obj == reporter || obj.IsHiddenFrom(clientId)) continue;
            var nt = obj.NetTransform;
            Vector2 pos = nt.transform.position;
            WriteSnapTo(m, nt, pos, RoundSid(nt, sids));
            n++;
        }
        return n;
    }

    // この回に、この位置の持ち主へ送る通し番号。ホストの手元も同じ所まで進める
    // (進めないと、送った客の番号だけが先へ行き、ホストが後で小さく進めて送る SnapTo がその客で捨てられる)
    private static ushort RoundSid(CustomNetworkTransform nt, Dictionary<uint, ushort> sids)
    {
        if (sids.TryGetValue(nt.NetId, out ushort sid)) return sid;
        sid = (ushort)(nt.lastSequenceId + OtherSidStep);
        nt.lastSequenceId = sid;
        sids[nt.NetId] = sid;
        return sid;
    }

    private static void WriteSnapTo(MessageWriter m, CustomNetworkTransform nt, Vector2 pos, ushort sid)
    {
        WriteRpc(m, nt.NetId, RpcCalls.SnapTo);
        NetHelpers.WriteVector2(pos, m);
        m.Write(sid);
        m.EndMessage();
    }

    private static void WriteRpc(MessageWriter m, uint netId, RpcCalls call)
    {
        m.StartMessage(2);
        m.WritePacked(netId);
        m.Write((byte)call);
    }
}
