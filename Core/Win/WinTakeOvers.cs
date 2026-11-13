using System.Collections.Generic;

using TownOfHostL.Roles.Core;

namespace TownOfHostL.Core.Win;

/// <summary>勝者が決まった後に、役職が勝ちを乗っ取る・単独で勝つ時の段</summary>
public enum WinTakeOverStage
{
    /// <summary>基本の勝者 (クルー・インポスター) を入れる直前。true を返したらその陣営の人を入れない (インポスター勝ちを奪うエゴイスト)</summary>
    BaseTeamWin,
    /// <summary>終了判定で勝者が決まった直後 (毎フレームの判定の中。ゾンビ・陰陽師)</summary>
    OnGameEnd,
    /// <summary>勝者を決める段 (WinnerResolver) の、単独勝利の上書きの後・追加勝利の前 (妖狐・マグロ・スペランカー・ゾンビ)</summary>
    AfterSoloWins,
}

/// <summary>勝ちを乗っ取る処理。勝者を書き換えたら true。team は BaseTeamWin・OnGameEnd では今の勝者、AfterSoloWins では上書き前の勝者</summary>
public delegate bool WinTakeOverCheck(ref GameOverReason reason, CustomWinner team);

/// <summary>役職が SimpleRoleInfo で宣言する、勝ちの乗っ取り。同じ段では order の小さい順に呼ぶ (BaseTeamWin は最初の true で止め、ほかの段は戻り値に関わらず全部呼ぶ)</summary>
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
/// 今の order: BaseTeamWin = エゴイスト 100 / OnGameEnd = ゾンビ 100・陰陽師 200 / AfterSoloWins = 妖狐 100・マグロ 200・スペランカー 300・ゾンビ 400
/// </summary>
public static class WinTakeOvers
{
    /// <summary>段ごとの並び (WinTakeOverStage の値で引く)。null の間はまだ組んでいない</summary>
    private static WinTakeOver[][] byStage;

    /// <summary>役職の登録 (SimpleRoleInfo) は起動時に揃うので、最初に使う時に 1 回だけ組む (static 初期化から役職を引かない)</summary>
    private static WinTakeOver[] Get(WinTakeOverStage stage)
    {
        if (byStage == null)
        {
            var lists = new List<WinTakeOver>[System.Enum.GetValues(typeof(WinTakeOverStage)).Length];
            for (var i = 0; i < lists.Length; i++) lists[i] = new();
            foreach (var info in CustomRoleManager.AllRolesInfo.Values)
            {
                if (info.WinTakeOvers == null) continue;
                foreach (var takeOver in info.WinTakeOvers) lists[(int)takeOver.Stage].Add(takeOver);
            }
            byStage = new WinTakeOver[lists.Length][];
            for (var i = 0; i < lists.Length; i++)
            {
                lists[i].Sort((a, b) => a.Order.CompareTo(b.Order));
                byStage[i] = lists[i].ToArray();
            }
        }
        return byStage[(int)stage];
    }

    /// <summary>基本の勝者を入れる直前。1 つでも true を返したら true (その陣営の人を入れない)。order の小さい順に、最初に true を返した所で止める</summary>
    public static bool RunBaseTeamWin(ref GameOverReason reason, CustomWinner team)
    {
        foreach (var takeOver in Get(WinTakeOverStage.BaseTeamWin))
            if (takeOver.Check(ref reason, team)) return true;
        return false;
    }

    /// <summary>終了判定の直後。勝者が決まっていない時・廃村・妨害での勝ち (全滅を除く) では呼ばない</summary>
    public static void RunOnGameEnd(ref GameOverReason reason)
    {
        var team = CustomWinnerHolder.WinnerTeam;
        if (team is CustomWinner.Default or CustomWinner.Draw) return;
        if (reason == GameOverReason.ImpostorsBySabotage && team is not CustomWinner.None) return;
        foreach (var takeOver in Get(WinTakeOverStage.OnGameEnd)) takeOver.Check(ref reason, team);
    }

    /// <summary>単独勝利の上書きの後。廃村・妨害での勝ちでは呼ばない</summary>
    public static void RunAfterSoloWins(ref GameOverReason reason, CustomWinner originalTeam)
    {
        if (CustomWinnerHolder.WinnerTeam is CustomWinner.Draw) return;
        if (reason == GameOverReason.ImpostorsBySabotage) return;
        foreach (var takeOver in Get(WinTakeOverStage.AfterSoloWins)) takeOver.Check(ref reason, originalTeam);
    }
}
