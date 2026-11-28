using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

internal static class JumpMenu
{
    private const float BtnW = 1.35f, BtnH = 0.42f;
    private const int Cols = 2;
    private const float ItemW = 1.9f, ItemH = 0.36f, Gap = 0.06f, Pad = 0.1f, Font = 1.25f;
    private static readonly Color Line = new(0.36f, 0.38f, 0.42f, 1f);
    private static readonly Color Fill = new(0.12f, 0.13f, 0.15f, 1f);
    private static readonly Color PanelFill = new(0.07f, 0.075f, 0.09f, 0.97f);
    private static readonly Color Ink = new(0.88f, 0.9f, 0.94f, 1f);

    private static UiScreen _button, _panel;
    private static SectionPage _open;

    public static void Create(Transform parent, Vector3 pos, int layer)
    {
        Forget();
        var b = Widget.Node("Tohl_JumpButton", parent, layer);
        b.transform.localPosition = pos;
        b.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 20;
        _button = new UiScreen(b.transform) { MaxWidth = BtnW, BaseOrder = 1 };
        _button.Set(new Button(new Label(Translator.GetString("SettingsUI.Jump") + " ▼", Font)
        {
            Bold = true, Width = BtnW - 0.2f, NoWrap = true, MinFontSize = 0.8f, TextAlign = TextAlignmentOptions.Center, Color = Ink,
        }, Toggle)
        {
            Size = new Vector2(BtnW, BtnH),
            Padding = new Vector2(0.1f, 0.02f),
            Color = Line,
            Fill = Fill,
        });

        var p = Widget.Node("Tohl_JumpPanel", parent, layer);
        p.transform.localPosition = pos + new Vector3(-BtnW / 2f, -BtnH / 2f - 0.06f, -5f);
        p.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 30;
        _panel = new UiScreen(p.transform) { Anchor = Align.Start, MaxWidth = 10f, BaseOrder = 1 };
        _open = null;
    }

    public static void Forget()
    {
        _button?.Clear();
        _panel?.Clear();
        if (_button != null && _button.Root) Object.Destroy(_button.Root.gameObject);
        if (_panel != null && _panel.Root) Object.Destroy(_panel.Root.gameObject);
        _button = _panel = null;
        _open = null;
    }

    private static SectionPage Current =>
        SettingsPages.Game.Visible ? SettingsPages.Game : SettingsPages.General.Visible ? SettingsPages.General
        : SettingsPages.Mode.Visible ? SettingsPages.Mode : SettingsPages.Assign.Visible ? SettingsPages.Assign : null;

    public static void Tick()
    {
        if (_button == null || !_button.Alive) return;
        var cur = Current;
        var go = _button.Root.gameObject;
        if (go.activeSelf != (cur != null)) go.SetActive(cur != null);
        if (_open != null && _open != cur) Close();
    }

    private static void Toggle()
    {
        if (_open != null) { Close(); return; }
        var page = Current;
        if (page == null || _panel == null || !_panel.Alive) return;
        var items = page.Sections();
        if (items.Count == 0) return;
        _open = page;
        var pos = _panel.Root.position;
        _panel.Root.position = new Vector3(pos.x, pos.y, Mathf.Min(pos.z, page.RootZ - 5f));

        float w = ItemW * Cols + Gap * (Cols - 1);
        var col = new Stack(false) { Gap = Gap, Width = w };
        Stack line = null;
        for (int i = 0; i < items.Count; i++)
        {
            if (i % Cols == 0)
            {
                line = new Stack(true) { Gap = Gap, Width = w, Align = Align.Start };
                col.Add(line);
            }
            int n = i;
            var (title, changed) = items[i];
            line.Add(new Button(new Label(SectionPage.JumpText(title, changed, ItemW - 0.24f, Font), Font)
            {
                Bold = true, Width = ItemW - 0.24f, NoWrap = true, TextAlign = TextAlignmentOptions.Center, Color = Ink,
            }, () => { var pg = _open; Close(); pg?.JumpTo(n); })
            {
                Size = new Vector2(ItemW, ItemH),
                Padding = new Vector2(0.12f, 0.02f),
                Color = Line,
                Fill = Fill,
            });
        }
        _panel.Set(new Box(col) { Padding = new Vector2(Pad, Pad), Color = Line, Fill = PanelFill });
    }

    private static void Close()
    {
        _open = null;
        _panel?.Clear();
    }
}
