using TownOfHostL.Modules;

namespace TownOfHostL.Roles.Core;

/// <summary>
/// 本編へキルを送る入口。役職・モードの処理は、本編の RpcMurderPlayer を直に呼ばずにここを通す
/// (直に呼んでいないことは tools/check-kill-entry.ps1 がビルドの前に確かめる)。
/// <para>
/// 入口は 3 つの意味に分ける。
/// <list type="bullet">
/// <item><see cref="Attempt"/>: 能力で他の人を殺す。キルボタンを通らないキルでも、相手の守り (役職に関わらない守り・相手の役職・守護者) を
/// キルの判定と同じ規則 (キルの力と守りの力) で通す。他の人へのキルは、守りを貫くのが仕様でなければこれを使う。</item>
/// <item><see cref="Force"/>: 守りもキルの判定も通さずに殺す。キルの判定を既に通した遅れたキル (噛みつき・1 回押し・ジャニターの的)、
/// 範囲のキル・反撃・後追い・勝利時の全員のキルなど、守りを貫くのが仕様の物。</item>
/// <item><see cref="Suicide"/>: 本人の死 (自殺・誤射の罰・後追い)。</item>
/// </list>
/// どれも、相手が既に死んでいる・抜けている時は何もしない (同じ人を 2 度殺して死体や死の処理が重ならないように)。
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

    /// <summary>
    /// <paramref name="killer"/> に <paramref name="target"/> を、相手の守りを通して殺させる。殺した時は true、守りや相手の役職に止められた時は false。
    /// 守られた時は、キルボタンのキルと同じく守られた見せ方をし、守護者が守ったなら守護者が身代わりになる。
    /// キルボタンを通らないので、殺し手の役職のキルチェック・キルクール・位置の条件 (ベント・はしご) は見ない
    /// </summary>
    /// <param name="realKiller">本当の殺し手 (人形や呪われた人に殺させた役職)。殺す時にだけ書く (守られて生き残った人に残さない)</param>
    /// <param name="killPower">キルの力。相手の守りの力がこれ以上なら守られる (ふつうのキルは 1)</param>
    public static bool Attempt(PlayerControl killer, PlayerControl target, PlayerControl realKiller = null,
        CustomDeathReason deathReason = CustomDeathReason.Kill, int killPower = 1)
    {
        if (!AmongUsClient.Instance.AmHost || killer == null || !CanDie(target)) return false;
        // 会議・追放の演出・暗転対策の窓の間と、一時的な幽霊は、キルボタンのキルと同じく通さない
        if (MeetingHud.Instance != null || ExileController.Instance != null || AntiBlackout.IsCached || TempGhost.IsGhost(target))
        {
            Logger.Info($"{killer.GetNameWithRole().RemoveHtmlTags()} → {target.GetNameWithRole().RemoveHtmlTags()}: 今は殺せない時なので止めます", "Kill");
            return false;
        }

        var info = new MurderInfo(killer, target, killer, target, false, killPower, 0, deathReason);
        if (!FirstTurnMeeting.AllowKill(info, deathReason)) return false;
        if (!CustomRoleManager.CheckGuards(info, out var guardianGuarded)) return false;
        if (CustomRoleManager.ResolveGuard(info, guardianGuarded)) return false;
        // 役職に関わらない守りは、守られた時に CanKill を落とす
        if (!info.CanKill) return false;
        // 相手の役職が見た目の相手を替えた時 (身代わりなど) は替わった人を殺す
        var victim = info.AppearanceTarget;
        if (!CanDie(victim)) return false;
        if (realKiller != null) victim.SetRealKiller(realKiller);
        // キルの後の処理 (役職の OnMurderPlayer・死因) へ、この判定の結果を渡す
        CustomRoleManager.CheckMurderInfos[info.AppearanceKiller.PlayerId] = info;
        Send(info.AppearanceKiller, victim, dissolveBody: false, noBody: false);
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
        // 見せるための CNO (中身は PlayerControl) は殺さない
        if (target == null || target.Data == null || target.Data.Disconnected || TownOfHostL.Core.CustomNetObject.Is(target)) return false;
        if (target.IsAlive()) return true;
        Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} は既に死んでいるので、キルを送りません", "Kill");
        return false;
    }
}
