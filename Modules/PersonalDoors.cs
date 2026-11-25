using System.Collections.Generic;
using Hazel;
using HarmonyLib;

namespace TownOfHostL.Modules;

/// <summary>
/// 特定の人の画面でだけドアを閉める。ホストの手元のドアは開いたままにし、その人へだけドアの系統の状態を送る。
/// 客はドアの当たり判定を手元の状態で決めるので、閉まって見える人だけが通れなくなる。
/// </summary>
/// <remarks>
/// 本編の送り方 (2026-11-25 逆アセンブルで確認):
/// - スケルドの AutoDoorsSystemType は、変わったドアの印 (AllDoors の番号のビット) を WritePacked で書き、印のあるドアの開閉だけを続ける。
/// - それ以外の DoorsSystemType は、部屋ごとの閉めの待ち時間と、全部のドアの開閉を毎回書く。
/// - PlainDoor.SetDoorway は開閉が同じなら何もしない (音も動きも出ない)。
/// ホストが全員へ送り直すと、閉めた人の画面でもドアが開くので、送り直しの後にその人へ閉めた状態を送り直す。
/// ホスト本人の画面は手元のドアそのものなので、ここでは閉めない。
/// </remarks>
public static class PersonalDoors
{
    static readonly Dictionary<byte, HashSet<int>> closedFor = new();
    // ドアの作業でも開けられないように閉めた人
    static readonly HashSet<byte> taskLocked = new();
    static bool resendPending;
    static float nextKeepAlive;
    static float nextResendAllowed;
    // スケルドの AutoOpenDoor は客の手元でも閉めてから 10 秒で開く。閉めた状態を受け取るたびに 10 秒へ戻るので、10 秒より短い間隔で送り直す。
    const float KeepAliveInterval = 5f;
    // 全員向けの送り直しが続いた時に、同じ人へ毎フレーム送らないための間隔。
    const float MinResendInterval = 0.2f;
    // AutoDoorsSystemType の印は 32 ビット。
    const int AutoDoorsMax = 32;

    public static bool HasAny => closedFor.Count > 0;

    /// <summary>その部屋のドアを、target の画面でだけ閉める。閉めたドアの数を返す。</summary>
    /// <param name="lockAgainstTask">true なら、その人がドアの作業 (ポーラス以降) をしても開かない。</param>
    public static int CloseRoomFor(PlayerControl target, SystemTypes room, bool lockAgainstTask = false)
    {
        var ship = ShipStatus.Instance;
        if (ship == null) return 0;
        List<int> doors = new();
        for (var i = 0; i < ship.AllDoors.Count; i++)
            if (ship.AllDoors[i] != null && ship.AllDoors[i].Room == room) doors.Add(i);
        return CloseFor(target, doors, lockAgainstTask);
    }

    /// <summary>AllDoors の番号で、target の画面でだけドアを閉める。閉めたドアの数を返す。</summary>
    public static int CloseFor(PlayerControl target, IEnumerable<int> doorIndexes, bool lockAgainstTask = false)
    {
        if (!CanSend(target)) return 0;
        var ship = ShipStatus.Instance;
        var max = IsAutoDoors(ship) ? System.Math.Min(ship.AllDoors.Count, AutoDoorsMax) : ship.AllDoors.Count;
        if (!closedFor.TryGetValue(target.PlayerId, out var set)) set = new();
        List<int> touched = new();
        foreach (var i in doorIndexes)
            if (i >= 0 && i < max && ship.AllDoors[i] != null && set.Add(i)) touched.Add(i);
        if (set.Count > 0)
        {
            closedFor[target.PlayerId] = set;
            if (lockAgainstTask) taskLocked.Add(target.PlayerId);
            else taskLocked.Remove(target.PlayerId);
        }
        if (touched.Count > 0) Send(target, touched);
        return touched.Count;
    }

    /// <summary>target の画面でだけ閉めたドアを、ホストの手元の状態へ戻す。</summary>
    public static void ClearFor(PlayerControl target)
    {
        if (target == null) return;
        taskLocked.Remove(target.PlayerId);
        if (!closedFor.Remove(target.PlayerId, out var set) || set.Count == 0) return;
        if (CanSend(target)) Send(target, set);
    }

    public static bool IsClosedFor(byte playerId, int doorIndex) =>
        closedFor.TryGetValue(playerId, out var set) && set.Contains(doorIndex);

    public static void Reset()
    {
        closedFor.Clear();
        taskLocked.Clear();
        resendPending = false;
    }

    /// <summary>会議の後など、客の手元のドアが替わった後に閉めた状態を送り直す。</summary>
    public static void RequestResend()
    {
        if (HasAny) resendPending = true;
    }

    static bool CanSend(PlayerControl target) =>
        AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost && ShipStatus.Instance != null
        && target != null && !target.AmOwner && target.Data != null && !target.Data.Disconnected
        && ShipStatus.Instance.Systems.ContainsKey(SystemTypes.Doors);

    static bool IsAutoDoors(ShipStatus ship) =>
        ship.Systems.TryGetValue(SystemTypes.Doors, out var sys) && sys.TryCast<AutoDoorsSystemType>() != null;

