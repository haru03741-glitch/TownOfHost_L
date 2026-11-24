using System;
using Hazel;
using TMPro;
using UnityEngine;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Modules;

/// <summary>
/// 役職ごとのボタンの見え方 (字・隠すか・隅の残り回数)。
/// ホストは自分の画面にこれを当て、同じ版の TOHL の客には客の分を組んで送る (<see cref="Net.ModClientSync"/>)。
/// 客の画面では隠す・字を替える・数を出すだけで、本編のボタンを使えるようにはしない。
/// </summary>
public readonly struct RoleHudState : IEquatable<RoleHudState>
{
    /// <summary>キルボタンの字。null は本編の字</summary>
    public readonly string KillLabel;
    /// <summary>能力ボタンの字。null は本編の字</summary>
    public readonly string AbilityLabel;
    public readonly bool HideKill, HideAbility, HideVent, HideSabotage;
    /// <summary>ボタンの隅の残り回数。負なら出さない</summary>
    public readonly int Uses;
    public readonly bool UsesOnKill;
    public readonly Color32 UsesColor;

    public RoleHudState(string killLabel, string abilityLabel, bool hideKill, bool hideAbility, bool hideVent, bool hideSabotage, int uses, bool usesOnKill, Color32 usesColor)
    {
        KillLabel = killLabel;
        AbilityLabel = abilityLabel;
        HideKill = hideKill;
        HideAbility = hideAbility;
        HideVent = hideVent;
        HideSabotage = hideSabotage;
        Uses = uses;
        UsesOnKill = usesOnKill;
        UsesColor = usesColor;
    }

    public static readonly RoleHudState None = new(null, null, false, false, false, false, -1, false, Color.white);

    public bool Equals(RoleHudState o)
        => KillLabel == o.KillLabel && AbilityLabel == o.AbilityLabel
           && HideKill == o.HideKill && HideAbility == o.HideAbility && HideVent == o.HideVent && HideSabotage == o.HideSabotage
           && Uses == o.Uses && UsesOnKill == o.UsesOnKill
           && UsesColor.r == o.UsesColor.r && UsesColor.g == o.UsesColor.g && UsesColor.b == o.UsesColor.b;
    public override bool Equals(object obj) => obj is RoleHudState o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(KillLabel, AbilityLabel, Uses, HideKill, HideAbility);

    public void Write(MessageWriter w)
    {
        byte flags = 0;
        if (KillLabel != null) flags |= 1;
        if (AbilityLabel != null) flags |= 2;
        if (HideKill) flags |= 4;
        if (HideAbility) flags |= 8;
        if (HideVent) flags |= 16;
        if (HideSabotage) flags |= 32;
        if (UsesOnKill) flags |= 64;
        w.Write(flags);
        if (KillLabel != null) w.Write(KillLabel);
        if (AbilityLabel != null) w.Write(AbilityLabel);
        w.WritePacked(Uses);
        if (Uses >= 0) { w.Write(UsesColor.r); w.Write(UsesColor.g); w.Write(UsesColor.b); }
    }

    public static RoleHudState Read(MessageReader r)
    {
        byte flags = r.ReadByte();
        string kill = (flags & 1) != 0 ? r.ReadString() : null;
        string ability = (flags & 2) != 0 ? r.ReadString() : null;
        int uses = r.ReadPackedInt32();
        Color32 color = uses >= 0 ? new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), 255) : Color.white;
        return new(kill, ability, (flags & 4) != 0, (flags & 8) != 0, (flags & 16) != 0, (flags & 32) != 0, uses, (flags & 64) != 0, color);
    }
}

public static class RoleHud
{
    // 1 つの字の上限 (文字数)。役職の字は長くても十数文字
    private const int MaxLabelLength = 40;

    private static readonly System.Collections.Generic.Dictionary<Type, bool> overridesAbilityText = new();

