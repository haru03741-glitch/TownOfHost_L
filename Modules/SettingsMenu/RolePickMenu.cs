using System;
using System.Collections.Generic;
using TMPro;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

/// <summary>
/// 役職を選ぶ設定 (FilterOptionItem) の一覧。± で 1 つずつ送る代わりに、選べる役職を陣営ごとのタブに並べ、押して ON/OFF する。
/// 枠が複数ある設定 (ジャッカルウルフの「併せ持つ役職」2 つ・役職の組み合わせなど) は、まとめて 1 つの一覧で選ぶ (枠の数まで ON にできる)。
/// 一覧は頁で送る (スクロール欄は設定画面の切り抜きと重なって中身が見えなくなるので使わない)。
/// </summary>
internal static class RolePickMenu
{
    private const int Cols = 6, Rows = 8, PerPage = Cols * Rows;
    private const float PanelW = 10.2f, ItemW = 1.6f, ItemH = 0.34f, Gap = 0.06f, Pad = 0.14f, Font = 1.1f;
    private static readonly Color Line = new(0.36f, 0.38f, 0.42f, 1f);
    private static readonly Color Fill = new(0.12f, 0.13f, 0.15f, 1f);
    private static readonly Color OnFill = new(0.12f, 0.36f, 0.18f, 1f);
    private static readonly Color TabFill = new(0.2f, 0.3f, 0.45f, 1f);
    private static readonly Color PanelFill = new(0.07f, 0.075f, 0.09f, 0.98f);
    private static readonly Color Shade = new(0f, 0f, 0f, 0.55f);
    private static readonly Color Ink = new(0.88f, 0.9f, 0.94f, 1f);
    private static readonly Color Sub = new(0.7f, 0.72f, 0.76f, 1f);

    private static UiScreen _shade, _panel;
    private static MenuOpt _from;
    private static Action<MenuOpt, int> _apply;
    private static int _team, _page;

