using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL;

public static class GmOptions
{
    public static OptionItem AutoChat;
    public static OptionItem AutoPossess;
    public static OptionItem AutoPossessAliveOnly;
    public static OptionItem AutoPossessPreferImpostor;
    public static OptionItem HideTaskButton;
    public static OptionItem CanSeeRoles;
    public static OptionItem CanSeeTaskProgress;
    public static OptionItem CanSeeVotes;
    public static OptionItem CanSeeDeathReason;
    public static OptionItem CanSeeOverallTaskProgress;
    public static OptionItem CanSeeEmergencyCount;
    public static OptionItem CanSeeKillFlash;

    static OptionItem Make(string name, bool on, OptionItem parent, string color = "#00c1ff")
        => StringOptionItem.Create(name, new string[] { "ColoredOff", "ColoredOn" }, on ? 1 : 0, TabGroup.ModMainSettings, false)
            .SetParent(parent)
            .SetColorcode(color)
            .SetGameMode(CustomGameMode.All);

    public static void Setup(OptionItem gm)
    {
        AutoChat = Make("GMAutoChat", false, gm);
        AutoPossess = Make("GMAutoPossess", false, gm);
        AutoPossessAliveOnly = Make("GMAutoPossessAliveOnly", true, AutoPossess, "#ff3300");
        AutoPossessPreferImpostor = Make("GMAutoPossessPreferImpostor", false, AutoPossessAliveOnly, "#ff3300");
        HideTaskButton = Make("GMHideTaskButton", false, gm);
        CanSeeRoles = Make("GMCanSeeRoles", true, gm);
        CanSeeTaskProgress = Make("GMCanSeeTaskProgress", true, gm);
        CanSeeVotes = Make("GMCanSeeVotes", true, gm);
        CanSeeDeathReason = Make("GMCanSeeDeathReason", true, gm);
        CanSeeOverallTaskProgress = Make("GMCanSeeOverallTaskProgress", true, gm);
        CanSeeEmergencyCount = Make("GMCanSeeEmergencyCount", true, gm);
        CanSeeKillFlash = Make("GMCanSeeKillFlash", true, gm);
    }

    public static bool IsGM(PlayerControl pc) => pc != null && pc.Is(CustomRoles.GM);

    public static bool Get(OptionItem opt, bool fallback = true) => opt?.GetBool() ?? fallback;

