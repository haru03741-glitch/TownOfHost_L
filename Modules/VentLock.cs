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
    // 全員への送りの直後に送り直した後の、念押しの送り直しの時刻 (0 = なし)
    static float followUpAt;
    const float FollowUpDelay = 0.05f;

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
        followUpAt = 0f;
    }

    public static void Update()
    {
        if (!ShipStatus.Instance.Systems.TryGetValue(SystemTypes.Ventilation, out ISystemType sys)) return;
        var vs = sys.TryCast<VentilationSystem>();
        if (vs == null) return;

        var refresh = ResendAll || Time.time >= nextRefresh;
        if (Time.time >= nextRefresh) nextRefresh = Time.time + 1f;
        ResendAll = false;

        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc == null || pc.AmOwner || pc.Data == null || pc.Data.Disconnected) continue;

            List<byte> fake = new();
            if (vs.PlayersInsideVents.TryGetValue(pc.PlayerId, out byte inVent))
                foreach (var v in ShipStatus.Instance.AllVents)
                    if (v.Id == inVent)
                        foreach (var next in v.NearbyVents)
                            if (next != null && IsBlocked(inVent, next.Id)) fake.Add((byte)next.Id);

            var now = string.Join(",", fake);
            LastSent.TryGetValue(pc.PlayerId, out var last);
            if (now == (last ?? "") && !(refresh && fake.Count > 0)) continue;

            Send(vs, pc, fake);
            LastSent[pc.PlayerId] = now;
        }
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

    // 全員へのベントの状態は SendAllStreamedObjects の中で書かれる。同じフレームのうちに偽の状態を送り直し、
    // 客の画面でふさいだベントが開いて見える間を縮める。2 つは別の電文で届く順が入れ替わることがあるので、少し後にもう一度送る。
    [HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.SendAllStreamedObjects))]
    static class AfterStreamPatch
    {
        public static void Postfix()
        {
            if (!ResendAll || ShipStatus.Instance == null || !AmongUsClient.Instance.AmHost || !GameStates.IsInTask || !IsActive()) return;
            followUpAt = Time.time + FollowUpDelay;
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
            if (followUpAt > 0f && Time.time >= followUpAt) { followUpAt = 0f; ResendAll = true; }
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
