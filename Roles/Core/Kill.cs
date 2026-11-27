using TownOfHostL.Modules;

namespace TownOfHostL.Roles.Core;

/// <summary>
/// 本編へキルを送る入口。役職・モードの処理は、本編の RpcMurderPlayer を直に呼ばずにここを通す
/// (直に呼んでいないことは tools/check-kill-entry.ps1 がビルドの前に確かめる)。
/// <para>
/// 入口は 2 つの意味に分ける。
/// <list type="bullet">
/// <item><see cref="Force"/>: 守りもキルの判定も通さずに殺す。キルの判定を既に通した遅れたキル (噛みつき・1 回押し・ジャニターの的)、
/// 範囲のキル・反撃・後追い・勝利時の全員のキルなど、守りを貫くのが仕様の物。</item>
/// <item><see cref="Suicide"/>: 本人の死 (自殺・誤射の罰・後追い)。</item>
/// </list>
/// どちらも、相手が既に死んでいる・抜けている時は何もしない (同じ人を 2 度殺して死体や死の処理が重ならないように)。
/// 殺し手の生死は見ない (殺された人の反撃・死後に起こす噛みつきなど、死んだ殺し手のキルが仕様の物がある)。
/// 死因は送る前に <see cref="PlayerState.DeathReason"/> へ書く。<see cref="PlayerState.SetDead"/> を先に呼ぶと、ここで死んでいるとみなして送らない。
/// </para>
/// </summary>
public static class Kill
{
    /// <summary>
    /// <paramref name="killer"/> に <paramref name="target"/> を、守りもキルの判定も通さずに殺させる。送った時は true。
    /// <paramref name="dissolveBody"/> は、本編の客の画面で死体を溶かして消す時 (<see cref="DissolveKill"/>)
    /// </summary>
    public static bool Force(PlayerControl killer, PlayerControl target, bool dissolveBody = false)
    {
        if (!AmongUsClient.Instance.AmHost || killer == null || !CanDie(target)) return false;
        Send(killer, target, dissolveBody, noBody: false);
        return true;
    }

    /// <summary><paramref name="player"/> を本人の死として死なせる。送った時は true</summary>
    public static bool Suicide(PlayerControl player) => Force(player, player);

    /// <summary>
    /// 決まったキルを本編へ送る (キルの判定の送り口と <see cref="Force"/> の共通)。
    /// <paramref name="noBody"/> は死体を残さずに死なせる時 (<see cref="DeadBodyEraser.KillWithoutBody"/>)
    /// </summary>
    internal static void Send(PlayerControl appearanceKiller, PlayerControl appearanceTarget, bool dissolveBody, bool noBody)
    {
        if (noBody)
        {
            DeadBodyEraser.ExileInsteadOfMurder(appearanceKiller, appearanceTarget);
            return;
        }
        // 溶かす手当ては、キルの電文より前に送る
        if (dissolveBody) DissolveKill.Prepare(appearanceKiller, appearanceTarget);
        appearanceKiller.RpcMurderPlayer(appearanceTarget, true);
    }

    private static bool CanDie(PlayerControl target)
    {
        if (target == null || target.Data == null || target.Data.Disconnected) return false;
        if (target.IsAlive()) return true;
        Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} は既に死んでいるので、キルを送りません", "Kill");
        return false;
    }
}