    public static string ExtraProgress(PlayerControl seer, PlayerControl seen)
    {
        if (!IsGM(seer) || seen == null) return "";
        var sb = new System.Text.StringBuilder();
        if (Get(CanSeeEmergencyCount) && !TownOfHostL.Core.NativeObject.Same(seer, seen))
        {
            var st = PlayerState.GetByPlayerId(seen.PlayerId);
            if (st != null) sb.Append(Utils.ColorString(new Color32(255, 140, 60, 255), $"[{(int)st.NumberOfRemainingButtons}]"));
        }
        if (Get(CanSeeOverallTaskProgress) && TownOfHostL.Core.NativeObject.Same(seer, seen) && GameData.Instance)
            sb.Append(Utils.ColorString(Color.yellow, $"({GameData.Instance.CompletedTasks}/{GameData.Instance.TotalTasks})"));
        return sb.ToString();
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
public static class GmAutoChatPatch
{
    const float TryInterval = 0.25f;
    const float GiveUpSeconds = 8f;

    public static void Postfix()
    {
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
        if (!GmOptions.Get(GmOptions.AutoChat, false)) return;
        if (!GmOptions.IsGM(PlayerControl.LocalPlayer)) return;
        var hud = HudManager.Instance;
        if (hud == null) return;
        hud.StartCoroutine(CoOpenChat().WrapToIl2Cpp());
    }

    static System.Collections.IEnumerator CoOpenChat()
    {
        var waited = 0f;
        while (waited < GiveUpSeconds)
        {
            if (MeetingHud.Instance == null) yield break;
            var chat = HudManager.Instance ? HudManager.Instance.Chat : null;
            if (chat != null)
            {
                if (chat.IsOpenOrOpening) yield break;
                var ready = chat.chatButton == null || chat.chatButton.gameObject.activeInHierarchy;
                if (chat.gameObject.activeInHierarchy && ready) chat.Toggle();
            }
            yield return new WaitForSeconds(TryInterval);
            waited += TryInterval;
        }
    }
}

public static class GmAutoPossessTiming
{
    const float AirshipDelay = 15f;
    const float OtherDelay = 3f;
    const float RoundStartGrace = 5f;
    public static float LastMeetingEnd = -999f;
    static float? roundStartAt;

    static float Delay => Main.NormalOptions != null && Main.NormalOptions.MapId == 4 ? AirshipDelay : OtherDelay;

    public static bool CanStart()
    {
        if (LastMeetingEnd < 0f)
        {
            roundStartAt ??= Time.realtimeSinceStartup;
            return Time.realtimeSinceStartup - roundStartAt.Value >= RoundStartGrace;
        }
        return Time.realtimeSinceStartup - LastMeetingEnd >= Delay;
    }

    public static void Reset()
    {
        roundStartAt = null;
        LastMeetingEnd = -999f;
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
public static class GmAutoPossessMeetingEndPatch
{
    public static void Postfix()
    {
        GmAutoPossessTiming.LastMeetingEnd = Time.realtimeSinceStartup;
        GmAutoPossessPatch.ResetLatch();
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
public static class GmAutoPossessPatch
{
    const float VerifySeconds = 1.5f;
    const int MaxClickAttempts = 5;
    const float StuckRecoverySeconds = 20f;
    static bool clicked;
    static bool verified;
    static int attempts;
    static float clickedAt = -1f;
    static float stuckSince = -1f;

    static bool HauntMenuOpen()
    {
        try { return Minigame.Instance != null && Minigame.Instance.TryCast<HauntMenuMinigame>() != null; }
        catch { return false; }
    }

    public static void ResetLatch()
    {
        clicked = false;
        verified = false;
        attempts = 0;
        clickedAt = -1f;
    }

    public static void Postfix(PlayerControl __instance)
    {
        if (__instance != PlayerControl.LocalPlayer) return;
        if (!GmOptions.Get(GmOptions.AutoPossess, false)) return;
        if (!GameStates.IsInGame || GameStates.IsLobby) return;
        if (__instance.IsAlive() || MeetingHud.Instance != null) return;
        if (!GmOptions.IsGM(__instance)) return;
        if (!GmAutoPossessTiming.CanStart())
        {
            stuckSince = -1f;
            return;
        }
        if (clicked)
        {
            if (verified) return;
            if (HauntMenuOpen()) { verified = true; return; }
            if (Time.realtimeSinceStartup - clickedAt < VerifySeconds || attempts >= MaxClickAttempts) return;
            clicked = false;
        }
        var hud = HudManager.Instance;
        if (hud == null || hud.AbilityButton == null || !hud.AbilityButton.gameObject.activeInHierarchy)
        {
            stuckSince = -1f;
            return;
        }
        if (!hud.AbilityButton.canInteract)
        {
            if (stuckSince < 0f) stuckSince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - stuckSince >= StuckRecoverySeconds)
            {
                try { hud.AbilityButton.isCoolingDown = false; } catch { }
                stuckSince = -1f;
            }
            return;
        }
        stuckSince = -1f;
        if (!Main.AllAlivePlayerControls.Any(p => p != null && p.PlayerId != __instance.PlayerId)) return;
        Logger.Info("GM の自動憑依: 憑依のボタンを押す", "GmAutoPossess");
        hud.AbilityButton.DoClick();
        clicked = true;
        clickedAt = Time.realtimeSinceStartup;
        attempts++;
        verified = HauntMenuOpen();
        if (GmOptions.Get(GmOptions.AutoPossessPreferImpostor, false))
            hud.StartCoroutine(CoPreferImpostor().WrapToIl2Cpp());
    }

    static System.Collections.IEnumerator CoPreferImpostor()
    {
        for (int i = 0; i < 30; i++)
        {
            var menu = Object.FindObjectOfType<HauntMenuMinigame>(true);
            if (menu != null)
            {
                var pool = GmOptions.Get(GmOptions.AutoPossessAliveOnly)
                    ? Main.AllAlivePlayerControls
                    : Main.AllPlayerControls.Where(p => p != null && p.Data != null && !p.Data.Disconnected);
                var imp = pool.FirstOrDefault(p => p != null && !GmOptions.IsGM(p) && p.Is(CustomRoleTypes.Impostor));
                if (imp != null) menu.HauntTarget = imp;
                yield break;
            }
            yield return null;
        }
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
public static class GmHideTaskPanelPatch
{
    public static void Postfix(HudManager __instance)
    {
        if (!GameStates.IsInGame || GameStates.IsLobby) return;
        if (!GmOptions.Get(GmOptions.HideTaskButton, false)) return;
        if (!GmOptions.IsGM(PlayerControl.LocalPlayer)) return;
        var panel = __instance.TaskPanel;
        if (panel != null && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
    }
}
