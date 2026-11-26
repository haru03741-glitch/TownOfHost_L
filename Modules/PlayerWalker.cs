using System.Collections.Generic;
using Hazel;
using TownOfHostL.Attributes;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 人を、本人の画面ごと決めた所まで歩かせる (ホストのみ)。
///
/// その人の PlayerPhysics の Pet の電文を送ると、本編の客の画面は電文の座標まで (足元を合わせて) まっすぐ歩き、着くと撫でる。
/// 歩いている間は本人の操作が効かない。机や壁に当たると着かないまま歩き続けるので、行き先は当たる手前で止める。
/// 受け側は最新の歩きを 1 本だけ覚えていて、CancelPet でそれを止める。止めずに次を送ると古い歩きが会議まで残るので、
/// 送る前に必ず CancelPet を送る。歩かせる相手はペットを持っていないといけない (本編がペットの体を触る)。
/// 本編の RpcPet はホストの手元でも同じ歩きを回すので、電文は自前で組んで客だけへ送る。ホスト自身を歩かせる時は手元で回す。
/// </summary>
public static class PlayerWalker
{
    // 同じ人へ送る間隔の下限。1 回に CancelPet と Pet の 2 通を全員へ送るので、秒に 8 通までにする
    private const float MinInterval = 0.25f;
    // 行き先までに当たる物がある時は、その手前この距離で止める
    private const float WallMargin = 0.1f;
    // これより近い行き先は送らない (撫でるだけになる)
    private const float MinStep = 0.15f;
    // 足元の円の半径 (人の当たりより少し細く: 接している面を数えない)
    private const float FootRadius = 0.15f;

    private static readonly Dictionary<byte, float> lastSentAt = new();
    private static readonly HashSet<byte> walking = new();

    [GameModuleInitializer]
    public static void Init()
    {
        lastSentAt.Clear();
        walking.Clear();
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
        var to = ClampToWall(pc, from, target);
        if ((to - from).sqrMagnitude < MinStep * MinStep) return false;

        lastSentAt[pc.PlayerId] = now;
        walking.Add(pc.PlayerId);
        // 撫でる手はペットの方へ伸びる。行き先の少し先に置き、歩いた向きのまま撫でさせる
        var petPos = to + (to - from).normalized * 0.5f;
        if (pc.AmOwner)
        {
            pc.MyPhysics.CancelPet();
            pc.MyPhysics.PetPet(to, petPos);
            return true;
        }
        SendCancel(pc);
        var writer = AmongUsClient.Instance.StartRpcImmediately(pc.MyPhysics.NetId, (byte)RpcCalls.Pet, SendOption.Reliable, -1);
        NetHelpers.WriteVector2(to, writer);
        NetHelpers.WriteVector2(petPos, writer);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
        return true;
    }

    /// <summary>歩かせているなら止める (本人は操作できるように戻る)</summary>
    public static void Stop(PlayerControl pc)
    {
        if (pc == null || !walking.Remove(pc.PlayerId)) return;
        lastSentAt.Remove(pc.PlayerId);
        if (!AmongUsClient.Instance.AmHost || pc.Data == null || pc.Data.Disconnected) return;
        if (pc.AmOwner)
        {
            pc.MyPhysics.CancelPet();
            return;
        }
        SendCancel(pc);
    }

    /// <summary>歩かせている記録だけ消す (会議の始めなど、本編が歩きを止める時)</summary>
    public static void Forget(PlayerControl pc)
    {
        if (pc == null) return;
        walking.Remove(pc.PlayerId);
        lastSentAt.Remove(pc.PlayerId);
    }

    private static void SendCancel(PlayerControl pc)
    {
        var writer = AmongUsClient.Instance.StartRpcImmediately(pc.MyPhysics.NetId, (byte)RpcCalls.CancelPet, SendOption.Reliable, -1);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    /// <summary>
    /// from から target へ足元の円を流し、人の体が当たる物 (壁・机・閉じたドア) があればその手前を返す。
    /// 本編の ShipAndObjectsMask には机 (層 12) が入っていないので、人の層の衝突表から作る。部屋の入り口のトリガーは数えない
    /// </summary>
    public static Vector2 ClampToWall(PlayerControl pc, Vector2 from, Vector2 target)
    {
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
            return from + dir / dist * Mathf.Max(0f, hit.distance - WallMargin);
        }
        finally { Physics2D.queriesHitTriggers = queries; }
    }
}
