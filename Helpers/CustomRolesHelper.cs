using System.Linq;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Core;

namespace TownOfHostL;

static class CustomRolesHelper
{
    public static readonly CustomRoles[] AllRoles = EnumHelper.GetAllValues<CustomRoles>().Where(role => !role.IsNotAssignedRoles()).ToArray();
    public static readonly CustomRoleTypes[] AllRoleTypes = EnumHelper.GetAllValues<CustomRoleTypes>();

    /// <summary>すべてのメイン役職(ゲームモードも含む、属性は含まない)</summary>
    public static readonly CustomRoles[] AllMainRoles = EnumHelper.GetAllValues<CustomRoles>().Where(role => role < CustomRoles.StartAddon && !role.IsNotAssignedRoles()).ToArray();
    /// <summary>すべての属性</summary>
    public static readonly CustomRoles[] AllAddOnRoles = EnumHelper.GetAllValues<CustomRoles>().Where(role => role > CustomRoles.StartAddon && !role.IsNotAssignedRoles()).ToArray();
    /// <summary>スタンダードモードのメイン役職</summary>
    public static readonly CustomRoles[] AllStandardRoles = EnumHelper.GetAllValues<CustomRoles>().Where(role => role < CustomRoles.MaxMain && !role.IsNotAssignedRoles()).ToArray();
    /// <summary>HASモードのメイン役職</summary>
    public static readonly CustomRoles[] AllHASRoles = { CustomRoles.HASFox, CustomRoles.HASTroll, CustomRoles.HASPortalMaker };
    /// <summary>CCモードのメイン役職</summary>
    // static 初期化は RoleCatalog.Build より先に走る。ここで分類を引けるのは、表 (Unclassed) だけで決まる役職に限る
    public static readonly CustomRoles[] AllCCRoles = EnumHelper.GetAllValues<CustomRoles>().Where(role => role.IsCCRole()).ToArray();

    // 分類は役職ごとの定義 (SimpleRoleInfo.Create の flags と RoleCatalog の表) から引く
    private static RoleDef Def(this CustomRoles role) => RoleCatalog.Get(role);

    public static bool IsImpostor(this CustomRoles role) => role.Def().Team == CustomRoleTypes.Impostor;
    public static bool IsMadmate(this CustomRoles role) => role.Def().Team == CustomRoleTypes.Madmate;
    public static bool IsImpostorTeam(this CustomRoles role) => role.Def().Team is CustomRoleTypes.Impostor or CustomRoleTypes.Madmate;
    public static bool IsNeutral(this CustomRoles role) => role.Def().Team == CustomRoleTypes.Neutral;
    /// <summary>インポスター陣営でも第三陣営でもない役職 (ユニット役職の親も含む)</summary>
    public static bool IsCrewmate(this CustomRoles role) => role.Def().Team is not (CustomRoleTypes.Impostor or CustomRoleTypes.Madmate or CustomRoleTypes.Neutral);
    public static bool IsVanilla(this CustomRoles role) => role.Def().Has(RoleFlags.Vanilla);
    public static CustomRoles VanillaRoleConversion(this CustomRoles role)
    {
        return role switch
        {
            CustomRoles.NormalImpostor => CustomRoles.Impostor,
            CustomRoles.NormalShapeshifter => CustomRoles.Shapeshifter,
            CustomRoles.NormalPhantom => CustomRoles.Phantom,
            CustomRoles.NormalEngineer => CustomRoles.Engineer,
            CustomRoles.NormalScientist => CustomRoles.Scientist,
            CustomRoles.NormalTracker => CustomRoles.Tracker,
            CustomRoles.NormalNoisemaker => CustomRoles.Noisemaker,
            CustomRoles.NormalViper => CustomRoles.Viper,
            CustomRoles.NormalDetective => CustomRoles.Detective,
            CustomRoles.NormalJudge => CustomRoles.Judge,
            _ => role
        };
    }
    public static bool IsNormalVanillaRole(this CustomRoles role) => role.VanillaRoleConversion() != role;

