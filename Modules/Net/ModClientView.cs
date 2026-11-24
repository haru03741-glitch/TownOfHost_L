using HarmonyLib;
using TownOfHostL.Core.Modes;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Modules.Net;

/// <summary>
/// 同じ版の TOHL のホストの部屋にいる客の画面に、ホストから届いた役職と設定で TOHL の表示を出す。
/// 客の間はホストの処理 (Standard) を外しているので、表示だけをここ (Core) で当て、描き方はホストの画面と同じ処理を呼ぶ。
/// 役職の判定や送る処理は持たない。頭の上の名前はホストが一人ずつ送っている物をそのまま使う。
/// </summary>
[HarmonyPatch, PatchBundle(PatchBundles.Core)]
public static class ModClientView
{
    // ロビーの左上の設定の一覧と、試合中のボタンの見え方。ホストが替わった時は届いた物を捨てる
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update)), HarmonyPostfix]
    public static void HudUpdatePostfix(HudManager __instance)
    {
        if (!TownOfHostL.Core.VanillaHideAndSeek.IsClientMode) return;
        ModClientSync.Tick();
        ApplyHud(__instance);
        if (!ModClientSync.IsLinked) return;
        HudManagerPatch.EnsureOptionShower(__instance);
        HudManagerPatch.UpdateOptionShower();
    }

    private static RoleHudState applied = RoleHudState.None;

    /// <summary>客をやめた時。当てていた見え方の記録を捨てる (画面はもう無いか、本編が作り直す)</summary>
    public static void ForgetHud()
    {
        applied = RoleHudState.None;
        RoleHud.ClearUses();
    }

    /// <summary>
    /// ホストから届いたボタンの見え方を当てる。隠す・字を替える・数を出すだけで、本編が隠したボタンは出さない。
    /// 隠していた物を戻す時は、本編の決め方 (SetHudActive) に任せる
    /// </summary>
    private static void ApplyHud(HudManager hud)
    {
        var client = AmongUsClient.Instance;
        bool started = client && client.IsGameStarted;
        var state = started && ModClientSync.ViewReady ? ModClientSync.RemoteHud : RoleHudState.None;
        var prev = applied;
        if (state.Equals(RoleHudState.None) && prev.Equals(RoleHudState.None)) return;
        var pc = PlayerControl.LocalPlayer;
        if (pc == null || pc.Data == null || hud.KillButton == null || hud.AbilityButton == null) return;
        applied = state;

        // 戻す物は先に本編に出し直させ、その後でまだ隠す物を隠す (逆だと 1 フレームだけ出る)
        bool shown = (prev.HideKill && !state.HideKill) || (prev.HideAbility && !state.HideAbility)
                     || (prev.HideVent && !state.HideVent) || (prev.HideSabotage && !state.HideSabotage);
        bool inTask = started && !MeetingHud.Instance && !ExileController.Instance && !IntroCutscene.Instance;
        if (shown && inTask && !pc.Data.IsDead && pc.Data.Role != null) hud.SetHudActive(pc, pc.Data.Role, true);

        if (state.KillLabel != null) RoleHud.SetButtonText(hud.KillButton, state.KillLabel);
        else if (prev.KillLabel != null) RoleHud.SetButtonText(hud.KillButton, Translator.GetString(StringNames.KillLabel));
        if (state.AbilityLabel != null) RoleHud.SetButtonText(hud.AbilityButton, state.AbilityLabel);
        else if (prev.AbilityLabel != null && pc.Data.Role != null
                 && RoleBase.DefaultAbilityButtonText(pc.Data.Role.Role) is string label)
            RoleHud.SetButtonText(hud.AbilityButton, label);

        if (state.HideKill) hud.KillButton.ToggleVisible(false);
        if (state.HideAbility) hud.AbilityButton.ToggleVisible(false);
        if (state.HideVent && !pc.inVent && hud.ImpostorVentButton != null) hud.ImpostorVentButton.ToggleVisible(false);
        if (state.HideSabotage && hud.SabotageButton != null) hud.SabotageButton.ToggleVisible(false);

        RoleHud.ApplyUses(hud, state);
    }

    // イントロの役職の名前・色・説明
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin)), HarmonyPostfix]
    public static void CoBeginPostfix(IntroCutscene __instance, ref Il2CppSystem.Collections.IEnumerator __result)
    {
        if (!ModClientSync.ViewReady) return;
        var intro = __instance;
        var patcher = new CoroutinPatcher(__result);
        patcher.AddPrefix(typeof(IntroCutscene), nameof(IntroCutscene.ShowRole), () => SetUpRoleTextPatch.Apply(intro));
        __result = patcher.EnumerateWithPatch();
    }

    // イントロの陣営の帯と、一緒に並べる仲間
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginCrewmate)), HarmonyPrefix]
    public static void BeginCrewmatePrefix(IntroCutscene __instance, ref Il2CppSystem.Collections.Generic.List<PlayerControl> teamToDisplay)
    {
        if (ModClientSync.ViewReady) BeginCrewmatePatch.Prefix(__instance, ref teamToDisplay);
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginCrewmate)), HarmonyPostfix]
    public static void BeginCrewmatePostfix(IntroCutscene __instance, ref Il2CppSystem.Collections.Generic.List<PlayerControl> teamToDisplay)
    {
        if (ModClientSync.ViewReady) BeginCrewmatePatch.Postfix(__instance, ref teamToDisplay);
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginImpostor)), HarmonyPrefix]
    public static bool BeginImpostorPrefix(IntroCutscene __instance, ref Il2CppSystem.Collections.Generic.List<PlayerControl> yourTeam)
    {
        return !ModClientSync.ViewReady || BeginImpostorPatch.Prefix(__instance, ref yourTeam);
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginImpostor)), HarmonyPostfix]
    public static void BeginImpostorPostfix(IntroCutscene __instance, ref Il2CppSystem.Collections.Generic.List<PlayerControl> yourTeam)
    {
        if (ModClientSync.ViewReady) BeginImpostorPatch.Postfix(__instance, ref yourTeam);
    }

    // タスクの欄の頭の、自分の役職の名前と説明
    [HarmonyPatch(typeof(TaskPanelBehaviour), nameof(TaskPanelBehaviour.SetTaskText)), HarmonyPostfix]
    public static void SetTaskTextPostfix(TaskPanelBehaviour __instance)
    {
        if (!ModClientSync.ViewReady || GameStates.IsLobby) return;
        TaskPanelBehaviourPatch.PrependRoleInfo(__instance);
    }

    // ホスト: 試合を終える電文の直前に、同じ版の TOHL の客へ結果を送る (終わりの電文の後では客の側で受ける物が無くなる)
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.RpcEndGame)), HarmonyPrefix]
    public static void RpcEndGamePrefix()
    {
        try { ModClientSync.SendEnd(); }
        catch (System.Exception ex) { Logger.Exception(ex, "ModClientSync.SendEnd"); }
    }

    // 結果画面の勝者の文と、全員の役職の一覧
    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp)), HarmonyPostfix]
    public static void SetEverythingUpPostfix(EndGameManager __instance)
    {
        if (!ModClientSync.EndReady) return;
        SetEverythingUpPatch.Postfix(__instance);
        ModClientSync.OnEndShown();
    }
}
