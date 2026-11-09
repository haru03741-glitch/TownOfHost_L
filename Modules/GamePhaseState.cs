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
    public static void InitPhaseFlags()
    {
        CalledMeeting = false;
        ExiledAnimate = false;
        Intro = false;
    }
}

public partial class Main
{
    /// <summary>客との通信の遅れの見込み (秒)。演出の後の待ちに足す</summary>
    public static float LagTime = 0.2f;
}

public static partial class CustomWinnerHolder
{
    /// <summary>単独勝利の優先度。今の勝者の優先度</summary>
    public static int WinPriority = -1;

    /// <summary>
    /// 優先度を比べて勝者を決める。
    /// 今の勝者より優先度が大きければ単独勝利、同じなら追加勝利。
    /// </summary>
    public static bool ResetAndSetAndChWinner(CustomWinner winner, byte playerId, bool AddWin = true, CustomRoles hantrole = CustomRoles.NotAssigned)
    {
        GameStates.CalledMeeting = false;
        var priority = SoloWinOption.AllData.TryGetValue(hantrole is CustomRoles.NotAssigned ? (CustomRoles)winner : hantrole, out var data)
            ? data.OptionWin.GetInt() : 0;
        if (WinPriority < priority)
        {
            if (WinnerTeam is not CustomWinner.Default and not CustomWinner.Draw)
                Utils.AddGameLog("Winner", $"{Utils.GetRoleColorAndName((CustomRoles)WinnerTeam)} => {Utils.GetRoleColorAndName((CustomRoles)winner)}");
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
            Utils.AddGameLog("AddWinner", $"AddWin:{Utils.GetRoleColorAndName((CustomRoles)winner)}");
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
class CalledMeetingStatePatch
{
    public static void Prefix() { if (AmongUsClient.Instance.AmHost) GameStates.CalledMeeting = true; }
}
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
class MeetingStartStatePatch
{
    public static void Prefix()
    {
        GameStates.ExiledAnimate = false;
        SpawnState.SetAll(false);
    }
}
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.VotingComplete))]
class VotingCompleteStatePatch
{
    public static void Postfix() => GameStates.ExiledAnimate = true;
}
[HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
class ExileWrapUpStatePatch
{
    public static void Postfix()
    {
        GameStates.CalledMeeting = false;
        _ = new LateTask(() => GameStates.ExiledAnimate = false, 3f + Main.LagTime, "Tuihoufin");
        SpawnState.SetAll(true);
        RoleHooks.OnExileWrapUp(false);
    }
}
[HarmonyPatch(typeof(AirshipExileController), nameof(AirshipExileController.WrapUpAndSpawn))]
class AirshipExileWrapUpStatePatch
{
    public static void Postfix()
    {
        GameStates.CalledMeeting = false;
        _ = new LateTask(() => GameStates.ExiledAnimate = false, 3f + Main.LagTime, "Tuihoufin");
        SpawnState.SetAll(true);
        RoleHooks.OnExileWrapUp(true);
    }
}
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
class IntroStatePatch
{
    public static void Prefix() => GameStates.Intro = true;
}
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
class IntroEndStatePatch
{
    public static void Postfix()
    {
        GameStates.Intro = false;
        SpawnState.SetAll(true);
        RoleHooks.OnIntroEnd();
    }
}

static class SpawnState
{
    public static void SetAll(bool spawned)
    {
        foreach (var state in PlayerState.AllPlayerStates.Values) state.HasSpawned = spawned;
    }
}
