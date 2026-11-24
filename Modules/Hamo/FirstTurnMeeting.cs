using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem FirstTurnMeeting;
    public static OptionItem FirstTurnMeetingCantability;
    public static OptionItem FirstTurnMeetingChatBlock;
    public static bool firstturnmeeting;

    static void SetupFirstTurnMeetingOptions()
    {
        FirstTurnMeeting = BooleanOptionItem.Create("FirstTurnMeeting", false, TabGroup.ModMainSettings, false)
            .SetColorcode("#4fd6a7");
        FirstTurnMeetingCantability = BooleanOptionItem.Create("FirstTurnMeetingCantability", false, TabGroup.ModMainSettings, false)
            .SetParent(FirstTurnMeeting);
        FirstTurnMeetingChatBlock = new ChatBlockSecondsOptionItem("FirstTurnMeetingChatBlock", 0, TabGroup.ModMainSettings)
            .SetValueFormat(OptionFormat.Seconds)
            .SetParent(FirstTurnMeeting);
    }
}

public sealed class ChatBlockSecondsOptionItem : IntegerOptionItem
{
    public ChatBlockSecondsOptionItem(string name, int defaultValue, TabGroup tab)
        : base(name, defaultValue, tab, false, new(0, FirstMeetingChatBlock.UntilEnd, 1)) { }

    string Label(string normal)
    {
        var v = GetInt();
        if (v >= FirstMeetingChatBlock.UntilEnd) return Translator.GetString("FirstMeetingChatBlockUntilEndValue");
        if (v <= 0) return Translator.GetString("ColoredOff");
        return normal;
    }
    public override string GetString() => Label(base.GetString());
}

public static class FirstTurnMeeting
{
    const float Step = 0.25f;
    const float MaxWait = 15f;
    public const float IntroGrace = 1.5f;

    public static bool InFirst => Options.firstturnmeeting && MeetingStates.First;
    public static bool BeforeFirstMeeting => Options.firstturnmeeting && MeetingStates.FirstMeeting && !GameStates.IsMeeting;
    public static bool CantAbilityNow => Options.firstturnmeeting && Options.FirstTurnMeetingCantability.GetBool() && MeetingStates.FirstMeeting;

    [GameModuleInitializer]
    public static void Init()
    {
        Options.firstturnmeeting = Options.FirstTurnMeeting.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard;
        MeetingStates.First = true;
        FirstMeetingChatBlock.Reset();
    }

    public static bool AllowKill(MurderInfo info, CustomDeathReason reason)
    {
        if (!Options.firstturnmeeting || !MeetingStates.FirstMeeting) return true;
        Logger.Info($"{info.AppearanceKiller?.GetNameWithRole().RemoveHtmlTags()} が強制会議の前にキルしようとしたので止めた", "FirstTurnMeeting");
        return false;
    }

    public static void OnExileFinished()
    {
        MeetingStates.First = false;
    }

    public static void StartWhenSpawned(float waited)
    {
        if (!AmongUsClient.Instance.AmHost || GameStates.IsLobby || GameStates.IsMeeting || MeetingStates.MeetingCalled) return;
        if (!GameStates.IsInGame) return;
        var notSpawned = Main.AllAlivePlayerControls.Count(pc => pc != null && !(PlayerState.GetByPlayerId(pc.PlayerId)?.HasSpawned ?? true));
        if (notSpawned > 0 && waited < MaxWait)
        {
            _ = new LateTask(() => StartWhenSpawned(waited + Step), Step, "FirstTurnMeeting.Wait");
            return;
        }
        if (notSpawned > 0) Logger.Warn($"湧いていない人が {notSpawned} 人いますが、{MaxWait} 秒待ったので 1 ターン目の会議を始めます", "FirstTurnMeeting");
        _ = new LateTask(() => ReportDeadBodyPatch.ExReportDeadBody(PlayerControl.LocalPlayer, PlayerControl.LocalPlayer.Data, false, "Firstmeetinginfo", "#23dbc0"),
            waited > 0f ? 0.5f : 0f, "FirstTurnMeeting");
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    static class IntroPatch
    {
        public static void Postfix()
        {
            if (!GameStates.IsInGame || !AmongUsClient.Instance.AmHost || !Options.firstturnmeeting) return;
            _ = new LateTask(() => StartWhenSpawned(0f), IntroGrace + Main.LagTime, "FirstTurnMeeting.Start");
        }
    }
}

public static class FirstMeetingChatBlock
{
    public const int UntilEnd = 1000;
    static bool active;
    static float startedAt;
    static bool hiding;
    static int Seconds => Options.FirstTurnMeetingChatBlock?.GetInt() ?? 0;

    /// <summary>会議の途中で試合が終わった時に、止めている印を次の試合へ持ち越さない</summary>
    public static void Reset() { active = false; hiding = false; }

