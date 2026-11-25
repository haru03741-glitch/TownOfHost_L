// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Options/SettingsMenu.cs (GPL-3.0),
// which is based on https://github.com/waffle-ful/Aeterna-End-K-not Patches/GameOptionsMenuPatch.cs (GPL-3.0)
// 画面の枠は本編の大きさのまま。左の列 1 本 (ゲーム / 全般 / 区分ごと) と板の上の検索欄で、全部の設定へ行く
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using TownOfHostL.Core.Modes;
using Object = UnityEngine.Object;

namespace TownOfHostL;

[HarmonyPatch(typeof(GameSettingMenu))]
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Settings)]
public class GameSettingMenuPatch
{
    // ゲーム設定メニュータブ
    public enum GameSettingMenuTab
    {
        GamePresets = 0,
        GameSettings,
        RoleSettings,
        Mod_MainSettings,
        Mod_ImpostorRoles,
        Mod_MadmateRoles,
        Mod_CrewmateRoles,
        Mod_NeutralRoles,
        Mod_UnitRoles,
        Mod_AddOns,

        MaxCount,
    }

    private static GameOptionsMenu templateGameOptionsMenu;
    private static PassiveButton templateGameSettingsButton;

    public static bool PresetChanged = false;

    private static string PresetChangedWarning
        => $"\n\n<color=#ffff00><b>{Translator.GetString("PresetChangedWarning")}</b></color>";

    public static void NotifyPresetChanged()
    {
        PresetChanged = true;

        var menu = Object.FindObjectOfType<GameSettingMenu>();
        if (menu == null || menu.MenuDescriptionText == null) return;

        menu.MenuDescriptionText.DestroyTranslator();
        if (!menu.MenuDescriptionText.text.Contains(PresetChangedWarning))
            menu.MenuDescriptionText.text += PresetChangedWarning;
    }

    // 頁の置き場にする画面 (本編のゲーム設定の画面の複製)。全般の頁と、役職の区分・検索の結果の頁 (RoleBrowser 1 つ) で 1 枚ずつ
    static GameOptionsMenu ModTab;
    static GameOptionsMenu RoleHost;
    static GameOptionsMenu PresetHost;
    static GameOptionsMenu ModeHost;
    static SettingsSearchBox SearchBox;
    // 検索欄の今の語 (検索の結果の頁を出している間だけ頁に渡す)
    static string searchQuery = "";
    static GameSettingMenu OpenMenu;
    // 検索を空にした時に戻るタブ
    static int ReturnTab = 1;

    // 左の列 (設定画面の座標・More Roles Plus と同じ寸法)。X は本編のタブのボタンと同じ所。
    // ボタンが CatFitCount 個を超えたら、同じ高さに収まるよう間隔とボタンの高さを詰める
    const float CatX = -2.96f, CatTopY = 1.6f, CatStepY = 0.46f;
    const float CatScaleX = 1f, CatScaleY = 0.78f;
    const int CatFitCount = 7;
    // 区分の絵: ボタンの高さに対する絵の高さ・選んだ時の大きさ
    const float CatArtHeight = 1.05f, CatArtPicked = 1.15f;
    static readonly Vector2 SearchBoxPosition = new(-2.3f, 2.6f);
    const float SearchBoxWidth = 2.1f;
    static readonly Vector2 JumpButtonPosition = new(-0.47f, 2.6f);
    const float SearchBoxHeight = 0.42f;

    // 左の列のボタン (押すと出すタブの番号)
    sealed class Cat
    {
        public PassiveButton Button;
        public int TabNum;
        public TabGroup? Tab; // null = 本編のゲーム設定 (Presets の時はプリセット)
        public bool Presets;
        public bool Mode; // 今のモードだけの設定の頁
        public TextMeshPro Label;
        public string Shown; // 字に書いた今の文
        public Transform Art;
        public Vector3 ArtPos, ArtScale;
        public float ArtLift;
    }
    static readonly List<Cat> Cats = new();

    static readonly Dictionary<TabGroup, string> CategoryColors = new()
    {
        [TabGroup.ModMainSettings] = "#ffef3a",
        [TabGroup.ImpostorRoles] = "#ff3b3b",
        [TabGroup.MadmateRoles] = "#ff6a3d",
        [TabGroup.CrewmateRoles] = "#8cffff",
        [TabGroup.NeutralRoles] = "#ffab1b",
        [TabGroup.UnitRoles] = "#c6f26e",
        [TabGroup.Addons] = "#ff9ae8",
    };
    const string GameCategoryColor = "#8aa2ff";

    // モードの頁のボタンの字 (今のモードで替わる)
    static string ModeLabel() => SettingsUI.SettingsPages.ModeTitle();

    internal static Color CatColor(TabGroup? tab)
    {
        var hex = tab is TabGroup t && CategoryColors.TryGetValue(t, out var c) ? c : GameCategoryColor;
        return ColorUtility.TryParseHtmlString(hex, out var col) ? col : Color.white;
    }

