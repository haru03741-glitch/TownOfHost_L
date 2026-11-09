// 設定画面の頁 (全般・役職・ゲーム) の置き場になる本編の画面 (GameOptionsMenu) の初期化。
// 頁そのものは https://github.com/haru03741-glitch/More-Roles-Plus src/Options/SettingsMenu.cs (GPL-3.0) を参考にしている
// (その元は https://github.com/waffle-ful/Aeterna-End-K-not Patches/GameOptionsMenuPatch.cs (GPL-3.0))
using System;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Roles.Core;

using TownOfHostL.Core.Modes;

namespace TownOfHostL;

public static class ModGameOptionsMenu
{
    // タブの番号: 本編は 0〜2、TOHL の区分は 3 から TabGroup の順、その次が検索の結果
    public const int FirstModTab = 3;
    public static readonly int SearchTab = FirstModTab + Enum.GetValues(typeof(TabGroup)).Length;
    public static readonly int PresetTab = SearchTab + 1;
    public static int TabIndex = 0;
    public static bool IsModTab(int tabNum) => tabNum >= FirstModTab;
}

[HarmonyPatch(typeof(GameOptionsMenu))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Settings)]
public static class GameOptionsMenuPatch
{
    [HarmonyPatch(nameof(GameOptionsMenu.Initialize)), HarmonyPrefix]
    private static bool InitializePrefix(GameOptionsMenu __instance)
    {
        if (!ModGameOptionsMenu.IsModTab(ModGameOptionsMenu.TabIndex)) return true;

        if (__instance.Children == null || __instance.Children.Count == 0)
        {
            __instance.MapPicker.gameObject.SetActive(false);
            __instance.Children = new Il2CppSystem.Collections.Generic.List<OptionBehaviour>();
            __instance.CreateSettings();
            __instance.cachedData = GameOptionsManager.Instance.CurrentGameOptions;
            for (int i = 0; i < __instance.Children.Count; i++)
            {
                OptionBehaviour optionBehaviour = __instance.Children[i];
                optionBehaviour.OnValueChanged = new Action<OptionBehaviour>(__instance.ValueChanged);
            }
            __instance.InitializeControllerNavigation();
        }

        return false;
    }
    [HarmonyPatch(nameof(GameOptionsMenu.CreateSettings)), HarmonyPrefix]
    private static bool CreateSettingsPrefix(GameOptionsMenu __instance)
    {
        var gradient = __instance.transform.FindChild("Gradient");
        gradient.localPosition = new Vector3(3.88f, -4.44f, -20f);
        gradient.localScale = new Vector3(0.449f, 0.3605f, 7.2125f);

        var maskBg = __instance.MaskBg.transform;
        maskBg.localScale = new Vector3(6.6f, 5f, 0.56f);
        var maskArea = __instance.MaskArea.transform;
        maskArea.localScale = new Vector3(6.6f, 5f, 0.56f);

        if (!ModGameOptionsMenu.IsModTab(ModGameOptionsMenu.TabIndex)) return true;
        // 全般と役職・検索の結果は行を並べず、自前の頁にする (この画面は頁の置き場)
        try
        {
            if (GameSettingMenuPatch.IsPresetHost(__instance)) SettingsUI.PresetPage.Build(__instance);
            else if (GameSettingMenuPatch.IsRoleHost(__instance)) SettingsUI.RolePage.Build(__instance);
            else if (GameSettingMenuPatch.IsModTabMenu(__instance)) SettingsUI.SettingsPages.General.Build(__instance);
            else return true;
        }
        catch (Exception e) { Logger.Exception(e, "GameOptionsMenuPatch.Page"); }
        return false;
    }

