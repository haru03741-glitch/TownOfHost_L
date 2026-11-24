namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>
/// 状態によって仲間のインポスターもキルできる役職 (ホストのみ)。
/// インポスター同士のキルを止める関所で、<see cref="CanKillTeammates"/> が true の間は止めない。
/// 本編の客の画面では仲間をキルの対象にできないので、役職の側で仲間の見え方 (RoleView) も替えること。
/// </summary>
public interface ITeammateKiller
{
    bool CanKillTeammates { get; }
}