    /// <summary>
    /// <paramref name="player"/> のボタンの見え方を組む。
    /// <paramref name="remote"/> は客の画面に送る分: 能力ボタンの字は役職が書き換える時だけ載せる
    /// (既定の字はホストから見た本編の役職で決まり、客の手元の役職と食い違うことがある)。
    /// </summary>
    public static RoleHudState Build(PlayerControl player, bool remote)
    {
        if (player == null || !player.IsAlive()) return RoleHudState.None;
        var roleClass = player.GetRoleClass();
        if (roleClass == null) return RoleHudState.None;

        string killLabel = (roleClass as IKiller)?.OverrideKillButtonText(out string text) == true ? Clip(text) : null;
        string abilityLabel = null;
        bool hideAbility = false;
        if (roleClass.HasAbility)
        {
            if (!remote || OverridesAbilityText(roleClass)) abilityLabel = Clip(roleClass.GetAbilityButtonText());
            hideAbility = !roleClass.CanUseAbilityButton();
        }
        if (player.GetCustomRole().IsCCCatRoles())
            hideAbility = !CatchCat.CatPlayer.CanUseVent(player);

        bool hideKill = !player.CanUseKillButton();
        bool hideVent = !player.CanUseImpostorVentButton();
        bool hideSabotage = roleClass is IKiller killer && !killer.CanUseSabotageButton();

        int uses = -1;
        bool usesOnKill = false;
        Color32 usesColor = Color.white;
        try
        {
            if (roleClass.UsesRemaining is int n)
            {
                uses = Math.Max(0, n);
                usesOnKill = roleClass.UsesOnKillButton;
                usesColor = Utils.GetRoleColor(player.GetCustomRole());
            }
        }
        catch (Exception ex) { Logger.Exception(ex, "RoleHud.Uses"); }

        return new(killLabel, abilityLabel, hideKill, hideAbility, hideVent, hideSabotage, uses, usesOnKill, usesColor);
    }

    private static bool OverridesAbilityText(RoleBase roleClass)
    {
        var type = roleClass.GetType();
        if (!overridesAbilityText.TryGetValue(type, out bool overrides))
        {
            var method = type.GetMethod(nameof(RoleBase.GetAbilityButtonText), Type.EmptyTypes);
            overridesAbilityText[type] = overrides = method != null && method.DeclaringType != typeof(RoleBase);
        }
        return overrides;
    }

    /// <summary>ボタンの字は前と同じなら書かない (毎フレーム文字列をゲーム側へ写さない)。本編が書き換えた時は書き直す</summary>
    public static void SetButtonText(ActionButton button, string text)
    {
        if (button == null || text == null) return;
        var label = button.buttonLabelText;
        if (label != null && TextWriteCache.IsCurrent(label, text)) return;
        button.OverrideText(text);
        if (label != null) TextWriteCache.Remember(label, text);
    }

    private static string Clip(string text)
        => text == null ? null : text.Length > MaxLabelLength ? text.Substring(0, MaxLabelLength) : text;

    // ── ボタンの隅の数 (ホストと客で共通) ──

    private static ActionButton usesButton;
    private static int usesShown = -1;

    /// <summary>ボタンの隅の残り回数を、変わった時だけ書く。負なら消す</summary>
    public static void ApplyUses(HudManager hud, in RoleHudState state)
    {
        if (hud == null) return;
        ActionButton target = state.Uses < 0 ? null : state.UsesOnKill ? hud.KillButton : hud.AbilityButton;
        if (usesButton != null && usesButton != target)
        {
            HideUses(usesButton);
            usesButton = null;
            usesShown = -1;
        }
        if (target == null || !EnsureUsesBadge(target, hud)) return;
        if (usesButton == target && usesShown == state.Uses) return;
        target.SetUsesRemaining(state.Uses);
        if (target.usesRemainingSprite != null) target.usesRemainingSprite.color = state.UsesColor;
        usesButton = target;
        usesShown = state.Uses;
    }

    /// <summary>出している数を消す (試合の終わり・役職が無くなった時)</summary>
    public static void ClearUses()
    {
        if (usesButton != null) HideUses(usesButton);
        usesButton = null;
        usesShown = -1;
    }

    private static void HideUses(ActionButton button)
    {
        if (button.usesRemainingSprite != null) button.usesRemainingSprite.gameObject.SetActive(false);
    }

    /// <summary>
    /// 数を出す部品があるか。キルボタンに無い時は、能力ボタンの部品を写して付ける
    /// </summary>
    private static bool EnsureUsesBadge(ActionButton button, HudManager hud)
    {
        if (button.usesRemainingSprite != null && button.usesRemainingText != null) return true;
        var source = hud.AbilityButton;
        if (source == null || source == button || source.usesRemainingSprite == null) return false;
        var copy = UnityEngine.Object.Instantiate(source.usesRemainingSprite, button.transform);
        copy.transform.localPosition = source.usesRemainingSprite.transform.localPosition;
        copy.gameObject.SetActive(false);
        button.usesRemainingSprite = copy;
        button.usesRemainingText = copy.GetComponentInChildren<TextMeshPro>(true);
        return button.usesRemainingText != null;
    }
}
