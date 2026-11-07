using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL.Core;

/// <summary>
/// 役職と属性のどちらからでも付く性質をまとめて引く。
/// 役職側は RoleBase の宣言 (IsClumsy など) を上書きし、ここでは役職の名前を並べない。
/// </summary>
public static class PlayerTraits
{
    /// <summary>停電・通信妨害を直せない</summary>
    public static bool IsClumsy(PlayerControl pc)
        => pc.Is(CustomRoles.Clumsy) || (pc.GetRoleClass()?.IsClumsy ?? false);

    /// <summary>アドミン・カメラ・バイタルなどの情報機器を使えない</summary>
    public static bool IsInfoPoor(PlayerControl pc)
        => pc != null && ((pc.GetRoleClass()?.IsInfoPoor ?? false) || pc.Is(CustomRoles.InfoPoor));

    /// <summary>投票で追放された時に誰かを道連れにする</summary>
    public static bool RevengesOnExile(PlayerControl pc)
        => (pc.Is(CustomRoles.SKMadmate) && Options.MadmateRevengeCrewmate.GetBool())
           || (pc.GetRoleClass()?.RevengesOnExile ?? false)
           || pc.Is(CustomRoles.Revenger);

    /// <summary>妨害の修理の制限でマッドメイトとして扱う</summary>
    public static bool IsMadmateForSabotage(PlayerControl pc, RoleBase role)
        => pc.Is(CustomRoles.SKMadmate) ||
           // マッド属性化時に削除
           (role is SchrodingerCat schrodingerCat && schrodingerCat.AmMadmate);

    /// <summary>追放された人がインポスターかどうかを、自分の名前の所で知る</summary>
    public static bool LearnsExiledTeam(PlayerControl pc) => Has(pc, RoleFlags.LearnsExiledTeam);
    /// <summary>死体を通報できない (target の死体が ReportableByAll なら通報できる)</summary>
    public static bool CannotReport(PlayerControl reporter, PlayerControl target)
        => Has(reporter, RoleFlags.CannotReport) && !Has(target, RoleFlags.ReportableByAll);
    /// <summary>死んでもタスクが免除されない</summary>
    public static bool KeepsTasksWhenDead(PlayerControl pc) => Has(pc, RoleFlags.KeepsTasksWhenDead);
    /// <summary>会議の始めに、自分の名前を送り直す</summary>
    public static bool RefreshesNameOnMeetingStart(PlayerControl pc) => Has(pc, RoleFlags.RefreshesNameOnMeetingStart);

    /// <summary>元の陣営 (クルー・インポスター) が勝った時に、一緒に勝者に入る</summary>
    public static bool WinsWithBaseTeam(PlayerControl pc)
        => !Has(pc, RoleFlags.NoTeamWin) && (pc.GetRoleClass()?.WinsWithBaseTeam ?? true);
    /// <summary>どの勝ち方でも勝者にならない</summary>
    public static bool NeverWins(PlayerControl pc) => Has(pc, RoleFlags.NeverWins);

    /// <summary>他の役職の能力が「キル役」として扱う人 (インポスター・CountedAsKiller の第三陣営とクルー)</summary>
    public static bool IsCountedKiller(PlayerControl pc)
        => pc.GetCustomRole().IsImpostor() || pc.IsCountedNeutralKiller() || pc.IsCountedCrewKiller();
    /// <summary>占い・霊媒の「キル役だけ」の設定で、キル役として数える (マッドシェリフ・グラージシェリフを含む)</summary>
    public static bool IsKillerForDivination(PlayerControl pc)
        => IsCountedKiller(pc) || pc.Is(CustomRoles.MadSheriff) || pc.Is(CustomRoles.GrudgeSheriff);

    /// <summary>役職か、付いている属性のどれかがその印を持つ</summary>
    private static bool Has(PlayerControl pc, RoleFlags flag)
    {
        if (pc == null) return false;
        if (pc.GetCustomRole().HasRoleFlag(flag)) return true;
        foreach (var subRole in pc.GetCustomSubRoles())
            if (subRole.HasRoleFlag(flag)) return true;
        return false;
    }
}