    /// <summary>新役職等 (トップ掲載の常設役職)</summary>
    public static bool IsNewRole(this CustomRoles role) => role.Def().Has(RoleFlags.NewRole);
    public static bool IsPairRole(this CustomRoles role) => role.Def().Has(RoleFlags.Pair);
    public static bool IsFixedCountRole(this CustomRoles role) => role.Def().Has(RoleFlags.Pair | RoleFlags.FixedCount);
    /// <summary>試合に加わらない観戦者 (GM)</summary>
    public static bool IsSpectator(this CustomRoles role) => role.Def().Has(RoleFlags.Spectator);
    public static bool IsDontShowOptionRole(this CustomRoles role) => role.Def().Has(RoleFlags.DontShowOption);
    public static bool IsDontSelectLoyaltyRole(this CustomRoles role) => role.Def().Has(RoleFlags.DontSelectLoyalty);
    public static bool IsAddAddOn(this CustomRoles role) => role.IsMadmate() || role.Def().Has(RoleFlags.AddAddOn);
    public static bool IsAddOn(this CustomRoles role) => role.Def().Has(RoleFlags.BuffAddOn | RoleFlags.DebuffAddOn);
    public static bool IsBuffAddOn(this CustomRoles role) => role.Def().Has(RoleFlags.BuffAddOn);
    public static bool IsDebuffAddOn(this CustomRoles role) => role.Def().Has(RoleFlags.DebuffAddOn);
    public static bool IsOtherAddOn(this CustomRoles role) => role.Def().Has(RoleFlags.OtherAddOn);
    public static bool IsFirstWriteAddOn(this CustomRoles role) => role.Def().Has(RoleFlags.FirstWriteAddOn);
    public static bool IsDirectKillRole(this CustomRoles role) => role.Def().Has(RoleFlags.DirectKill);
    public static bool IsLoneImpostor(this CustomRoles role) => role.Def().Has(RoleFlags.LoneImpostor);
    public static bool IsNeutralKiller(this CustomRoles role) => role.Def().Has(RoleFlags.NeutralKiller);
    public static bool IsCrewKiller(this CustomRoles role) => role.Def().Has(RoleFlags.CrewKiller);
    /// <summary>他の役職の能力が「キル役」として扱う第三陣営 (RoleFlags.CountedAsKiller)</summary>
    public static bool IsCountedNeutralKiller(this CustomRoles role) => role.IsNeutral() && role.Def().Has(RoleFlags.CountedAsKiller);
    /// <summary>他の役職の能力が「キル役」として扱うクルー (RoleFlags.CountedAsKiller)。シェリフ系のイントロもこれを見る</summary>
    public static bool IsCountedCrewKiller(this CustomRoles role) => role.IsCrewmate() && role.Def().Has(RoleFlags.CountedAsKiller);
    /// <summary>印のどれかを持つか (Enum.HasFlag と取り違えないよう名前を分けている)</summary>
    public static bool HasRoleFlag(this CustomRoles role, RoleFlags flags) => role.Def().Has(flags);

    //CC
    public static bool IsCCRole(this CustomRoles role) => role.Def().Has(RoleFlags.CCLeader | RoleFlags.CCColorCat | RoleFlags.CCNoCat);
    public static bool IsCCLeaderRoles(this CustomRoles role) => role.Def().Has(RoleFlags.CCLeader);
    public static bool IsCCCatRoles(this CustomRoles role) => role.Def().Has(RoleFlags.CCColorCat | RoleFlags.CCNoCat);
    public static bool IsCCColorCatRoles(this CustomRoles role) => role.Def().Has(RoleFlags.CCColorCat);

