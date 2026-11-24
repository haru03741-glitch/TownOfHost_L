using AmongUs.GameOptions;
using TownOfHostL.Roles.Core;

namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem MadmateHasLighting;
    public static OptionItem MadmateHasMoon;
    public static OptionItem MadmateRevengeCanImpostor;
    public static OptionItem MadmateRevengeToCrewmate;
    public static OptionItem MadmateRevengeMadmate;
    public static OptionItem MadmateRevengeNeutral;
    public static OptionItem MadCanSeeImpostor;

    /// <summary>サイドキックマッドメイトの「追放時に道連れ」の下に置く、道連れ先の陣営</summary>
    static void SetupHamoMadmateOptions()
    {
        // 既定は、この設定が入る前の道連れ先 (クルーだけ) に合わせる
        MadmateRevengeCanImpostor = BooleanOptionItem.Create("NekoKabochaImpostorsGetRevenged", false, TabGroup.MadmateRoles, false).SetParent(MadmateRevengeCrewmate).SetGameMode(CustomGameMode.Standard);
        MadmateRevengeToCrewmate = BooleanOptionItem.Create("RevengeToCrewmate", true, TabGroup.MadmateRoles, false).SetParent(MadmateRevengeCrewmate).SetGameMode(CustomGameMode.Standard);
        MadmateRevengeMadmate = BooleanOptionItem.Create("NekoKabochaMadmatesGetRevenged", false, TabGroup.MadmateRoles, false).SetParent(MadmateRevengeCrewmate).SetGameMode(CustomGameMode.Standard);
        MadmateRevengeNeutral = BooleanOptionItem.Create("RevengeToNeutral", false, TabGroup.MadmateRoles, false).SetParent(MadmateRevengeCrewmate).SetGameMode(CustomGameMode.Standard);
    }

    /// <summary>マッドメイト共通 (サイドキックに限らず全部のマッドメイトに効く) の視界と見え方</summary>
    static void SetupHamoCommonMadmateOptions()
    {
        MadmateHasLighting = BooleanOptionItem.Create("MadmateHasLighting", false, TabGroup.MadmateRoles, false).SetColorcode("#ec6800").SetGameMode(CustomGameMode.Standard);
        MadmateHasMoon = BooleanOptionItem.Create("MadmateHasMoon", false, TabGroup.MadmateRoles, false).SetColorcode("#ffff33").SetGameMode(CustomGameMode.Standard);
        MadCanSeeImpostor = BooleanOptionItem.Create("MadmateCanSeeImpostor", false, TabGroup.MadmateRoles, false).SetColor(Utils.GetRoleColor(CustomRoles.Snitch)).SetGameMode(CustomGameMode.Standard);
    }
}

public static class HamoMadmate
{
    static bool Standard => Options.CurrentGameMode == CustomGameMode.Standard;

    public static bool IsMadmate(PlayerControl pc)
        => pc != null && (pc.Is(CustomRoles.SKMadmate) || pc.Is(CustomRoleTypes.Madmate));

    public static bool SeesImpostors(PlayerControl seer)
        => Standard && Options.MadCanSeeImpostor != null && Options.MadCanSeeImpostor.GetBool() && IsMadmate(seer);

    public static bool SeesImpostor(PlayerControl seer, PlayerControl target)
        => target != null && SeesImpostors(seer) && target.Is(CustomRoleTypes.Impostor);

    /// <summary>追放時の道連れはサイドキックマッドメイトの設定 (設定の置き場所もサイドキックの下)</summary>
    public static bool RevengesOnExile(PlayerControl pc)
        => pc != null && pc.Is(CustomRoles.SKMadmate) && Options.MadmateRevengeCrewmate.GetBool();

    public static bool UsesRevengeFilter(PlayerControl exiled, CustomDeathReason deathReason)
    {
        if (!Standard || deathReason != CustomDeathReason.Vote || !RevengesOnExile(exiled)) return false;
        if (exiled.GetRoleClass()?.RevengesOnExile ?? false) return false;
        return !Core.PlayerTraits.Has(exiled, Roles.Core.RoleFlags.RevengesOnExile);
    }

    public static bool RevengeTargetOk(PlayerControl candidate)
        => (candidate.Is(CustomRoleTypes.Impostor) && Options.MadmateRevengeCanImpostor.GetBool())
        || (candidate.Is(CustomRoleTypes.Neutral) && Options.MadmateRevengeNeutral.GetBool())
        || (candidate.Is(CustomRoleTypes.Crewmate) && Options.MadmateRevengeToCrewmate.GetBool())
        || (candidate.Is(CustomRoleTypes.Madmate) && Options.MadmateRevengeMadmate.GetBool());

    public static void ApplyVision(PlayerControl player, IGameOptions opt)
    {
        if (!Standard || !IsMadmate(player)) return;
        bool lighting = Options.MadmateHasLighting.GetBool();
        bool moon = Options.MadmateHasMoon.GetBool();
        if (!lighting && !moon) return;
        bool electrical = Utils.IsActive(SystemTypes.Electrical);
        if (moon && electrical)
        {
            opt.SetFloat(FloatOptionNames.CrewLightMod, Main.DefaultCrewmateVision * 5f);
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, Main.DefaultImpostorVision);
        }
        if (!lighting) return;
        if (electrical && moon)
        {
            opt.SetFloat(FloatOptionNames.CrewLightMod, Main.DefaultImpostorVision * 5f);
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, Main.DefaultImpostorVision);
        }
        else if (electrical)
        {
            opt.SetFloat(FloatOptionNames.CrewLightMod, Main.DefaultCrewmateVision);
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, Main.DefaultCrewmateVision);
        }
        else
        {
            opt.SetFloat(FloatOptionNames.CrewLightMod, Main.DefaultImpostorVision);
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, Main.DefaultImpostorVision);
        }
    }
}
