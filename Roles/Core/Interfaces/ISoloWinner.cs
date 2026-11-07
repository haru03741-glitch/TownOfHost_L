using TownOfHostL.Core.Win;

namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>単独勝利の上書きの段。後の段ほど強く、前の段の勝者をリセットする。</summary>
public enum SoloWinTier
{
    /// <summary>タスクを終えて生き残る</summary>
    Tasks,
    /// <summary>生き残る</summary>
    Survival,
    /// <summary>組の全員で生き残る</summary>
    Pair,
    /// <summary>他の陣営の勝ちを横取りする</summary>
    Steal,
}

/// <summary>
/// 試合の終わりに、勝者を自分の陣営に置き換える役職。
/// 同じ段で最初に当たった人が勝者をリセットし、2 人目からは勝者に足す。
/// </summary>
public interface ISoloWinner
{
    public SoloWinTier SoloWinTier { get; }
    /// <summary>勝つなら true と、置き換える勝者のチームを返す</summary>
    public bool CheckSoloWin(WinnerResolver.Context ctx, out CustomWinner winner);
}
