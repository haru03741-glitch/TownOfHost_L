using System;
using System.Collections.Generic;
using Hazel;
using HarmonyLib;
using TownOfHostL.Attributes;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 人をベントに閉じ込め、決めた時間の後に別のベントから出す (ホストのみ)。
///
/// その人の PlayerPhysics の EnterVent を全員へ送ると、本編の客の画面でもベントに入って体が消える。
/// ベントのボタンを持たない人は自分では出られない。BootFromVent(別のベント) を送ると、本人の画面ごとそのベントへ移って出てくる。
/// ベントのボタンを持つ人 (インポスターの見え方・エンジニア) は自分で出られる。出た時はそこで閉じ込めを終える。
///
/// 閉じ込めは役職の「ベントに入った」を起こさない (CoEnterVentPatch が Forcing を見て素通しする)。
/// 会議の前・死んだ時・切断した時に解く。ベントの中の人はキルできない (CheckMurderPatch の inVent)。
/// </summary>
public static class VentCage
{
    private sealed class Entry
    {
        public int VentId;
        public float ReleaseAt;
        public Action<PlayerControl> OnReleased;
    }

    // 同時に閉じ込める人数の上限。会議の前に全員を出す時、同じフレームに出す BootFromVent の本数を抑える
    private const int MaxCaged = 4;

    private static readonly Dictionary<byte, Entry> caged = new();
    private static readonly List<byte> scratch = new();

    /// <summary>閉じ込めの EnterVent をホストの手元で走らせている間 true (CoEnterVentPatch が役職の処理と押し出しを飛ばす)</summary>
    public static bool Forcing { get; private set; }

    [GameModuleInitializer]
    public static void Init() => caged.Clear();

    public static bool IsCaged(byte playerId) => caged.Count != 0 && caged.ContainsKey(playerId);
    public static bool IsCaged(PlayerControl pc) => pc != null && IsCaged(pc.PlayerId);

