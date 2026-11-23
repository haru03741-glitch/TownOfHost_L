using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TownOfHostL.Roles.Core;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 役職辞典。全部の役職を陣営ごと・五十音順に並べ、選んだ役職の名前・陣営・出どころ (From)・説明・今の設定を出す。
/// タイトル画面のボタン、試合中の右上の本のボタン、F4 キー、チャットの /dic で開く。どこでも見られる (自分の画面だけ。通信はしない)。
/// </summary>
/// <remarks>
/// 見せ方の案は TownOfHost-hamo の役職の説明画面 (rar006/TownOfHost-hamo@f7b00a9e Modules/RoleInfoShower.cs) で、IMGUI で一から描く。
/// 窓の見た目と OnGUI の扱いは <see cref="HostPanel"/> に合わせる (閉じている間は OnGUI ごと止め、中身の組み直しは選び直した時だけ)。
/// </remarks>
public class RoleDictionary : MonoBehaviour
{
    public RoleDictionary(IntPtr ptr) : base(ptr) { }

    public static RoleDictionary Instance;
    static bool open;
    public static bool IsOpen => open;

    public static void Toggle() => SetOpen(!open);
    public static void SetOpen(bool value)
    {
        open = value;
        if (!value && typing) SetTyping(false);
        if (Instance != null) Instance.enabled = value;
        if (value) { listDirty = true; detailDirty = true; FocusMine(); }
    }

    public void Awake()
    {
        Instance = this;
        useGUILayout = false;
        enabled = false;
    }

