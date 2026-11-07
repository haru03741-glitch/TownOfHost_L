namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>自分以外の人を追加勝利させる役職 (相手役の属性など)。追加勝利の段で呼ぶ。</summary>
public interface IAdditionalWinGranter
{
    public void GrantAdditionalWins();
}
