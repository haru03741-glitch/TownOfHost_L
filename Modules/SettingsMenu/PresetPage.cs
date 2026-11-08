// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Options/PresetPage.cs (GPL-3.0)
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHostL.SettingsUI;

// 設定画面の「プリセット」の頁。
//   上: 新しいプリセットの名前の欄と知らせの文
//   中: 保存したプリセットの枠 (3 列・クルーの絵・保存した日時・読み込む / 書き出す / 消す)。今の設定のままのプリセットは金の縁。
//       最後の枠は「今の設定を保存」(名前の欄の名前で)
//   下: 取り込む (受け渡しのフォルダから)・フォルダを開く・コードをコピー・コードを読む
// 消すは 2 回押した時だけ消す。押すたびに頁を作り直す (スクロールの位置は残す)
internal static class PresetPage
{
    public static readonly Color TabColor = new(0.45f, 0.85f, 0.6f);

    // 頁の置き場 (ほかの頁と同じ)
    private static readonly Vector3 Center = new(4.41f, -2.2f, -6f);
    private const float W = 6.3f, H = 4.15f;
    private const float NameW = 3.4f, NameH = 0.42f, Gap = 0.12f, SlotH = 1.3f, BarH = 0.4f;
    private const int Cols = 3;

    private static readonly Color SubText = new(0.78f, 0.8f, 0.86f, 1f);
    private static readonly Color Gold = new(1f, 0.82f, 0.2f, 1f);
    private static readonly Color Dim = new(0.42f, 0.42f, 0.45f, 1f);
    private static readonly Color Blue = new(0.4f, 0.75f, 1f), Green = new(0.45f, 0.9f, 0.55f), Red = new(1f, 0.45f, 0.45f);
    // 枠に置く絵 (区分の絵を並び順で回す)
    private static readonly string[] SlotArt = { "Presets", "Game", "ImpostorRoles", "CrewmateRoles", "NeutralRoles", "MadmateRoles", "UnitRoles", "Addons", "ModMainSettings" };

    private static GameObject _root;
    private static UiScreen _screen;
    private static IntPtr _menu;
    private static string _nameText = "";
    private static string _status = "";
    private static List<string> _names = new();
    private static string _current;   // 今の値と同じプリセット (無ければ null)
    private static int _armed = -1;

    public static bool Built => _root && _screen != null && _screen.Alive;

    public static void Forget()
    {
        _screen?.Clear();
        _screen = null;
        if (_root) Object.Destroy(_root);
        _root = null;
        _menu = IntPtr.Zero;
        _armed = -1;
        _status = "";
        _nameText = "";
    }

    public static void Build(GameOptionsMenu m)
    {
        // 同じ画面を出し直しただけなら、中身だけ新しくする (外でプリセットのファイルが増えているかもしれない)
        if (Built && m && _menu == m.Pointer) { Refresh(); return; }
        Forget();
        if (!m || !UiAssets.Box) return;
        // 画面の元の中身 (本編の行の一覧・スクロール) は使わない。コントローラーで閉じるボタンだけ残す
        for (int i = 0; i < m.transform.childCount; i++)
        {
            var c = m.transform.GetChild(i);
            if (c.name != "CloseButton") c.gameObject.SetActive(false);
        }
        _root = Widget.Node("Tohl_presetpage", m.transform, m.gameObject.layer);
        _root.transform.localPosition = Center;
        _root.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 5;
        SettingsCover.Add(_root);
        _menu = m.Pointer;
        _screen = new UiScreen(_root.transform) { MaxWidth = W, BaseOrder = 1 };
        Refresh();
    }

    // 外 (ほかの頁・コマンド・取り消し) で値を変えた後: 金の縁を付け直す
    // (見えていない間は何もしない。出す時に Show が付け直す)
    public static void RefreshFromOutside()
    {
        if (Built && _root.activeInHierarchy) Refresh();
    }

    // 頁を出した時 (別の頁で値を変えていたかもしれない)
    public static void Show()
    {
        if (Built) Refresh();
    }

    private static void Refresh()
    {
        if (!Built) return;
        _names = PresetStore.Names();
        _current = null;
        foreach (var n in _names)
            if (PresetStore.Matches(n)) { _current = n; break; }
        _screen.Set(Page());
    }

