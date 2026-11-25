using System.Collections.Generic;
using Hazel;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <remarks>TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Patches/VentLockPatch.cs) を元にした。hamo ではいつも ON だが、L では設定で選べるようにした。</remarks>
public static class VentLock
{
    static OptionItem enabled;

    public static void SetupCustomOption()
    {
        enabled = StringOptionItem.Create("MiraVentLock", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.yellow)
            .SetGameMode(CustomGameMode.Standard);
    }

    static readonly (int, int)[] BlockedPairs =
    {
        (6, 7),
        (6, 5),
        (5, 4),
        (4, 3),
        (3, 9),
        (9, 10),
        (10, 11),
        (10, 8),
        (1, 2),
    };
    static readonly Dictionary<byte, string> LastSent = new();
    static float nextRefresh;
    public static bool ResendAll;
    // 全員向けの送りから外した間に、本当の状態を 1 人ずつ送り直す客 (送る順)
    static readonly List<byte> Pending = new();
    static float nextPendingAt;
    static bool pickVentedTurn;
    // 1 人ずつの送り直しは、全部の客を合わせて 1 秒にこの本数まで (待つ間の変化は送る時の状態で 1 本にまとまる)。
    // 同じ種類の Reliable を毎秒十数本出し続けると公式サーバーに切られるので、人数に比例させない
    const float PendingInterval = 1f / 6f;
    // 偽の状態を持つ客への念のための送り直しの間隔 (全員向けの送りから外している間は上書きされないので長めでよい)
    const float RefreshInterval = 2f;

    public static bool IsActive() =>
        enabled != null && enabled.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard
        && GameOptionsManager.Instance != null && GameOptionsManager.Instance.CurrentGameOptions.MapId == 1;

    public static bool IsBlocked(int a, int b)
    {
        foreach (var (x, y) in BlockedPairs)
            if ((x == a && y == b) || (x == b && y == a)) return true;
        return false;
    }

    public static void Reset()
    {
        LastSent.Clear();
        nextRefresh = 0f;
        Pending.Clear();
        nextPendingAt = 0f;
    }

    static VentilationSystem GetSystem() =>
        ShipStatus.Instance != null && ShipStatus.Instance.Systems.TryGetValue(SystemTypes.Ventilation, out ISystemType sys)
            ? sys.TryCast<VentilationSystem>() : null;

    static bool IsClient(PlayerControl pc) => pc != null && !pc.AmOwner && pc.Data != null && !pc.Data.Disconnected;

    // ホスト以外の誰かがベントの中にいるか。いる間は、全員向けの送りがその人の偽の状態を上書きするので、ベントの状態を 1 人ずつ送る
    static bool AnyClientInVent(VentilationSystem vs)
    {
        foreach (var kv in vs.PlayersInsideVents)
            if (IsClient(Utils.GetPlayerById(kv.Key))) return true;
        return false;
    }

    static List<byte> FakeFor(VentilationSystem vs, PlayerControl pc)
    {
        List<byte> fake = new();
        if (vs.PlayersInsideVents.TryGetValue(pc.PlayerId, out byte inVent))
            foreach (var v in ShipStatus.Instance.AllVents)
                if (v.Id == inVent)
                    foreach (var next in v.NearbyVents)
                        if (next != null && IsBlocked(inVent, next.Id)) fake.Add((byte)next.Id);
        return fake;
    }

    public static void Update()
    {
        var vs = GetSystem();
        if (vs == null) return;

        // 全員向けに換気の系統が出た直後 (上書きされた) は、偽の状態をすぐ送り直す。時間ごとの念押しは列へ回す
        var overwritten = ResendAll;
        var refresh = Time.time >= nextRefresh;
        if (refresh) nextRefresh = Time.time + RefreshInterval;
        ResendAll = false;

        // ふさぐ向きが変わった客には、すぐ送る (その人の操作で変わるので本数はその人のベントの出入りの数まで)。
        // 念のための送り直しは、1 人ずつの列へ回す
        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (!IsClient(pc)) continue;
            var fake = FakeFor(vs, pc);
            var now = string.Join(",", fake);
            LastSent.TryGetValue(pc.PlayerId, out var last);
            if (now == (last ?? "") && !(overwritten && fake.Count > 0))
            {
                if (refresh && fake.Count > 0 && !Pending.Contains(pc.PlayerId)) Pending.Add(pc.PlayerId);
                continue;
            }

            Send(vs, pc, fake);
            LastSent[pc.PlayerId] = now;
            Pending.Remove(pc.PlayerId);
        }

