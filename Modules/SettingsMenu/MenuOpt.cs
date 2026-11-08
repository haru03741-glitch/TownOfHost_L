// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Options/Opt.cs (GPL-3.0)
using System;
using System.Collections.Generic;
using System.Globalization;
using TownOfHostL.Core.Modes;
using TownOfHostL.Roles.Core;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

// 設定画面の行から見た設定項目 1 つ。値も保存も持ち主 (OptionItem・本編の設定) が持ち、ここは読み書きの窓口だけ。
// 値は「何番目の目盛りか」(Index) で扱う。OptionItem の窓口が本体で、本編のゲーム設定の窓口は VanillaMenuOpt。
public class MenuOpt
{
    private static readonly Dictionary<OptionItem, MenuOpt> Cache = new();

    public static MenuOpt Of(OptionItem item)
    {
        if (!Cache.TryGetValue(item, out var opt)) Cache[item] = opt = new MenuOpt(item);
        return opt;
    }

    // 本編の設定の窓口では null
    public readonly OptionItem Item;

    protected MenuOpt(OptionItem item) { Item = item; }

    public virtual string Key => Item.Key;
    // 名前 (役職名などは色付き)
    public virtual string Label => Item.GetName();
    public virtual string PlainLabel => Item.GetName(disableColor: true).RemoveHtmlTags();
    public virtual int Index => Item.CurrentValue;
    public virtual int DefaultIndex => Item.DefaultValue;

    public virtual int Count => Item switch
    {
        BooleanOptionItem => 2,
        IntegerOptionItem i => (i.Rule.MaxValue - i.Rule.MinValue) / i.Rule.Step + 1,
        FloatOptionItem f => (int)((f.Rule.MaxValue - f.Rule.MinValue) / f.Rule.Step) + 1,
        StringOptionItem s => s.Selections.Length,
        PresetOptionItem => OptionItem.NumPresets,
        _ => 1,
    };

    // ON/OFF の項目。TOHL は「ColoredOff / ColoredOn」の 2 択の文字の選択肢も ON/OFF として使う
    public virtual bool IsToggle => Item is BooleanOptionItem
        || Item is StringOptionItem { Selections: { Length: 2 } sel } && sel[0] == BooleanOptionItem.TEXT_false && sel[1] == BooleanOptionItem.TEXT_true;
    // 見出しだけの項目 (値を持たない)
    public virtual bool IsText => Item is TextOptionItem;
    // 値を変えられない項目 (表示だけ)
    public virtual bool Fixed => Item.IsFixValue;
    // ON/OFF と文字の選択肢は回る。数は端で止まる (押しすぎて反対の端へ飛ばないように)
    public virtual bool WrapsAround => Item is BooleanOptionItem or StringOptionItem or PresetOptionItem;

    public virtual string Display() => Item.GetString();

    // 名前の色 (見出しの帯の色にも使う)
    public virtual Color NameColor => Item.NameColor;

    // 画面に出すか: 今のモードで出す項目で、親が全部 ON の時
    public virtual bool Shown => IsEnabled(Item);

    public static bool IsEnabled(OptionItem option)
    {
        if (!option.IsShown()) return false;
        return option.Parent == null || (IsEnabled(option.Parent) && option.Parent.GetBool());
    }

    // 親の深さ (行の字下げ)
    public virtual int Depth
    {
        get
        {
            int d = 0;
            for (var p = Item.Parent; p != null && d < 4; p = p.Parent) d++;
            return d;
        }
    }

    // 既定へ戻せるか。プリセットとゲームモードは「戻す」で切り替わってしまうので持たない
    public virtual bool HasDefault => !IsText && Item is not PresetOptionItem && Item != Options.GameMode;
    public bool Changed => HasDefault && Index != DefaultIndex;

    // 外 (本編の設定) に値がある項目が、番号を読み直す。Index を画面に出す前に呼ぶ
    public virtual void Sync() { }

    // 番号を書く
    protected virtual void Write(int index) => Item.SetValue(index);

    // ± を押した。clamp = 端で止める (Shift の 5 倍幅は回らない)。変わったら true
    public bool Step(int delta, bool clamp = false)
    {
        if (Fixed || IsText) return false;
        Sync();
        int n = Count, before = Index;
        int next = WrapsAround && !clamp ? ((before + delta) % n + n) % n : Math.Clamp(before + delta, 0, n - 1);
        if (next == before) return false;
        Write(next);
        return true;
    }

    // 番号を書く (既定へ戻す・取り消し)。変わったら true
    public bool Restore(int index)
    {
        Sync();
        if (Fixed || IsText || index < 0 || index >= Count || index == Index) return false;
        Write(index);
        return true;
    }

    public bool ResetToDefault() => HasDefault && Restore(DefaultIndex);

    // 数の項目の、番号 index の値 (数でない項目は null)
    public virtual double? ValueAt(int index) => Item switch
    {
        IntegerOptionItem i => i.Rule.GetValueByIndex(index),
        FloatOptionItem f => Math.Round((double)f.Rule.GetValueByIndex(index), 4),
        _ => null,
    };

    public bool Numeric => ValueAt(0) != null;

    // 打ち込んだ字 (全角・単位付きでもよい) を、範囲の中のいちばん近い番号に。数が読めなければ -1
    public int IndexForInput(string input)
    {
        if (!Numeric || !NumberInput.TryParse(input, out double v)) return -1;
        int n = Count;
        double lo = ValueAt(0).Value, hi = ValueAt(n - 1).Value;
        if (n <= 1 || hi == lo) return 0;
        double i = (v - lo) / (hi - lo) * (n - 1);
        return Math.Clamp((int)Math.Round(i, MidpointRounding.AwayFromZero), 0, n - 1);
    }

    // 打ち込む欄に最初に出す字 (今の値の数だけ)
    public string InputText()
    {
        Sync();
        var v = ValueAt(Index);
        return v == null ? "" : v.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    // ---- 項目の説明 (名前の右の「?」) ----

    private bool? _hasDetail;

    // 長い説明がある項目か (役職の説明か、<名前>InfoLong の翻訳がある項目)
    public virtual bool HasDetail => _hasDetail ??= Item is not TextOptionItem
        && (Enum.IsDefined(typeof(CustomRoles), Item.Name) || Translator.HasString(Item.Name + "InfoLong"));

    // 説明の字。出す時にだけ組む
    public virtual string Detail()
    {
        if (!HasDetail) return null;
        if (Enum.TryParse(typeof(CustomRoles), Item.Name, true, out var role) && role != null && Enum.IsDefined(typeof(CustomRoles), role))
            return Utils.GetRoleInfoLongWithColor((CustomRoles)role);
        return $"<b>{Item.GetName(colorLighter: true)}</b>\n{Translator.GetString(Item.Name + "InfoLong", Item.ReplacementDictionary)}";
    }
}

// 打ち込んだ数を読む。全角の数字・小数点・負の印、後ろの単位 (秒・%・x など) を許す
public static class NumberInput
{
    private static readonly System.Text.RegularExpressions.Regex Num = new(@"-?(?:\d+(?:\.\d*)?|\.\d+)");

    public static bool TryParse(string input, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        string s = input.Normalize(System.Text.NormalizationForm.FormKC)
            .Replace('ー', '-').Replace('−', '-').Replace('‐', '-').Replace('―', '-').Replace('－', '-')
            .Replace('。', '.').Replace('、', '.').Replace(',', '.').Replace(" ", "");
        var m = Num.Match(s);
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