    public void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (typing) SetTyping(false);
            else SetOpen(false);
            return;
        }
        if (!typing) return;
        var changed = false;
        var sb = new StringBuilder(search ?? "");
        foreach (var c in Input.inputString ?? "")
        {
            if (c == '\b') { if (sb.Length > 0) { sb.Length--; changed = true; } }
            else if (c == '\n' || c == '\r') SetTyping(false);
            else if (!char.IsControl(c) && sb.Length < 30) { sb.Append(c); changed = true; }
        }
        var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.V)) { sb.Append((GUIUtility.systemCopyBuffer ?? "").Replace("\n", " ")); if (sb.Length > 30) sb.Length = 30; changed = true; }
        if (changed) { search = sb.ToString(); listDirty = true; }
    }

    static bool typing;
    public static bool Typing => open && typing;
    static void SetTyping(bool value)
    {
        typing = value;
        Input.imeCompositionMode = value ? IMECompositionMode.On : IMECompositionMode.Auto;
    }

    [HarmonyLib.HarmonyPatch(typeof(PassiveButtonManager), nameof(PassiveButtonManager.Update))]
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    static class BlockButtonsPatch
    {
        public static bool Prefix(PassiveButtonManager __instance)
        {
            if (!open) return true;
            __instance.HandleMouseOut();
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    static class MovePatch
    {
        public static bool Prefix() => !Typing;
    }

    public static void CheckHotkey()
    {
        if (!Input.GetKeyDown(KeyCode.F4)) return;
        try
        {
            if (HudManager.InstanceExists && HudManager.Instance.Chat != null && HudManager.Instance.Chat.IsOpenOrOpening) return;
        }
        catch { }
        Toggle();
    }


    enum Tab { Mine, All, Crewmate, Impostor, Madmate, Neutral, Unit, AddOn }
    static readonly Tab[] Tabs = (Tab[])Enum.GetValues(typeof(Tab));
    static readonly Tab[] TabsNoMine = Tabs.Where(t => t != Tab.Mine).ToArray();
    static readonly Dictionary<Tab, int> teamCounts = new();
    static readonly List<CustomRoles> mine = new();
    static Texture2D bar;
    static Tab tab = Tab.All;
    static bool onlyEnabled;
    static string search = "";
    static CustomRoles selected = CustomRoles.NotAssigned;

    sealed class Entry
    {
        public CustomRoles Role;
        public string Name;
        public string Shown;
        public Tab Team;
        public Color Color;
    }

    static List<Entry> all;
    static readonly Dictionary<CustomRoles, Entry> extra = new();
    static readonly List<Entry> shown = new();
    static bool listDirty = true, detailDirty = true;
    static GUIContent detailContent = GUIContent.none;
    static float detailHeight;
    static float listScroll, detailScroll;
    static string builtLang;

    static Entry MakeEntry(CustomRoles role)
    {
        var name = Plain(Utils.GetRoleName(role));
        if (name.Length == 0) return null;
        var color = Utils.GetRoleColor(role);
        return new Entry
        {
            Role = role,
            Name = name,
            Shown = $"<color=#{ColorUtility.ToHtmlStringRGB(Readable(color))}>{name}</color>",
            Team = TeamOf(role),
            Color = color,
        };
    }

    static Entry EntryOf(CustomRoles role)
    {
        foreach (var e in all) if (e.Role == role) return e;
        if (!extra.TryGetValue(role, out var x))
        {
            try { x = MakeEntry(role); } catch { x = null; }
            extra[role] = x;
        }
        return x;
    }

    static List<CustomRoles> MyRoles()
    {
        mine.Clear();
        try
        {
            var pc = PlayerControl.LocalPlayer;
            if (pc == null || GameStates.IsLobby || !GameStates.IsInGame) return mine;
            var main = pc.GetCustomRole();
            if (main != CustomRoles.NotAssigned) mine.Add(main);
            var subs = pc.GetCustomSubRoles();
            if (subs != null) foreach (var s in subs) if (s != CustomRoles.NotAssigned && !mine.Contains(s)) mine.Add(s);
        }
        catch { }
        return mine;
    }

    static void FocusMine()
    {
        var roles = MyRoles();
        if (roles.Count == 0)
        {
            if (tab == Tab.Mine) tab = Tab.All;
            return;
        }
        tab = Tab.Mine;
        selected = roles[0];
        listScroll = 0;
    }

    static void BuildAll()
    {
        all = new();
        extra.Clear();
        CompareInfo ja = null;
        try { ja = CultureInfo.GetCultureInfo("ja-JP").CompareInfo; } catch { }
        foreach (var role in Options.CustomRoleSpawnChances.Keys)
        {
            try
            {
                if (role.IsDontShowOptionRole() && !role.IsVanilla()) continue;
                if (role.AliasOf() != CustomRoles.NotAssigned) continue;
                if (!Translator.HasString(role.ToString())) continue;
                var entry = MakeEntry(role);
                if (entry != null) all.Add(entry);
            }
            catch (Exception ex) { Logger.Warn($"役職辞典に {role} を入れられませんでした: {ex.Message}", nameof(RoleDictionary)); }
        }
        Comparison<Entry> cmp = ja != null
            ? (a, b) => ja.Compare(a.Name, b.Name, CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth | CompareOptions.IgnoreCase)
            : (a, b) => string.CompareOrdinal(a.Name, b.Name);
        all.Sort((a, b) => a.Team != b.Team ? ((int)a.Team).CompareTo((int)b.Team) : cmp(a, b));
        teamCounts.Clear();
        teamCounts[Tab.All] = all.Count;
        foreach (var e in all) teamCounts[e.Team] = teamCounts.TryGetValue(e.Team, out var c) ? c + 1 : 1;
        builtLang = TranslationController.InstanceExists ? TranslationController.Instance.currentLanguage?.languageID.ToString() : "";
    }

    static Tab TeamOf(CustomRoles role)
    {
        if (role.IsAddOn() || role.IsOtherAddOn() || role == CustomRoles.Lovers) return Tab.AddOn;
        return role.GetCustomRoleTypes() switch
        {
            CustomRoleTypes.Impostor => Tab.Impostor,
            CustomRoleTypes.Madmate => Tab.Madmate,
            CustomRoleTypes.Neutral => Tab.Neutral,
            CustomRoleTypes.Unit => Tab.Unit,
            _ => Tab.Crewmate,
        };
    }

    static void BuildList()
    {
        listDirty = false;
        if (all == null) BuildAll();
        shown.Clear();
        var q = (search ?? "").Trim();
        if (tab == Tab.Mine)
        {
            foreach (var r in MyRoles())
                if (EntryOf(r) is { } me) shown.Add(me);
            if (shown.Count > 0 && !shown.Any(e => e.Role == selected)) { selected = shown[0].Role; detailDirty = true; }
            listScroll = 0f;
            return;
        }
        foreach (var e in all)
        {
            if (tab != Tab.All && e.Team != tab) continue;
            if (onlyEnabled && !SafeEnabled(e.Role)) continue;
            if (q.Length > 0 && e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && e.Role.ToString().IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            shown.Add(e);
        }
        if (shown.Count > 0 && !shown.Any(e => e.Role == selected)) { selected = shown[0].Role; detailDirty = true; }
        listScroll = Mathf.Min(listScroll, Mathf.Max(0f, shown.Count * RowH - 1f));
    }

    static bool SafeEnabled(CustomRoles role)
    {
        try { return role.IsEnable(); } catch { return false; }
    }

    static string TeamName(Tab t) => t switch
    {
        Tab.Mine => GetString("RoleDictionary.Mine"),
        Tab.All => GetString("RoleDictionary.All"),
        Tab.Crewmate => GetString("TeamCrewmate"),
        Tab.Impostor => GetString("TeamImpostor"),
        Tab.Madmate => GetString("Madmate"),
        Tab.Neutral => GetString("Neutral"),
        Tab.Unit => GetString("RoleDictionary.Unit"),
        Tab.AddOn => GetString("RoleDictionary.AddOn"),
        _ => "",
    };

    static Color TeamColor(Tab t) => t switch
    {
        Tab.Crewmate => new Color(0.55f, 0.85f, 1f),
        Tab.Impostor => new Color(1f, 0.35f, 0.35f),
        Tab.Madmate => new Color(1f, 0.45f, 0.25f),
        Tab.Neutral => new Color(0.85f, 0.65f, 1f),
        Tab.Unit => new Color(0.6f, 1f, 0.6f),
        Tab.AddOn => new Color(1f, 0.85f, 0.4f),
        Tab.Mine => new Color(1f, 0.95f, 0.55f),
        _ => Color.white,
    };

    static void BuildDetail(float width)
    {
        detailDirty = false;
        var sb = new StringBuilder();
        var roles = tab == Tab.Mine ? MyRoles() : null;
        if (roles != null && roles.Count > 0)
        {
            sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(TeamColor(Tab.Mine))}><b>★ {GetString("RoleDictionary.MineTitle")}</b></color>\n");
            for (var i = 0; i < roles.Count; i++)
            {
                sb.Append(i == 0 ? "\n" : "\n<color=#3a4566>────────────────────────</color>\n\n");
                AppendRole(sb, roles[i]);
            }
        }
        else if (selected != CustomRoles.NotAssigned) AppendRole(sb, selected);
        else { detailContent = GUIContent.none; detailHeight = 0; return; }
        detailContent = new GUIContent(sb.ToString());
        detailHeight = sDetail.CalcHeight(detailContent, width);
        detailScroll = 0f;
    }

    static void AppendRole(StringBuilder sb, CustomRoles role)
    {
        var team = TeamOf(role);
        var isMine = MyRoles().Contains(role);
        var c = Readable(Utils.GetRoleColor(role));
        sb.Append($"<size={Mathf.RoundToInt(BigFont)}><b><color=#{ColorUtility.ToHtmlStringRGB(c)}>{Plain(Utils.GetRoleName(role))}</color></b></size>\n");
        if (isMine) sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(TeamColor(Tab.Mine))}>★ {GetString(role.IsAddOn() || role.IsOtherAddOn() || role == CustomRoles.Lovers ? "RoleDictionary.MyAddOn" : "RoleDictionary.MyRole")}</color>   ");
        sb.Append($"<color=#{ColorUtility.ToHtmlStringRGB(TeamColor(team))}>{TeamName(team)}</color>");
        var from = RoleFrom.GetText(role);
        if (from.Length > 0) sb.Append("   ").Append(Rich(from));
        sb.Append('\n');
        try
        {
            if (Options.CustomRoleSpawnChances.TryGetValue(role, out var opt))
            {
                var chance = role.GetChance();
                var count = role.GetCount();
                var state = count > 0 && chance > 0
                    ? $"<color=#7dff9a>{string.Format(GetString("RoleDictionary.On"), chance, count)}</color>"
                    : $"<color=#9aa3b5>{GetString("RoleDictionary.Off")}</color>";
                sb.Append(state).Append('\n');
            }
        }
        catch { }
        try
        {
            var win = PlayerStats.RoleWinLine(role);
            if (win != null) sb.Append($"<color=#9fd7ff>{GetString("RoleDictionary.WinRate")}</color> ").Append(Rich(win)).Append('\n');
        }
        catch { }
        if (Translator.HasString(role + "Info"))
            sb.Append('\n').Append(Heading("RoleDictionary.Summary")).Append("<b>").Append(Rich(GetString(role + "Info"))).Append("</b>\n");
        var info = Translator.HasString(role + "InfoLong") ? GetString(role + "InfoLong") : "";
        if (info.Length > 0) sb.Append('\n').Append(Heading("RoleDictionary.Detail")).Append(Rich(info)).Append('\n');

        try
        {
            if (Options.CustomRoleSpawnChances.TryGetValue(role, out var opt) && opt.Children.Count > 0)
            {
                var set = new StringBuilder();
                Utils.ShowChildrenSettings(opt, ref set);
                if (set.Length > 0)
                    sb.Append('\n').Append(Heading("RoleDictionary.Settings")).Append(Rich(set.ToString()));
            }
        }
        catch { }
    }


    static string Heading(string key) => $"<b><color=#9fd7ff>▍{GetString(key)}</color></b>\n";

    static readonly Regex HexTag = new(@"<#([0-9a-fA-F]{6,8})>", RegexOptions.Compiled);
    static readonly Regex ColorEq = new(@"<color=#([0-9a-fA-F]{6,8})>", RegexOptions.Compiled);
    static readonly Regex Unsupported = new(@"</?(size|line-height|line-indent|voffset|font|mark|sprite|align|alpha|cspace|indent|margin|mspace|nobr|noparse|pos|rotate|s|u|sub|sup|width|link|uppercase|lowercase|smallcaps|style|gradient)(=[^>]*)?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex AnyTag = new("<[^>]+>", RegexOptions.Compiled);

    static string Rich(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\\n", "\n");
        s = Unsupported.Replace(s, "");
        s = HexTag.Replace(s, m => $"<color=#{Lighten(m.Groups[1].Value)}>");
        s = ColorEq.Replace(s, m => $"<color=#{Lighten(m.Groups[1].Value)}>");
        return s;
    }

    static string Plain(string s) => AnyTag.Replace(s ?? "", "").Replace("\\n", " ").Trim();

    static string Lighten(string hex)
    {
        if (!ColorUtility.TryParseHtmlString("#" + hex.Substring(0, 6), out var c)) return hex;
        return ColorUtility.ToHtmlStringRGB(Readable(c));
    }

    static Color Readable(Color c)
    {
        var l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
        if (l >= 0.42f) return c;
        var k = 0.42f / Mathf.Max(0.05f, l);
        return new Color(Mathf.Min(1f, c.r * k + 0.15f), Mathf.Min(1f, c.g * k + 0.15f), Mathf.Min(1f, c.b * k + 0.15f));
    }


    static float sw, sh, scale = -1f;
    static float Scale => sw / 1920f;
    static int Font => Mathf.Max(12, Mathf.RoundToInt(22f * Scale));
    static float BigFont => Font * 1.6f;
    static float RowH => 50f * Scale;
    static float Pad => 10f * Scale;
    static GUIStyle sWindow, sTitle, sRow, sRowOn, sTab, sTabOn, sDetail, sField, sFieldOn, sSmall, sClose, sPanel, sOnChip;
    static Texture2D Bar
    {
        get
        {
            if (bar != null) return bar;
            bar = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            bar.SetPixel(0, 0, Color.white);
            bar.Apply(false, true);
            return bar;
        }
    }
    static float wx = -1, wy, ww, wh;
    static bool dragging;
    static float dragDx, dragDy;

    public void OnGUI()
    {
        sw = Screen.width; sh = Screen.height;
        if (!open) { enabled = false; return; }
        if (!Mathf.Approximately(scale, Scale)) BuildStyles();
        var e = Event.current;
        var type = e.type;

        ww = Mathf.Min(sw * 0.9f, 1400f * Scale);
        wh = Mathf.Min(sh * 0.86f, 860f * Scale);
        if (wx < 0 || type == EventType.Layout && (wx + ww > sw || wy + wh > sh)) { wx = (sw - ww) / 2f; wy = (sh - wh) / 2f; }

        var titleH = 56f * Scale;
        if (type == EventType.MouseDown && e.button == 0 && Inside(e.mousePosition, wx, wy, ww * 0.22f, titleH)) { dragging = true; dragDx = e.mousePosition.x - wx; dragDy = e.mousePosition.y - wy; e.Use(); }
        else if (type == EventType.MouseDrag && dragging) { wx = e.mousePosition.x - dragDx; wy = e.mousePosition.y - dragDy; e.Use(); }
        else if (type == EventType.MouseUp) dragging = false;
        wx = Mathf.Clamp(wx, 0f, Mathf.Max(0f, sw - ww));
        wy = Mathf.Clamp(wy, 0f, Mathf.Max(0f, sh - wh));

        GUI.Box(new Rect(wx, wy, ww, wh), GUIContent.none, sWindow);
        GUI.Label(new Rect(wx + Pad * 2, wy, ww * 0.4f, titleH), GetString("RoleDictionary.Title"), sTitle);
        var cs = titleH * 0.7f;
        if (GUI.Button(new Rect(wx + ww - cs - Pad * 1.5f, wy + (titleH - cs) / 2f, cs, cs), "×", sClose)) { SetOpen(false); return; }

        var fw = ww * 0.24f;
        var fx = wx + ww - cs - Pad * 3f - fw;
        var fh = titleH * 0.62f;
        GUI.Label(new Rect(fx - 70f * Scale, wy + (titleH - fh) / 2f, 70f * Scale, fh), GetString("RoleDictionary.Search"), sSmall);
        var fieldRect = new Rect(fx, wy + (titleH - fh) / 2f, fw, fh);
        if (type == EventType.MouseDown && e.button == 0)
        {
            var hit = fieldRect.Contains(e.mousePosition);
            if (hit != typing) SetTyping(hit);
        }
        var comp = typing ? Input.compositionString ?? "" : "";
        var caret = typing && (int)(Time.unscaledTime * 2f) % 2 == 0 ? "|" : "";
        var fieldText = (search ?? "") + (comp.Length > 0 ? $"<color=#9fd7ff>{comp}</color>" : "") + caret;
        if (fieldText.Length == 0) fieldText = $"<color=#5a6478>{GetString("RoleDictionary.SearchHint")}</color>";
        GUI.Box(fieldRect, fieldText, typing ? sFieldOn : sField);
        var ow = 230f * Scale;
        var ox = fx - 80f * Scale - ow;
        if (GUI.Button(new Rect(ox, wy + (titleH - fh) / 2f, ow, fh), (onlyEnabled ? "☑ " : "☐ ") + GetString("RoleDictionary.OnlyEnabled"), onlyEnabled ? sTabOn : sTab)) { onlyEnabled = !onlyEnabled; listDirty = true; }
        if (FreePlayRole.Available && selected != CustomRoles.NotAssigned)
        {
            var pw = 220f * Scale;
            if (GUI.Button(new Rect(ox - Pad - pw, wy + (titleH - fh) / 2f, pw, fh), "▶ " + GetString("RoleDictionary.PlayAs"), sTabOn))
            {
                var role = selected;
                SetOpen(false);
                FreePlayRole.Become(role);
                return;
            }
        }

        if (all == null) BuildAll();
        var hasMine = MyRoles().Count > 0;
        if (!hasMine && tab == Tab.Mine) { tab = Tab.All; listDirty = true; }
        var tabs = hasMine ? Tabs : TabsNoMine;
        var ty = wy + titleH;
        var th = 44f * Scale;
        var tw = (ww - Pad * 2f) / tabs.Length;
        for (var i = 0; i < tabs.Length; i++)
        {
            var t = tabs[i];
            var num = t == Tab.Mine ? mine.Count : teamCounts.TryGetValue(t, out var tc) ? tc : 0;
            var label = $"<color=#{ColorUtility.ToHtmlStringRGB(TeamColor(t))}>{(t == Tab.Mine ? "★ " : "")}{TeamName(t)}</color> <size={Mathf.RoundToInt(Font * 0.75f)}><color=#8f9ab3>{num}</color></size>";
            if (GUI.Button(new Rect(wx + Pad + tw * i + 2f, ty, tw - 4f, th), label, t == tab ? sTabOn : sTab)) { tab = t; listDirty = true; detailDirty = true; listScroll = 0; }
        }

        if (all == null || builtLang != (TranslationController.InstanceExists ? TranslationController.Instance.currentLanguage?.languageID.ToString() : "")) { all = null; listDirty = true; detailDirty = true; }
        if (listDirty) BuildList();

        var top = ty + th + Pad;
        var bodyH = wy + wh - top - Pad;
        var lx = wx + Pad; var lw = ww * 0.30f;
        GUI.Box(new Rect(lx, top, lw, bodyH), GUIContent.none, sPanel);
        var listH = shown.Count * RowH;
        var maxL = Mathf.Max(0f, listH - bodyH + Pad);
        if (type == EventType.ScrollWheel && Inside(e.mousePosition, lx, top, lw, bodyH)) { listScroll += e.delta.y * RowH; e.Use(); }
        listScroll = Mathf.Clamp(listScroll, 0f, maxL);
        if (maxL > 0f) listScroll = GUI.VerticalScrollbar(new Rect(lx + lw - 16f * Scale, top, 16f * Scale, bodyH), listScroll, bodyH, 0f, listH + Pad);
        var first = Mathf.Max(0, Mathf.FloorToInt(listScroll / RowH));
        for (var i = first; i < shown.Count; i++)
        {
            var y = top + Pad * 0.5f + i * RowH - listScroll;
            if (y + RowH > top + bodyH) break;
            var en = shown[i];
            var r = new Rect(lx + Pad * 0.5f, y, lw - Pad - 18f * Scale, RowH - 4f * Scale);
            if (GUI.Button(r, "  " + en.Shown, en.Role == selected ? sRowOn : sRow)) { selected = en.Role; detailDirty = true; }
            if (type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = Readable(en.Color);
                ImguiTex.Draw(new Rect(r.x + 4f * Scale, r.y + r.height * 0.2f, 5f * Scale, r.height * 0.6f), Bar);
                if (SafeEnabled(en.Role))
                {
                    GUI.color = new Color(0.45f, 1f, 0.6f);
                    GUI.Label(new Rect(r.xMax - 60f * Scale, r.y, 54f * Scale, r.height), "ON", sOnChip);
                }
                GUI.color = old;
            }
        }
        if (shown.Count == 0) GUI.Label(new Rect(lx + Pad, top + Pad, lw - Pad * 2, RowH), GetString("RoleDictionary.None"), sSmall);

        var dx = lx + lw + Pad; var dw = wx + ww - Pad - dx;
        GUI.Box(new Rect(dx, top, dw, bodyH), GUIContent.none, sPanel);
        if (type == EventType.Repaint && selected != CustomRoles.NotAssigned)
        {
            var old = GUI.color;
            var rc = Readable(Utils.GetRoleColor(selected));
            GUI.color = new Color(rc.r, rc.g, rc.b, 0.9f);
            ImguiTex.Draw(new Rect(dx + Pad * 0.5f, top + Pad, 6f * Scale, bodyH - Pad * 2f), Bar);
            GUI.color = old;
        }
        var textW = dw - Pad * 4f - 16f * Scale;
        if (detailDirty) BuildDetail(textW);
        var maxD = Mathf.Max(0f, detailHeight - bodyH + Pad * 2f);
        if (type == EventType.ScrollWheel && Inside(e.mousePosition, dx, top, dw, bodyH)) { detailScroll += e.delta.y * RowH; e.Use(); }
        detailScroll = Mathf.Clamp(detailScroll, 0f, maxD);
        if (maxD > 0f) detailScroll = GUI.VerticalScrollbar(new Rect(dx + dw - 16f * Scale, top, 16f * Scale, bodyH), detailScroll, bodyH, 0f, detailHeight + Pad * 2f);
        GUI.BeginGroup(new Rect(dx + Pad * 2f, top + Pad, textW, bodyH - Pad * 2f));
        GUI.Label(new Rect(0, -detailScroll, textW, detailHeight + 4f), detailContent, sDetail);
        GUI.EndGroup();
    }

    static bool Inside(Vector2 p, float x, float y, float w, float h) => p.x >= x && p.x <= x + w && p.y >= y && p.y <= y + h;

    static void BuildStyles()
    {
        scale = Scale;
        var k = Mathf.Max(0.5f, scale);
        var r = Mathf.RoundToInt(10f * k) * 2;
        var br = Mathf.RoundToInt(6f * k) * 2;
        sWindow = new GUIStyle { normal = { background = HostPanel.RoundTex(new Color(0.05f, 0.06f, 0.13f, 0.97f), r) }, border = HostPanel.Border(r) };
        sPanel = new GUIStyle { normal = { background = HostPanel.RoundTex(new Color(0.09f, 0.11f, 0.21f, 1f), br) }, border = HostPanel.Border(br) };
        sTitle = Text(Font + 6, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.45f, 0.92f, 1f));
        sSmall = Text(Font - 2, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.75f, 0.8f, 0.9f));
        sDetail = Text(Font + 1, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.93f, 0.95f, 1f));
        sOnChip = Text(Mathf.Max(10, Font - 6), FontStyle.Bold, TextAnchor.MiddleRight, Color.white);
        sDetail.wordWrap = true;
        sRow = Btn(new Color(0, 0, 0, 0), new Color(0.2f, 0.26f, 0.45f), br, TextAnchor.MiddleLeft);
        sRowOn = Btn(new Color(0.18f, 0.30f, 0.55f), new Color(0.24f, 0.38f, 0.66f), br, TextAnchor.MiddleLeft);
        sTab = Btn(new Color(0.12f, 0.15f, 0.28f), new Color(0.2f, 0.26f, 0.45f), br, TextAnchor.MiddleCenter);
        sTabOn = Btn(new Color(0.20f, 0.33f, 0.58f), new Color(0.26f, 0.40f, 0.68f), br, TextAnchor.MiddleCenter);
        sClose = Btn(new Color(0.38f, 0.08f, 0.08f), new Color(0.6f, 0.12f, 0.12f), br, TextAnchor.MiddleCenter);
        sField = new GUIStyle
        {
            fontSize = Font - 2,
            alignment = TextAnchor.MiddleLeft,
            normal = { background = HostPanel.RoundTex(new Color(0.02f, 0.03f, 0.07f), br), textColor = Color.white },
            focused = { background = HostPanel.RoundTex(new Color(0.04f, 0.06f, 0.14f), br), textColor = Color.white },
            border = HostPanel.Border(br),
            padding = HostPanel.Border(Mathf.Max(4, br / 2)),
            clipping = TextClipping.Clip,
            richText = true,
        };
        sFieldOn = new GUIStyle(sField) { normal = { background = HostPanel.RoundTex(new Color(0.06f, 0.10f, 0.22f), br), textColor = Color.white } };
        detailDirty = true;
    }

    static GUIStyle Text(int size, FontStyle fs, TextAnchor anchor, Color color)
        => new() { fontSize = size, fontStyle = fs, alignment = anchor, richText = true, normal = { textColor = color } };

    static GUIStyle Btn(Color normal, Color hover, int r, TextAnchor anchor) => new()
    {
        fontSize = Font - 1,
        fontStyle = FontStyle.Bold,
        alignment = anchor,
        richText = true,
        clipping = TextClipping.Clip,
        border = HostPanel.Border(r),
        padding = HostPanel.Border(Mathf.Max(4, r / 2)),
        normal = { background = HostPanel.RoundTex(normal, r), textColor = Color.white },
        hover = { background = HostPanel.RoundTex(hover, r), textColor = Color.white },
        active = { background = HostPanel.RoundTex(new Color(normal.r * 0.7f, normal.g * 0.7f, normal.b * 0.7f, Mathf.Max(normal.a, 0.5f)), r), textColor = Color.white },
    };
}