    public static bool IsNotAssignedRoles(this CustomRoles role)
    {
        return
            role is
            CustomRoles.NotAssigned or
            CustomRoles.MaxMain or
            CustomRoles.ONStart or
            CustomRoles.MaxON or
            CustomRoles.CCStart or
            CustomRoles.MaxCC or
            CustomRoles.HASStart or
            CustomRoles.MaxHAS or
            CustomRoles.StartAddon or
            CustomRoles.MaxAddon;
    }

    public static CustomRoleTypes GetCustomRoleTypes(this CustomRoles role) => role.Def().Team;
    public static int GetCount(this CustomRoles role)
    {
        if (role.IsVanilla())
        {
            var roleOpt = Main.NormalOptions.RoleOptions;
            return role switch
            {
                CustomRoles.Engineer => roleOpt.GetNumPerGame(RoleTypes.Engineer),
                CustomRoles.Scientist => roleOpt.GetNumPerGame(RoleTypes.Scientist),
                CustomRoles.Tracker => roleOpt.GetNumPerGame(RoleTypes.Tracker),
                CustomRoles.Noisemaker => roleOpt.GetNumPerGame(RoleTypes.Noisemaker),
                CustomRoles.Detective => roleOpt.GetNumPerGame(RoleTypes.Detective),
                CustomRoles.Judge => roleOpt.GetNumPerGame(RoleTypes.Judge),
                CustomRoles.Shapeshifter => roleOpt.GetNumPerGame(RoleTypes.Shapeshifter),
                CustomRoles.Phantom => roleOpt.GetNumPerGame(RoleTypes.Phantom),
                CustomRoles.Viper => roleOpt.GetNumPerGame(RoleTypes.Viper),
                CustomRoles.GuardianAngel => roleOpt.GetNumPerGame(RoleTypes.GuardianAngel),
                CustomRoles.Crewmate => roleOpt.GetNumPerGame(RoleTypes.Crewmate),
                _ => 0
            };
        }
        else
        {
            return Options.GetRoleCount(role);
        }
    }
    public static int GetChance(this CustomRoles role)
    {
        if (role.IsVanilla())
        {
            var roleOpt = Main.NormalOptions.RoleOptions;
            return role switch
            {
                CustomRoles.Engineer => roleOpt.GetChancePerGame(RoleTypes.Engineer),
                CustomRoles.Scientist => roleOpt.GetChancePerGame(RoleTypes.Scientist),
                CustomRoles.Tracker => roleOpt.GetChancePerGame(RoleTypes.Tracker),
                CustomRoles.Noisemaker => roleOpt.GetChancePerGame(RoleTypes.Noisemaker),
                CustomRoles.Shapeshifter => roleOpt.GetChancePerGame(RoleTypes.Shapeshifter),
                CustomRoles.Phantom => roleOpt.GetChancePerGame(RoleTypes.Phantom),
                CustomRoles.GuardianAngel => roleOpt.GetChancePerGame(RoleTypes.GuardianAngel),
                CustomRoles.Crewmate => roleOpt.GetChancePerGame(RoleTypes.Crewmate),
                _ => 0
            };
        }
        else
        {
            return Options.GetRoleChance(role);
        }
    }
    public static bool IsEnable(this CustomRoles role) => role.GetCount() > 0;
    public static bool CanMakeMadmate(this CustomRoles role)
    {
        if (role.GetRoleInfo() is SimpleRoleInfo info)
        {
            return info.CanMakeMadmate;
        }

        return false;
    }
    public static RoleTypes GetRoleTypes(this CustomRoles role)
    {
        var roleInfo = role.GetRoleInfo();
        if (roleInfo != null)
            return roleInfo.BaseRoleType.Invoke();
        return role switch
        {
            CustomRoles.GM => RoleTypes.GuardianAngel,

            _ => role.IsImpostor() ? RoleTypes.Impostor : RoleTypes.Crewmate,
        };
    }
}
public enum CountTypes
{
    OutOfGame,
    None,
    Crew,
    Impostor,
    Jackal,
    Pirate,
}