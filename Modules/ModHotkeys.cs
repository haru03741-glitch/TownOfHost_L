using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// TOHL のキー操作 (廃村・会議の強制終了など) を、好きなキーの組み合わせに変えられるようにする。
/// 1 つの操作に組み合わせを 2 つまで (例: Shift + L と F7) 持てる。設定は BepInEx の設定ファイル (Hotkeys) に残る。
/// 変える画面は本編の「キーボード割り当て」の画面を開いている間、その横に出す
/// </summary>
public static class ModHotkeys
{
    public sealed class Hotkey
    {
        public readonly string Id;
        public readonly string Default;
        public readonly bool HostOnly;
        internal ConfigEntry<string> Entry;
        internal KeyCode[][] Combos = Array.Empty<KeyCode[]>();

        public Hotkey(string id, string def, bool hostOnly)
        {
            Id = id;
            Default = def;
            HostOnly = hostOnly;
        }

        public string Name => GetString("Hotkey." + Id);

        /// <summary>このフレームで組み合わせのどれかが押されたか</summary>
        public bool Down()
        {
            if (Recording != null) return false;
            foreach (var c in Combos) if (ComboDown(c)) return true;
            return false;
        }

        internal void Set(KeyCode[][] combos)
        {
            Combos = combos.Where(c => c != null && c.Length > 0).Take(2).ToArray();
            if (Entry != null) Entry.Value = Format(Combos);
        }
    }

    // ---- 操作の一覧 (既定のキーは今までのままにする) ----
    public static readonly Hotkey Abandon = new("Abandon", "Return+LeftShift+L", true);
    public static readonly Hotkey EndMeeting = new("EndMeeting", "Return+LeftShift+M", true);
    public static readonly Hotkey ReportSelf = new("ReportSelf", "Return+RightShift+M", true);
    public static readonly Hotkey ExileSelf = new("ExileSelf", "Return+LeftShift+E", true);
    public static readonly Hotkey ShowChat = new("ShowChat", "Return+LeftShift+C", true);
    public static readonly Hotkey KillFlash = new("KillFlash", "LeftControl+K+L", true);
    public static readonly Hotkey ShowSettings = new("ShowSettings", "LeftControl+N", true);
    public static readonly Hotkey ShowSettingsHelp = new("ShowSettingsHelp", "LeftControl+LeftShift+N", true);
    public static readonly Hotkey ResetOptions = new("ResetOptions", "LeftControl+Delete", true);
    public static readonly Hotkey CopySettings = new("CopySettings", "LeftAlt+C", false);
    public static readonly Hotkey SaveOptionsClipboard = new("SaveOptionsClipboard", "LeftAlt+O", false);
    public static readonly Hotkey SaveOptionsFile = new("SaveOptionsFile", "LeftAlt+L", false);
    public static readonly Hotkey LoadOptionsClipboard = new("LoadOptionsClipboard", "LeftAlt+P", false);
    public static readonly Hotkey DumpLog = new("DumpLog", "LeftControl+F1", false);
    public static readonly Hotkey OpenFolder = new("OpenFolder", "F10", false);

    public static readonly Hotkey[] All =
    {
        Abandon, EndMeeting, ReportSelf, ExileSelf, ShowChat, KillFlash, ShowSettings, ShowSettingsHelp, ResetOptions,
        CopySettings, SaveOptionsClipboard, SaveOptionsFile, LoadOptionsClipboard, DumpLog, OpenFolder,
    };

    public static void Init(ConfigFile config)
    {
        foreach (var h in All)
        {
            h.Entry = config.Bind("Hotkeys", h.Id, h.Default);
            h.Combos = Parse(h.Entry.Value);
            if (h.Combos.Length == 0 && h.Entry.Value.Trim().Length > 0 && h.Entry.Value.Trim() != "None") h.Combos = Parse(h.Default);
        }
    }

    // ---- 組み合わせの判定 ----

    static readonly KeyCode[][] Modifiers =
    {
        new[] { KeyCode.LeftShift, KeyCode.RightShift },
        new[] { KeyCode.LeftControl, KeyCode.RightControl },
        new[] { KeyCode.LeftAlt, KeyCode.RightAlt },
    };