    static string CategoryLabel(TabGroup? tab)
    {
        if (tab is not TabGroup group) return Translator.GetString("MenuCat.Game");
        var name = Translator.GetString($"MenuCat.{group}");
        if (group == TabGroup.ModMainSettings) return name;

        int total = 0, lit = 0;
        foreach (var option in Options.CustomRoleSpawnChances.Values)
        {
            if (option.Tab != group || !option.IsShown()) continue;
            total++;
            if (option.GetInt() > 0) lit++;
        }
        return total == 0 ? name : $"{name}<size=65%>  {lit}/{total}</size>";
    }

    // 出る役職の数が変わった時 (出現率の項目は子を持つので RefreshChildSettings から呼ばれる)
    public static void RefreshCategoryLabels()
    {
        if (OpenMenu == null) return;
        // モードを変えると出す区分が変わる (左の列を組み直す)
        if (ShownCatsKey() != builtCatsKey)
        {
            BuildCats(OpenMenu);
            return;
        }
        foreach (var cat in Cats)
        {
            if (cat.Label == null || cat.Presets) continue;
            // 書き込みは字の組み直しを伴うので、変わった時だけ
            var text = cat.Mode ? ModeLabel() : CategoryLabel(cat.Tab);
            if (text == cat.Shown) continue;
            cat.Shown = text;
            cat.Label.text = text;
        }
    }

    // 頁の置き場の画面か (CreateSettings がどの頁を作るかを決める)
    internal static bool IsRoleHost(GameOptionsMenu menu) => menu && RoleHost && menu.Pointer == RoleHost.Pointer;
    internal static bool IsModTabMenu(GameOptionsMenu menu) => menu && ModTab && menu.Pointer == ModTab.Pointer;
    internal static bool IsPresetHost(GameOptionsMenu menu) => menu && PresetHost && menu.Pointer == PresetHost.Pointer;
    internal static bool IsModeHost(GameOptionsMenu menu) => menu && ModeHost && menu.Pointer == ModeHost.Pointer;
    internal static bool IsPageHost(GameOptionsMenu menu) => IsRoleHost(menu) || IsModTabMenu(menu) || IsPresetHost(menu) || IsModeHost(menu);

    // 左の列の一番下のボタンのすぐ下 (世界座標の左上と幅)。役職の区分ではここに絞り込みの札を置く
    internal static bool CatFoot(out Vector3 topLeft, out float width)
    {
        topLeft = default;
        width = 0f;
        if (Cats.Count == 0) return false;
        var btn = Cats[Cats.Count - 1].Button;
        var col = btn ? btn.GetComponent<Collider2D>() : null;
        if (!col) return false;
        var b = col.bounds;
        topLeft = new Vector3(b.min.x, b.min.y - b.size.y * 0.25f, b.center.z);
        width = b.size.x;
        return true;
    }

    // 左の列のボタン 1 つ (本編のゲーム設定のボタンの複製・押す音と当てた時の見た目は本編のまま)
    static void AddCat(GameSettingMenu menu, TabGroup? tab, int tabNum, float step, float scaleY, bool presets = false, bool mode = false)
    {
        var proto = templateGameSettingsButton;
        var btn = Object.Instantiate(proto, menu.GameSettingsButton.transform.parent);
        string key = presets ? "Presets" : mode ? "Mode" : tab?.ToString() ?? "Game";
        btn.name = "Cat_" + key;
        btn.gameObject.SetActive(true);
        var z = proto.transform.localPosition.z;
        btn.transform.localPosition = new Vector3(CatX, CatTopY - Cats.Count * step, z);
        var ps = proto.transform.localScale;
        btn.transform.localScale = new Vector3(ps.x * CatScaleX, ps.y * scaleY, ps.z);

        // 字: 縦横の縮みを揃え (横だけ潰れないよう)、はみ出す分は自動で小さくする
        var label = btn.GetComponentInChildren<TextMeshPro>();
        label.DestroyTranslator();
        float fix = scaleY / CatScaleX;
        var ls = label.transform.localScale;
        label.transform.localScale = new Vector3(ls.x * fix, ls.y, ls.z);
        var rt = label.rectTransform;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x / fix, rt.sizeDelta.y);
        label.enableWordWrapping = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = label.fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.text = presets ? Translator.GetString("MenuCat.Presets") : mode ? ModeLabel() : CategoryLabel(tab);

