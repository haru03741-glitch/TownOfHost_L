using TownOfHostL.Roles.Core;

namespace TownOfHostL;

public static partial class Options
{
    // ===== 役職に共通の設定 =====
    public static OptionItem DoubleTriggerThreshold;
    public static OptionItem AutoGrantPet;
    public static OptionItem ErasedBodyRemovePet;
    public static OptionItem ErasedBodyCover;
    public static OptionItem ClientEffects;
    public static OptionItem ClientEffectsBeam;
    public static OptionItem ClientEffectsSlugger;
    public static OptionItem ClientEffectsSound;
    public static OptionItem ClientEffectsVolume;
    public static OptionItem ClientEffectsMeeting;
    public static OptionItem ClientEffectsDeath;
    public static OptionItem ClientEffectsShake;
    public static OptionItem ClientEffectsSkipDummy;
    public static OptionItem ClientEffectsTeleport;
    public static OptionItem MadmateTell;
    static readonly string[] Tellopt = { "NoProcessing", "Crewmate", "Madmate", "Impostor" };

    /// <summary>マッドメイトを占った時の結果</summary>
    public static CustomRoles MadTellOpt()
    {
        switch (Tellopt[MadmateTell?.GetValue() ?? 0])
        {
            case "Crewmate": return CustomRoles.Crewmate;
            case "Madmate": return CustomRoles.Madmate;
            case "Impostor": return CustomRoles.Impostor;
        }
        return CustomRoles.NotAssigned;
    }

    /// <summary>役職に共通の設定 (ダブルトリガー・ペット・死体の消し方・演出など) を作る (Options.Load から呼ぶ)</summary>
    public static void LoadRoleCommonOptions()
    {
        DoubleTriggerThreshold = FloatOptionItem.Create("DoubleTriggerThreshold", new(0.3f, 1f, 0.1f), 1f, TabGroup.ImpostorRoles, false)
            .SetHeader(true)
            .SetValueFormat(OptionFormat.Seconds);
        MadmateTell = StringOptionItem.Create("MadmateTellOption", Tellopt, 0, TabGroup.MadmateRoles, false)
            .SetHeader(true);

        AutoGrantPet = BooleanOptionItem.Create("AutoGrantPet", true, TabGroup.ModMainSettings, false)
            .SetHeader(true)
            .SetColorcode("#f0d36a");
        ErasedBodyRemovePet = BooleanOptionItem.Create("ErasedBodyRemovePet", true, TabGroup.ModMainSettings, false)
            .SetColorcode("#f0d36a");
        ErasedBodyCover = BooleanOptionItem.Create("ErasedBodyCover", true, TabGroup.ModMainSettings, false)
            .SetColorcode("#f0d36a");

        ClientEffects = BooleanOptionItem.Create("ClientEffects", true, TabGroup.ModMainSettings, false)
            .SetColorcode("#7fefff")
            .SetHeader(true);
        ClientEffectsBeam = BooleanOptionItem.Create("ClientEffectsBeam", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
        ClientEffectsSlugger = BooleanOptionItem.Create("ClientEffectsSlugger", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
        ClientEffectsSound = BooleanOptionItem.Create("ClientEffectsSound", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
        ClientEffectsVolume = IntegerOptionItem.Create("ClientEffectsVolume", new(10, 100, 10), 80, TabGroup.ModMainSettings, false)
            .SetValueFormat(OptionFormat.Percent).SetParent(ClientEffectsSound);
        ClientEffectsMeeting = BooleanOptionItem.Create("ClientEffectsMeeting", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
        ClientEffectsDeath = BooleanOptionItem.Create("ClientEffectsDeath", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
        ClientEffectsShake = BooleanOptionItem.Create("ClientEffectsShake", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
        ClientEffectsSkipDummy = BooleanOptionItem.Create("ClientEffectsSkipDummy", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
        ClientEffectsTeleport = BooleanOptionItem.Create("ClientEffectsTeleport", true, TabGroup.ModMainSettings, false).SetParent(ClientEffects);
    }
}
