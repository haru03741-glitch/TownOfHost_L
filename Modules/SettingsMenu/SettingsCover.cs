// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Options/SettingsCover.cs (GPL-3.0)
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

// 設定画面に足した頁 (SortingGroup の順番 5) は描く順番を上げて本編の画面の手前に描くが、
// 本編の ESC の窓とチャットは順番 0 のままなので、それらの手前にも出てしまう。どちらかが開いている間は隠し、閉じたら隠した物だけ戻す。
// 開く・閉じるの後置で即座に見て、後置が呼ばれない道の分は 0.1 秒ごとにも見る (頁が 1 つも無い間は何もしない)
internal static class SettingsCover
{
    private const float Every = 0.1f;

    private static readonly List<GameObject> Roots = new();
    private static readonly List<GameObject> Hidden = new();
    private static bool _covered;
    private static float _next;

    internal static void Add(GameObject root)
    {
        if (!root) return;
        Roots.Add(root);
        if (_covered && root.activeSelf)
        {
            root.SetActive(false);
            Hidden.Add(root);
        }
    }

    // 設定画面が開いている間、毎フレーム
    internal static void Tick()
    {
        if (Roots.Count == 0 || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + Every;
        Refresh();
    }

    internal static void Refresh()
    {
        if (Roots.Count == 0) return;
        for (int i = Roots.Count - 1; i >= 0; i--)
            if (!Roots[i]) Roots.RemoveAt(i);
        bool covered = Roots.Count > 0 && Covered();
        if (covered == _covered) return;
        _covered = covered;
        if (covered)
        {
            foreach (var r in Roots)
            {
                if (!r.activeSelf) continue;
                r.SetActive(false);
                Hidden.Add(r);
            }
        }
        else
        {
            foreach (var h in Hidden)
                if (h) h.SetActive(true);
            Hidden.Clear();
        }
    }

    // 設定画面を閉じた時 (頁は破棄される)
    internal static void Reset()
    {
        Roots.Clear();
        Hidden.Clear();
        _covered = false;
    }

    // 設定画面の頁 (順番 5) や項目へ移動の窓 (30) より手前に、届いたチャットの知らせを描く順番
    private const int ChatNoticeOrder = 40;

    /// <summary>
    /// チャットが届いた時、左上に出る本編の知らせ (と未読の点) を設定画面の頁の手前に描く。
    /// 本編の知らせは順番 0 のままなので、設定画面を開いていると頁の後ろに隠れて、届いたのが分からない
    /// </summary>
    internal static void RaiseChatNotice(ChatController chat)
    {
        if (!chat) return;
        try
        {
            if (chat.chatNotification) Raise(chat.chatNotification.gameObject);
            if (chat.chatNotifyDot) Raise(chat.chatNotifyDot.gameObject);
        }
        catch (System.Exception ex) { Logger.Warn($"チャットの知らせを手前に出せません: {ex.Message}", "SettingsCover"); }
    }

    private static void Raise(GameObject go)
    {
        var g = go.GetComponent<UnityEngine.Rendering.SortingGroup>();
        if (!g) g = go.AddComponent<UnityEngine.Rendering.SortingGroup>();
        if (g.sortingOrder != ChatNoticeOrder) g.sortingOrder = ChatNoticeOrder;
    }

    private static bool Covered()
    {
        var hud = HudManager.InstanceExists ? HudManager.Instance : null;
        if (!hud) return false;
        return hud.GameMenu && hud.GameMenu.IsOpen || hud.Chat && hud.Chat.IsOpenOrOpening;
    }
}

[HarmonyPatch]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Settings)]
internal static class SettingsCoverPatches
{
    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Open)), HarmonyPostfix]
    public static void Open() => SettingsCover.Refresh();

    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Close)), HarmonyPostfix]
    public static void Close() => SettingsCover.Refresh();

    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Toggle)), HarmonyPostfix]
    public static void Toggle() => SettingsCover.Refresh();

    // チャットが届いたら、その知らせを設定画面の手前に出す
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.AddChat)), HarmonyPostfix]
    public static void AddChat(ChatController __instance) => SettingsCover.RaiseChatNotice(__instance);
}
