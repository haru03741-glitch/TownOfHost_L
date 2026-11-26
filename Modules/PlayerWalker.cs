using System.Collections.Generic;
using Hazel;
using TownOfHostL.Attributes;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 人を、本人の画面ごと決めた所まで歩かせる (ホストのみ)。
///
/// その人の PlayerPhysics の Pet の電文を送ると、本編の客の画面は電文の座標まで (足元を合わせて) まっすぐ歩き、着くと撫でる。
/// 電文を受けた画面では、ペットが電文の位置へ飛んで止まり、人が着くと手を伸ばして撫でる。これは歩かされていることの目印になるので、
/// 電文は歩かせる本人にだけ送る。ほかの人の画面では、その人は普通に歩いて見え、ペットも本編どおりに付いて歩く。
/// 本人の画面でも、行き先の先を狙わせて着く頃に止め (撫でさせない)、ペットの位置にはホストの手元で付いて歩いているペットの位置を入れる。
/// 歩いている間は本人の操作が効かない。机や壁に当たると着かないまま歩き続けるので、行き先は当たる手前で止める。
/// 受け側は最新の歩きを 1 本だけ覚えていて、CancelPet でそれを止める。止めずに次を送ると古い歩きが会議まで残るので、
/// 送る前に必ず CancelPet を送る。歩かせる相手はペットを持っていないといけない (本編がペットの体を触る)。
/// 本編の RpcPet はホストの手元でも同じ歩きを回すので、電文は自前で組んで客だけへ送る。ホスト自身を歩かせる時は手元で回す。
/// </summary>
public static class PlayerWalker
{
    // 同じ人へ送る間隔の下限 (1 回に CancelPet と Pet の 2 通を全員へ送る)
    private const float MinInterval = 0.25f;
    // 行き先までに当たる物がある時は、その手前この距離で止める
    private const float WallMargin = 0.1f;
    // これより近い行き先は送らない (撫でるだけになる)
    private const float MinStep = 0.15f;
    // 足元の円の半径 (人の当たりより少し細く: 接している面を数えない)
    private const float FootRadius = 0.15f;
    // 行き先のこれだけ先を狙わせる。本編の歩きは狙いの 0.5 手前から緩むので、行き先までは緩まずに歩く
    private const float Overshoot = 0.6f;
    // ホストの手元にペットがいない時の、歩く人の後ろのペットの位置
    private const float PetTrail = 0.5f;

    // 全員分の合計で、直近 1 秒に送る電文の上限。公式サーバーは同じ種類の Reliable を毎秒 10 本なら続けても切らず、
    // 毎秒 15 本を 2 秒続けると切った。操る人が何人いても、他の送信の分を残してこの本数に抑える (区切りの境目で倍にならないよう、滑る窓で数える)
    private const int MaxPerSecond = 8;

    private static readonly Dictionary<byte, float> lastSentAt = new();
    private static readonly HashSet<byte> walking = new();
    private static readonly Queue<float> sentTimes = new();
    // 行き先に着く頃 (歩きを止める時刻)
    private static readonly Dictionary<byte, float> arriveAt = new();

    [GameModuleInitializer]
    public static void Init()
    {
        lastSentAt.Clear();
        walking.Clear();
        sentTimes.Clear();
        arriveAt.Clear();
    }

    // 直近 1 秒の送信の本数に count を足しても上限を超えないか。超えないなら数えて true
    private static bool TakeBudget(int count, float now)
    {
        while (sentTimes.Count > 0 && now - sentTimes.Peek() >= 1f) sentTimes.Dequeue();
        if (sentTimes.Count + count > MaxPerSecond) return false;
        for (int i = 0; i < count; i++) sentTimes.Enqueue(now);
        return true;
    }

    public static bool IsWalking(byte playerId) => walking.Contains(playerId);