        // 全員向けの送りから外した変化を、1 人ずつ順に送る。
        // 1 本ごとに「ベントの中の客 (ふさぎが見た目に効く)」と「一番長く待っている客」を交互に選び、どちらも待たせ続けない
        if (Pending.Count == 0 || Time.time < nextPendingAt) return;
        var pick = 0;
        pickVentedTurn = !pickVentedTurn;
        if (pickVentedTurn)
            for (var i = 0; i < Pending.Count; i++)
                if (vs.PlayersInsideVents.ContainsKey(Pending[i])) { pick = i; break; }
        var target = Utils.GetPlayerById(Pending[pick]);
        Pending.RemoveAt(pick);
        if (!IsClient(target)) return;
        nextPendingAt = Time.time + PendingInterval;
        var f = FakeFor(vs, target);
        Send(vs, target, f);
        LastSent[target.PlayerId] = string.Join(",", f);
    }

    // 全員向けの送りから換気の系統を外し、送り直す客の列に積む (もう並んでいる客は、送る時の状態で 1 本にまとまる)
    static void Defer(VentilationSystem vs)
    {
        vs.IsDirty = false;
        foreach (var pc in PlayerControl.AllPlayerControls)
            if (IsClient(pc) && !Pending.Contains(pc.PlayerId)) Pending.Add(pc.PlayerId);
    }

    static void Send(VentilationSystem vs, PlayerControl target, List<byte> fake)
    {
        Dictionary<byte, byte> cleaning = new();
        foreach (var kv in vs.PlayersCleaningVents) cleaning[kv.Key] = kv.Value;

        // 掃除している人には、部屋にいる人の番号を借りる (本人以外)。本編の客は、いない人の番号の掃除を読み捨てる
        var next = 0;
        var players = GameData.Instance.AllPlayers;
        foreach (var vent in fake)
        {
            while (next < players.Count && (players[next] == null || players[next].Disconnected
                || players[next].PlayerId == target.PlayerId || cleaning.ContainsKey(players[next].PlayerId))) next++;
            if (next >= players.Count) break;
            cleaning[players[next].PlayerId] = vent;
            next++;
        }

        var w = MessageWriter.Get(SendOption.Reliable);
        w.StartMessage(6);
        w.Write(AmongUsClient.Instance.GameId);
        w.WritePacked(target.OwnerId);
        w.StartMessage(1);
        w.WritePacked(ShipStatus.Instance.NetId);
        w.StartMessage((byte)SystemTypes.Ventilation);
        w.WritePacked(cleaning.Count);
        foreach (var kv in cleaning) { w.Write(kv.Key); w.Write(kv.Value); }
        w.WritePacked(vs.PlayersInsideVents.Count);
        foreach (var kv in vs.PlayersInsideVents) { w.Write(kv.Key); w.Write(kv.Value); }
        w.EndMessage();
        w.EndMessage();
        w.EndMessage();
        AmongUsClient.Instance.SendOrDisconnect(w);
        w.Recycle();
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    static class ResetPatch
    {
        public static void Postfix() => Reset();
    }

    [HarmonyPatch(typeof(VentilationSystem), nameof(VentilationSystem.Serialize))]
    static class SerializePatch
    {
        public static void Postfix() => ResendAll = true;
    }

    // 全員向けの船の状態は ShipStatus.Serialize で書かれ、換気の系統は印 (IsDirty) が立っている時だけ入る。
    // ホスト以外の誰かがベントの中にいる間は印を下ろして全員向けから外し、1 人ずつ送る (客の画面でふさいだベントが一瞬開いて見えないように)
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Serialize))]
    static class ShipSerializePatch
    {
        public static void Prefix([HarmonyArgument(1)] bool initialState)
        {
            if (initialState || !AmongUsClient.Instance.AmHost || !GameStates.IsInTask || !IsActive()) return;
            try
            {
                var vs = GetSystem();
                if (vs == null || !vs.IsDirty || !AnyClientInVent(vs)) return;
                Defer(vs);
            }
            catch (System.Exception e) { Logger.Warn($"ベントの区切りでエラー: {e.Message}", nameof(VentLock)); }
        }
    }

    // 全員向けに換気の系統が送られた時 (ベントの中に客がいない時) は、同じフレームのうちに偽の状態を送り直す
    [HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.SendAllStreamedObjects))]
    static class AfterStreamPatch
    {
        public static void Postfix()
        {
            if (!ResendAll || ShipStatus.Instance == null || !AmongUsClient.Instance.AmHost || !GameStates.IsInTask || !IsActive()) return;
            try { Update(); }
            catch (System.Exception e) { Logger.Warn($"ベントの区切りでエラー: {e.Message}", nameof(VentLock)); }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    static class UpdatePatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            if (__instance != PlayerControl.LocalPlayer || !AmongUsClient.Instance.AmHost || !GameStates.IsInTask || !IsActive()) return;
            try { Update(); }
            catch (System.Exception e) { Logger.Warn($"ベントの区切りでエラー: {e.Message}", nameof(VentLock)); }
        }
    }

    [HarmonyPatch(typeof(Vent), nameof(Vent.UpdateArrows))]
    static class ArrowPatch
    {
        public static void Postfix(Vent __instance)
        {
            if (!IsActive()) return;
            var nearby = __instance.NearbyVents;
            var marks = __instance.CleaningIndicators;
            for (var i = 0; i < nearby.Length && i < marks.Length; i++)
                if (nearby[i] != null && marks[i] != null && IsBlocked(__instance.Id, nearby[i].Id))
                    marks[i].SetActive(true);
        }
    }

    [HarmonyPatch(typeof(Vent), nameof(Vent.TryMoveToVent))]
    static class MovePatch
    {
        public static bool Prefix(Vent __instance, Vent otherVent, ref bool __result)
        {
            if (!IsActive() || otherVent == null || !IsBlocked(__instance.Id, otherVent.Id)) return true;
            __result = false;
            return false;
        }
    }
}