        var c = presets ? SettingsUI.PresetPage.TabColor : mode ? SettingsUI.SettingsPages.ModeColor() : CatColor(tab);
        btn.inactiveSprites.GetComponent<SpriteRenderer>().color = new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, 1f);
        btn.activeSprites.GetComponent<SpriteRenderer>().color = c;
        btn.selectedSprites.GetComponent<SpriteRenderer>().color = c;

        var cat = new Cat { Button = btn, TabNum = tabNum, Tab = tab, Presets = presets, Mode = mode, Label = label, Shown = label.text };
        AddCatArt(cat, $"TownOfHost_L.Resources.MenuCat_{key}.png");

        btn.OnMouseOver = new UnityEngine.Events.UnityEvent();
        btn.OnMouseOut = new UnityEngine.Events.UnityEvent();
        btn.OnClick = new();
        btn.OnClick.AddListener((Action)(() => menu.ChangeTab(tabNum, false)));
        menu.ControllerSelectable.Add(btn);
        Cats.Add(cat);
    }

    // ボタンの左端に区分の絵 (クルーのポーズ)。字は絵の右の残りの幅へ寄せる
    static void AddCatArt(Cat cat, string resource)
    {
        var btn = cat.Button;
        var bg = btn.inactiveSprites.GetComponent<SpriteRenderer>();
        var sprite = Utils.LoadSprite(resource, 100f);
        if (bg == null || sprite == null) return;
        var t = btn.transform;
        var b = bg.bounds;
        Vector3 min = t.InverseTransformPoint(b.min), max = t.InverseTransformPoint(b.max);
        float h = max.y - min.y;
        var ls = t.lossyScale;
        float aspect = ls.y / ls.x; // ボタンは縦横で縮みが違うので、絵の横を戻す
        float artH = h * CatArtHeight;

        var go = new GameObject("CatArt");
        go.layer = btn.gameObject.layer;
        go.transform.SetParent(t, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = bg.sortingOrder + 1;
        float s = artH / sprite.bounds.size.y;
        go.transform.localScale = new Vector3(s * aspect, s, 1f);
        go.transform.localPosition = new Vector3(min.x, min.y + h * 0.04f + artH / 2f, -0.01f);
        // 絵の左端をボタンの左の縁の少し内側へ (ポーズで横幅が違う)
        go.transform.position += new Vector3(b.min.x + b.size.y * 0.08f - sr.bounds.min.x, 0f, 0f);
        cat.Art = go.transform;
        cat.ArtPos = go.transform.localPosition;
        cat.ArtScale = go.transform.localScale;
        cat.ArtLift = h * 0.07f;

        var label = cat.Label;
        if (label.sortingOrder <= sr.sortingOrder) label.sortingOrder = sr.sortingOrder + 1;
        // 字の左の余白 = 絵の右端まで
        float edge = label.transform.InverseTransformPoint(new Vector3(sr.bounds.max.x, b.center.y, b.center.z)).x;
        float m = edge - label.rectTransform.rect.xMin;
        if (m > 0f) label.margin = new Vector4(m, 0f, 0f, 0f);
    }

    static void PlaceDescription(GameSettingMenu menu)
    {
        var desc = menu.MenuDescriptionText;
        var last = Cats.Count > 0 ? Cats[^1].Button : null;
        var col = last != null ? last.GetComponent<Collider2D>() : null;
        var panel = menu.transform.FindChild("PanelSprite");
        var panelSr = panel != null ? panel.GetComponent<SpriteRenderer>() : null;
        if (desc == null || col == null || panelSr == null) return;
        var parent = last.transform.parent;
        descParent = parent;
        var cMin = parent.InverseTransformPoint(col.bounds.min);
        var cMax = parent.InverseTransformPoint(col.bounds.max);
        float lowest = cMin.y;
        foreach (var r in last.GetComponentsInChildren<Renderer>(true))
        {
            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            lowest = Mathf.Min(lowest, parent.InverseTransformPoint(r.bounds.min).y);
        }
        float h = cMax.y - cMin.y;
        float top = lowest - h * 0.35f;
        float bottom = parent.InverseTransformPoint(panelSr.bounds.min).y + h * 0.5f;
        if (top <= bottom) return;
        descTop = top;
        descBottom = bottom;
        descLeft = cMin.x;
        descWidth = cMax.x - cMin.x;

        desc.DestroyTranslator();
        var t = desc.transform;
        t.SetParent(parent, false);
        var rt = desc.rectTransform;
        rt.pivot = new Vector2(0f, 1f);
        desc.alignment = TextAlignmentOptions.TopLeft;
        desc.enableWordWrapping = true;
        desc.enableAutoSizing = false;
        if (DescBaseSize <= 0f) DescBaseSize = desc.fontSize;
        LayoutDescription(desc, 0f);
        desc.gameObject.SetActive(true);
        FitDescription(desc);
    }

    static Transform descParent;
    static float descTop, descBottom, descLeft, descWidth;

    static void LayoutDescription(TextMeshPro desc, float below)
    {
        if (descTop <= descBottom || !descParent || desc.transform.parent != descParent) return;
        var t = desc.transform;
        float top = descTop - below;
        desc.gameObject.SetActive(top - descBottom >= 0.35f);
        t.localPosition = new Vector3(descLeft, top, t.localPosition.z);
        var ls = t.localScale;
        if (ls.x > 0f && ls.y > 0f) desc.rectTransform.sizeDelta = new Vector2(descWidth / ls.x, (top - descBottom) / ls.y);
    }

    static void SetDescription(GameSettingMenu menu, string text, float below = 0f)
    {
        var desc = menu.MenuDescriptionText;
        if (desc == null) return;
        desc.DestroyTranslator();
        LayoutDescription(desc, below);
        desc.text = text + (PresetChanged ? PresetChangedWarning : "");
        FitDescription(desc);
    }

    static float DescBaseSize;

    // 説明が置き場の高さに入るまで字を小さくする
    static void FitDescription(TextMeshPro desc)
    {
        if (DescBaseSize <= 0f) return;
        desc.enableAutoSizing = true;
        desc.fontSizeMax = DescBaseSize;
        desc.fontSizeMin = DescBaseSize * 0.3f;
        desc.overflowMode = TextOverflowModes.Truncate;
        desc.ForceMeshUpdate();
    }

    // 今のタブに当たるボタンだけを選んだ見た目に (検索の間はどれも選ばない)。選んだ区分のクルーは少し大きく持ち上げる
    static void HighlightCat(int tabNum)
    {
        foreach (var cat in Cats)
        {
            if (cat.Button == null) continue;
            bool on = cat.TabNum == tabNum;
            cat.Button.SelectButton(on);
            if (cat.Art == null) continue;
            cat.Art.localScale = on ? cat.ArtScale * CatArtPicked : cat.ArtScale;
            cat.Art.localPosition = on ? cat.ArtPos + new Vector3(0f, cat.ArtLift, 0f) : cat.ArtPos;
        }
    }

    // 打つたびに全部の項目を照らして行を作り直すと重いので、打つ手が止まってから探す
    const float SearchDelay = 0.25f;
    static int searchSeq;

    static void OnSearchChanged(string query)
    {
        int seq = ++searchSeq;
        _ = new LateTask(() =>
        {
            if (seq == searchSeq) ApplySearch(query);
        }, SearchDelay, "", outlivesSession: true);
    }

    static void ApplySearch(string query)
    {
        if (OpenMenu == null) return;
        var q = query?.Trim() ?? "";
        // 英数字 1 文字ではほぼ全部の項目に当たるので探さない (日本語などは 1 文字でも役職の名前になる)
        if (q.Length == 1 && q[0] < 0x80) q = "";
        bool onSearch = ModGameOptionsMenu.TabIndex == ModGameOptionsMenu.SearchTab;
        if (SettingsSearch.Tokens(q).Length == 0)
        {
            searchQuery = "";
            if (onSearch) OpenMenu.ChangeTab(ReturnTab, false);
            return;
        }
        searchQuery = q;
        // 検索の結果を出している間は、頁の中身を替えて説明を書き替えるだけ (タブの出し直しはしない)
        if (onSearch && SettingsUI.RolePage.Built)
        {
            SettingsUI.RolePage.SetQuery(q);
            SetDescription(OpenMenu, SearchDescription());
            return;
        }
        if (!onSearch) ReturnTab = ModGameOptionsMenu.TabIndex;
        OpenMenu.ChangeTab(ModGameOptionsMenu.SearchTab, false);
    }

    static string SearchDescription()
    {
        int count = SettingsUI.RolePage.HitCount;
        return count == 0
            ? string.Format(Translator.GetString("MenuSearch.NoHit"), searchQuery)
            : string.Format(Translator.GetString("MenuSearch.Result"), searchQuery, count);
    }

    // ゲーム設定メニュー 初期関数
    [HarmonyPatch(nameof(GameSettingMenu.Start)), HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    public static void StartPostfix(GameSettingMenu __instance)
    {
        // 前の画面が Close を通らずに消えていたら (退出・試合の開始など)、閉じた時と同じ後始末をここで済ませる
        if ((object)OpenMenu != null) CloseMenu();
        ModGameOptionsMenu.TabIndex = 1;
        PresetChanged = false;
        OpenMenu = __instance;
        ReturnTab = 1;
        searchQuery = "";

        // 本編のタブのボタン 3 つと左上の題は出さない (左の列が同じ役目)
        foreach (var vanillaButton in new[] { __instance.GameSettingsButton, __instance.RoleSettingsButton, __instance.GamePresetsButton })
        {
            vanillaButton.gameObject.SetActive(false);
            __instance.ControllerSelectable.Remove(vanillaButton);
        }
        var gameSettingsLabel = __instance.transform.FindChild("GameSettingsLabel");
        if (gameSettingsLabel != null) gameSettingsLabel.gameObject.SetActive(false);

        // 本編の説明の箱は出さない。説明の字だけを左の列の下へ移す (列を作った後)
        __instance.transform.FindChild("What Is This?").gameObject.SetActive(false);

        // 検索欄: 板の上 (どの区分からでも全部を探す)
        var buttonParent = __instance.GameSettingsButton.transform.parent;
        var searchPos = new Vector3(SearchBoxPosition.x, SearchBoxPosition.y, __instance.GameSettingsButton.transform.localPosition.z);
        SearchBox = SettingsSearchBox.Create(buttonParent, searchPos, SearchBoxWidth, SearchBoxHeight,
            Translator.GetString("MenuSearch.Hint"), OnSearchChanged);
        SettingsUI.JumpMenu.Create(buttonParent, new Vector3(JumpButtonPosition.x, JumpButtonPosition.y, searchPos.z), __instance.gameObject.layer);

        // 左の列: プリセット・ゲーム・モードの頁と、全般・役職の区分 (出す設定のある物だけ)
        Cats.Clear();
        generalSet = SettingsUI.SettingsPages.GeneralGroups().SelectMany(g => g.Opts).Select(o => o.Item).ToHashSet();
        modeSet = SettingsUI.SettingsPages.ModeGroups().SelectMany(g => g.Opts).Select(o => o.Item).ToHashSet();
        BuildCats(__instance);

        // 頁の置き場にする画面を作る (ゲーム設定の画面の複製)。全般の頁用と、役職の区分・検索の結果用
        ModTab = Object.Instantiate(templateGameOptionsMenu, __instance.GameSettingsTab.transform.parent);
        ModTab.name = GameSettingMenuTab.Mod_MainSettings.ToString();
        ModTab.gameObject.SetActive(false);
        RoleHost = Object.Instantiate(templateGameOptionsMenu, __instance.GameSettingsTab.transform.parent);
        RoleHost.name = "RoleBrowser";
        RoleHost.gameObject.SetActive(false);
        PresetHost = Object.Instantiate(templateGameOptionsMenu, __instance.GameSettingsTab.transform.parent);
        PresetHost.name = "PresetPage";
        PresetHost.gameObject.SetActive(false);
        ModeHost = Object.Instantiate(templateGameOptionsMenu, __instance.GameSettingsTab.transform.parent);
        ModeHost.name = "ModePage";
        ModeHost.gameObject.SetActive(false);
    }

    // 全般の頁・モードの頁に置く TOHL の設定 (TabGroup.ModMainSettings の残りはゲームの頁)。並びは起動後に変わらないので画面を開く時に 1 回求める
    static HashSet<OptionItem> generalSet = new(), modeSet = new();
    static string builtCatsKey;

    // 出す区分 (出す設定が 1 つでもある区分)。全部の設定を 1 回なめ、区分ごとに 1 つ見つかったら後はその区分を見ない
    static (bool mode, bool general, HashSet<TabGroup> tabs) ShownCats()
    {
        bool mode = false, general = false;
        var tabs = new HashSet<TabGroup>();
        foreach (var o in OptionItem.AllOptions)
        {
            if (o.Tab == TabGroup.ModMainSettings)
            {
                bool g = !general && generalSet.Contains(o), m = !mode && modeSet.Contains(o);
                if ((!g && !m) || !o.IsShown()) continue;
                general |= g;
                mode |= m;
            }
            else if (!tabs.Contains(o.Tab) && o.IsShown()) tabs.Add(o.Tab);
        }
        return (mode, general, tabs);
    }

    static string KeyOf(bool mode, bool general, HashSet<TabGroup> tabs)
    {
        var sb = new System.Text.StringBuilder(mode ? "M" : "-").Append(general ? 'G' : '-');
        foreach (var tab in tabs.OrderBy(t => t)) sb.Append((int)tab).Append(',');
        return sb.ToString();
    }

    static string ShownCatsKey()
    {
        var (mode, general, tabs) = ShownCats();
        return KeyOf(mode, general, tabs);
    }

    // 左の列を (組み直して) 並べる。ボタンの数で間隔と高さが変わるので、出す区分が変わったら全部作り直す
    static void BuildCats(GameSettingMenu menu)
    {
        if (menu == null) return;
        bool rebuilt = Cats.Count > 0;
        foreach (var old in Cats)
        {
            if (old.Button == null) continue;
            try { old.Button.OnClick?.RemoveAllListeners(); } catch { }
            menu.ControllerSelectable.Remove(old.Button);
            Object.Destroy(old.Button.gameObject);
        }
        Cats.Clear();

        var (mode, general, tabs) = ShownCats();
        builtCatsKey = KeyOf(mode, general, tabs);
        var groups = EnumHelper.GetAllValues<TabGroup>()
            .Where(tab => tab == TabGroup.ModMainSettings ? general : tabs.Contains(tab))
            .ToList();
        int catCount = 2 + (mode ? 1 : 0) + groups.Count;
        float fit = catCount > CatFitCount ? (CatFitCount - 1f) / (catCount - 1f) : 1f;
        float step = CatStepY * fit, scaleY = CatScaleY * fit;
        AddCat(menu, null, ModGameOptionsMenu.PresetTab, step, scaleY, presets: true);
        AddCat(menu, null, 1, step, scaleY);
        if (mode) AddCat(menu, null, ModGameOptionsMenu.ModeTab, step, scaleY, mode: true);
        foreach (var tab in groups)
            AddCat(menu, tab, (int)tab + ModGameOptionsMenu.FirstModTab, step, scaleY);
        menu.DefaultButtonSelected = Cats[1].Button;
        PlaceDescription(menu);
        int cur = ModGameOptionsMenu.TabIndex;
        HighlightCat(cur == ModGameOptionsMenu.SearchTab ? -1 : cur);
        if (!rebuilt) return;

        // 役職の頁の絞り込みの札は左の列の一番下に合わせて置くので、役職の頁が隠れている間なら次に出す時に作り直させる
        bool onRolePage = cur == ModGameOptionsMenu.SearchTab
            || (cur >= ModGameOptionsMenu.FirstModTab && cur < ModGameOptionsMenu.SearchTab && cur != ModGameOptionsMenu.FirstModTab + (int)TabGroup.ModMainSettings);
        if (!onRolePage) SettingsUI.RolePage.Unbuild();
        // 開いている区分が消えたら、ゲームの頁へ (値を書き換えている最中に頁を替えないよう、後で)
        if (cur != ModGameOptionsMenu.SearchTab && !Cats.Any(c => c.TabNum == cur))
            _ = new LateTask(() => { if (OpenMenu == menu && menu) menu.ChangeTab(1, false); }, 0f, "", outlivesSession: true);
    }
    private static void SetDefaultButton(GameSettingMenu __instance)
    {
        // 左の列は Start で作る。コントローラーで選べるのはその列だけにする
        __instance.ControllerSelectable = new();
    }

    // ゲームの頁: 本編の ChangeTab が説明の字を本編の文に書き戻すので、その後に頁の操作の案内を付けて書き直す
    [HarmonyPatch(nameof(GameSettingMenu.ChangeTab)), HarmonyPostfix]
    public static void ChangeTabPostfix(GameSettingMenu __instance, int tabNum, [HarmonyArgument(1)] bool previewOnly)
    {
        if (previewOnly || ModGameOptionsMenu.IsModTab(tabNum)) return;
        // 本編のタブの組み立て (CreateSettings) の後に頁を作る形だけだと、組み立てが途中で止まった時や
        // 後から本編の行が出し直された時に本編の行が残る。タブを出すたびに頁があることを確かめる
        var tab = __instance.GameSettingsTab;
        if (tabNum == 1 && tab && tab.gameObject.activeInHierarchy)
        {
            SettingsUI.SettingsPages.GameTab = tab;
            try { SettingsUI.SettingsPages.Game.Build(tab); }
            catch (System.Exception e) { Logger.Exception(e, "GameSettingMenuPatch.GamePage"); }
        }
        if (!TranslationController.Instance) return;
        SetDescription(__instance, TranslationController.Instance.GetString(StringNames.GameSettingsDescription)
            + "\n\n" + Translator.GetString("GameSettingHelpText.Page"));
    }

    [HarmonyPatch(nameof(GameSettingMenu.ChangeTab)), HarmonyPrefix]
    public static bool ChangeTabPrefix(GameSettingMenu __instance, ref int tabNum, [HarmonyArgument(1)] bool previewOnly)
    {
        ModGameOptionsMenu.TabIndex = tabNum;
        bool isSearch = tabNum == ModGameOptionsMenu.SearchTab;

        // 検索の途中で区分を選んだら、検索の語を消してその区分を出す
        // (TabIndex を先に替えてあるので、語を消した時の「元のタブへ戻る」は走らない)
        if (!previewOnly && !isSearch && SearchBox != null && SearchBox.Query.Length > 0)
        {
            ReturnTab = tabNum;
            SearchBox.Clear();
            searchSeq++;
            searchQuery = "";
        }

        if ((previewOnly && Controller.currentTouchType == Controller.TouchType.Joystick) || !previewOnly)
        {
            if (ModTab != null) ModTab.gameObject.SetActive(false);
            if (RoleHost != null) RoleHost.gameObject.SetActive(false);
            if (PresetHost != null) PresetHost.gameObject.SetActive(false);
            if (ModeHost != null) ModeHost.gameObject.SetActive(false);
            HighlightCat(isSearch ? -1 : tabNum);
        }

        if (!previewOnly)
        {
            __instance.transform.FindChild("What Is This?").gameObject.SetActive(false);
            // 検索の結果の行など、頁の外で変えた値を頁へ写す
            SettingsUI.SettingsPages.RefreshFromOutside();
        }
        if (!ModGameOptionsMenu.IsModTab(tabNum))
        {
            return true;
        }

        if ((previewOnly && Controller.currentTouchType == Controller.TouchType.Joystick) || !previewOnly)
        {
            __instance.PresetsTab.gameObject.SetActive(false);
            __instance.GameSettingsTab.gameObject.SetActive(false);
            __instance.RoleSettingsTab.gameObject.SetActive(false);
            __instance.GamePresetsButton.SelectButton(false);
            __instance.GameSettingsButton.SelectButton(false);
            __instance.RoleSettingsButton.SelectButton(false);

            var group = isSearch ? (TabGroup?)null : (TabGroup)(tabNum - ModGameOptionsMenu.FirstModTab);
            bool modPage = group == TabGroup.ModMainSettings;
            var shown = modPage ? ModTab : RoleHost;
            if (tabNum == ModGameOptionsMenu.PresetTab)
            {
                if (PresetHost != null)
                {
                    PresetHost.gameObject.SetActive(true);
                    SettingsUI.PresetPage.Show();
                    SetDescription(__instance, Translator.GetString("MenuDescriptionText.Presets"));
                }
            }
            else if (tabNum == ModGameOptionsMenu.ModeTab)
            {
                if (ModeHost != null)
                {
                    ModeHost.gameObject.SetActive(true);
                    SettingsUI.SettingsPages.RefreshFromOutside();
                    SetDescription(__instance, Translator.GetString("MenuDescriptionText.Mode") + "\n\n" + Translator.GetString("GameSettingHelpText.Page"));
                }
            }
            else if (shown != null)
            {
                // 役職の頁は区分と検索の語を先に渡す (初めて出す時は、出す時に頁が作られる)
                if (!modPage) SettingsUI.RolePage.Prepare(group, isSearch ? searchQuery : "");
                shown.gameObject.SetActive(true);
                if (!modPage) SettingsUI.RolePage.Apply();
                // 検索の結果の行など、頁の外で変えた値を頁へ写す
                SettingsUI.SettingsPages.RefreshFromOutside();
                // 名前の「?」と打ち込みで操作するので、説明の操作の案内も頁用
                if (isSearch) SetDescription(__instance, SearchDescription());
                else
                    SetDescription(__instance,
                        Translator.GetString($"MenuDescriptionText.{group}") + (modPage ? "\n\n" + Translator.GetString("GameSettingHelpText.Page") : ""),
                        modPage ? 0f : SettingsUI.RolePage.ChipsWorldHeight);
            }
        }
        if (previewOnly)
        {
            __instance.ToggleLeftSideDarkener(false);
            __instance.ToggleRightSideDarkener(true);
            return false;
        }
        __instance.ToggleLeftSideDarkener(true);
        __instance.ToggleRightSideDarkener(false);
        return false;
    }

    [HarmonyPatch(nameof(GameSettingMenu.OnEnable)), HarmonyPrefix]
    private static bool OnEnablePrefix(GameSettingMenu __instance)
    {
        // 本編の画面の後ろの星空の幕 (Background) は消し、板の周りからロビーが見えるようにする
        __instance.transform.Find("Background")?.gameObject.SetActive(false);
        if (templateGameOptionsMenu == null)
        {
            templateGameOptionsMenu = Object.Instantiate(__instance.GameSettingsTab, __instance.GameSettingsTab.transform.parent);
            templateGameOptionsMenu.gameObject.SetActive(false);
        }
        if (templateGameSettingsButton == null)
        {
            templateGameSettingsButton = Object.Instantiate(__instance.GameSettingsButton, __instance.GameSettingsButton.transform.parent);
            templateGameSettingsButton.gameObject.SetActive(false);
        }

        SetDefaultButton(__instance);

        ControllerManager.Instance.OpenOverlayMenu(__instance.name, __instance.BackButton, __instance.DefaultButtonSelected, __instance.ControllerSelectable, false);
        DestroyableSingleton<HudManager>.Instance.menuNavigationPrompts.SetActive(false);
        if (Controller.currentTouchType != Controller.TouchType.Joystick)
        {
            __instance.ChangeTab(1, Controller.currentTouchType == Controller.TouchType.Joystick);
        }
        __instance.StartCoroutine(__instance.CoSelectDefault());

        return false;
    }
    // 数を打ち込んでいる間の Esc は、打ち込みをやめるだけ (設定画面は閉じない)
    [HarmonyPatch(nameof(GameSettingMenu.Close)), HarmonyPrefix]
    private static bool ClosePrefix()
    {
        if (SettingsUI.OptEditor.IsTyping)
        {
            SettingsUI.OptEditor.EndInput(false);
            return false;
        }
        // 名前の欄などに字を打っている間の Esc は、欄を抜けるだけ
        if (SettingsUI.TextField.Typing || SettingsUI.TextField.CancelledFrame == Time.frameCount)
        {
            SettingsUI.TextField.Blur();
            return false;
        }
        return true;
    }

    [HarmonyPatch(nameof(GameSettingMenu.Close)), HarmonyPostfix]
    private static void ClosePostfix() => CloseMenu();

    private static void CloseMenu()
    {
        // 頁 (自前の部品) を全部捨てる。ホストは本編のゲーム設定を変えていれば保存して全員へ送る
        SettingsUI.SettingsPages.Forget();
        if (AmongUsClient.Instance && AmongUsClient.Instance.AmHost) SettingsUI.VanillaGame.OnMenuClosed();
        else SettingsUI.VanillaGame.Dirty = false;
        // 左の列のボタンに入れたデリゲートは画面を握り、GameObject を消しても画面全体が回収されなくなる。消す前に外す
        foreach (var cat in Cats)
        {
            try { cat.Button?.OnClick?.RemoveAllListeners(); } catch { }
        }
        Cats.Clear();
        if (ModTab != null) UnityEngine.Object.Destroy(ModTab);
        if (RoleHost != null) UnityEngine.Object.Destroy(RoleHost);
        if (PresetHost != null) UnityEngine.Object.Destroy(PresetHost);
        if (ModeHost != null) UnityEngine.Object.Destroy(ModeHost);
        ModTab = null;
        RoleHost = null;
        PresetHost = null;
        ModeHost = null;
        builtCatsKey = null;
        SearchBox = null;
        searchSeq++;
        SettingsSearchBox.Forget();
        searchQuery = "";
        OpenMenu = null;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSyncSettings))]
