using System;
using System.Collections.Generic;
using HarmonyLib;
using TownOfHostL.Modules.Net;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼのポータルメーカー。ペットをかわいがった場所に印 (CNO) を置き、2 つ置くと印どうしがつながる。
/// 誰でも (鬼も) 片方の印に乗るともう片方へ飛ぶ。飛んだ人は、一度印から降りて、少し時間がたつまでもう一度は飛ばない
/// (降りるのを待たないと、着いた印の上に立っているだけで行き来させられる)。
/// 印は置き直さない (1 試合に 2 つまで)。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsPortalMaker
{
    // 濃い紫の枠に桃色の扉 (4×6 マス)。透き間の無い形で、客の画面に出る CNO の体を覆う。
    // 絵は Endless Host Roles (Gurge44, GPL-3.0) の CustomNetObject.Portal から (Gurge44/EndlessHostRoles@97aa6333)
    private const string PortalSprite = "<size=70%><line-height=97%><cspace=0.16em><mark=#2b006b>WWWW</mark>\n<mark=#2b006b>W</mark><mark=#fa69ff>WW</mark><mark=#2b006b>W</mark>\n<mark=#2b006b>W</mark><mark=#fa69ff>WW</mark><mark=#2b006b>W</mark>\n<mark=#2b006b>W</mark><mark=#fa69ff>WW</mark><mark=#2b006b>W</mark>\n<mark=#2b006b>W</mark><mark=#fa69ff>WW</mark><mark=#2b006b>W</mark>\n<mark=#2b006b>WWWW";
    // 2 つ目の印は 1 つ目からこれより離す (近すぎると飛んだ先で踏み直す)
    private const float MinPortalDistance = 4f;
    private const float EnterRange = 1f;
    private const float TeleportCooldown = 5f;
    private const float PetThrottle = 1f;
    private const float CheckInterval = 0.1f;

    private static readonly List<CustomNetObject> portals = new();
    private static readonly Dictionary<byte, float> lastTeleportAt = new();
    private static readonly Dictionary<byte, float> lastPetAt = new();
    // 印の上に立っていて、降りるまで飛ばさない人
    private static readonly HashSet<byte> mustStepOff = new();
    private static float nextCheckAt;

    public static void Reset()
    {
        // CNO は試合の終わりに CustomNetObject の側で消える。ここは参照を捨てるだけ
        portals.Clear();
        lastTeleportAt.Clear();
        lastPetAt.Clear();
        mustStepOff.Clear();
    }

    private static void OnPet(PlayerControl pc)
    {
        if (!pc || pc.Data == null || pc.Data.IsDead || CustomNetObject.Is(pc)) return;
        if (VanillaHideAndSeekRoles.RoleOf(pc.PlayerId) != CustomRoles.HASPortalMaker) return;
        float now = Time.realtimeSinceStartup;
        if (lastPetAt.TryGetValue(pc.PlayerId, out var last) && now - last < PetThrottle) return;
        lastPetAt[pc.PlayerId] = now;
        // かわいがりはホストから止めない。本人がボタンを離すか歩くと本編が自分で止めて、次の電文が来る。
        // 押している間にホストが止めると、離した時の本編の解除 (PlayerPhysics.CheckCancelPetting) が走らず、
        // ペットのボタンが押せないまま残る (2026-11-08 実機で確認)

        if (portals.Count >= 2) return;
        Vector2 pos = pc.GetTruePosition();
        if (portals.Count == 1 && Vector2.Distance(portals[0].Position, pos) < MinPortalDistance) return;
        portals.Add(CustomNetObject.Create(PortalSprite, pos, CustomNetObject.ColorPurple));
        // 置いた人は印の上に立っているので、つながった瞬間に飛ばないよう降りるのを待つ
        mustStepOff.Add(pc.PlayerId);
        Logger.Info($"{pc.Data.PlayerName} が印 {portals.Count} を置きました: {pos}", "VanillaHnsPortalMaker");
    }

    private static void Check()
    {
        if (portals.Count < 2 || !portals[0].IsLive || !portals[1].IsLive) return;
        float now = Time.realtimeSinceStartup;
        if (now < nextCheckAt) return;
        nextCheckAt = now + CheckInterval;

        foreach (var pc in Players.All)
        {
            if (!pc || pc.Data == null || pc.Data.IsDead || pc.Data.Disconnected || CustomNetObject.Is(pc)) continue;
            if (pc.inVent || pc.onLadder || pc.inMovingPlat) continue;

            Vector2 pos = pc.GetTruePosition();
            int near = Vector2.Distance(portals[0].Position, pos) <= EnterRange ? 0
                : Vector2.Distance(portals[1].Position, pos) <= EnterRange ? 1 : -1;
            if (near < 0)
            {
                mustStepOff.Remove(pc.PlayerId);
                continue;
            }
            if (mustStepOff.Contains(pc.PlayerId)) continue;
            if (lastTeleportAt.TryGetValue(pc.PlayerId, out var last) && now - last < TeleportCooldown) continue;

            // TOHL の通常の送信の数え口 (Standard の束) は休んでいるので、ここで払う
            if (!SnapToBudget.CanSend()) return;
            pc.NetTransform.RpcSnapTo(portals[1 - near].Position);
            SnapToBudget.Record();
            lastTeleportAt[pc.PlayerId] = now;
            mustStepOff.Add(pc.PlayerId);
            Logger.Info($"{pc.Data.PlayerName} が印 {near + 1} から印 {2 - near} へ飛びました", "VanillaHnsPortalMaker");
        }
    }

    // 客がペットをかわいがった電文
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc))]
    private static class RemotePetPatch
    {
        public static void Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] byte callId)
        {
            if (callId != (byte)RpcCalls.Pet || !AmongUsClient.Instance.AmHost) return;
            try { OnPet(__instance.myPlayer); }
            catch (Exception ex) { Logger.Exception(ex, "VanillaHnsPortalMaker"); }
        }
    }

    // ホスト自身がかわいがった時 (自分の操作は電文を経ない)
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.TryPet))]
    private static class LocalPetPatch
    {
        public static void Prefix(PlayerControl __instance)
        {
            if (!AmongUsClient.Instance.AmHost || __instance != PlayerControl.LocalPlayer) return;
            try { OnPet(__instance); }
            catch (Exception ex) { Logger.Exception(ex, "VanillaHnsPortalMaker"); }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    private static class CheckPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            // 1 フレームに 1 回だけ回す
            if (__instance != PlayerControl.LocalPlayer || !AmongUsClient.Instance.AmHost || GameSession.Current == null) return;
            try { Check(); }
            catch (Exception ex) { Logger.Exception(ex, "VanillaHnsPortalMaker"); }
        }
    }
}
