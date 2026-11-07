using HarmonyLib;
using TownOfHostL.Roles.Core;
using TownOfHostL.Attributes;

namespace TownOfHostL;

public static partial class GameStates
{
    /// <summary>会議が呼ばれてから会議が終わるまで (ホストのみ)</summary>
    public static bool CalledMeeting;
    /// <summary>追放の演出中 (ホストのみ)</summary>
    public static bool ExiledAnimate;
    /// <summary>イントロ中</summary>
    public static bool Intro;

    [GameModuleInitializer]
    public static void HamoInit()
    {
        CalledMeeting = false;
        ExiledAnimate = false;
        Intro = false;
    }
}

public partial class Main
{
    /// <summary>hamo の通信の遅れの見込み</summary>
    public static float LagTime = 0.2f;
}

public static partial class CustomWinnerHolder
{
    /// <summary>hamo の単独勝利の優先度。今の勝者の優先度</summary>
    public static int WinPriority = -1;

    /// <summary>
    /// 優先度を比べて勝者を決める (hamo)。
    /// 今の勝者より優先度が大きければ単独勝利、同じなら追加勝利。
    /// </summary>
    public static bool ResetAndSetAndChWinner(CustomWinner winner, byte playerId, bool AddWin = true, CustomRoles hantrole = CustomRoles.NotAssigned)
    {
        GameStates.CalledMeeting = false;
        if (WinnerTeam == CustomWinner.Default) WinPriority = -1;
        var priority = SoloWinOption.AllData.TryGetValue(hantrole is CustomRoles.NotAssigned ? (CustomRoles)winner : hantrole, out var data)
            ? data.OptionWin.GetInt() : 0;
        if (WinPriority < priority)
        {
            if (WinnerTeam is not CustomWinner.Default and not CustomWinner.Draw)
                UtilsGameLog.AddGameLog("Winner", $"{UtilsRoleText.GetRoleColorAndtext((CustomRoles)WinnerTeam)} => {UtilsRoleText.GetRoleColorAndtext((CustomRoles)winner)}");
            Reset();
            WinPriority = priority;
            WinnerTeam = winner;
            winners.Add(winner);
            if (playerId is byte.MaxValue) return true;
            WinnerIds.Add(playerId);
            return true;
        }
        if (WinPriority == priority && AddWin)
        {
            UtilsGameLog.AddGameLog("AddWinner", $"AddWin:{UtilsRoleText.GetRoleColorAndtext((CustomRoles)winner)}");
            AdditionalWinnerRoles.Add((CustomRoles)winner);
            winners.Add(winner);
            if (playerId is byte.MaxValue) return true;
            WinnerIds.Add(playerId);
            CantWinPlayerIds.Remove(playerId);
            return true;
        }
        return false;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.StartMeeting))]
class HamoCalledMeetingPatch
{
    public static void Prefix() { if (AmongUsClient.Instance.AmHost) GameStates.CalledMeeting = true; }
}
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
class HamoMeetingStartStatePatch
{
    public static void Prefix()
    {
        GameStates.ExiledAnimate = false;
        HamoSpawn.SetAll(false);
    }
}
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.VotingComplete))]
class HamoVotingCompleteStatePatch
{
    public static void Postfix() => GameStates.ExiledAnimate = true;
}
[HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
class HamoExileWrapUpStatePatch
{
    public static void Postfix()
    {
        GameStates.CalledMeeting = false;
        _ = new LateTask(() => GameStates.ExiledAnimate = false, 3f + Main.LagTime, "Tuihoufin", true);
        HamoSpawn.SetAll(true);
        TownOfHostL.Roles.Hamo.HamoHookPatches.OnExileWrapUp(false);
    }
}
[HarmonyPatch(typeof(AirshipExileController), nameof(AirshipExileController.WrapUpAndSpawn))]
class HamoAirshipExileWrapUpStatePatch
{
    public static void Postfix()
    {
        GameStates.CalledMeeting = false;
        _ = new LateTask(() => GameStates.ExiledAnimate = false, 3f + Main.LagTime, "Tuihoufin", true);
        HamoSpawn.SetAll(true);
        TownOfHostL.Roles.Hamo.HamoHookPatches.OnExileWrapUp(true);
    }
}
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
class HamoIntroStatePatch
{
    public static void Prefix() => GameStates.Intro = true;
}
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
class HamoIntroEndStatePatch
{
    public static void Postfix()
    {
        GameStates.Intro = false;
        HamoSpawn.SetAll(true);
        TownOfHostL.Roles.Hamo.HamoHookPatches.OnIntroEnd();
    }
}

static class HamoSpawn
{
    public static void SetAll(bool spawned)
    {
        foreach (var state in PlayerState.AllPlayerStates.Values) state.HasSpawned = spawned;
    }
}
