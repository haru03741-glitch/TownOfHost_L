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

    /// <summary>
    /// 会議の呼び出しから、追放の演出の後に暗転対策が客の画面の役職を戻し終えるまで (ホストのみ)。
    /// この間に客へ送る変身・役職・死は、客の画面の頭数や見え方を崩しうるので、明けてから送る
    /// </summary>
    public static bool InMeetingWindow
        => CalledMeeting || ExiledAnimate || MeetingHud.Instance || ExileController.Instance
        || AntiBlackout.IsCached || AntiBlackout.IsRevertPending;

    /// <summary>
    /// 通報を受けてから暗転対策の戻し (追放の締めの 2 秒後) までの間か。InMeetingWindow から、戻しの後の追放の演出の残り (ExiledAnimate だけが立っている間) を除いた物。
    /// 他人の操作を変える仕組み (歩かせる・閉じ込める・死体を隠すなど) は、この間は動かさない
    /// </summary>
    public static bool InMeetingUntilRevert
        => MeetingHud.Instance || ExileController.Instance || MeetingStates.MeetingCalled
        || AntiBlackout.IsCached || AntiBlackout.IsRevertPending;

    /// <summary>
    /// 通報を受けてから追放の締め (退避した生死を戻す所) までの間か。変身・消えるの能力はこの間は使えない
    /// </summary>
    public static bool InMeetingUntilWrapUp
        => MeetingHud.Instance || MeetingStates.MeetingCalled || ExileController.Instance || AntiBlackout.IsCached;

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
        // 終わりの送信を始めた後は勝者を変えない (幽霊の役職は元の勝者で配っていて、結果の画面と記録だけが食い違う)
        if (GameEndChecker.EndingStarted) return false;
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
        // 会議ごとに、追放の締めの処理を 1 回だけ走らせる準備をする
        AirshipExileWrapUpStatePatch.Arm();
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
    // 追放の確定 (ExilePatch の WrapUp の Postfix) より先に走る。優先度が無いと順は型の並びで決まり、ファイルの増減で入れ替わる
    [HarmonyPriority(Priority.First)]
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
    // 今の本編では WrapUpAndSpawn が呼ばれない (追放の演出のコルーチンの中で済む) ので、
    // 追放の締め (ExilePatch の Animate の包み) からも呼ぶ。1 回の追放で 2 度走らせない
    static bool armed;
    public static void Arm() => armed = true;
    public static void Postfix() => Run();
    public static void Run()
    {
        if (!armed) return;
        armed = false;
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
