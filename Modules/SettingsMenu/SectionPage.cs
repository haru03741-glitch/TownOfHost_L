// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Options/SectionPage.cs (GPL-3.0),
// which is based on https://github.com/Dolly1016/Nebula-Public NebulaPluginNova/Configuration/ConfigurationVariations.cs (GPL-3.0)
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

// 設定画面の「ゲーム」「全般」の頁。真ん中 = (上の段) + 節の見出しと行のスクロール欄・右 = 頁の見出しと節の目次・戻すボタン。
// 行は OptEditor の行 (名前の右の点で初期値から変えた項目が分かる)。
internal sealed class SectionPage
{
    internal sealed class Group
    {
        public string Title;
        public Color Color = Color.white;
        public readonly List<MenuOpt> Opts = new();
    }

    // 頁の置き場と大きさ (左の列からの並びを揃える)。右の欄は目次だけなので行の欄を広く取る
    private static readonly Vector3 Center = new(4.41f, -2.2f, -6f);
    private const float W = 6.3f, H = 4.15f;
    private static readonly float ListW = (float)Math.Round(W * 0.65f, 2), SideW = W - ListW - 0.15f;
    internal static float RowW => ListW - 0.24f;
    private const float HeadH = 0.4f, BarH = 0.4f, RowPad = OptEditor.PlatePad;
    private static readonly Color HeadLine = new(0.45f, 0.62f, 0.8f, 1f);
    private static readonly Color HeadFill = new(0.13f, 0.17f, 0.24f, 1f);
    private static readonly Color HeadText = new(0.9f, 0.95f, 1f, 1f);
    private static readonly Color SubText = new(0.72f, 0.75f, 0.8f, 1f);

    private readonly string _tag;
    private readonly Func<string> _title;
    private readonly Func<Color> _color;
    private readonly Func<List<Group>> _groups;
    private readonly Func<bool, Widget> _top;   // 節の前に置く物 (編集できるか)。null = 無し
    private readonly Action _onBuild;           // 組む前に呼ぶ (項目の作り直しなど)
    private readonly Func<int> _topKey;         // 上の段の見た目が変わる値 (外で変わったら組み直す)
    private int _builtTopKey;
    public Func<string> HeroSub;                // 頁の見出しの下の字 (null = 無し)
    public bool HasPreview;                     // 右の欄に本編の設定の動く図を置く (ゲームの頁)

    private GamePreview _preview;
    private const float PreviewH = 1.24f;   // 映像 (16:9) が欄の幅いっぱいに入る高さ
    // 図のある行と、その地 (カーソルが乗った行を測る)
    private readonly List<(MenuOpt opt, Plate plate)> _hits = new();
    private Vector3 _lastMouse = new(float.NaN, 0f, 0f);
    private Plate _marked;   // 図を出している行 (地を図の色に寄せて、どの行の図かを字を使わずに示す)
    private readonly OptEditor _editor;

    private sealed class Entry
    {
        public Group Group;
        public Box Head;
        public Label TocLabel;
        public bool Changed;
    }

    private readonly List<Entry> _entries = new();
    private readonly List<MenuOpt> _shownOpts = new(); // 今並べている行 (外で値を変えた後に、出る行が変わったかを比べる)
    private List<Group> _all = new();
    private UiScreen _screen;
    private ScrollView _list;
    private Button _resetBtn, _undoBtn;
    private Label _resetLabel;
    private float _resetArmedAt = -100f;
    private const float ResetArmSeconds = 3f;
    private bool _built;
    private IntPtr _menu;

    public SectionPage(string tag, Func<string> title, Color color, Func<List<Group>> groups, Func<bool, Widget> top = null, Action onBuild = null, Func<int> topKey = null)
        : this(tag, title, () => color, groups, top, onBuild, topKey) { }

    // 色が今の状態で替わる頁 (モードの頁)。色は組み直す時に読む
    public SectionPage(string tag, Func<string> title, Func<Color> color, Func<List<Group>> groups, Func<bool, Widget> top = null, Action onBuild = null, Func<int> topKey = null)
    {
        _tag = tag;
        _title = title;
        _color = color;
        _groups = groups;
        _top = top;
        _onBuild = onBuild;
        _topKey = topKey;
        _editor = new OptEditor(OnShownChanged, AfterEdit, CanEdit);
    }