    private static readonly string[] TeamKeys = { "RolePick.Impostor", "RolePick.Madmate", "RolePick.Crewmate", "RolePick.Neutral", "RolePick.Addon" };

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
        BringToFront();
        // 最初に出すタブ: 選んである役職の陣営 (無ければ候補のある最初の陣営)
        var groups = Groups();
        _team = -1;
        foreach (var s in Slots())
        {
            var r = s.GetRole();
            if (r != CustomRoles.NotAssigned) { _team = TeamOf(r); break; }
        }
        if (_team < 0 || groups[_team].Count == 0) _team = Math.Max(0, Array.FindIndex(groups, g => g.Count > 0));
        _page = 0;
        Build(true);
    }

    public static void Close()
    {
        _from = null;
        _apply = null;
        _shade?.Clear();
        _panel?.Clear();
    }

    // 後ろの頁の行 (スクロール欄の中は手前へずらしてある) より手前に置く。押す所は z の小さい物が勝つので、
    // 設定画面の中のいちばん手前の当たり判定よりさらに手前へ
    private static void BringToFront()
    {
        var root = _panel.Root.parent;
        if (!root) return;
        float minZ = float.MaxValue;
        foreach (var c in root.GetComponentsInChildren<Collider2D>(false))
        {
            if (!c || c.transform.IsChildOf(_panel.Root) || c.transform.IsChildOf(_shade.Root)) continue;
            minZ = Mathf.Min(minZ, c.transform.position.z);
        }
        if (minZ == float.MaxValue) return;
        var sp = _shade.Root.position;
        _shade.Root.position = new Vector3(sp.x, sp.y, Mathf.Min(sp.z, minZ - 2f));
        var pp = _panel.Root.position;
        _panel.Root.position = new Vector3(pp.x, pp.y, Mathf.Min(pp.z, minZ - 3f));
    }

    // 一緒に選ぶ枠 (並びは設定の順)
    private static List<FilterOptionItem> Slots()
    {
        var self = (FilterOptionItem)_from.Item;
        if (self.Linked != null) return new List<FilterOptionItem>(self.Linked);
        return new List<FilterOptionItem> { self };
    }

    private static int TeamOf(CustomRoles role) => role > CustomRoles.StartAddon ? 4 : role.GetCustomRoleTypes() switch
    {
        CustomRoleTypes.Impostor => 0,
        CustomRoleTypes.Madmate => 1,
        CustomRoleTypes.Neutral => 3,
        _ => 2,
    };

    private static List<CustomRoles>[] Groups()
    {
        var groups = new List<CustomRoles>[TeamKeys.Length];
        for (int i = 0; i < groups.Length; i++) groups[i] = new();
        var self = (FilterOptionItem)_from.Item;
        foreach (var role in self.Sele)
        {
            if (role == CustomRoles.NotAssigned || !self.IsSelectable(role)) continue;
            groups[TeamOf(role)].Add(role);
        }
        foreach (var g in groups)
            g.Sort((a, b) => string.Compare(Utils.GetRoleName(a), Utils.GetRoleName(b), StringComparison.CurrentCulture));
        return groups;
    }

    private static Button Small(string text, Action click, float w, Color line, Color fill, float font = 1.15f)
        => new(new Label(text, font) { Bold = true, Width = w - 0.14f, NoWrap = true, MinFontSize = 0.6f, TextAlign = TextAlignmentOptions.Center, Color = Ink }, click)
        {
            Size = new Vector2(w, ItemH),
            Padding = new Vector2(0.07f, 0.02f),
            Color = line,
            Fill = fill,
        };

    private static void Build(bool first)
    {
        var slots = Slots();
        var self = (FilterOptionItem)_from.Item;
        var chosen = new HashSet<CustomRoles>();
        foreach (var s in slots) { var r = s.GetRole(); if (r != CustomRoles.NotAssigned) chosen.Add(r); }
        var groups = Groups();

        if (first)
            _shade.Set(new Button(new Spacer(20f, 12f), Close) { Size = new Vector2(20f, 12f), Padding = Vector2.zero, Color = Color.clear, Fill = Shade });

        float innerW = PanelW - Pad * 2f;
        var col = new Stack(false) { Gap = 0.09f, Width = innerW, Align = Align.Start };
        col.Add(new Label($"<b>{_from.PlainLabel}</b>   <size=80%>{string.Format(Translator.GetString("RolePick.Count"), chosen.Count, slots.Count)}</size>", 1.5f)
        { Width = innerW, NoWrap = true, MinFontSize = 0.8f, Color = Ink });
        // 枠が複数ある時は、今の中身を枠の順に (役職の組み合わせは「出たら → 一緒に出る」の向きがある)
        if (slots.Count > 1)
            col.Add(new Label(self.LinkedDisplay(), 1.2f) { Width = innerW, NoWrap = true, MinFontSize = 0.7f, Color = Ink });
        string hint = self.PickHintKey ?? (slots.Count > 1 ? "RolePick.HintMulti" : "RolePick.Hint");
        col.Add(new Label(Translator.GetString(hint), 1.0f) { Width = innerW, Color = Sub });

        // 陣営のタブ
        var tabs = new Stack(true) { Gap = Gap, Width = innerW, Align = Align.Start };
        float tabW = (innerW - Gap * (TeamKeys.Length - 1)) / TeamKeys.Length;
        for (int i = 0; i < TeamKeys.Length; i++)
        {
            int t = i;
            int on = groups[i].FindAll(chosen.Contains).Count;
            string text = $"{Translator.GetString(TeamKeys[i])} <size=75%>({groups[i].Count}){(on > 0 ? $" <color=#5f5>✔{on}</color>" : "")}</size>";
            if (groups[i].Count == 0) text = $"<color=#777>{Translator.GetString(TeamKeys[i])}</color>";
            tabs.Add(Small(text, () => { if (groups[t].Count == 0) return; _team = t; _page = 0; Build(false); }, tabW,
                i == _team ? OptEditor.OnColor : Line, i == _team ? TabFill : Fill, 1.15f));
        }
        col.Add(tabs);

        // 役職 (頁で送る)
        var roles = groups[_team];
        int pages = Math.Max(1, (roles.Count + PerPage - 1) / PerPage);
        _page = Math.Clamp(_page, 0, pages - 1);
        var grid = new Stack(false) { Gap = Gap, Width = innerW, Align = Align.Start };
        Stack line = null;
        int start = _page * PerPage;
        for (int i = 0; i < PerPage; i++)
        {
            if (i % Cols == 0)
            {
                line = new Stack(true) { Gap = Gap, Width = innerW, Align = Align.Start };
                grid.Add(line);
            }
            int n = start + i;
            if (n >= roles.Count) { line.Add(new Spacer(ItemW, ItemH)); continue; }
            var role = roles[n];
            bool on = chosen.Contains(role);
            string text = (on ? "<color=#5f5>✔</color> " : "") + Utils.GetRoleColorAndName(role);
            line.Add(Small(text, () => Toggle(role), ItemW, on ? OptEditor.OnColor : Line, on ? OnFill : Fill, Font));
        }
        col.Add(grid);

        // 下の段: 頁送り・全部 OFF・閉じる
        var foot = new Stack(true) { Gap = 0.1f, Width = innerW, Align = Align.Start };
        foot.Add(Small("◀", () => { if (_page > 0) { _page--; Build(false); } }, 0.6f, _page > 0 ? Line : Fill, Fill));
        foot.Add(new Label($"{_page + 1} / {pages}", 1.2f) { Width = 0.8f, TextAlign = TextAlignmentOptions.Center, Color = Ink });
        foot.Add(Small("▶", () => { if (_page < pages - 1) { _page++; Build(false); } }, 0.6f, _page < pages - 1 ? Line : Fill, Fill));
        foot.Add(new Spacer(innerW - 0.6f * 2f - 0.8f - 1.7f * 2f - 0.1f * 5f, 0.1f));
        foot.Add(Small(Translator.GetString("RolePick.Clear"), ClearAll, 1.7f, OptEditor.OffColor, Fill));
        foot.Add(Small(Translator.GetString("RolePick.Close"), Close, 1.7f, Line, Fill));
        col.Add(foot);

        _panel.Set(new Box(col) { Padding = new Vector2(Pad, Pad), Color = Line, Fill = PanelFill });
    }

    // 押した役職を ON/OFF。ON の役職を押すと外し、OFF の役職は空いている枠 (無ければ開いた枠) に入れる
    private static void Toggle(CustomRoles role)
    {
        if (_from == null || _apply == null) return;
        var slots = Slots();
        var holder = slots.Find(s => s.GetRole() == role);
        if (holder != null) Write(holder, CustomRoles.NotAssigned);
        else Write(slots.Find(s => s.GetRole() == CustomRoles.NotAssigned) ?? (FilterOptionItem)_from.Item, role);
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