    static void Send(PlayerControl target, IEnumerable<int> touched)
    {
        var ship = ShipStatus.Instance;
        if (!ship.Systems.TryGetValue(SystemTypes.Doors, out var sys)) return;
        var auto = sys.TryCast<AutoDoorsSystemType>();
        var doors = auto == null ? sys.TryCast<DoorsSystemType>() : null;
        if (auto == null && doors == null) return;
        closedFor.TryGetValue(target.PlayerId, out var set);
        bool OpenOf(int i) => (set == null || !set.Contains(i)) && (ship.AllDoors[i] == null || ship.AllDoors[i].IsOpen);

        var w = MessageWriter.Get(SendOption.Reliable);
        try
        {
            w.StartMessage(6);
            w.Write(AmongUsClient.Instance.GameId);
            w.WritePacked(target.OwnerId);
            w.StartMessage(1);
            w.WritePacked(ship.NetId);
            w.StartMessage((byte)SystemTypes.Doors);
            if (auto != null)
            {
                uint mask = 0;
                foreach (var i in touched) if (i < AutoDoorsMax) mask |= 1u << i;
                w.WritePacked(mask);
                for (var i = 0; i < ship.AllDoors.Count && i < AutoDoorsMax; i++)
                    if ((mask & (1u << i)) != 0) w.Write(OpenOf(i));
            }
            else
            {
                w.Write((byte)doors.timers.Count);
                foreach (var kv in doors.timers)
                {
                    w.Write((byte)kv.Key);
                    w.Write(kv.Value);
                }
                for (var i = 0; i < ship.AllDoors.Count; i++) w.Write(OpenOf(i));
            }
            w.EndMessage();
            w.EndMessage();
            w.EndMessage();
            AmongUsClient.Instance.SendOrDisconnect(w);
        }
        finally
        {
            w.Recycle();
        }
    }

    static void ResendAll()
    {
        resendPending = false;
        if (ShipStatus.Instance == null) { Reset(); return; }
        foreach (var (id, set) in new List<KeyValuePair<byte, HashSet<int>>>(closedFor))
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null || pc.Data.Disconnected) { closedFor.Remove(id); taskLocked.Remove(id); continue; }
            if (!CanSend(pc)) continue;
            try { Send(pc, set); }
            catch (System.Exception e) { Logger.Warn($"人ごとのドアの送り直しでエラー ({id}): {e.Message}", nameof(PersonalDoors)); }
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    static class ResetPatch
    {
        public static void Postfix() => Reset();
    }

    // 全員への送り直しはこの後に送られるので、閉めた人への送り直しは次のフレーム以降にする。
    [HarmonyPatch(typeof(AutoDoorsSystemType), nameof(AutoDoorsSystemType.Serialize))]
    static class AutoSerializePatch
    {
        public static void Prefix(AutoDoorsSystemType __instance, bool initialState)
        {
            if (!HasAny || initialState) return;
            var dirty = __instance.dirtyBits;
            foreach (var set in closedFor.Values)
                foreach (var i in set)
                    if (i < AutoDoorsMax && (dirty & (1u << i)) != 0) { resendPending = true; return; }
        }
    }

    [HarmonyPatch(typeof(DoorsSystemType), nameof(DoorsSystemType.Serialize))]
    static class DoorsSerializePatch
    {
        public static void Postfix(bool initialState)
        {
            if (HasAny && !initialState) resendPending = true;
        }
    }

    // 閉めた人がドアの操作 (ポーラス以降の開ける作業) で開けたら、その人の閉めを解く。
    // 作業でも開けられないように閉めた人は解かない。ホストの手元のドアが開いていれば本編の処理も止め、
    // 全員への送り直し (その人の画面で一瞬開く) を起こさない。閉まっていれば (全員向けのサボタージュ) 本編どおり開け、その人へは閉めを送り直す。
    // 役職の都合で本編の処理が止められた時 (別の Prefix が false を返した時) は解かない。
    [HarmonyPatch(typeof(DoorsSystemType), nameof(DoorsSystemType.UpdateSystem))]
    static class OpenByMinigamePatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix([HarmonyArgument(0)] PlayerControl player, [HarmonyArgument(1)] MessageReader msgReader, out int __state)
        {
            __state = -1;
            if (!HasAny || player == null || msgReader == null || !closedFor.TryGetValue(player.PlayerId, out var set)) return true;
            var copy = MessageReader.Get(msgReader);
            __state = copy.ReadByte();
            copy.Recycle();
            if ((__state & DoorsSystemType.OpenDoor) == 0 || !taskLocked.Contains(player.PlayerId)) return true;
            var door = FindDoor(__state & DoorsSystemType.IdMask, set);
            if (door < 0) return true;
            var hostDoor = ShipStatus.Instance.AllDoors[door];
            if (hostDoor != null && hostDoor.IsOpen) { __state = -1; return false; }
            return true;
        }

        public static void Postfix([HarmonyArgument(0)] PlayerControl player, int __state, bool __runOriginal)
        {
            if (__state < 0 || !__runOriginal || (__state & DoorsSystemType.OpenDoor) == 0) return;
            if (!closedFor.TryGetValue(player.PlayerId, out var set)) return;
            if (!taskLocked.Contains(player.PlayerId))
            {
                var doorId = __state & DoorsSystemType.IdMask;
                var ship = ShipStatus.Instance;
                for (var i = 0; i < ship.AllDoors.Count; i++)
                    if (ship.AllDoors[i] != null && ship.AllDoors[i].Id == doorId) set.Remove(i);
                if (set.Count == 0) closedFor.Remove(player.PlayerId);
            }
            resendPending = true;
        }

        static int FindDoor(int doorId, HashSet<int> set)
        {
            var ship = ShipStatus.Instance;
            foreach (var i in set)
                if (i < ship.AllDoors.Count && ship.AllDoors[i] != null && ship.AllDoors[i].Id == doorId) return i;
            return -1;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    static class ResendPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            if (!HasAny || __instance != PlayerControl.LocalPlayer) return;
            var now = UnityEngine.Time.time;
            if (!(resendPending && now >= nextResendAllowed) && now < nextKeepAlive) return;
            nextKeepAlive = now + KeepAliveInterval;
            nextResendAllowed = now + MinResendInterval;
            ResendAll();
        }
    }
}