    // 本編のゲーム設定のタブ: 本編の行は隠し、本編の設定を自前の頁で並べる
    [HarmonyPatch(nameof(GameOptionsMenu.CreateSettings)), HarmonyPostfix]
    private static void CreateSettingsPostfix(GameOptionsMenu __instance)
    {
        if (ModGameOptionsMenu.IsModTab(ModGameOptionsMenu.TabIndex)) return;
        // 本編の OnEnable を差し替えているので、この時点では GameSettingMenu.Instance がまだ無いことがある
        var menu = GameSettingMenu.Instance ? GameSettingMenu.Instance : __instance.GetComponentInParent<GameSettingMenu>();
        if (!menu || !menu.GameSettingsTab || menu.GameSettingsTab.Pointer != __instance.Pointer) return;
        SettingsUI.SettingsPages.GameTab = __instance;
        try { SettingsUI.SettingsPages.Game.Build(__instance); }
        catch (Exception e) { Logger.Exception(e, "GameOptionsMenuPatch.GamePage"); }
    }
}

// 本編の数値と文字の選択肢の ± の動き (Shift で 5 倍幅・端で反対の端へ回る)。本編の画面の ± にも掛かる
[HarmonyPatch(typeof(NumberOption))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Settings)]
public static class NumberOptionPatch
{
    [HarmonyPatch(nameof(NumberOption.AdjustButtonsActiveState)), HarmonyPrefix]
    private static bool AdjustButtonsActiveStatePrefix(NumberOption __instance)
    {
        return false;
    }
    [HarmonyPatch(nameof(NumberOption.Increase)), HarmonyPrefix]
    public static bool IncreasePrefix(NumberOption __instance)
    {
        // Shift押しながらの値更新
        if (Input.GetKey(KeyCode.LeftShift))
        {
            __instance.Value = __instance.Value + (__instance.Increment * 5);
            // 超えている場合は最大値
            if (__instance.Value > __instance.ValidRange.max)
            {
                __instance.Value = __instance.ValidRange.max;
            }
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            return false;
        }

        if (__instance.Value == __instance.ValidRange.max)
        {
            __instance.Value = __instance.ValidRange.min;
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            return false;
        }
        return true;
    }
    [HarmonyPatch(nameof(NumberOption.Decrease)), HarmonyPrefix]
    public static bool DecreasePrefix(NumberOption __instance)
    {
        // Shift押しながらの値更新
        if (Input.GetKey(KeyCode.LeftShift))
        {
            __instance.Value = __instance.Value - (__instance.Increment * 5);
            // 超えている場合は最小値
            if (__instance.Value < __instance.ValidRange.min)
            {
                __instance.Value = __instance.ValidRange.min;
            }
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            return false;
        }

        if (__instance.Value == __instance.ValidRange.min)
        {
            __instance.Value = __instance.ValidRange.max;
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            return false;
        }
        return true;
    }
}
[HarmonyPatch(typeof(StringOption))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Settings)]
public static class StringOptionPatch
{
    [HarmonyPatch(nameof(StringOption.AdjustButtonsActiveState)), HarmonyPrefix]
    private static bool AdjustButtonsActiveStatePrefix(StringOption __instance)
    {
        return false;
    }
    [HarmonyPatch(nameof(StringOption.Increase)), HarmonyPrefix]
    public static bool IncreasePrefix(StringOption __instance)
    {
        // Shift押しながらの値更新
        if (Input.GetKey(KeyCode.LeftShift))
        {
            __instance.Value = __instance.Value + 5;
            // 超えている場合は最大値
            if (__instance.Value > __instance.Values.Length - 1)
            {
                __instance.Value = __instance.Values.Length - 1;
            }
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            return false;
        }

        if (__instance.Value == __instance.Values.Length - 1)
        {
            __instance.Value = 0;
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            return false;
        }
        return true;
    }
    [HarmonyPatch(nameof(StringOption.Decrease)), HarmonyPrefix]
    public static bool DecreasePrefix(StringOption __instance)
    {
        // Shift押しながらの値更新
        if (Input.GetKey(KeyCode.LeftShift))
        {
            __instance.Value = __instance.Value - 5;
            // 超えている場合は最小値
            if (__instance.Value < 0)
            {
                __instance.Value = 0;
            }
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            return false;
        }

        if (__instance.Value == 0)
        {
            __instance.Value = __instance.Values.Length - 1;
            __instance.UpdateValue();
            __instance.OnValueChanged.Invoke(__instance);
            return false;
        }
        return true;
    }
}
