// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Options/RoleSearch.cs (GPL-3.0),
// which is based on https://github.com/waffle-ful/Aeterna-End-K-not Modules/OptionSearch.cs (GPL-3.0)
using System;
using System.Collections.Generic;
using System.Text;
using TownOfHostL.SettingsUI;

namespace TownOfHostL;

// 設定画面の検索の一致判定。
// - 比べる前に、全角英数を半角に・カタカナをひらがなに・英字を小文字にそろえ、空白と中黒を除く
//   (「きる」でも「キル」でも「ＫＩＬＬ」でも同じに当たる。漢字の読みまでは見ない)。
//   ただしカタカナを含む語はカタカナのまま比べる (「キル」が「できる」に当たらないように)。
//   そのために探す対象は、ひらがなにそろえた名前とカタカナを残した名前の両方を持つ。
// - 空白で区切った語はすべて含まれている時だけ当たる (AND)。
// 項目の探す対象は一度作ったら使い回す (打つたびに作り直さない)。画面を閉じる時に捨てる。
public static class SettingsSearch
{
    private static readonly Dictionary<MenuOpt, string> Cache = new();

    public static void ClearCache() => Cache.Clear();

    public static string Normalize(string s) => Normalize(s, true);

    private static string Normalize(string s, bool foldKana)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c0 in s)
        {
            char c = c0;
            if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);   // 全角英数記号 → 半角
            else if (foldKana && c >= 'ァ' && c <= 'ヶ') c = (char)(c - 0x60); // カタカナ → ひらがな
            if (c == ' ' || c == '　' || c == '・' || c == '･' || c == '\t') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private static bool HasKatakana(string s)
    {
        foreach (char c in s)
            if (c >= 'ァ' && c <= 'ヶ') return true;
        return false;
    }

    // 検索語を語ごとに分けてそろえる。空なら 0 個
    public static string[] Tokens(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<string>();
        var parts = query.Split(new[] { ' ', '　', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            string n = Normalize(p, !HasKatakana(p));
            if (n.Length > 0) list.Add(n);
        }
        return list.ToArray();
    }

    // 文字列を探す対象にする (ひらがなにそろえた物とカタカナを残した物)
    public static string Haystack(string text) => Normalize(text, true) + "\n" + Normalize(text, false);

    // 項目の名前 (見えている名前と内部の名前) を探す対象にする
    public static string Haystack(MenuOpt opt)
    {
        if (Cache.TryGetValue(opt, out var hay)) return hay;
        hay = Haystack(opt.PlainLabel + "\n" + (opt.Item != null ? opt.Item.Name : ""));
        Cache[opt] = hay;
        return hay;
    }

    // 語が全部入っているか
    public static bool AllIn(string[] tokens, string hay)
    {
        foreach (var t in tokens)
            if (!hay.Contains(t, StringComparison.Ordinal)) return false;
        return true;
    }

    // 語のどれかが入っているか
    public static bool AnyIn(string[] tokens, string hay)
    {
        foreach (var t in tokens)
            if (hay.Contains(t, StringComparison.Ordinal)) return true;
        return false;
    }
}