    private static bool CanEdit() => SettingsPages.CanEdit();

    public bool Built => _built && _screen != null && _screen.Alive;

    public void Build(GameOptionsMenu m)
    {
        // 同じ画面を出し直しただけなら、そのまま使う (本編が画面を出すたびに組み立てを呼ぶ)
        // (その間に本編が行を出し直していたら隠し直す)
        if (Built && m && _menu == m.Pointer)
        {
            HideVanilla(m);
            return;
        }
        Forget();
        if (!m || !UiAssets.Box) return;
        HideVanilla(m);
        var root = Widget.Node(RootName, m.transform, m.gameObject.layer);
        root.transform.localPosition = Center;
        root.AddComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder = 5;
        SettingsCover.Add(root);
        _screen = new UiScreen(root.transform) { MaxWidth = W, BaseOrder = 1 };
        _menu = m.Pointer;
        _built = true;
        Rebuild();
    }

    private string RootName => "Tohl_" + _tag;

    // タブの元の中身 (本編の行の一覧・スクロール・マップ選び) は使わない。コントローラーで閉じるボタンと頁だけ残す
    private void HideVanilla(GameOptionsMenu m)
    {
        string root = RootName;
        for (int i = 0; i < m.transform.childCount; i++)
        {
            var c = m.transform.GetChild(i);
            if (c.name != "CloseButton" && c.name != root && c.gameObject.activeSelf) c.gameObject.SetActive(false);
        }
    }

    public void Forget()
    {
        _list?.Remember();
        _screen?.Clear();
        if (_screen != null && _screen.Root) UnityEngine.Object.Destroy(_screen.Root.gameObject);
        _screen = null;
        _list = null;
        _resetBtn = _undoBtn = null;
        _entries.Clear();
        _shownOpts.Clear();
        _all = new List<Group>();
        _visible.Clear();
        _pendingFrom = 0;
        _editor.Clear();
        _built = false;
        _menu = IntPtr.Zero;
    }

    public void Rebuild()
    {
        if (_screen == null || !_screen.Alive) return;
        _list?.Remember();
        _entries.Clear();
        _shownOpts.Clear();
        _editor.Clear();
        _hits.Clear();
        _preview = null;
        _visible.Clear();
        _pendingFrom = 0;
        _onBuild?.Invoke();
        _all = _groups();
        _builtTopKey = _topKey?.Invoke() ?? 0;
        bool edit = CanEdit();
        _buildEdit = edit;

        float rowW = RowW;
        var rows = new Stack(false) { Gap = RowGap, Width = rowW };
        var watch = new List<MenuOpt>();
        foreach (var g in _all) watch.AddRange(g.Opts);
        _editor.Watch(watch);
        var top = _top?.Invoke(edit);
        if (top != null)
        {
            top.Align = Align.Start;
            rows.Add(top);
        }
        _hasTop = top != null;
        foreach (var g in _all)
        {
            var shown = new List<MenuOpt>();
            int values = 0;
            foreach (var opt in g.Opts)
            {
                if (!opt.Shown) continue;
                shown.Add(opt);
                if (!opt.IsText) values++;
            }
            _shownOpts.AddRange(shown);
            if (values > 0) _visible.Add((g, shown, values));
        }
        // 目次は全部の節で先に作る (見出しの箱は節を作った時に入る)
        foreach (var v in _visible) _entries.Add(new Entry { Group = v.g, Changed = AnyChanged(v.g) });

        // 開いた時に見える所 (欄の高さほど) だけ先に作り、残りの節は次のフレームから少しずつ足す。
        // 前に下の方までスクロールしていた時は、その位置を出せるよう全部作る
        bool defer = ScrollView.RememberedY(_tag) < 0.01f;
        int built = 0;
        while (_pendingFrom < _visible.Count && (!defer || built < FirstRows))
            built += AddGroup(rows, _pendingFrom++);
        _editor.Refresh();
        _list = new ScrollView(new Vector2(ListW, H), rows) { Tag = _tag };
        _screen.Set(new Stack(true, _list, Side(edit)) { Gap = 0.15f, Align = Align.Start });
        _marked = null;
        MarkShown();
    }

    // 開いた時にまとめて作る行の数。残りは Tick で 1 フレームに数節ずつ足す
    private const int FirstRows = 14;
    private const float RowGap = 0.06f;
    private const float PumpMs = 6f;
    private readonly List<(Group g, List<MenuOpt> shown, int values)> _visible = new();
    private int _pendingFrom;
    private bool _buildEdit, _hasTop;