    static int ModGroup(KeyCode k)
    {
        for (int i = 0; i < Modifiers.Length; i++) if (Modifiers[i].Contains(k)) return i;
        return -1;
    }

    static bool Held(KeyCode k, bool exactSide)
    {
        int g = ModGroup(k);
        if (g < 0 || exactSide) return Input.GetKey(k);
        return Input.GetKey(Modifiers[g][0]) || Input.GetKey(Modifiers[g][1]);
    }

    static bool Pressed(KeyCode k, bool exactSide)
    {
        int g = ModGroup(k);
        if (g < 0 || exactSide) return Input.GetKeyDown(k);
        return Input.GetKeyDown(Modifiers[g][0]) || Input.GetKeyDown(Modifiers[g][1]);
    }

    // 組み合わせのキーが全部押されていて、そのどれかがこのフレームで押され、組み合わせに無い Shift・Ctrl・Alt は押されていない。
    // 左右の Shift などは、両方の側が同じ組み合わせの中で使い分けられている時 (右 Shift + Enter + M など、他の操作と同じキーの時) だけ区別する
    static bool ComboDown(KeyCode[] combo)
    {
        if (combo.Length == 0) return false;
        bool exact = NeedsExactSide(combo);
        bool any = false;
        foreach (var k in combo)
        {
            if (!Held(k, exact)) return false;
            if (Pressed(k, exact)) any = true;
        }
        if (!any) return false;
        for (int g = 0; g < Modifiers.Length; g++)
        {
            if (combo.Any(k => ModGroup(k) == g)) continue;
            if (Input.GetKey(Modifiers[g][0]) || Input.GetKey(Modifiers[g][1])) return false;
        }
        return true;
    }

    // 同じキーで左右の Shift などだけが違う組み合わせが他の操作にあれば、左右を区別する
    static bool NeedsExactSide(KeyCode[] combo)
    {
        var plain = Plain(combo);
        foreach (var h in All)
            foreach (var c in h.Combos)
                if (!ReferenceEquals(c, combo) && Plain(c) == plain && !c.OrderBy(x => x).SequenceEqual(combo.OrderBy(x => x)))
                    return true;
        return false;
    }

    static string Plain(KeyCode[] combo)
        => string.Join("+", combo.Select(k => ModGroup(k) is var g && g >= 0 ? "M" + g : k.ToString()).OrderBy(x => x));

    // ---- 文字との行き来 (設定ファイル: "LeftShift+L | F7") ----

    public static KeyCode[][] Parse(string text)
    {
        var list = new List<KeyCode[]>();
        foreach (var part in (text ?? "").Split('|'))
        {
            var keys = new List<KeyCode>();
            foreach (var name in part.Split('+'))
            {
                var n = name.Trim();
                if (n.Length == 0) continue;
                if (Enum.TryParse<KeyCode>(n, true, out var k) && k != KeyCode.None) keys.Add(k);
                else { keys.Clear(); break; }
            }
            if (keys.Count > 0) list.Add(keys.Distinct().ToArray());
        }
        return list.Take(2).ToArray();
    }

    public static string Format(KeyCode[][] combos) => combos.Length == 0 ? "None" : string.Join(" | ", combos.Select(c => string.Join("+", c)));

    static int Order(KeyCode k) => ModGroup(k) switch { 1 => 0, 2 => 1, 0 => 2, _ => 3 };

    public static string Display(KeyCode[] combo)
        => combo == null || combo.Length == 0 ? GetString("Hotkey.None") : string.Join(" + ", combo.OrderBy(Order).Select(KeyName));

    static string KeyName(KeyCode k) => k switch
    {
        KeyCode.LeftShift => "Shift", KeyCode.RightShift => "右Shift",
        KeyCode.LeftControl => "Ctrl", KeyCode.RightControl => "右Ctrl",
        KeyCode.LeftAlt => "Alt", KeyCode.RightAlt => "右Alt",
        KeyCode.Return => "Enter", KeyCode.KeypadEnter => "Enter(テンキー)",
        >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => ((int)(k - KeyCode.Alpha0)).ToString(),
        _ => k.ToString(),
    };

    // ---- 組み合わせを押して覚える ----