    /// <summary>閉じ込められない理由。閉じ込められるなら null</summary>
    public static string BlockReason(PlayerControl pc)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return "host";
        if (ShipStatus.Instance == null || !GameStates.IsInTask || !Main.IsSetRoleFinished) return "not in task";
        if (AmongUsClient.Instance.IsGameOver || GameEndChecker.EndingStarted) return "game over";
        if (MeetingHud.Instance || ExileController.Instance || MeetingStates.MeetingCalled || AntiBlackout.IsCached || AntiBlackout.IsRevertPending) return "meeting";
        if (!pc.IsAlive() || pc.Data == null || pc.Data.Disconnected) return "dead";
        if (IsCaged(pc)) return "already caged";
        if (caged.Count >= MaxCaged) return "too many";
        if (TempGhost.IsGhost(pc)) return "ghost";
        if (pc.inVent || pc.walkingToVent || pc.MyPhysics.Animations.IsPlayingEnterVentAnimation()) return "in vent";
        if (pc.onLadder || pc.inMovingPlat || pc.MyPhysics.Animations.IsPlayingAnyLadderAnimation()) return "moving";
        return null;
    }

    /// <summary>
    /// pc を一番近いベントに閉じ込め、seconds 秒後に別のベントから出す。閉じ込めたら true。
    /// onReleased は出した後に呼ぶ (自分で出た時・会議で解いた時・死んだ時も。切断した時は呼ばない)。
    /// </summary>
    public static bool Trap(PlayerControl pc, float seconds, Action<PlayerControl> onReleased = null)
    {
        if (BlockReason(pc) is { } reason)
        {
            Logger.Info($"閉じ込められない ({reason}): {pc?.GetNameWithRole()}", nameof(VentCage));
            return false;
        }
        var vent = NearestVent(pc.GetTruePosition());
        if (vent == null) return false;
        // 本編の CoEnterVent は、本人の画面でベントまでまっすぐ歩かせる。壁を挟むと歩きが詰まるので、先にベントの上へ置く。
        // 公式サーバーで SnapTo の送りすぎを抑えている時は、閉じ込めをやめる
        if (!pc.RpcSnapTo((Vector2)vent.transform.position + new Vector2(0f, 0.3636f))) return false;

        caged[pc.PlayerId] = new Entry { VentId = vent.Id, ReleaseAt = Time.time + seconds, OnReleased = onReleased };
        Logger.Info($"閉じ込める: {pc.GetNameWithRole()} → {vent.name}({vent.Id}) {seconds}s", nameof(VentCage));

        var writer = AmongUsClient.Instance.StartRpcImmediately(pc.MyPhysics.NetId, (byte)RpcCalls.EnterVent, SendOption.Reliable, -1);
        writer.WritePacked(vent.Id);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
        Forcing = true;
        try { pc.MyPhysics.StartCoroutine(pc.MyPhysics.CoEnterVent(vent.Id)); }
        finally { Forcing = false; }
        return true;
    }

    /// <summary>閉じ込めを解き、ランダムな別のベントから出す</summary>
    public static void Release(PlayerControl pc)
    {
        if (pc == null || !caged.Remove(pc.PlayerId, out var entry)) return;
        var exit = PickExitVent(entry.VentId);
        Logger.Info($"出す: {pc.GetNameWithRole()} → {exit}", nameof(VentCage));
        pc.MyPhysics.RpcBootFromVent(exit);
        InvokeReleased(pc, entry);
    }

    /// <summary>通報で会議が開く前に全員を出す。会議の間にベントの中に残すと、会議明けに出られなくなる</summary>
    public static void ReleaseAllForMeeting()
    {
        if (caged.Count == 0) return;
        scratch.Clear();
        scratch.AddRange(caged.Keys);
        foreach (var id in scratch)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null || pc.Data.Disconnected) caged.Remove(id);
            else Release(pc);
        }
    }

    /// <summary>
    /// 一番近いベントから出す (壁にはまった人を戻す)。本人の画面ごとベントの位置へ移る。送ったら true。
    /// 梯子・動く床の途中は本編の動きと競るので送らない。閉じ込めている人は、ここで閉じ込めを解く
    /// </summary>
    public static bool BootToNearestVent(PlayerControl pc)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost || ShipStatus.Instance == null || !GameStates.IsInTask) return false;
        if (MeetingHud.Instance || ExileController.Instance || MeetingStates.MeetingCalled || AntiBlackout.IsCached || AntiBlackout.IsRevertPending) return false;
        if (!pc.IsAlive() || pc.Data == null || pc.Data.Disconnected) return false;
        if (pc.onLadder || pc.inMovingPlat || pc.MyPhysics.Animations.IsPlayingAnyLadderAnimation()) return false;
        var vent = NearestVent(pc.GetTruePosition());
        if (vent == null) return false;
        caged.Remove(pc.PlayerId, out var entry);
        Logger.Info($"一番近いベントから出す: {pc.GetNameWithRole()} → {vent.name}({vent.Id})", nameof(VentCage));
        pc.MyPhysics.RpcBootFromVent(vent.Id);
        if (entry != null) InvokeReleased(pc, entry);
        return true;
    }

    private static void InvokeReleased(PlayerControl pc, Entry entry)
    {
        try { entry.OnReleased?.Invoke(pc); }
        catch (Exception ex) { Logger.Exception(ex, nameof(VentCage)); }
    }

    private static Vent NearestVent(Vector2 pos)
    {
        Vent nearest = null;
        float best = float.MaxValue;
        foreach (var vent in ShipStatus.Instance.AllVents)
        {
            float d = ((Vector2)vent.transform.position - pos).sqrMagnitude;
            if (d < best) { best = d; nearest = vent; }
        }
        return nearest;
    }

    private static int PickExitVent(int entryId)
    {
        var vents = ShipStatus.Instance?.AllVents;
        if (vents == null || vents.Length < 2) return entryId;
        int pick = IRandom.Instance.Next(vents.Length - 1);
        // 入ったベントを飛ばして選ぶ
        foreach (var vent in vents)
        {
            if (vent.Id == entryId) continue;
            if (pick-- == 0) return vent.Id;
        }
        return entryId;
    }

    private static void Update()
    {
        if (caged.Count == 0 || !AmongUsClient.Instance.AmHost) return;
        // 試合の終わりの後は送らない
        if (!GameStates.IsInGame || AmongUsClient.Instance.IsGameOver || GameEndChecker.EndingStarted) { caged.Clear(); return; }
        scratch.Clear();
        scratch.AddRange(caged.Keys);
        foreach (var id in scratch)
        {
            if (!caged.TryGetValue(id, out var entry)) continue;
            var pc = Utils.GetPlayerById(id);
            // 切断した人は記録だけ消す (onReleased は呼ばない)
            if (pc == null || pc.Data == null || pc.Data.Disconnected)
            {
                caged.Remove(id);
                continue;
            }
            // 死んだ人はその場のベントから出す (ベントの中に幽霊が残らないように)
            if (!pc.IsAlive())
            {
                caged.Remove(id);
                pc.MyPhysics.RpcBootFromVent(entry.VentId);
                InvokeReleased(pc, entry);
                continue;
            }
            // 会議の間は送らない。通報を通らずに開いた会議は、本編が会議の始めに全員をベントから出す
            if (MeetingHud.Instance || ExileController.Instance || MeetingStates.MeetingCalled)
            {
                caged.Remove(id);
                InvokeReleased(pc, entry);
                continue;
            }
            if (Time.time >= entry.ReleaseAt) Release(pc);
        }
    }

    /// <summary>ベントから出た時 (ベントのボタンを持つ人が自分で出た・ほかの処理が BootFromVent で出した) は、閉じ込めを終える</summary>
    public static void OnExitVent(PlayerPhysics physics)
    {
        // Release と死んだ時の出しは記録を消してから送るので、ここでは拾わない
        if (caged.Count == 0 || physics?.myPlayer == null) return;
        if (!caged.Remove(physics.myPlayer.PlayerId, out var entry)) return;
        Logger.Info($"ベントから出た: {physics.myPlayer.GetNameWithRole()}", nameof(VentCage));
        InvokeReleased(physics.myPlayer, entry);
    }

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    private static class DrivePatch
    {
        public static void Postfix() => Update();
    }
}
