namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>
/// 会議の途中で残り時間を動かす力を持つ役職。自分への投票か、チャットの /time で使う
/// (会議中の素の客にはペットなどのボタンが出ないため)
/// </summary>
public interface IMeetingTimeActive
{
    /// <summary>力を使う。返事 (使えない理由など) は役職の側で本人へ送る</summary>
    public void UseMeetingTimeAbility();
}
