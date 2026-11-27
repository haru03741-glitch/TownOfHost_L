using System;
using System.Collections.Generic;
using TMPro;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

/// <summary>
/// 役職を選ぶ設定 (FilterOptionItem) の一覧。± で 1 つずつ送る代わりに、選べる役職を陣営ごとに全部並べ、押して ON/OFF する。
/// 枠が複数ある設定 (ジャッカルウルフの「併せ持つ役職」2 つなど) は、まとめて 1 つの一覧で選ぶ (枠の数まで ON にできる)。
/// </summary>
internal static class RolePickMenu
{
    private const int Cols = 5;
    private const float PanelW = 9.2f, ItemW = 1.7f, ItemH = 0.34f, Gap = 0.06f, Pad = 0.14f, Font = 1.15f, ListH = 3.6f;
    private static readonly Color Line = new(0.36f, 0.38f, 0.42f, 1f);
    private static readonly Color Fill = new(0.12f, 0.13f, 0.15f, 1f);
    private static readonly Color OnFill = new(0.12f, 0.36f, 0.18f, 1f);
    private static readonly Color PanelFill = new(0.07f, 0.075f, 0.09f, 0.98f);
    private static readonly Color Shade = new(0f, 0f, 0f, 0.55f);
    private static readonly Color Ink = new(0.88f, 0.9f, 0.94f, 1f);

    private static UiScreen _shade, _panel;
    private static ScrollView _list;
    private static MenuOpt _from;
    private static Action<MenuOpt, int> _apply;

    public static bool IsOpen => _from != null;

    public static void Create(Transform parent, int layer, float z)
    {
        Forget();
        var s = Widget.Node("Tohl_RolePickShade", parent, layer);
        s.transform.localPosition = new Vector3(0f, 0f, z - 40f);
        s.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 34;
        _shade = new UiScreen(s.transform) { MaxWidth = 20f, BaseOrder = 1 };

        var p = Widget.Node("Tohl_RolePickPanel", parent, layer);
        p.transform.localPosition = new Vector3(0f, -0.15f, z - 41f);
        p.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 35;
        _panel = new UiScreen(p.transform) { MaxWidth = PanelW + 0.5f, BaseOrder = 1 };
    }

    public static void Forget()
    {
        _shade?.Clear();
        _panel?.Clear();
        if (_shade != null && _shade.Alive) UnityEngine.Object.Destroy(_shade.Root.gameObject);
        if (_panel != null && _panel.Alive) UnityEngine.Object.Destroy(_panel.Root.gameObject);
        _shade = _panel = null;
        _from = null;
        _list = null;
        _apply = null;
    }

    /// <summary>設定画面が無くなったら閉じる。Esc でも閉じる (開いている間は後ろを暗い幕で覆うので、頁は替えられない)</summary>
    public static void Tick()
    {
        if (_from == null) return;
        if (_panel == null || !_panel.Alive || Input.GetKeyDown(KeyCode.Escape)) Close();
    }

    /// <summary>opt の一覧を開く。apply(opt, index) で値を書く (取り消しの履歴・画面の作り直しは呼んだ側)</summary>
    public static void Open(MenuOpt opt, Action<MenuOpt, int> apply)
    {
        if (opt?.Item is not FilterOptionItem || _panel == null || !_panel.Alive) return;
        _from = opt;
        _apply = apply;
        Build(true);
    }

    public static void Close()
    {
        _from = null;
        _apply = null;
        _list = null;
        _shade?.Clear();
        _panel?.Clear();
    }

    // 一緒に選ぶ枠 (開いた設定を先頭に)
    private static List<FilterOptionItem> Slots()
    {
        var self = (FilterOptionItem)_from.Item;
        var list = new List<FilterOptionItem> { self };
        if (self.Linked != null)
            foreach (var o in self.Linked)
                if (o != self && !list.Contains(o)) list.Add(o);
        return list;
    }

