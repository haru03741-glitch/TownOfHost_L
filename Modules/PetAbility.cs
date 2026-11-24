using System;
using System.Collections.Generic;
using HarmonyLib;
using TownOfHostL.Core.Modes;
using TownOfHostL.Modules.Net;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// ペットの能力の下回り (ホストのみ)。
/// 入口: ホスト自身の <c>TryPet</c> と、客の Pet の電文を同じ門に通し、役職 (<see cref="IUsePetButton"/>) と
/// 本編のかくれんぼのポータルメーカーへ渡す。
/// かわいがりはホストから止めない。本人がボタンを離すか歩くと本編が自分で止める。押している間にホストが止めると、
/// 離した時の本編の解除 (PlayerPhysics.CheckCancelPetting) が走らず、ペットのボタンが押せないまま残る (2026-11-08 実機で確認)。
/// ペット: 押す口が無いと能力が使えないので、生きている人にペットが無ければ既定のペットを持たせる (設定 AutoGrantPet)。
/// ペットを持っているかで役職が分からないよう、ペットの能力が無い人にも配る。外見の変更は <see cref="OutfitSync"/> で送る。
/// </summary>
public static class PetAbility
{
    public const string DefaultPetId = "pet_test";
    // 同じ人の押しを受け付ける間隔。押したまま歩いて離すなどで電文が続けて来ても 1 回にする
    private const float PressInterval = 0.5f;

    private static readonly Dictionary<byte, float> lastPressAt = new();

    public static bool AutoGrant => Options.AutoGrantPet?.GetBool() ?? true;

    [Attributes.GameModuleInitializer]
    public static void Init() => lastPressAt.Clear();

    /// <summary>ペットをかわいがった (ホストのみ)。ホスト自身の操作と客の電文の両方がここに来る</summary>
    public static void Pressed(PlayerControl pc)
    {
        if (!AmongUsClient.Instance.AmHost || pc == null) return;
        var data = pc.Data;
        if (data == null || data.IsDead || data.Disconnected) return;
        if (GameStates.IsLobby || GameStates.IsMeeting || ExileController.Instance) return;
        if (pc.inVent || pc.inMovingPlat || pc.onLadder || pc.walkingToVent) return;
        var anim = pc.MyPhysics?.Animations;
        if (anim != null && (anim.IsPlayingEnterVentAnimation() || anim.IsPlayingAnyLadderAnimation())) return;

        float now = Time.realtimeSinceStartup;
        if (lastPressAt.TryGetValue(pc.PlayerId, out var last) && now - last < PressInterval) return;
        lastPressAt[pc.PlayerId] = now;

        if (FirstTurnMeeting.BeforeFirstMeeting) return;
        try { Kedoro.OnPet(pc); }
        catch (Exception ex) { Logger.Exception(ex, "PetAbility"); }
        try { VanillaHnsPortalMaker.OnPet(pc); }
        catch (Exception ex) { Logger.Exception(ex, "PetAbility"); }

        if (pc.GetRoleClass() is not IUsePetButton user || !user.UsesPet || !pc.IsAlive()) return;
        Logger.Info($"{data.PlayerName} がペットの能力を使った", "PetAbility");
        try { user.OnPet(); }
        catch (Exception ex) { Logger.Exception(ex, "PetAbility"); }
    }

