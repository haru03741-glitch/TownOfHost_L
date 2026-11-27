namespace TownOfHostL.Roles.Core.Interfaces;

/// <summary>
/// キルと同時に死体を消す役職。キルの電文より前に、本編の客の画面で死体が溶けるように送る (<see cref="Modules.DissolveKill"/>)。
/// </summary>
public interface IDissolvingKiller
{
    /// <summary>このキルの死体を溶かすか (消さない死体・設定で使わない時は false)</summary>
    public bool DissolvesBody(PlayerControl target);
}