public class RpcSyncSettingsPatch
{
    public static void Postfix()
    {
        OptionItem.SyncAllOptions();
    }
}

//[HarmonyPatch(typeof(NormalGameOptionsV08), nameof(NormalGameOptionsV08.SetRecommendations))]
//public static class SetRecommendationsPatch
//{
//    public static bool Prefix(NormalGameOptionsV08 __instance, int numPlayers, bool isOnline)
//    {
//        numPlayers = Mathf.Clamp(numPlayers, 4, 15);
//        __instance.PlayerSpeedMod = __instance.MapId == 4 ? 1.25f : 1f; //AirShipなら1.25、それ以外は1
//        __instance.CrewLightMod = 0.5f;
//        __instance.ImpostorLightMod = 1.75f;
//        __instance.KillCooldown = 25f;
//        __instance.NumCommonTasks = 2;
//        __instance.NumLongTasks = 4;
//        __instance.NumShortTasks = 6;
//        __instance.NumEmergencyMeetings = 1;
//        if (!isOnline)
//            __instance.NumImpostors = NormalGameOptionsV08.RecommendedImpostors[numPlayers];
//        __instance.KillDistance = 0;
//        __instance.DiscussionTime = 0;
//        __instance.VotingTime = 150;
//        __instance.IsDefaults = true;
//        __instance.ConfirmImpostor = false;
//        __instance.VisualTasks = false;