    /// <summary>歩かせられない理由。歩かせられるなら null</summary>
    public static string BlockReason(PlayerControl pc)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return "host";
        if (ShipStatus.Instance == null || !GameStates.IsInTask || !Main.IsSetRoleFinished) return "not in task";
        if (AmongUsClient.Instance.IsGameOver || GameEndChecker.EndingStarted) return "game over";
        if (MeetingHud.Instance || ExileController.Instance || MeetingStates.MeetingCalled || AntiBlackout.IsCached || AntiBlackout.IsRevertPending) return "meeting";
        if (!pc.IsAlive() || pc.Data == null || pc.Data.Disconnected) return "dead";
        if (TempGhost.IsGhost(pc)) return "ghost";
        if (!PetAbility.IsPet(pc.Data.DefaultOutfit?.PetId)) return "no pet";
        if (pc.inVent || pc.walkingToVent || VentCage.IsCaged(pc) || pc.MyPhysics.Animations.IsPlayingEnterVentAnimation()) return "in vent";
        if (pc.onLadder || pc.inMovingPlat || pc.MyPhysics.Animations.IsPlayingAnyLadderAnimation()) return "moving";
        return null;
    }

    /// <summary>
    /// pc を足元が target に来るまで歩かせる。途中に当たる物があれば手前で止める。送ったら true。
    /// 前の歩きは止めてから送るので、歩いている途中に呼べば行き先が変わる。
    /// </summary>
    public static bool WalkTo(PlayerControl pc, Vector2 target)
    {
        if (BlockReason(pc) != null) return false;
        float now = Time.realtimeSinceStartup;
        if (lastSentAt.TryGetValue(pc.PlayerId, out var last) && now - last < MinInterval) return false;

        var from = pc.GetTruePosition();
        var to = ClampToWall(pc, from, target, out var wallNormal);
        if ((to - from).sqrMagnitude < MinStep * MinStep)
        {
            // まっすぐは壁に当たって進めない。自分で歩く人は壁に沿って滑るので、壁に沿う分だけで進めるかを見る
            if (wallNormal == Vector2.zero) return false;
            var rest = target - from;
            to = ClampToWall(pc, from, from + rest - Vector2.Dot(rest, wallNormal) * wallNormal, out _);
            if ((to - from).sqrMagnitude < MinStep * MinStep) return false;
        }
        if (!pc.AmOwner && !TakeBudget(2, now)) return false;

        lastSentAt[pc.PlayerId] = now;
        walking.Add(pc.PlayerId);
        float dist = (to - from).magnitude;
        var dir = (to - from) / dist;
        var aim = to + dir * Overshoot;
        var petPos = PetSpot(pc, from - dir * PetTrail);
        // 歩かされる速さは本編の決まった値 (速さの設定は掛からない)。止める電文も同じ道を通るので、送る間隔がそのまま歩く時間になる
        arriveAt[pc.PlayerId] = now + dist / Mathf.Max(0.1f, pc.MyPhysics.Speed);
        if (pc.AmOwner)
        {
            // ホスト自身は電文を経ずに本編の受け口を手元で呼ぶ (本編の更新で形が変わっても他の処理を止めない)
            try
            {
                pc.MyPhysics.CancelPet();
                pc.MyPhysics.PetPet(aim, petPos);
            }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(PlayerWalker)); }
            return true;
        }
        SendCancel(pc);
        var writer = AmongUsClient.Instance.StartRpcImmediately(pc.MyPhysics.NetId, (byte)RpcCalls.Pet, SendOption.Reliable, pc.OwnerId);
        NetHelpers.WriteVector2(aim, writer);
        NetHelpers.WriteVector2(petPos, writer);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
        return true;
    }

    /// <summary>歩かせている人が行き先に着く頃に、歩きを止める (歩かせている間は毎フレーム呼ぶ)</summary>
    public static void Tick(PlayerControl pc)
    {
        if (pc == null || !arriveAt.TryGetValue(pc.PlayerId, out var at)) return;
        float now = Time.realtimeSinceStartup;
        if (now < at) return;
        if (!walking.Contains(pc.PlayerId) || !AmongUsClient.Instance.AmHost || pc.Data == null || pc.Data.Disconnected
            || AmongUsClient.Instance.IsGameOver || GameEndChecker.EndingStarted)
        {
            arriveAt.Remove(pc.PlayerId);
            return;
        }
        if (pc.AmOwner)
        {
            arriveAt.Remove(pc.PlayerId);
            try { pc.MyPhysics.CancelPet(); }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(PlayerWalker)); }
            return;
        }
        // 本数の上限に当たっている間は送らずに待つ (遅れた分だけ狙いの先へ歩き、遅れが長いと着いて撫でる。動けなくはならない)
        if (!TakeBudget(1, now)) return;
        arriveAt.Remove(pc.PlayerId);
        SendCancel(pc);
    }

    // 電文に入れるペットの位置。受けた画面のペットはここへ移る
    private static Vector2 PetSpot(PlayerControl pc, Vector2 fallback)
    {
        try
        {
            var cosmetics = pc.cosmetics;
            var pet = cosmetics != null ? cosmetics.CurrentPet : null;
            if (pet != null) return pet.transform.position;
        }
        catch (System.Exception ex) { Logger.Exception(ex, nameof(PlayerWalker)); }
        return fallback;
    }

    /// <summary>歩かせているなら止める (本人は操作できるように戻る)</summary>
    public static void Stop(PlayerControl pc)
    {
        if (pc == null || !walking.Remove(pc.PlayerId)) return;
        lastSentAt.Remove(pc.PlayerId);
        arriveAt.Remove(pc.PlayerId);
        if (!AmongUsClient.Instance.AmHost || pc.Data == null || pc.Data.Disconnected) return;
        // 試合の終わり際は送らない (相手の歩きは次の試合に持ち越されない)
        if (AmongUsClient.Instance.IsGameOver || GameEndChecker.EndingStarted) return;
        if (pc.AmOwner)
        {
            try { pc.MyPhysics.CancelPet(); }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(PlayerWalker)); }
            return;
        }
        // 止める電文は本数の上限を待たずに送る (止め損ねると相手が動けないままになる)。本数には数える
        float now = Time.realtimeSinceStartup;
        TakeBudget(0, now);
        sentTimes.Enqueue(now);
        SendCancel(pc);
    }

    /// <summary>歩かせている記録だけ消す (会議の始めなど、本編が歩きを止める時)</summary>
    public static void Forget(PlayerControl pc)
    {
        if (pc == null) return;
        walking.Remove(pc.PlayerId);
        lastSentAt.Remove(pc.PlayerId);
        arriveAt.Remove(pc.PlayerId);
    }

    private static void SendCancel(PlayerControl pc)
    {
        if (pc.MyPhysics == null) return;
        var writer = AmongUsClient.Instance.StartRpcImmediately(pc.MyPhysics.NetId, (byte)RpcCalls.CancelPet, SendOption.Reliable, pc.OwnerId);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    /// <summary>
    /// from から target へ足元の円を流し、人の体が当たる物 (壁・机・閉じたドア) があればその手前を返す。
    /// 本編の ShipAndObjectsMask には机 (層 12) が入っていないので、人の層の衝突表から作る。部屋の入り口のトリガーは数えない
    /// wallNormal = 当たった面の向き (当たらなければ zero)
    /// </summary>
    public static Vector2 ClampToWall(PlayerControl pc, Vector2 from, Vector2 target, out Vector2 wallNormal)
    {
        wallNormal = Vector2.zero;
        var dir = target - from;
        float dist = dir.magnitude;
        if (dist < 1e-4f) return from;
        int layer = pc.gameObject.layer;
        int mask = Physics2D.GetLayerCollisionMask(layer) & ~(1 << layer);
        bool queries = Physics2D.queriesHitTriggers;
        Physics2D.queriesHitTriggers = false;
        try
        {
            var hit = Physics2D.CircleCast(from, FootRadius, dir / dist, dist, mask);
            if (!hit.collider) return target;
            wallNormal = hit.normal;
            return from + dir / dist * Mathf.Max(0f, hit.distance - WallMargin);
        }
        finally { Physics2D.queriesHitTriggers = queries; }
    }
}
