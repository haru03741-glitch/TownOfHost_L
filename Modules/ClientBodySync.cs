using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
/// 役職の送り直しは、客の手元で本人の役職を作り直し、キルクールと能力のクールを始めの値にする。
/// 送り直しを「作り直しの間だけの設定」と「今の設定」で挟んで元の値にする (緊急会議の残り回数は設定の回数へ戻る)。能力のクールの残りは
/// <see cref="AbilityCooldownClock"/> が数えた値を使う。
/// </para>
/// <para>
/// 送るのは、本人の画面に悪い変化が出ない客だけ: 作り直しで変わる物を元へ戻せる役職
/// (Scientist・Tracker・Detective は戻せない)・変身や透明の途中でない・ベントやはしごの途中でない。
/// 死者は、幽霊の役職の CoSetRole が地図のボタンを出さないので、生きた役職 (クルー) を送ってから同じ包みで幽霊の役職を送る
/// (生きた役職の枝が地図のボタンを出し直し、幽霊の役職の枝が通報のボタンを隠す)。本編の客は canOverride の無い SetRole を
/// 受けたことのある人への幽霊でない SetRole を捨てるので、ホストの手元でその印が立っている死者には送らない。
/// 合わない客は合うまで待ち、その間は覆い (<see cref="DeadBodyEraser"/>) のまま。
/// 送った客からは覆いを外す。会議の始まりが開いている地図とタスクの窓を閉じる点は残る。
/// </para>
/// <para>
/// 会議の始まりを処理した客は、自分を席へ動かす SnapTo を配る (同じ包みの位置の戻しで、その位置自体は捨てられる)。
/// ホストの受け口でこれを見て届いた印にし、来なければ合わせ直し待ちへ戻す (2026-11-27 実機: 送ってから 0.13〜0.30 秒で届いた)。
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
    // 設定の更新 1 つの長さの見積もり
    private const int OptionsBytes = 170;
    // 能力のクールの残りがこれ以下なら、始め直さない (作り直した役職のクールは 0 から始まる)
    private const float MinAbilityCooldown = 0.3f;

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

    // 偽の会議を受けた客は、自分の体を会議の席へ動かす SnapTo を全員へ配る。送ってからこれだけ待っても来なければ、届かなかったとみなす
    private const float AckTimeout = 3f;

    // 合わせ直しを待っている客 (clientId)
    private static readonly HashSet<int> pending = new();
    // 包みを送って、届いた印 (本人の SnapTo) を待っている人: 本人の NetTransform の netId → (clientId, PlayerId, 送った時刻)
    private static readonly Dictionary<uint, (int ClientId, byte PlayerId, float SentAt)> awaitingAck = new();
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
        awaitingAck.Clear();
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
    /// 今消した死体を、本編の客の画面で合わせ直す見込みがあるか。あれば覆いは先に出さず、
    /// 合わせ直せなかった客にだけ後から出す (<see cref="DeadBodyEraser.CoverUnsynced"/>)
    /// </summary>
    public static bool WillSync => Enabled && GameStates.IsInGame && ShipStatus.Instance != null;

    /// <summary>
    /// 待っても画面の死体を合わせ直せない客か。役職や生き返りなど、試合の途中ですぐには変わらない理由だけを見る
    /// (ベントの中・変身の途中などの、しばらく待てば送れる理由は入れない)
    /// </summary>
    public static bool CannotSync(PlayerControl pc)
    {
        if (pc == null || pc.Data == null) return true;
        if (Revive.WasRevived(pc.PlayerId) || TempGhost.IsGhost(pc) || SeekerBody.IsMasked(pc.PlayerId)) return true;
        if (!pc.IsAlive())
            // 死んだばかりで幽霊の役職がまだ付いていない人は、付けば送れるので待つ
            return pc.roleAssigned || pc.Data.Role?.Role is RoleTypes.GuardianAngel;
        return RoleBlock(pc, RoleView.Living(pc, pc)) == "role";
    }

    /// <summary>
    /// ホストの手元の死体を消した・動かした後に呼ぶ。本編の客を、画面の死体の合わせ直し待ちに入れる。
    /// 合わせ直せた客には <see cref="DeadBodyEraser.HideCoversFrom"/> と <see cref="DeadBodyMover.HideCoversFrom"/> で覆いを外す。
    /// </summary>
    public static void Request(HashSet<int> skip = null)
    {
        if (!Enabled || !GameStates.IsInGame || ShipStatus.Instance == null) return;
        foreach (var pc in Main.AllPlayerControls)
            if (IsVanillaClient(pc) && (skip == null || !skip.Contains(pc.OwnerId))) pending.Add(pc.OwnerId);
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
        awaitingAck.Clear();
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
            if (pending.Count == 0 && round == null && retiring.Count == 0 && awaitingAck.Count == 0)
            {
                ticking = false;
                return;
            }
            ScheduleTick();
        }, 0.1f, "");

    private static void Step(float now)
    {
        StepRetiring(now);
        StepAcks(now);
        if (round != null)
        {
            StepRound(now);
            return;
        }
        if (pending.Count == 0 || now < nextCheckAt) return;
        nextCheckAt = now + CheckInterval;
        if (!Enabled) { pending.Clear(); DeadBodyEraser.CoverUnsynced(null, force: true); return; }
        // キルの死体を溶かしている間は、通報者の CNO も覆いも出さない (DissolveKill.IsBusy)
        if (DissolveKill.IsBusy)
        {
            if (lastWhy != "dissolving") Logger.Info("死体の合わせ直しを、溶かしている死体が溶け切るまで待ちます", Tag);
            lastWhy = "dissolving";
            return;
        }
        string why = GlobalBlock(now);
        List<Keep> keep = null;
        if (why == null)
        {
            keep = KeepList(now, out bool blocked);
            if (blocked || keep.Count > MaxKeep) why = "bodies";
        }
        // SnapTo の財布が足りない間は、CNO も出さずに待つ (この回に出す殺し手と通報者の分も見込む)
        if (why == null && !SnapToBudget.HasHeadroom(SnapCount(keep.Count, false) + (keep.Count + 1) * CnoSnapCost + BudgetReserve)) why = "budget";
        if (why == null)
        {
            var sb = new System.Text.StringBuilder();
            HashSet<int> ready = null;
            foreach (var pc in Targets())
            {
                string block = ClientBlock(pc, keep);
                if (block == null) (ready ??= new()).Add(pc.OwnerId);
                else sb.Append(pc.PlayerId).Append(':').Append(block).Append(' ');
            }
            if (ready != null)
            {
                lastWhy = null;
                // 待たせている客には覆いを出す (待ちの上限を過ぎた死体だけ)
                DeadBodyEraser.CoverUnsynced(ready);
                StartRound(keep, now);
                return;
            }
            why = sb.ToString();
        }
        DeadBodyEraser.CoverUnsynced(null);
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
        if (!HudManager.InstanceExists || HudManager.Instance.MeetingPrefab == null) return "no hud";
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
        // ホストの手元で隠した死体もキルの演出が終わるまでは見る (キルと同じ時に消すと、客の手元ではまだ演出の途中)
        foreach (var body in Object.FindObjectsOfType<DeadBody>(true))
        {
            if (body == null) continue;
            if (!body.enabled) bodyBusyAt = now;
            if (!body.gameObject.activeSelf) continue;
            var viper = body.TryCast<ViperDeadBody>();
            if (viper != null && viper.maxDissolveTime > 0f)
            {
                // 溶ける死体は、作り直すと溶けない死体になる。溶け切るまで待つ。
                // 溶け切った死体は客の画面にも無いので残さない (本編は溶け切ると victimDissolving を下ろすが、死体の物は残す)
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
        if (Revive.WasRevived(pc.PlayerId) || TempGhost.IsGhost(pc) || SeekerBody.IsMasked(pc.PlayerId)) return "revived/ghost";
        bool dead = !pc.IsAlive();
        if (dead)
        {
            // 本人の画面に canOverride の無い SetRole が届いていると、生きた役職が捨てられ、地図のボタンが戻らない。
            // 全員宛ての SetRole はホストの手元でも同じ値で CoSetRole を通るので、ホストの手元の印で見る
            if (pc.roleAssigned) return "role locked";
            // 戻す幽霊の役職はクルーかインポスターの幽霊だけ (守護天使などは作り直すと能力の状態が失われる)
            if (pc.Data.Role == null || pc.Data.Role.Role is not (RoleTypes.CrewmateGhost or RoleTypes.ImpostorGhost)) return "ghost role";
        }
        else
        {
            string role = RoleBlock(pc, RoleView.Living(pc, pc));
            if (role != null) return role;
            // ベントの中の本人に送ると、本人の画面でだけ外に出る
            if (pc.inVent || pc.walkingToVent || VentCage.IsCaged(pc) || pc.MyPhysics.Animations.IsPlayingEnterVentAnimation()) return "in vent";
        }
        if (PlayerWalker.IsWalking(pc.PlayerId)) return "walking";
        if (SeesAny(pc, keep)) return "body in view";
        if (EstimateBytes(keep.Count, dead) > MaxPacketBytes) return "too long";
        return null;
    }

    // 役職の送り直しは、客の手元で本人の役職を作り直す。作り直しで変わる物を元へ戻せない役職には送らない (送れるなら null)
    private static string RoleBlock(PlayerControl pc, RoleTypes role)
    {
        switch (role)
        {
            case RoleTypes.Crewmate:
            case RoleTypes.Noisemaker:
            case RoleTypes.Impostor:
            case RoleTypes.Viper:
            case RoleTypes.Engineer:
                break;
            case RoleTypes.Shapeshifter:
                // 変身の残り時間は役職の中にあり、作り直すと自動で戻らなくなる
                if (pc.shapeshifting || pc.CurrentOutfitType == PlayerOutfitType.Shapeshifted
                    || (Main.CheckShapeshift.TryGetValue(pc.PlayerId, out bool shifted) && shifted)) return "shifted";
                break;
            case RoleTypes.Phantom:
                // 透明の残り時間も役職の中にある
                if (pc.shouldAppearInvisible || pc.invisibilityAlpha < 1f) return "vanished";
                break;
            default:
                // Scientist の電池・Tracker の追跡・Detective のメモは、ホストから見えず、作り直すと失われる
                return "role";
        }
        if (!RoleView.IsKillerRole(role) && !AbilityCooldownClock.Tracks(role)) return null;
        // 作り直しで変わる物は設定で元へ戻すので、今の設定を組み立てられる人だけ。
        // 懐中電灯の点け直しの間は、客の設定がかくれんぼ型になっていて、ふだんの設定で上書きできない
        if (Flashlight.IsHolding(pc.PlayerId)) return "flashlight";
        if (PlayerGameOptionsSender.Of(pc.PlayerId) == null) return "no options";
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

    private static int SnapCount(int keepCount, bool withDead)
    {
        int n = keepCount;
        foreach (var pc in PositionHolders(withDead))
            if (pc && !CustomNetObject.Is(pc)) n++;
        foreach (var _ in CustomNetObject.Bodies) n++;
        // 通報者の分 (条件を見る時にはまだ出していない)
        return n + 1;
    }

    private static int EstimateBytes(int keepCount, bool withDead)
        => BaseBytes + OptionsBytes * 2 + SnapCount(keepCount, withDead) * SnapBytes + keepCount * RebuildBytes;

    // 位置を戻す人。会議の始まりは死者も席へ動かし、死者の画面には死者も映るので、宛先が死者なら死者も戻す
    private static IEnumerable<PlayerControl> PositionHolders(bool withDead)
    {
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
            if (withDead || pc.IsAlive()) yield return pc;
        }
    }

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
        var synced = new List<PlayerControl>();
        foreach (var pc in Targets())
        {
            if (ClientBlock(pc, keep) != null) continue;
            bool dead = !pc.IsAlive();
            int cost = SnapCount(keep.Count, dead);
            if (!SnapToBudget.HasHeadroom(cost + BudgetReserve)) break;
            parentSids ??= ParentSids(keep);

            // 死者は、生きた役職 (クルー) で地図のボタンを出させてから、同じ包みで本人から見た幽霊の役職へ戻す
            var role = dead ? RoleTypes.Crewmate : RoleView.Living(pc, pc);
            // 役職の送り直しを「作り直しの間だけの設定」と「今の設定」で挟み、作り直しで変わる物を元の値にする:
            // - キルクールは、本編が 10 秒へ書き換える → 設定のキルクールを 0 にしておく。本編は設定が 0 以下だと
            //   キルクールの残りを書き換えないので、残りがそのまま続く。
            // - 能力のクールは、役職の中にあって 0 になる → 設定のクールを残りの秒数にしておき、守護の電文で設定値から始めさせる。
            // どちらも要らない役職 (キルも能力のクールも無い) には、設定を入れない
            bool killer = RoleView.IsKillerRole(role);
            float ability = AbilityCooldownClock.Tracks(role) ? AbilityCooldownClock.Remaining(pc.PlayerId) : 0f;
            bool restartAbility = ability > MinAbilityCooldown;
            PlayerGameOptionsSender options = null;
            Il2CppStructArray<byte> frozen = null, current = null;
            if (killer || restartAbility)
            {
                options = PlayerGameOptionsSender.Of(pc.PlayerId);
                frozen = options?.BuildBytes(o =>
                {
                    if (killer) o.SetFloat(FloatOptionNames.KillCooldown, 0f);
                    if (restartAbility) o.SetFloat(AbilityCooldownClock.OptionOf(role), ability);
                });
                current = options?.BuildBytes();
                if (frozen == null || current == null) continue;
            }

            var m = MessageWriter.Get(SendOption.Reliable);
            int snaps, length;
            try
            {
                m.StartMessage(6);
                m.Write(client.GameId);
                m.WritePacked(pc.OwnerId);
                WriteFakeMeeting(m, r.Reporter.NetId);
                snaps = WritePositions(m, pc, sids, dead);
                if (frozen != null) PlayerGameOptionsSender.WriteOptions(m, frozen);
                WriteRpc(m, pc.NetId, RpcCalls.SetRole);
                m.Write((ushort)role);
                m.Write(true);
                m.EndMessage();
                if (dead)
                {
                    WriteRpc(m, pc.NetId, RpcCalls.SetRole);
                    m.Write((ushort)RoleView.Ghost(pc, pc));
                    m.Write(true);
                    m.EndMessage();
                }
                if (restartAbility)
                {
                    WriteRpc(m, pc.NetId, RpcCalls.ProtectPlayer);
                    m.WriteNetObject(pc);
                    m.Write(0);
                    m.EndMessage();
                }
                if (current != null) PlayerGameOptionsSender.WriteOptions(m, current);
                for (int i = 0; i < keep.Count; i++)
                    DeadBodyMover.WriteRecreate(m, keep[i].Parent, keep[i].Stand, parentSids[i], r.Killers[i].NetId);
                m.EndMessage();
                length = m.Length;
                if (length > MaxPacketBytes)
                {
                    // 分割されると順番が崩れる。見積もりより長くなった包みは送らない
                    Logger.Warn($"{pc.PlayerId} への死体の合わせ直しの包みが長すぎるので送りません ({length} バイト)", Tag);
                    continue;
                }
                client.SendOrDisconnect(m);
            }
            catch (System.Exception ex)
            {
                // 組んでいる途中で壊れた相手がいた時は、この回をやめる (送らなかった客は待ちに残る)
                Logger.Exception(ex, Tag);
                break;
            }
            finally
            {
                m.Recycle();
            }
            for (int i = 0; i < snaps + keep.Count; i++) SnapToBudget.Record();
            if (current != null) options.NoteSent(current);

            synced.Add(pc);
            pending.Remove(pc.OwnerId);
            awaitingAck[pc.NetTransform.NetId] = (pc.OwnerId, pc.PlayerId, now);
            DeadBodyEraser.HideCoversFrom(pc.OwnerId);
            DeadBodyMover.HideCoversFrom(pc.OwnerId);
            sent++;
            Logger.Info($"{pc.PlayerId} の画面の死体を合わせ直しました ({(dead ? $"死者・{RoleView.Ghost(pc, pc)}" : role.ToString())}・能力のクール {ability:0.0}・残す死体 {keep.Count}・位置 {snaps}・{length} バイト)", Tag);
        }
        if (sent == 0) Logger.Info("死体の合わせ直しは、送れる客がいなかったので次へ回します", Tag);
        else SendSeatFix(synced);
        return sent;
    }

    /// <summary>
    /// 会議の始まりを受けた客は、自分の体を会議の席へ動かし、その位置を全員へ配る。送った客とホストは同じ包みの通し番号で
    /// その位置を捨てるが、送らなかった客 (待っている客・同じ版の客) の画面では席に立ったまま残る。
    /// その客たちへ、送った人の本当の位置を、本人へ送ったのと同じ通し番号で送る (後から届く席の位置は古い番号で捨てられる)
    /// </summary>
    private static void SendSeatFix(List<PlayerControl> synced)
    {
        var client = AmongUsClient.Instance;
        var syncedClients = new HashSet<int>();
        foreach (var pc in synced) syncedClients.Add(pc.OwnerId);
        int fixedClients = 0;
        foreach (var target in Main.AllPlayerControls)
        {
            if (!target || target.AmOwner || CustomNetObject.Is(target) || target.Data == null || target.Data.Disconnected) continue;
            if (target.OwnerId < 0 || syncedClients.Contains(target.OwnerId)) continue;
            if (!SnapToBudget.HasHeadroom(synced.Count + BudgetReserve))
            {
                Logger.Warn("SnapTo の財布が足りないので、会議の席の位置の戻しを送れませんでした", Tag);
                break;
            }
            var m = MessageWriter.Get(SendOption.Reliable);
            try
            {
                m.StartMessage(6);
                m.Write(client.GameId);
                m.WritePacked(target.OwnerId);
                foreach (var pc in synced)
                {
                    if (!pc) continue;
                    var nt = pc.NetTransform;
                    WriteSnapTo(m, nt, pc.transform.position, nt.lastSequenceId);
                }
                m.EndMessage();
                client.SendOrDisconnect(m);
            }
            finally
            {
                m.Recycle();
            }
            for (int i = 0; i < synced.Count; i++) SnapToBudget.Record();
            fixedClients++;
        }
        if (fixedClients > 0) Logger.Info($"合わせ直さなかった {fixedClients} 人の画面へ、会議の席の位置の戻しを送りました ({synced.Count} 人分)", Tag);
    }

    // 届いた印が来ないまま待ちすぎた人は、包みが届かなかったか途中で捨てられたとみなし、合わせ直し待ちへ戻す
    // (外した覆いは、待つ間に CoverUnsynced が出し直す)
    private static void StepAcks(float now)
    {
        if (awaitingAck.Count == 0) return;
        List<uint> expired = null;
        foreach (var (netId, a) in awaitingAck)
            if (now - a.SentAt > AckTimeout) (expired ??= new()).Add(netId);
        if (expired == null) return;
        foreach (var netId in expired)
        {
            var a = awaitingAck[netId];
            awaitingAck.Remove(netId);
            var pc = Utils.GetPlayerById(a.PlayerId);
            if (pc == null || pc.OwnerId != a.ClientId || !IsVanillaClient(pc))
            {
                Logger.Info($"{a.PlayerId} への死体の合わせ直しは届いた印が来ないまま、抜けたか握手したので捨てます", Tag);
                continue;
            }
            Logger.Warn($"{a.PlayerId} への死体の合わせ直しが届いた印が {AckTimeout:0} 秒たっても来ないので、合わせ直し待ちへ戻します", Tag);
            DeadBodyEraser.ForgetSynced(a.ClientId);
            pending.Add(a.ClientId);
        }
    }

    /// <summary>
    /// ホストが客から SnapTo を受けた時 (番号で捨てる前)。合わせ直しを送った本人からなら、偽の会議まで処理された印として記録する
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(CustomNetworkTransform), nameof(CustomNetworkTransform.HandleRpc))]
    private static class AckPatch
    {
        public static void Prefix(CustomNetworkTransform __instance, byte callId)
        {
            if (callId != (byte)RpcCalls.SnapTo || awaitingAck.Count == 0 || !__instance) return;
            if (!awaitingAck.Remove(__instance.NetId, out var a)) return;
            Logger.Info($"{a.PlayerId} への死体の合わせ直しが届きました ({Time.realtimeSinceStartup - a.SentAt:0.00} 秒)", Tag);
        }
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
    private static int WritePositions(MessageWriter m, PlayerControl self, Dictionary<uint, ushort> sids, bool withDead)
    {
        int n = 0;
        foreach (var pc in PositionHolders(withDead))
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
            // この客から消した CNO は、客の手元に無い。通報者も席へ動かされるので、地図の外へ戻す
            // (戻さないと、可視化までの間、名前の無い人が会議の席に立って見える)
            if (obj.IsHiddenFrom(clientId)) continue;
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