    /// <summary>今キーを覚えている操作 (無ければ null)。この間は TOHL のキー操作を受け付けない</summary>
    public static Hotkey Recording { get; private set; }
    static int recordingSlot;
    static readonly List<KeyCode> held = new();
    static readonly KeyCode[] candidates = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
        .Where(k => k != KeyCode.None && k < KeyCode.Mouse0).Distinct().ToArray();

    public static void StartRecording(Hotkey h, int slot)
    {
        Recording = h;
        recordingSlot = slot;
        held.Clear();
    }

    public static void CancelRecording()
    {
        Recording = null;
        held.Clear();
    }

    // 押している間に増えたキーを集め、全部離した時に 1 つの組み合わせとして決める。Esc でやめる・Backspace で消す
    static void UpdateRecording()
    {
        if (Recording == null) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { CancelRecording(); return; }
        if (held.Count == 0 && (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)) && !AnyModifier())
        {
            SetSlot(Recording, recordingSlot, null);
            CancelRecording();
            return;
        }
        foreach (var k in candidates)
            if (Input.GetKeyDown(k) && !held.Contains(k) && held.Count < 4) held.Add(k);
        if (held.Count > 0 && !held.Any(Input.GetKey))
        {
            // Shift などだけの組み合わせは使わせない (文字の入力や他の操作とぶつかる)
            if (held.Any(k => ModGroup(k) < 0)) SetSlot(Recording, recordingSlot, held.ToArray());
            CancelRecording();
        }
    }

    static bool AnyModifier() => Modifiers.Any(g => Input.GetKey(g[0]) || Input.GetKey(g[1]));

    static void SetSlot(Hotkey h, int slot, KeyCode[] combo)
    {
        var list = h.Combos.ToList();
        while (list.Count <= slot) list.Add(null);
        list[slot] = combo;
        h.Set(list.ToArray());
        Logger.Info($"キーを変えた: {h.Id} = {h.Entry?.Value}", nameof(ModHotkeys));
    }

    public static void ResetAll()
    {
        foreach (var h in All) h.Set(Parse(h.Default));
    }

    /// <summary>同じ組み合わせを使っている他の操作 (重なりの注意を出す)</summary>
    static Hotkey ConflictOf(Hotkey h, KeyCode[] combo)
    {
        if (combo == null) return null;
        var me = combo.OrderBy(x => x).ToArray();
        foreach (var o in All)
        {
            if (o == h) continue;
            foreach (var c in o.Combos)
                if (c.OrderBy(x => x).SequenceEqual(me)) return o;
        }
        return null;
    }

    // ---- 画面 (本編のキーボード割り当ての画面の横に出す) ----

    public static bool ForceOpen;

    static bool MapperOpen()
    {
        try
        {
            var m = Rewired.UI.ControlMapper.ControlMapper.current;
            return m != null && m.isOpen;
        }
        catch { return false; }
    }

    public static bool PanelOpen => ForceOpen || MapperOpen();

    public class Panel : MonoBehaviour
    {
        public Panel(IntPtr ptr) : base(ptr) { }

        static GUIStyle sBox, sTitle, sRow, sKey, sKeyOn, sSmall, sBtn;
        static Texture2D bg, keyBg, keyOnBg;
        static float lastScale = -1f;
        static Vector2 scroll;

        public void Update()
        {
            if (Recording != null && !PanelOpen) CancelRecording();
            UpdateRecording();
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static void Styles(float scale)
        {
            if (sBox != null && Mathf.Approximately(scale, lastScale) && bg) return;
            lastScale = scale;
            bg = Solid(new Color(0.08f, 0.09f, 0.12f, 0.96f));
            keyBg = Solid(new Color(0.22f, 0.25f, 0.32f, 1f));
            keyOnBg = Solid(new Color(0.95f, 0.75f, 0.2f, 1f));
            int f = Mathf.Max(11, Mathf.RoundToInt(19f * scale));
            sBox = new GUIStyle { normal = { background = bg } };
            sTitle = new GUIStyle { fontSize = Mathf.RoundToInt(f * 1.25f), fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.3f) }, alignment = TextAnchor.MiddleLeft };
            sRow = new GUIStyle { fontSize = f, normal = { textColor = Color.white }, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            sSmall = new GUIStyle { fontSize = Mathf.RoundToInt(f * 0.8f), normal = { textColor = new Color(0.72f, 0.76f, 0.82f) }, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            sKey = new GUIStyle { fontSize = Mathf.RoundToInt(f * 0.9f), alignment = TextAnchor.MiddleCenter, normal = { background = keyBg, textColor = Color.white }, hover = { background = keyBg, textColor = new Color(1f, 0.9f, 0.5f) }, clipping = TextClipping.Clip };
            sKeyOn = new GUIStyle(sKey) { normal = { background = keyOnBg, textColor = Color.black }, hover = { background = keyOnBg, textColor = Color.black } };
            sBtn = new GUIStyle(sKey) { fontSize = Mathf.RoundToInt(f * 0.8f) };
        }

        public void OnGUI()
        {
            if (!PanelOpen) return;
            float scale = Screen.width / 1920f;
            Styles(scale);
            float pad = 12f * scale, rowH = 34f * scale;
            float w = 640f * scale, h = Mathf.Min(Screen.height - 40f * scale, rowH * (All.Length + 4) + pad * 4f);
            float x = Screen.width - w - 16f * scale, y = (Screen.height - h) / 2f;
            var area = new Rect(x, y, w, h);
            GUI.Box(area, GUIContent.none, sBox);

            float cy = y + pad;
            GUI.Label(new Rect(x + pad, cy, w - pad * 2f, rowH), GetString("Hotkey.Title"), sTitle);
            if (GUI.Button(new Rect(x + w - pad - 150f * scale, cy + 3f * scale, 150f * scale, rowH - 6f * scale), GetString("Hotkey.ResetAll"), sBtn))
            {
                CancelRecording();
                ResetAll();
            }
            cy += rowH;
            string hint = Recording != null ? string.Format(GetString("Hotkey.Recording"), Recording.Name) : GetString("Hotkey.Hint");
            GUI.Label(new Rect(x + pad, cy, w - pad * 2f, rowH * 1.3f), hint, sSmall);
            cy += rowH * 1.4f;

            float nameW = w * 0.36f, keyW = (w - nameW - pad * 4f - 60f * scale) / 2f;
            var view = new Rect(x, cy, w, y + h - cy - pad);
            var content = new Rect(0, 0, w - 20f * scale, rowH * All.Length);
            scroll = GUI.BeginScrollView(view, scroll, content);
            for (int i = 0; i < All.Length; i++)
            {
                var hk = All[i];
                float ry = i * rowH;
                var label = hk.HostOnly ? $"{hk.Name} <size={Mathf.RoundToInt(sSmall.fontSize * 0.9f)}><color=#9aa4b2>({GetString("Hotkey.HostOnly")})</color></size>" : hk.Name;
                sRow.richText = true;
                GUI.Label(new Rect(pad, ry, nameW, rowH), label, sRow);
                for (int slot = 0; slot < 2; slot++)
                {
                    var combo = slot < hk.Combos.Length ? hk.Combos[slot] : null;
                    bool rec = Recording == hk && recordingSlot == slot;
                    var conflict = ConflictOf(hk, combo);
                    string text = rec ? GetString("Hotkey.PressKeys") : Display(combo);
                    if (conflict != null && !rec) text = "⚠ " + text;
                    var r = new Rect(pad + nameW + slot * (keyW + pad), ry + 3f * scale, keyW, rowH - 6f * scale);
                    if (GUI.Button(r, new GUIContent(text, conflict != null ? string.Format(GetString("Hotkey.Conflict"), conflict.Name) : ""), rec ? sKeyOn : sKey))
                    {
                        if (rec) CancelRecording();
                        else StartRecording(hk, Math.Min(slot, hk.Combos.Length));
                    }
                }
                var rr = new Rect(pad + nameW + 2f * (keyW + pad), ry + 3f * scale, 60f * scale, rowH - 6f * scale);
                if (GUI.Button(rr, GetString("Hotkey.Default"), sBtn))
                {
                    CancelRecording();
                    hk.Set(Parse(hk.Default));
                }
            }
            GUI.EndScrollView();
            if (!string.IsNullOrEmpty(GUI.tooltip))
                GUI.Label(new Rect(x + pad, y + h - rowH, w - pad * 2f, rowH), GUI.tooltip, sSmall);
        }
    }
}
