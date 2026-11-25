using System.Collections.Generic;
using UnityEngine;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Core.Modes;

/// <summary>モードの一覧と、いま遊んでいるモード。</summary>
public static class ModeCatalog
{
    public static readonly StandardMode Standard = new();
    public static readonly CatchCatMode CatchCat = new();
    public static readonly VanillaHideAndSeekMode Vanilla = new();
    public static readonly TaskBattleMode TaskBattle = new();
    public static readonly SuddenDeathMode SuddenDeath = new();

    /// <summary>いま遊んでいるモード。本編のかくれんぼで TOHL が休んでいる間は <see cref="Vanilla"/>。</summary>
    public static GameMode Current
    {
        get
        {
            if (VanillaHideAndSeek.IsSuspended) return Vanilla;
            return Options.CurrentGameMode switch
            {
                CustomGameMode.CatchCat => CatchCat,
                CustomGameMode.TaskBattle => TaskBattle,
                CustomGameMode.SuddenDeath => SuddenDeath,
                _ => Standard,
            };
        }
    }

    public static bool IsCatchCat => Current is CatchCatMode;

    /// <summary>設定をメニューや一覧に出すか (どのモードでも隠す設定を含めて見る)。</summary>
    public static bool IsShown(this OptionItem option)
    {
        if (option.IsHidden || !(option.EnabledCondition?.Invoke() ?? true)) return false;
        var mode = Current;
        return mode.ShowsOption(option) && mode.HasTopics(option.AllTopics);
    }
}

public sealed class StandardMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.Standard;
}

/// <summary>CatchCat (選択肢には出していない)。</summary>
public sealed class CatchCatMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.CatchCat;

    public override bool UsesStandardRoles => false;
    public override bool AllowsDoorSabotage => false;
    public override bool AllowsSabotage => false;
    public override bool AllowsReport(PlayerControl reporter, NetworkedPlayerInfo target)
        => target == null || !TownOfHostL.CatchCat.Option.IgnoreReport.GetBool();
    public override bool KillsOnColorChange => true;

    public override void SelectRoles() => CatchCatRoleAssign.Assign();

    public override (string Key, Color Color)? Label => ("CatchCat", Color.gray);
}

/// <summary>本編のかくれんぼ (<see cref="VanillaHideAndSeek"/>)。TOHL は常駐の物と、ここで挙げた束だけを残して休む。</summary>
public sealed class VanillaHideAndSeekMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.VanillaHideAndSeek;
    public override PatchBundles LiveBundles => PatchBundles.Core | PatchBundles.Settings | PatchBundles.VanillaHideAndSeek;
    public override bool UsesStandardRoles => false;
    // 本編のかくれんぼで効くのは、本編のかくれんぼの決まり (VanillaHnsRules)・TOHL が載せるかくれんぼの役職の設定・「ゲームを終了しない」(親だけ。終わり方ごとの子は通常モード用)
    public override bool ShowsOption(OptionItem option) => option.GameMode == Id || option == Options.NoGameEnd;
    public override (string Key, Color Color)? Label => ("VanillaHideAndSeekLabel", Color.cyan);
}
