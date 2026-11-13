using System.Collections.Generic;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Core.Win;

/// <summary>勝者が決まった後に、役職が勝ちを乗っ取る・単独で勝つ時の段</summary>
public enum WinTakeOverStage
{
    /// <summary>終了判定で勝者が決まった直後 (毎フレームの判定の中。ゾンビ・陰陽師)</summary>
    OnGameEnd,
    /// <summary>勝者を決める段 (WinnerResolver) の、単独勝利の上書きの後・追加勝利の前 (妖狐・マグロ・スペランカー・ゾンビ)</summary>
    AfterSoloWins,
}

/// <summary>勝ちを乗っ取る処理。勝者を書き換えたら true。team は OnGameEnd では今の勝者、AfterSoloWins では上書き前の勝者</summary>
public delegate bool WinTakeOverCheck(ref GameOverReason reason, CustomWinner team);

/// <summary>役職が SimpleRoleInfo で宣言する、勝ちの乗っ取り。同じ段では order の小さい順に、戻り値に関わらず全部呼ぶ</summary>
public sealed class WinTakeOver
{
    public readonly WinTakeOverStage Stage;
    public readonly int Order;
    public readonly WinTakeOverCheck Check;

    public WinTakeOver(WinTakeOverStage stage, int order, WinTakeOverCheck check)
    {
        Stage = stage;
        Order = order;
        Check = check;
    }
}

/// <summary>
/// 役職が宣言した勝ちの乗っ取りを、段ごとに順に呼ぶ。
/// 今の order: OnGameEnd = ゾンビ 100・陰陽師 200 / AfterSoloWins = 妖狐 100・マグロ 200・スペランカー 300・ゾンビ 400
/// </summary>
public static class WinTakeOvers
{
    private static WinTakeOver[] onGameEnd;
    private static WinTakeOver[] afterSoloWins;

    /// <summary>役職の登録 (SimpleRoleInfo) は起動時に揃うので、最初に使う時に 1 回だけ組む (static 初期化から役職を引かない)</summary>
    private static void Build()
    {
        var gameEnd = new List<WinTakeOver>();
        var solo = new List<WinTakeOver>();
        foreach (var info in CustomRoleManager.AllRolesInfo.Values)
        {
            if (info.WinTakeOvers == null) continue;
            foreach (var takeOver in info.WinTakeOvers)
                (takeOver.Stage == WinTakeOverStage.OnGameEnd ? gameEnd : solo).Add(takeOver);
        }
        gameEnd.Sort((a, b) => a.Order.CompareTo(b.Order));
        solo.Sort((a, b) => a.Order.CompareTo(b.Order));
        onGameEnd = gameEnd.ToArray();
        afterSoloWins = solo.ToArray();
    }

    /// <summary>終了判定の直後。勝者が決まっていない時・廃村・妨害での勝ち (全滅を除く) では呼ばない</summary>
    public static void RunOnGameEnd(ref GameOverReason reason)
    {
        var team = CustomWinnerHolder.WinnerTeam;
        if (team is CustomWinner.Default or CustomWinner.Draw) return;
        if (reason == GameOverReason.ImpostorsBySabotage && team is not CustomWinner.None) return;
        if (onGameEnd == null) Build();
        foreach (var takeOver in onGameEnd) takeOver.Check(ref reason, team);
    }

    /// <summary>単独勝利の上書きの後。廃村・妨害での勝ちでは呼ばない</summary>
    public static void RunAfterSoloWins(ref GameOverReason reason, CustomWinner originalTeam)
    {
        if (CustomWinnerHolder.WinnerTeam is CustomWinner.Draw) return;
        if (reason == GameOverReason.ImpostorsBySabotage) return;
        if (afterSoloWins == null) Build();
        foreach (var takeOver in afterSoloWins) takeOver.Check(ref reason, originalTeam);
    }
}