    /// <summary>ペットとして数える ID か (空・ペット無しの ID は数えない)</summary>
    public static bool IsPet(string petId)
    {
        if (string.IsNullOrEmpty(petId)) return false;
        return !petId.Equals("pet_EmptyPet", StringComparison.OrdinalIgnoreCase)
            && !petId.Equals("pet_None", StringComparison.OrdinalIgnoreCase)
            && !petId.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 外見を着せ替える時のペット (<see cref="OutfitSync.Apply"/> が使う)。
    /// 付与が有効な試合中は、生きている人のペットを空にしない (カモフラの外見などにはペットが無い)。
    /// </summary>
    public static string KeepPet(PlayerControl target, string petId)
    {
        if (IsPet(petId) || !AutoGrant || !GameStates.IsInGame || GameStates.IsLobby) return petId ?? "";
        var data = target?.Data;
        if (data == null || data.IsDead || data.Disconnected) return petId ?? "";
        return DefaultPetId;
    }

    /// <summary>
    /// 生きていてペットが無ければ持たせる (ホストのみ)。試合の始めに覚えた本人のペットがあればそれを、無ければ既定のペットを。
    /// 本編の客は幽霊になると自分でペットを外し、生き返っても付け直さないので、復活や一時的な幽霊から戻った時にも呼ぶ
    /// </summary>
    public static void EnsurePet(PlayerControl pc)
    {
        if (!AmongUsClient.Instance.AmHost || !AutoGrant) return;
        if (!GameStates.IsInGame || GameStates.IsLobby) return;
        if (pc == null || !pc.IsAlive()) return;
        if (IsPet(pc.Data?.DefaultOutfit?.PetId)) return;
        string petId = Camouflage.PlayerSkins.TryGetValue(pc.PlayerId, out var original) && IsPet(original.PetId) ? original.PetId : DefaultPetId;
        if (SetPet(pc, petId)) Logger.Info($"{pc.Data?.PlayerName} にペットを持たせた: {petId}", "PetAbility");
    }

    /// <summary>死んだ人のペットを外す (ホストのみ)。本編では死んだ人のペットがその場に残る</summary>
    public static void RemovePet(PlayerControl pc)
    {
        // 死んだ直後 (PlayerControl.Die) は TOHL の生死より先に本編の IsDead が立つので、どちらかで死んでいれば外す
        if (!AmongUsClient.Instance.AmHost || pc?.Data == null || (pc.IsAlive() && !pc.Data.IsDead)) return;
        if (string.IsNullOrEmpty(pc.Data.DefaultOutfit?.PetId)) return;
        if (SetPet(pc, "")) Logger.Info($"{pc.Data.PlayerName} が死んだのでペットを外した", "PetAbility");
    }

    // 今の既定の外見のペットだけを変えて送る。カモフラ中などで戻す先の外見も同じペットにそろえる
    private static bool SetPet(PlayerControl pc, string petId)
    {
        if (Camouflage.PlayerSkins.TryGetValue(pc.PlayerId, out var original)) original.PetId = petId;
        var cur = pc.Data?.DefaultOutfit;
        if (cur == null) return false;
        var outfit = new NetworkedPlayerInfo.PlayerOutfit().Set(cur.PlayerName, cur.ColorId, cur.HatId, cur.SkinId, cur.VisorId, petId, cur.NamePlateId);
        return OutfitSync.Apply(pc, outfit);
    }

    private static void EnsureAll()
    {
        foreach (var pc in Main.AllAlivePlayerControls) EnsurePet(pc);
    }

    // ===== 入口 =====
    // 本編のかくれんぼで Standard が休んでいる間もポータルメーカーが使うので、Core の束に置く。電文は送らない

    // ホスト自身 (自分の操作は電文を経ない)。かわいがっている間は本編が TryPet を通さないので、押した時に 1 回だけ来る
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.TryPet)), PatchBundle(PatchBundles.Core)]
    private static class LocalPetPatch
    {
        public static void Prefix(PlayerControl __instance)
        {
            if (!AmongUsClient.Instance.AmHost || !__instance.AmOwner || __instance.petting) return;
            Pressed(__instance);
        }
    }

    // 客がかわいがった電文
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc)), PatchBundle(PatchBundles.Core)]
    private static class RemotePetPatch
    {
        public static void Prefix(PlayerPhysics __instance, [HarmonyArgument(0)] byte callId)
        {
            if (callId != (byte)RpcCalls.Pet || !AmongUsClient.Instance.AmHost) return;
            Pressed(__instance.myPlayer);
        }
    }

    // ===== ペットを持たせる・外す =====

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    private static class IntroPatch
    {
        // 試合開始の直後は PlayerInfo を送れないので、OutfitSync が送れるようになるまで持つ
        public static void Postfix()
        {
            if (AmongUsClient.Instance.AmHost) EnsureAll();
        }
    }

    [HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
    private static class AfterMeetingPatch
    {
        // 会議の間に復活した人など
        public static void Postfix()
        {
            if (AmongUsClient.Instance.AmHost) EnsureAll();
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    private static class MeetingStartPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            foreach (var pc in Main.AllPlayerControls) RemovePet(pc);
        }
    }
}