    private static Widget Page()
    {
        // 上の段: 名前の欄と、右に知らせの文
        string status = _status.Length > 0 ? _status : _names.Count == 0 ? Tr("PresetPage.NoSaved") : "";
        var field = new TextField(new Vector2(NameW, NameH), _nameText, Tr("PresetPage.NameHint")) { MaxLength = 30, OnChange = s => _nameText = s ?? "", OnSubmit = () => Click(0), OnCancel = TextField.Blur };
        var top = new Stack(true,
            field,
            new Label(status, 1.15f) { Color = SubText, Width = W - NameW - 0.15f, MinFontSize = 0.8f }) { Gap = 0.15f };

        float slotW = (W - 0.2f - Gap * (Cols - 1)) / Cols;
        var grid = new Stack(false) { Gap = Gap, Width = W - 0.2f, Align = Align.Start };
        Stack row = null;
        for (int i = 0; i <= _names.Count; i++)
        {
            if (i % Cols == 0) { row = new Stack(true) { Gap = Gap, Align = Align.Start }; grid.Add(row); }
            row.Add(i < _names.Count ? Slot(i, slotW) : SaveSlot(slotW));
        }
        float listH = H - NameH - BarH - 0.24f;
        var list = new ScrollView(new Vector2(W, listH), grid) { Tag = "presets" };

        float bw = (W - 0.1f * 3) / 4f;
        var bar = new Stack(true,
            new Button(Tr("PresetPage.Import"), () => Click(1), 1.3f) { Size = new Vector2(bw, BarH), Color = Blue },
            new Button(Tr("PresetPage.OpenFolder"), () => Click(2), 1.3f) { Size = new Vector2(bw, BarH), Color = Dim },
            new Button(Tr("PresetPage.CopyCode"), () => Click(5), 1.3f) { Size = new Vector2(bw, BarH), Color = Blue },
            new Button(Tr("PresetPage.PasteCode"), () => Click(6), 1.3f) { Size = new Vector2(bw, BarH), Color = Green }) { Gap = 0.1f };
        return new Stack(false, top, list, bar) { Gap = 0.12f, Width = W };
    }

    // 保存したプリセット 1 つの枠
    private static Widget Slot(int n, float w)
    {
        string name = _names[n];
        bool current = name == _current;
        var slot = new Layers(w, SlotH);
        slot.Add(new Box(null) { Size = new Vector2(w, SlotH), Color = current ? Gold : Color.white, Fill = current ? new Color(0.16f, 0.13f, 0.04f) : Color.black }, 0f, 0f);
        var sp = Utils.LoadSprite($"TownOfHost_L.Resources.MenuCat_{SlotArt[n % SlotArt.Length]}.png", 100f);
        if (sp) slot.Add(new Picture(sp, 0.75f, 0.68f) { Fit = true, Color = new Color(1f, 1f, 1f, 0.9f) }, 0.06f, 0.06f, true);

        float textW = w - 0.2f;
        var at = PresetStore.SavedAt(name);
        var text = new Stack(false, new Label(name, 1.8f) { Bold = true, Width = textW - 0.6f, MinFontSize = 1.1f, Align = Align.Start }) { Gap = 0f, Width = textW };
        if (current) text.Add(new Label(Tr("PresetPage.Current"), 1.0f) { Color = Gold, Width = textW, Align = Align.Start });
        else if (at is DateTime t) text.Add(new Label(string.Format(Tr("PresetPage.SavedAt"), t.ToString("M/d HH:mm")), 1.0f) { Color = SubText, Width = textW, Align = Align.Start });
        slot.Add(text, 0.1f, 0.1f);

        int id = 100 + n * 4;
        float bw = (w - 0.2f - 0.06f * 2) / 3f;
        string del = _armed == n ? Tr("PresetPage.Really") : Tr("PresetPage.Delete");
        var buttons = new Stack(true,
            SmallButton(Tr("PresetPage.Load"), id, bw, Green),
            SmallButton(Tr("PresetPage.Export"), id + 1, bw, Dim),
            SmallButton(del, id + 2, bw, Red)) { Gap = 0.06f };
        slot.Add(buttons, 0.1f, 0.1f, false, true);
        return slot;
    }

    private static Button SmallButton(string text, int id, float w, Color c) =>
        new(new Label(text, 1.1f) { Width = w - 0.08f, MinFontSize = 0.7f, TextAlign = TextAlignmentOptions.Center }, () => Click(id))
        {
            Size = new Vector2(w, 0.32f),
            Padding = new Vector2(0.04f, 0.02f),
            Color = c,
        };

    // 最後の枠: 今の設定を名前の欄の名前で保存する
    private static Widget SaveSlot(float w) =>
        new Button(new Label("＋ " + Tr("PresetPage.SaveCurrent"), 1.5f) { Width = w - 0.3f, MinFontSize = 1f, Color = SubText, TextAlign = TextAlignmentOptions.Center }, () => Click(0))
        {
            Size = new Vector2(w, SlotH),
            Color = Dim,
        };

    private static void Say(string s)
    {
        _status = s ?? "";
        Logger.Info($"プリセット: {s}", "PresetPage");
    }

    private static void Click(int id)
    {
        try { DoClick(id); }
        catch (Exception e) { Logger.Exception(e, "PresetPage"); }
    }

    private static void DoClick(int id)
    {
        int armed = _armed;
        _armed = -1;
        switch (id)
        {
            case 0:
                Say(PresetStore.Save(_nameText.Trim()));
                break;
            case 1:
                Say(PresetStore.Import());
                break;
            case 2:
                Say(PresetStore.OpenShare());
                break;
            case 5:
                Say(PresetStore.CopyCode());
                break;
            case 6:
                Say(PresetStore.PasteCode());
                break;
            default:
            {
                int n = (id - 100) / 4, kind = (id - 100) % 4;
                if (n < 0 || n >= _names.Count) break;
                string name = _names[n];
                if (kind == 0) Say(PresetStore.Load(name));
                else if (kind == 1) Say(PresetStore.Export(name));
                else if (kind == 2)
                {
                    if (armed != n) { _armed = n; Say(string.Format(Tr("PresetPage.PressAgain"), name)); }
                    else Say(PresetStore.Delete(name));
                }
                break;
            }
        }
        Refresh();
    }

    private static string Tr(string key) => Translator.GetString(key);
}