    // 節を 1 つ (間の空き・見出し・行) target に並べる。並べた行の数を返す
    private int AddGroup(Stack target, int i)
    {
        var (g, shown, values) = _visible[i];
        float rowW = RowW;
        bool edit = _buildEdit;
        var e = _entries[i];
        if (i > 0 || _hasTop) target.Add(new Spacer(rowW, 0.16f));
        string title = $"{g.Title}  <size=70%><color=#B8C0CC>{string.Format(Translator.GetString("SettingsUI.Count"), values)}</color></size>";
        e.Head = new Box(new Label(title, 1.65f) { Bold = true, Color = HeadText, Width = rowW - 0.3f, MinFontSize = 1.1f })
        {
            Size = new Vector2(rowW, HeadH + 0.06f),
            Padding = new Vector2(0.15f, 0.04f),
            Color = HeadLine,
            Fill = HeadFill,
            Align = Align.Start,
        };
        target.Add(e.Head);
        // 行ごとに地を交互に濃淡 (横に長い行でも目で追いやすく)
        int k = 0;
        foreach (var opt in shown)
        {
            Widget r;
            if (opt.IsText)
            {
                // 親のある見出し (設定の途中の小見出し): 字だけ
                var tc = opt.NameColor;
                r = new Label(opt.Label, OptEditor.NarrowFont) { Bold = true, Width = rowW - RowPad * 2f - opt.Depth * 0.2f, Color = tc == Color.white ? SubText : MenuOpt.Readable(tc) };
            }
            else r = _editor.NarrowRow(opt, edit, rowW - RowPad * 2f, opt.Depth * 0.2f);
            var plated = OptEditor.Plated(r, rowW, k++);
            if (HasPreview && GamePreview.Has(opt) && plated is Plate plate) _hits.Add((opt, plate));
            target.Add(plated);
            var map = SpawnMapView.After(opt, rowW, edit);
            if (map != null) target.Add(map);
        }
        return shown.Count;
    }

