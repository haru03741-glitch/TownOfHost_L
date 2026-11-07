namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>勝者と追加勝利が出そろった後に、それを見て勝ち方を決める役職。勝者をリセットしてよい。</summary>
public interface ILateWinner
{
    public void CheckLateWin();
}