    public static bool IsBlocking
        => active && GameStates.IsMeeting && (Seconds >= UntilEnd || Time.time - startedAt < Seconds);

    public static bool ShouldBlock(string text)
        => IsBlocking && !string.IsNullOrWhiteSpace(text) && !text.TrimStart().StartsWith("/");

    static string Notice => Seconds >= UntilEnd
        ? Translator.GetString("FirstMeetingChatBlockedUntilEnd")
        : string.Format(Translator.GetString("FirstMeetingChatBlockedSeconds"), Seconds);

    public static void ShowLocalNotice()
    {
        try { HudManager.Instance?.Chat?.AddChat(PlayerControl.LocalPlayer, Notice); } catch { }
    }

    static bool CanUseDeadTrick =>AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost
        && !AntiBlackout.IsCached && !AntiBlackout.IsRevertPending;

    public static void HideOthersForVanilla()
    {
        if (!CanUseDeadTrick) return;
        var state = new Dictionary<byte, bool>();
        foreach (var p in Main.AllPlayerControls) if (p?.Data != null) state[p.PlayerId] = p.Data.IsDead;
        GameDataSerializePatch.SerializeMessageCount++;
        try
        {
            foreach (var pc in Main.AllAlivePlayerControls)
            {
                if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId || pc.IsModClient()) continue;
                foreach (var tg in Main.AllAlivePlayerControls)
                {
                    if (tg.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
                    tg.Data.IsDead = true;
                }
                pc.Data.IsDead = false;
                ExtendedPlayerControl.RpcSyncAllNetworkedPlayer(pc.GetClientId());
            }
        }
        finally
        {
            GameDataSerializePatch.SerializeMessageCount--;
            foreach (var p in Main.AllPlayerControls)
                if (p?.Data != null && state.TryGetValue(p.PlayerId, out var dead)) p.Data.IsDead = dead;
        }
        hiding = true;
        ResendMeetingNames(0.3f);
        Logger.Info("初手会議のチャット無効化: 本編の客の画面で他の全員を死亡扱いにした", "FirstMeetingChatBlock");
    }

    static void RestoreForVanilla()
    {
        if (!hiding) return;
        hiding = false;
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsMeeting) return;
        if (AntiBlackout.IsCached || AntiBlackout.IsRevertPending) return;
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId || pc.IsModClient()) continue;
            ExtendedPlayerControl.RpcSyncAllNetworkedPlayer(pc.GetClientId());
        }
        ResendMeetingNames(0.3f);
        Utils.SendMessage(Translator.GetString("FirstMeetingChatUnblocked"));
        Logger.Info("初手会議のチャット無効化: 時間が来たので元に戻した", "FirstMeetingChatBlock");
    }

    public static void ResendMeetingNames(float delay)
        => _ = new LateTask(() =>
        {
            if (GameStates.IsMeeting && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
                Utils.NotifyRoles(isForMeeting: true, NoCache: true);
        }, delay, "FirstMeetingChatBlock.ResendNames");

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    static class StartPatch
    {
        public static void Postfix()
        {
            active = FirstTurnMeeting.InFirst && MeetingStates.FirstMeeting && Seconds > 0;
            startedAt = Time.time;
            hiding = false;
            if (!active || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            _ = new LateTask(() => { if (IsBlocking) HideOthersForVanilla(); }, 0.6f, "FirstMeetingChatBlock.Hide");
            _ = new LateTask(() => { if (GameStates.IsMeeting) Utils.SendMessage(Notice); }, 1.5f, "FirstMeetingChatBlock.Notice");
            if (Seconds < UntilEnd)
                _ = new LateTask(RestoreForVanilla, Seconds, "FirstMeetingChatBlock.Restore");
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
    static class EndPatch
    {
        public static void Postfix() { active = false; hiding = false; }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSendChatNote))]
    static class RpcSendChatNotePatch
    {
        public static bool Prefix([HarmonyArgument(1)] ChatNoteTypes noteType)
            => !(noteType == ChatNoteTypes.DidVote && IsBlocking);
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChatNote))]
    static class AddChatNotePatch
    {
        public static bool Prefix([HarmonyArgument(1)] ChatNoteTypes noteType)
            => !(noteType == ChatNoteTypes.DidVote && IsBlocking);
    }

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat))]
    static class AddChatPatch
    {
        public static bool Prefix([HarmonyArgument(0)] PlayerControl sourcePlayer, [HarmonyArgument(1)] string chatText)
        {
            if (!IsBlocking || sourcePlayer == null) return true;
            if (sourcePlayer.PlayerId == PlayerControl.LocalPlayer.PlayerId) return true;
            // TOHL 入りの客の画面では、ホストの知らせ (止めている間の案内など) を隠さない
            if (sourcePlayer.OwnerId == AmongUsClient.Instance.HostId) return true;
            return !ShouldBlock(chatText);
        }
    }
}

public static partial class MeetingStates
{
    public static bool First = true;
}