//        __instance.roleOptions.SetRoleRate(RoleTypes.Shapeshifter, 0, 0);
//        __instance.roleOptions.SetRoleRate(RoleTypes.Scientist, 0, 0);
//        __instance.roleOptions.SetRoleRate(RoleTypes.GuardianAngel, 0, 0);
//        __instance.roleOptions.SetRoleRate(RoleTypes.Engineer, 0, 0);
//        __instance.roleOptions.SetRoleRecommended(RoleTypes.Shapeshifter);
//        __instance.roleOptions.SetRoleRecommended(RoleTypes.Scientist);
//        __instance.roleOptions.SetRoleRecommended(RoleTypes.GuardianAngel);
//        __instance.roleOptions.SetRoleRecommended(RoleTypes.Engineer);

//        if (Options.CurrentGameMode == CustomGameMode.HideAndSeek) //HideAndSeek
//        {
//            __instance.PlayerSpeedMod = 1.75f;
//            __instance.CrewLightMod = 5f;
//            __instance.ImpostorLightMod = 0.25f;
//            __instance.NumImpostors = 1;
//            __instance.NumCommonTasks = 0;
//            __instance.NumLongTasks = 0;
//            __instance.NumShortTasks = 10;
//            __instance.KillCooldown = 10f;
//        }
//        if (Options.IsStandardHAS) //StandardHAS
//        {
//            __instance.PlayerSpeedMod = 1.75f;
//            __instance.CrewLightMod = 5f;
//            __instance.ImpostorLightMod = 0.25f;
//            __instance.NumImpostors = 1;
//            __instance.NumCommonTasks = 0;
//            __instance.NumLongTasks = 0;
//            __instance.NumShortTasks = 10;
//            __instance.KillCooldown = 10f;
//        }
//        if (Options.IsCCMode)
//        {
//            __instance.PlayerSpeedMod = 1.5f;
//            __instance.CrewLightMod = 0.5f;
//            __instance.ImpostorLightMod = 0.75f;
//            __instance.NumImpostors = 1;
//            __instance.NumCommonTasks = 0;
//            __instance.NumLongTasks = 0;
//            __instance.NumShortTasks = 1;
//            __instance.KillCooldown = 20f;
//            __instance.NumEmergencyMeetings = 1;
//            __instance.EmergencyCooldown = 30;
//            __instance.KillDistance = 0;
//            __instance.DiscussionTime = 0;
//            __instance.VotingTime = 60;
//        }
//        //if (Options.IsONMode)
//        //{
//        //    __instance.NumCommonTasks = 1;
//        //    __instance.NumLongTasks = 0;
//        //    __instance.NumShortTasks = 1;
//        //    __instance.KillCooldown = 20f;
//        //    __instance.NumEmergencyMeetings = 0;
//        //    __instance.KillDistance = 0;
//        //    __instance.DiscussionTime = 0;
//        //    __instance.VotingTime = 300;
//        //}

//        return false;
//    }
//}
//}