    private static void Build(bool first)
    {
        float keepY = !first && _list != null && _list.Scroller ? _list.Scroller.Inner.localPosition.y : 0f;
        var slots = Slots();
        var chosen = new HashSet<CustomRoles>();
        foreach (var s in slots) { var r = s.GetRole(); if (r != CustomRoles.NotAssigned) chosen.Add(r); }

        if (first)
            _shade.Set(new Button(new Spacer(20f, 12f), Close) { Size = new Vector2(20f, 12f), Padding = Vector2.zero, Color = Color.clear, Fill = Shade });

        var col = new Stack(false) { Gap = 0.1f, Width = PanelW, Align = Align.Start };
        string title = _from.PlainLabel;
        col.Add(new Label($"<b>{title}</b>   <size=80%>{string.Format(Translator.GetString("RolePick.Count"), chosen.Count, slots.Count)}</size>", 1.5f)
        { Width = PanelW, NoWrap = true, MinFontSize = 0.8f, Color = Ink });
        col.Add(new Label(Translator.GetString(slots.Count > 1 ? "RolePick.HintMulti" : "RolePick.Hint"), 1.05f) { Width = PanelW, Color = new Color(0.7f, 0.72f, 0.76f, 1f) });

        // 陣営ごとに並べる
        var groups = new (string key, List<CustomRoles> roles)[]
        {
            ("RolePick.Impostor", new()), ("RolePick.Madmate", new()), ("RolePick.Crewmate", new()), ("RolePick.Neutral", new()), ("RolePick.Addon", new()),
        };
        var self = (FilterOptionItem)_from.Item;
        foreach (var role in self.Sele)
        {
            if (role == CustomRoles.NotAssigned || !self.IsSelectable(role)) continue;
            int g = role > CustomRoles.StartAddon ? 4 : role.GetCustomRoleTypes() switch
            {
                CustomRoleTypes.Impostor => 0,
                CustomRoleTypes.Madmate => 1,
                CustomRoleTypes.Neutral => 3,
                _ => 2,
            };
            groups[g].roles.Add(role);
        }

        float listW = ItemW * Cols + Gap * (Cols - 1);
        var body = new Stack(false) { Gap = Gap, Width = listW, Align = Align.Start };
        foreach (var (key, roles) in groups)
        {
            if (roles.Count == 0) continue;
            roles.Sort((a, b) => string.Compare(Utils.GetRoleName(a), Utils.GetRoleName(b), StringComparison.CurrentCulture));
            body.Add(new Label($"<b>{Translator.GetString(key)}</b>  <size=80%>({roles.Count})</size>", 1.25f) { Width = listW, Color = Ink });
            Stack line = null;
            for (int i = 0; i < roles.Count; i++)
            {
                if (i % Cols == 0)
                {
                    line = new Stack(true) { Gap = Gap, Width = listW, Align = Align.Start };
                    body.Add(line);
                }
                var role = roles[i];
                bool on = chosen.Contains(role);
                string text = (on ? "<color=#5f5>✔</color> " : "") + Utils.GetRoleColorAndName(role);
                line.Add(new Button(new Label(text, Font) { Bold = true, Width = ItemW - 0.16f, NoWrap = true, MinFontSize = 0.6f, TextAlign = TextAlignmentOptions.Center }, () => Toggle(role))
                {
                    Size = new Vector2(ItemW, ItemH),
                    Padding = new Vector2(0.08f, 0.02f),
                    Color = on ? OptEditor.OnColor : Line,
                    Fill = on ? OnFill : Fill,
                });
            }
        }
        _list = new ScrollView(new Vector2(listW + 0.2f, ListH), body);
        col.Add(_list);

        var foot = new Stack(true) { Gap = 0.12f, Width = PanelW, Align = Align.Start };
        foot.Add(new Button(Translator.GetString("RolePick.Clear"), ClearAll, 1.25f) { Size = new Vector2(1.7f, 0.38f), Color = OptEditor.OffColor, Fill = Fill });
        foot.Add(new Spacer(PanelW - 1.7f * 2f - 0.12f * 2f - Pad * 2f, 0.1f));
        foot.Add(new Button(Translator.GetString("RolePick.Close"), Close, 1.25f) { Size = new Vector2(1.7f, 0.38f), Color = Line, Fill = Fill });
        col.Add(foot);

        _panel.Set(new Box(col) { Padding = new Vector2(Pad, Pad), Color = Line, Fill = PanelFill });
        if (!first && _list.Scroller) _list.ScrollTo(keepY);
    }

    // 押した役職を ON/OFF。ON の役職を押すと外し、OFF の役職は空いている枠 (無ければ開いた枠) に入れる
    private static void Toggle(CustomRoles role)
    {
        if (_from == null || _apply == null) return;
        var slots = Slots();
        var holder = slots.Find(s => s.GetRole() == role);
        if (holder != null) Write(holder, CustomRoles.NotAssigned);
        else Write(slots.Find(s => s.GetRole() == CustomRoles.NotAssigned) ?? slots[0], role);
        if (_from != null) Build(false);
    }

    private static void ClearAll()
    {
        if (_from == null || _apply == null) return;
        foreach (var s in Slots()) if (s.GetRole() != CustomRoles.NotAssigned) Write(s, CustomRoles.NotAssigned);
        if (_from != null) Build(false);
    }

    private static void Write(FilterOptionItem item, CustomRoles role)
    {
        int index = item.Sele.IndexOf(role);
        if (index < 0) return;
        var opt = item == _from.Item ? _from : MenuOpt.Of(item);
        _apply(opt, index);
    }
}