    // まだ作っていない節を、時間の許す分 (all なら全部) 足す
    private void Pump(bool all)
    {
        if (_pendingFrom >= _visible.Count || _list == null || !Built) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            var chunk = new Stack(false) { Gap = RowGap, Width = RowW };
            AddGroup(chunk, _pendingFrom++);
            _list.Append(chunk, RowGap);
        }
        while (_pendingFrom < _visible.Count && (all || sw.Elapsed.TotalMilliseconds < PumpMs));
        _editor.Refresh();
        MarkShown();
    }

    private Widget Side(bool edit)
    {
        var col = new Stack(false) { Gap = 0.06f, Width = SideW };
        col.Add(Hero(SideW, _color(), _title(), HeroSub?.Invoke() ?? ""));
        if (HasPreview)
        {
            _preview = new GamePreview(SideW, PreviewH);
            col.Add(_preview);
        }
        // 目次: 押すとその節の見出しへ (節の中に初期値から変えた項目があれば点)
        var toc = new Stack(false) { Gap = 0.05f, Width = SideW - 0.2f };
        for (int i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            int n = i;
            e.TocLabel = new Label(TocText(e), 1.3f) { Width = SideW - 0.44f, NoWrap = true, Bold = true, MinFontSize = 0.9f };
            toc.Add(new Button(e.TocLabel, () => Jump(n)) { Size = new Vector2(SideW - 0.2f, 0.34f), Padding = new Vector2(0.1f, 0.02f), Color = new Color(0.42f, 0.42f, 0.45f, 1f), Hollow = true });
        }
        float tocH = H - HeroH - 0.06f - (edit ? BarH + 0.06f : 0f) - (_preview != null ? PreviewH + 0.06f : 0f);
        col.Add(new ScrollView(new Vector2(SideW, tocH), toc) { Tag = _tag + ":toc" });
        if (edit)
        {
            float bw = (SideW - 0.05f) / 2f;
            _resetArmedAt = -100f;
            _resetLabel = new Label(Translator.GetString("SettingsUI.ResetAll"), 1.2f) { Width = bw - 0.3f, MinFontSize = 0.4f, NoWrap = true, TextAlign = TextAlignmentOptions.Center };
            _resetBtn = new Button(_resetLabel, ResetAll) { Size = new Vector2(bw, BarH), Color = ResetColor() };
            _undoBtn = new Button(Translator.GetString("SettingsUI.Undo"), Undo, 1.2f) { Size = new Vector2(bw, BarH), Color = UndoColor };
            col.Add(new Stack(true, _resetBtn, _undoBtn) { Gap = 0.05f });
        }
        return col;
    }

    // ---- 右の欄の見出し: 区分の色の地に大きな名前 ----

    internal const float HeroH = 0.98f;

    // tools = 見出しの左上に置く操作のボタン (役職の欄の前後・閉じる)
    internal static Widget Hero(float width, Color c, string title, string sub, Widget tools = null)
    {
        var hero = new Layers(width, HeroH);
        var fill = Color.Lerp(Color.black, c, 0.2f);
        fill.a = 0.55f;
        hero.Add(new Box(null) { Size = new Vector2(width, HeroH), Color = Color.Lerp(c, Color.black, 0.5f), Fill = fill, Hollow = true }, 0f, 0f);
        float textW = width - 0.3f;
        var text = new Stack(false, new Label(title, 1.9f) { Bold = true, Color = c, Width = textW, MinFontSize = 1.2f, Align = Align.Start }) { Gap = 0f, Width = textW };
        if (!string.IsNullOrEmpty(sub)) text.Add(new Label(sub, 1.05f) { Color = SubText, Width = textW, Align = Align.Start });
        hero.Add(text, 0.14f, 0.06f, false, true);
        if (tools != null) hero.Add(tools, 0.1f, 0.08f);
        return hero;
    }

    private static string TocText(Entry e) => e.Changed ? e.Group.Title + OptEditor.ChangedMark : e.Group.Title;

    internal static string JumpText(string title, bool changed, float width, float font)
    {
        string mark = changed ? OptEditor.ChangedMark : "";
        if (width <= 0f || Label.Measure(title + mark, font, true) <= width) return title + mark;
        for (int len = title.Length - 1; len > 1; len--)
        {
            string s = title[..len] + "…" + mark;
            if (Label.Measure(s, font, true) <= width) return s;
        }
        return title[..1] + "…" + mark;
    }

    public bool Visible => Built && _screen.Root.gameObject.activeInHierarchy;

    public List<(string title, bool changed)> Sections()
    {
        var list = new List<(string, bool)>(_entries.Count);
        foreach (var e in _entries) list.Add((e.Group.Title, AnyChanged(e.Group)));
        return list;
    }

    public void JumpTo(int n) => Jump(n);

    public float RootZ => _screen.Root.position.z;

    private static bool AnyChanged(Group g)
    {
        foreach (var opt in g.Opts)
            if (opt.Changed) return true;
        return false;
    }

    private Color ResetColor()
    {
        foreach (var g in _all)
            if (AnyChanged(g)) return OptEditor.ChangedColor;
        return OptEditor.Dim;
    }

    private static Color UndoColor => OptHistory.Any ? Color.white : OptEditor.Dim;

    // 見出しを欄の上端へ
    private void Jump(int n)
    {
        if (n < 0 || n >= _entries.Count || _list == null) return;
        // まだ作っていない節へ飛ぶ時は、残りを先に全部作る
        if (n >= _pendingFrom) Pump(true);
        var head = _entries[n].Head?.BackRenderer;
        var s = _list.Scroller;
        if (!head || !s) return;
        float hy = s.Inner.InverseTransformPoint(head.transform.position).y;
        _list.ScrollTo(_list.Size.y / 2f - hy - HeadH / 2f - 0.04f);
    }

    private void ResetAll()
    {
        if (!CanEdit()) return;
        if (Time.unscaledTime - _resetArmedAt > ResetArmSeconds)
        {
            _resetArmedAt = Time.unscaledTime;
            _resetLabel?.SetText(Translator.GetString("SettingsUI.ResetAllConfirm"));
            return;
        }
        _resetArmedAt = -100f;
        _resetLabel?.SetText(Translator.GetString("SettingsUI.ResetAll"));
        bool any = false;
        OptHistory.BeginGroup();
        OptEditor.Busy++;
        try
        {
            foreach (var g in _all)
                foreach (var opt in g.Opts)
                {
                    int before = opt.Index;
                    if (!opt.ResetToDefault()) continue;
                    OptHistory.Push(opt, before);
                    OptEditor.AfterStep(opt);
                    any = true;
                }
        }
        finally
        {
            OptEditor.Busy--;
            OptHistory.EndGroup();
        }
        if (any) SettingsPages.RefreshAll();
    }

    private static void Undo()
    {
        if (!OptHistory.Any || !CanEdit()) return;
        OptEditor.Busy++;
        try { OptHistory.Undo(); }
        finally { OptEditor.Busy--; }
        SettingsPages.RefreshAll();
    }

    private void OnShownChanged()
    {
        Rebuild();
        GameSettingMenuPatch.RefreshCategoryLabels();
    }

    private void AfterEdit(MenuOpt opt)
    {
        _preview?.Focus(opt);
        MarkShown();
        RefreshMarks();
        GameSettingMenuPatch.RefreshCategoryLabels();
    }

    // 値を変えた後: 目次の点と、戻すボタンの色
    public void RefreshMarks()
    {
        if (!Built) return;
        foreach (var e in _entries)
        {
            e.Changed = AnyChanged(e.Group);
            string s = TocText(e);
            var l = e.TocLabel;
            var t = l?.Built;
            if (t && s != l.Content) { l.Content = s; t.text = s; }
        }
        _resetBtn?.SetColor(ResetColor());
        _undoBtn?.SetColor(UndoColor);
        if (_resetArmedAt > 0f && Time.unscaledTime - _resetArmedAt > ResetArmSeconds)
        {
            _resetArmedAt = -100f;
            _resetLabel?.SetText(Translator.GetString("SettingsUI.ResetAll"));
        }
    }

    // 外 (プリセット・コマンド・検索の結果の行・取り消し) で値を変えた後。出る行が変わっていれば組み直す
    public void RefreshFromOutside()
    {
        if (!Built) return;
        _preview?.Refresh();
        // 自分の行を押して値を書き換えている最中は、その後始末が行う
        if (OptEditor.Busy > 0) return;
        if (_topKey != null && _topKey() != _builtTopKey) { Rebuild(); return; }
        int k = 0;
        bool same = true;
        foreach (var g in _all)
        {
            foreach (var opt in g.Opts)
            {
                if (!opt.Shown) continue;
                if (k >= _shownOpts.Count || _shownOpts[k] != opt) { same = false; break; }
                k++;
            }
            if (!same) break;
        }
        if (!same || k != _shownOpts.Count) { Rebuild(); return; }
        _editor.Refresh();
        RefreshMarks();
    }

    private void MarkShown()
    {
        if (_preview == null) return;
        Plate now = null;
        foreach (var (opt, plate) in _hits)
            if (GamePreview.Shows(opt)) { now = plate; break; }
        if (now == _marked) return;
        if (_marked?.BuiltBack) _marked.BuiltBack.color = _marked.Color;
        _marked = now;
        if (_marked?.BuiltBack) _marked.BuiltBack.color = Color.Lerp(_marked.Color, HeadLine, 0.45f);
    }

    // 毎フレーム (頁が見えている間): カーソルが乗った図のある行の図に切り替え、図を動かす
    public void Tick()
    {
        Pump(false);
        if (_resetArmedAt > 0f && Time.unscaledTime - _resetArmedAt > ResetArmSeconds)
        {
            _resetArmedAt = -100f;
            _resetLabel?.SetText(Translator.GetString("SettingsUI.ResetAll"));
        }
        if (_preview == null || !Visible) return;
        var mouse = Input.mousePosition;
        if (mouse != _lastMouse && _hits.Count > 0)
        {
            _lastMouse = mouse;
            var cam = Camera.main;
            var mask = _list?.Scroller ? _list.Scroller.ClickMask : null;
            if (cam && mask)
            {
                Vector2 p = cam.ScreenToWorldPoint(mouse);
                // 欄の外 (スクロールで隠れた行) には当てない
                if (mask.OverlapPoint(p))
                    foreach (var (opt, plate) in _hits)
                    {
                        var b = plate.BuiltBack;
                        if (!b) continue;
                        var bb = b.bounds;
                        if (p.x < bb.min.x || p.x > bb.max.x || p.y < bb.min.y || p.y > bb.max.y) continue;
                        _preview.Focus(opt);
                        MarkShown();
                        break;
                    }
            }
        }
        _preview.Tick();
    }
}
