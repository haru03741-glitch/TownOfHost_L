using System;

using TownOfHostL.Core.Win;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Core;

/// <summary>属性が持つ勝ち方。役職の ISoloWinner・IAdditionalWinner と同じ段で調べる。</summary>
public sealed class AddOnWin
{
    public readonly SoloWinTier SoloWinTier;
    public readonly CustomWinner SoloWinner;
    /// <summary>この人が単独勝利するか。null なら単独勝利しない</summary>
    public readonly Func<WinnerResolver.Context, PlayerControl, bool> CheckSoloWin;
    /// <summary>この人が追加勝利するか。null なら追加勝利しない</summary>
    public readonly Func<PlayerControl, bool> CheckAdditionalWin;

    public AddOnWin(
        SoloWinTier soloWinTier = default,
        CustomWinner soloWinner = CustomWinner.Default,
        Func<WinnerResolver.Context, PlayerControl, bool> checkSoloWin = null,
        Func<PlayerControl, bool> checkAdditionalWin = null)
    {
        SoloWinTier = soloWinTier;
        SoloWinner = soloWinner;
        CheckSoloWin = checkSoloWin;
        CheckAdditionalWin = checkAdditionalWin;
    }
}
