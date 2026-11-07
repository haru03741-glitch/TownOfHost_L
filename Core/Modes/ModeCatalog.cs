using System.Collections.Generic;
using UnityEngine;

namespace TownOfHostL.Core.Modes;

/// <summary>モードの一覧と、いま遊んでいるモード。</summary>
public static class ModeCatalog
{
    public static readonly StandardMode Standard = new();
    public static readonly HideAndSeekMode HideAndSeek = new();
    public static readonly CatchCatMode CatchCat = new();
    public static readonly VanillaHideAndSeekMode Vanilla = new();

    /// <summary>いま遊んでいるモード。本編のかくれんぼで TOHL が休んでいる間は <see cref="Vanilla"/>。</summary>
    public static GameMode Current
    {
        get
        {
            if (VanillaHideAndSeek.IsSuspended) return Vanilla;
            return Options.CurrentGameMode switch
            {
                CustomGameMode.HideAndSeek => HideAndSeek,
                CustomGameMode.CatchCat => CatchCat,
                _ => Standard,
            };
        }
    }

    /// <summary>かくれんぼ専用の役職 (狐・荒らし) で遊んでいるか。</summary>
    public static bool IsHideAndSeekWithOwnRoles => Current is HideAndSeekMode { UsesStandardRoles: false };
    /// <summary>通常の役職を入れてかくれんぼをしているか。</summary>
    public static bool IsHideAndSeekWithStandardRoles => Current is HideAndSeekMode { UsesStandardRoles: true };
    public static bool IsCatchCat => Current is CatchCatMode;

    /// <summary>設定をメニューや一覧に出すか (どのモードでも隠す設定を含めて見る)。</summary>
    public static bool IsShown(this OptionItem option) => !option.IsHidden && Current.ShowsOption(option);
}

public sealed class StandardMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.Standard;
}

/// <summary>
/// TOHL のかくれんぼ。会議とサボタージュが無く、インポスターは開始から待ってからキルできる。
/// 役職はかくれんぼ専用の物 (狐・荒らし) か、通常モードの役職かを設定で選ぶ。
/// </summary>
public sealed class HideAndSeekMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.HideAndSeek;

    public override bool UsesStandardRoles => Options.HideAndSeekRolePool.GetValue() == 1;
    public override bool IsHideAndSeek => true;
    public override float StartWaitTime => Options.KillDelay.GetFloat();

    public override bool AllowsDoorSabotage => Options.AllowCloseDoors.GetBool();
    public override bool AllowsSabotage => false;
    public override bool AllowsReport(PlayerControl reporter, NetworkedPlayerInfo target) => false;
    public override bool BootsFromVents => Options.IgnoreVent.GetBool();
    // 通常の役職ではアドミンなどで隠れ場所が割れるので、道具を使えなくする
    public override bool DisablesDevices => UsesStandardRoles;
    // 専用の役職では色で陣営を見分ける
    public override bool KillsOnColorChange => !UsesStandardRoles;

    public override bool ShowsOption(OptionItem option)
    {
        if (!UsesStandardRoles) return base.ShowsOption(option);
        if (option.GameMode == CustomGameMode.Standard) return true;
        // 通常の役職で遊ぶ時は、専用の役職の設定を出さない
        return base.ShowsOption(option) && !IsOwnRoleOption(option);
    }

    private static HashSet<OptionItem> ownRoleOptions;
    private static bool IsOwnRoleOption(OptionItem option)
    {
        if (ownRoleOptions == null)
        {
            ownRoleOptions = new();
            foreach (var role in CustomRolesHelper.AllHASRoles)
                if (Options.CustomRoleSpawnChances.TryGetValue(role, out var spawn)) ownRoleOptions.Add(spawn);
        }
        while (option.Parent != null) option = option.Parent;
        return ownRoleOptions.Contains(option);
    }

    public override void SelectRoles() => HideAndSeekRoleAssign.Assign();

    public override (string Key, Color Color)? Label => UsesStandardRoles ? ("StandardHAS", Color.yellow) : ("HideAndSeek", Color.red);
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

/// <summary>本編のかくれんぼ (<see cref="VanillaHideAndSeek"/>)。TOHL は常駐の物だけを残して休む。</summary>
public sealed class VanillaHideAndSeekMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.VanillaHideAndSeek;
    public override GamePatchScope PatchScope => GamePatchScope.ResidentOnly;
    public override bool UsesStandardRoles => false;
}
