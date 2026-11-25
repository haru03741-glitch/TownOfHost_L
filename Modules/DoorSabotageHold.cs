using System.Collections.Generic;
using HarmonyLib;

namespace TownOfHostL.Modules;

/// <summary>
/// スケルドのドアのサボタージュを、本編の 10 秒より長く閉めておく。
/// </summary>
/// <remarks>
/// スケルドのドア (AutoOpenDoor) は、ホストでも客の手元でも閉めてから 10 秒で勝手に開く。
/// 開いた後で閉め直すと、客の画面では ping ほどの間ドアが開いて通れてしまう (2026-11-25 実機で約 0.2 秒)。
/// そこで開く前に閉めを送り直し、ホストと客の両方の 10 秒の数えを戻し続ける。最後の送り直しを「終わる時刻の 10 秒前」にすると、ちょうど設定の時間で開く。
/// 閉まったままのドアに閉めを送り直しても、画面の動きは出ない。
/// ほかの地図のドアは作業で開けるまで閉まったままなので、ここでは扱わない。
/// </remarks>
public static class DoorSabotageHold
{
    // 閉めておく部屋 → 開く時刻 (Time.time)
    static readonly Dictionary<SystemTypes, float> until = new();
    static readonly Dictionary<SystemTypes, float> lastClose = new();
    // 本編のスケルドのドアが閉まっている時間
    const float AutoOpenSeconds = 10f;
    // 10 秒より短い間隔で送り直す
    const float ResendInterval = 4f;

    static void Record(SystemTypes room)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        var duration = Options.DoorSabotageDuration.GetFloat();
        if (duration <= AutoOpenSeconds || GetAutoDoors() == null) return;
        var now = UnityEngine.Time.time;
        until[room] = now + duration;
        lastClose[room] = now;
    }

    static AutoDoorsSystemType GetAutoDoors()
    {
        var ship = ShipStatus.Instance;
        if (ship == null || !ship.Systems.TryGetValue(SystemTypes.Doors, out var sys)) return null;
        return sys.TryCast<AutoDoorsSystemType>();
    }

    static bool AnyOpen(ShipStatus ship, SystemTypes room)
    {
        foreach (var door in ship.AllDoors)
            if (door != null && door.Room == room && door.IsOpen) return true;
        return false;
    }

    static void Tick()
    {
        var auto = GetAutoDoors();
        if (auto == null) { Clear(); return; }
        var ship = ShipStatus.Instance;
        var now = UnityEngine.Time.time;
        List<SystemTypes> done = null;
        foreach (var (room, end) in until)
        {
            var finalAt = end - AutoOpenSeconds;
            if (now < System.Math.Min(lastClose[room] + ResendInterval, finalAt)) continue;
            // 役職などで途中で開けられた部屋は、それで終わりにする
            if (!AnyOpen(ship, room)) auto.CloseDoorsOfType(room);
            else finalAt = now;
            lastClose[room] = now;
            if (now >= finalAt) (done ??= new()).Add(room);
        }
        if (done != null)
            foreach (var room in done) { until.Remove(room); lastClose.Remove(room); }
    }

    static void Clear()
    {
        until.Clear();
        lastClose.Clear();
    }

    // サボタージュのドアの閉め (インポスターのマップ・配信の視聴者の干渉)。役職の都合などで止められた時は数えない。
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.CloseDoorsOfType))]
    static class RecordPatch
    {
        public static void Postfix([HarmonyArgument(0)] SystemTypes room, bool __runOriginal)
        {
            if (__runOriginal) Record(room);
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    static class ResetPatch
    {
        public static void Postfix() => Clear();
    }

    // 会議の間と後は本編どおり (会議の後は閉めたドアを持ち越さない)
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    static class MeetingPatch
    {
        public static void Postfix() => Clear();
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    static class TickPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            if (until.Count == 0 || __instance != PlayerControl.LocalPlayer || !AmongUsClient.Instance.AmHost) return;
            Tick();
        }
    }
}
