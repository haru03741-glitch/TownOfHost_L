using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Neutral;
using TownOfHostL.Roles.AddOns;

namespace TownOfHostL.Core;

/// <summary>
/// 役職と属性のどちらからでも付く性質をまとめて引く。
/// 役職側は RoleBase の宣言 (IsClumsy など) を上書きし、ここでは役職の名前を並べない。
/// </summary>
public static class PlayerTraits
{
    /// <summary>停電・通信妨害を直せない</summary>
    public static bool IsClumsy(PlayerControl pc)
        => (pc.GetRoleClass()?.IsClumsy ?? false) || Has(pc, RoleFlags.Clumsy);

    /// <summary>アドミン・カメラ・バイタルなどの情報機器を使えない</summary>
    public static bool IsInfoPoor(PlayerControl pc)
        => pc != null && ((pc.GetRoleClass()?.IsInfoPoor ?? false) || Has(pc, RoleFlags.InfoPoor));

    /// <summary>投票で追放された時に誰かを道連れにする</summary>
    public static bool RevengesOnExile(PlayerControl pc)
        => (pc.Is(CustomRoles.SKMadmate) && Options.MadmateRevengeCrewmate.GetBool())
           || (pc.GetRoleClass()?.RevengesOnExile ?? false)
           || Has(pc, RoleFlags.RevengesOnExile);

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
    /// <summary>占い・霊媒の「キル役だけ」の設定で、キル役として数える (RoleFlags.DivinedAsKiller の役職を含む)</summary>
    public static bool IsKillerForDivination(PlayerControl pc)
        => IsCountedKiller(pc) || pc.GetCustomRole().HasRoleFlag(RoleFlags.DivinedAsKiller);

    /// <summary>生きている間、他人のキルの光が見える (役職ごとの条件は IKillFlashSeeable で別に決める)</summary>
    public static bool SeesKillFlash(PlayerControl pc)
        => Has(pc, RoleFlags.SeesKillFlash) || (pc.Is(CustomRoles.SKMadmate) && Options.MadmateCanSeeKillFlash.GetBool());
    /// <summary>この人がキルされた時は全員に光が見える</summary>
    public static bool KillFlashToAll(PlayerControl pc) => Has(pc, RoleFlags.KillFlashToAll);
    /// <summary>他人の死因が見える (役職ごとの条件は IDeathReasonSeeable で別に決める)</summary>
    public static bool SeesDeathReason(PlayerControl pc)
        => Has(pc, RoleFlags.SeesDeathReason) || (pc.Is(CustomRoles.SKMadmate) && Options.MadmateCanSeeDeathReason.GetBool());

    /// <summary>他人の名前を自分用に組み立て直す必要がある (役職は RoleBase.NeedsSeenNames で別に決める)</summary>
    public static bool NeedsSeenNames(PlayerControl pc) => Has(pc, RoleFlags.NeedsSeenNames);
    /// <summary>追放された人の道連れの対象にならない</summary>
    public static bool NotRevengeTarget(PlayerControl pc) => Has(pc, RoleFlags.NotRevengeTarget);
    /// <summary>役職を預かる印の属性が付いていれば、その役職 (無ければ NotAssigned)</summary>
    public static CustomRoles StandInRole(PlayerControl pc)
    {
        if (pc == null) return CustomRoles.NotAssigned;
        foreach (var subRole in pc.GetCustomSubRoles())
        {
            var standsFor = AddOnList.Get(subRole)?.StandsFor ?? CustomRoles.NotAssigned;
            if (standsFor != CustomRoles.NotAssigned) return standsFor;
        }
        return CustomRoles.NotAssigned;
    }

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
