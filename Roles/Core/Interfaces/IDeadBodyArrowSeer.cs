namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>
/// 死体への矢印 (TargetDeadArrow) を自分の名前に出す役職。
/// 矢印を出し始めるのは役職が TargetDeadArrow.AddSeer を呼んだ時で、これは「この試合で使うか」と「今の役職が矢印を見る役職か」の宣言。
/// </summary>
public interface IDeadBodyArrowSeer
{
    /// <summary>この試合で死体の矢印を使うか (設定で使わない時は false)</summary>
    public bool UsesDeadBodyArrows => true;
}
