// Based on https://github.com/haru03741-glitch/More-Roles-Plus src/Options/PresetStore.cs and src/Options/SettingCode.cs (GPL-3.0)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AmongUs.GameOptions;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

// 設定のプリセット (TOHL の設定値と本編のゲーム設定に名前を付けて保存した物) と、設定のコード (同じ中身を 1 行の文字にした物)。
// 保存先 = TOHL_DATA/Presets/<名前>.txt。人に渡す・人からもらう時は受け渡しのフォルダ (デスクトップの TOHL_Presets) を通す:
// 書き出し = そのフォルダへ写す・取り込み = そのフォルダにある物を全部写してくる (同じ名前は上書きしない)。
// 中身は 1 行目が版の印、続いて初期値と違う設定だけを「鍵=値」で 1 行ずつ、最後に本編の設定を 1 行。
// 読む時は最後の '=' で割り、知らない鍵は飛ばし、書かれていない設定は初期値に戻す
internal static class PresetStore
{
    private const string Header = "#TOHL-preset 1";
    private const string VanillaKey = "vanilla.options";
    private const string CodePrefix = "TOHL1.";
    private const int MaxCodeChars = 64 * 1024;   // 受け取るコードの上限 (大きな物でメモリを食わせない)
    private const int MaxText = 512 * 1024;       // 展開した後の上限

    public static string Dir => Path.GetFullPath("./TOHL_DATA/Presets/");
    public static string ShareDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TOHL_Presets");

    private static string PathOf(string name) => Path.Combine(Dir, name + ".txt");

    // 名前に使えるのは文字・数字・_ と - だけ (置き場の外を指せないように)。Windows が予約している名前 (CON・NUL など) は使わない
    public static bool ValidName(string s) => s != null && s.Length is > 0 and <= 30 && Regex.IsMatch(s, @"^[\w\-]+\z") && !Reserved.IsMatch(s);

    private static readonly Regex Reserved = new(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\z", RegexOptions.IgnoreCase);

    public static bool Exists(string name) => ValidName(name) && File.Exists(PathOf(name));

    private static bool CanLoad => SettingsPages.CanEdit();

    // 保存した日時 (読めなければ null)
    public static DateTime? SavedAt(string name)
    {
        try { return Exists(name) ? File.GetLastWriteTime(PathOf(name)) : null; }
        catch (Exception) { return null; }
    }

    public static List<string> Names()
    {
        var list = new List<string>();
        try
        {
            if (Directory.Exists(Dir))
                foreach (var f in Directory.GetFiles(Dir, "*.txt"))
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (ValidName(name)) list.Add(name);
                }
        }
        catch (Exception e) { Logger.Warn($"プリセットの一覧を読めません: {e.Message}", "PresetStore"); }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    // ---- 中身 ----

    // プリセットに入れる設定 (枠をまたいで 1 つの値を持つ設定は入れない)
    private static IEnumerable<OptionItem> Stored()
    {
        foreach (var o in OptionItem.AllOptions)
            if (!o.IsSingleValue && o is not PresetOptionItem) yield return o;
    }

    // 今の値を中身の文にする
    internal static string MakeText()
    {
        var sb = new StringBuilder();
        sb.Append(Header).Append('\n');
        foreach (var o in Stored())
            if (o.CurrentValue != o.DefaultValue) sb.Append(o.Key).Append('=').Append(o.CurrentValue.ToString(CultureInfo.InvariantCulture)).Append('\n');
        var vanilla = VanillaLine();
        if (vanilla != null) sb.Append(VanillaKey).Append('=').Append(vanilla).Append('\n');
        return sb.ToString();
    }

    // 中身の文を読む。版の印が無ければ false
    private static bool Parse(string text, out Dictionary<string, int> values, out string vanilla)
    {
        values = new Dictionary<string, int>();
        vanilla = null;
        if (string.IsNullOrEmpty(text)) return false;
        var lines = text.Split('\n');
        if (lines[0].Trim() != Header) return false;
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            // 本編の設定の行は base64 の末尾に '=' が付くので、頭の鍵で見分ける
            if (line.StartsWith(VanillaKey + "=", StringComparison.Ordinal)) { vanilla = line[(VanillaKey.Length + 1)..]; continue; }
            // 鍵の中に '=' が入りうるので、最後の '=' で割る
            int eq = line.LastIndexOf('=');
            if (eq <= 0) continue;
            if (int.TryParse(line[(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) values[line[..eq]] = v;
        }
        return true;
    }

    // 値を当てる。書かれていない設定は初期値・範囲の外の値は端に寄せる。変わった数を返す。
    // TOHL の設定は 1 回の「元に戻す」で戻る。本編の設定は丸ごと置き換えるので「元に戻す」には入らない
    private static int Apply(Dictionary<string, int> values, string vanilla)
    {
        int changed = 0;
        if (vanilla != null) LoadVanilla(vanilla);
        SettingsPages.Batch(() =>
        {
            OptHistory.BeginGroup();
            try
            {
                foreach (var o in Stored())
                {
                    int target = values.TryGetValue(o.Key, out int v) ? v : o.DefaultValue;
                    int count = MenuOpt.Of(o).Count;
                    if (count > 0) target = Math.Clamp(target, 0, count - 1);
                    int before = o.CurrentValue;
                    if (target == before) continue;
                    o.SetValue(target);
                    OptHistory.Push(MenuOpt.Of(o), before);
                    changed++;
                }
            }
            finally { OptHistory.EndGroup(); }
        });
        return changed;
    }

    // 今の値がこのプリセットと同じか (本編の設定は見ない)
    public static bool Matches(string name)
    {
        try
        {
            if (!Exists(name) || !Parse(File.ReadAllText(PathOf(name)), out var values, out _)) return false;
            foreach (var o in Stored())
            {
                int want = values.TryGetValue(o.Key, out int v) ? v : o.DefaultValue;
                if (o.CurrentValue != want) return false;
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    // ---- 本編のゲーム設定 (本編の形式のまま 1 行) ----

    private static string VanillaLine()
    {
        try
        {
            var gm = GameOptionsManager.Instance;
            var opts = gm?.CurrentGameOptions;
            if (opts == null) return null;
            return Convert.ToBase64String(gm.gameOptionsFactory.ToBytes(opts, false));
        }
        catch (Exception e) { Logger.Warn($"本編の設定を書けません: {e.Message}", "PresetStore"); return null; }
    }

    // 本編の設定を置き換え、閉じた時と同じ道で本編の保存と同期を回す。ゲームモードが違う物は当てない
    private static void LoadVanilla(string b64)
    {
        try
        {
            var gm = GameOptionsManager.Instance;
            if (gm == null || string.IsNullOrEmpty(b64)) return;
            var opts = gm.gameOptionsFactory.FromBytes(Convert.FromBase64String(b64));
            if (opts == null || gm.CurrentGameOptions == null || opts.GameMode != gm.CurrentGameOptions.GameMode) return;
            // 保存されるのはホストの設定の方 (今の設定と別に持っている) なので両方を置き換える
            gm.GameHostOptions = opts;
            gm.CurrentGameOptions = opts;
            // 試合の進行側が持つ設定も差し替える (同期はこれを読んで送る)
            if (GameManager.Instance) GameManager.Instance.LogicOptions.SetGameOptions(opts);
            VanillaGame.Dirty = true;
            VanillaGame.OnMenuClosed();
        }
        catch (Exception e) { Logger.Warn($"本編の設定を読めません: {e.Message}", "PresetStore"); }
    }

    // 枠を 1 つにする前の保存の 1 枠をプリセットのファイルにする。初期値のままの枠と、同じ名前が既にある時は書かない。
    // 書いた名前を返す (書かなかったら null)
    internal static string WriteMigrated(string name, string fallback, Dictionary<string, int> values)
    {
        try
        {
            if (!ValidName(name) || File.Exists(PathOf(name))) name = fallback;
            for (int n = 2; ValidName(name) && File.Exists(PathOf(name)) && n < 100; n++) name = $"{fallback}_{n}";
            if (!ValidName(name) || File.Exists(PathOf(name))) return null;
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            int lines = 0;
            foreach (var (key, value) in values)
            {
                if (!OptionItem.TryGetByKey(key, out var o) || o.IsSingleValue || o is PresetOptionItem || value == o.DefaultValue) continue;
                sb.Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                lines++;
            }
            if (lines == 0) return null;
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathOf(name), sb.ToString());
            return name;
        }
        catch (Exception e)
        {
            Logger.Warn($"枠の設定をプリセットに書き出せません: {e.Message}", "PresetStore");
            return null;
        }
    }

    // ---- 名前を付けた保存 ----

    public static string Save(string name)
    {
        if (!ValidName(name)) return Tr("PresetStore.BadName");
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(PathOf(name), MakeText());
            return string.Format(Tr("PresetStore.Saved"), name);
        }
        catch (Exception e) { return string.Format(Tr("PresetStore.CouldntSave"), e.Message); }
    }

    public static string Load(string name)
    {
        if (!CanLoad) return Tr("PresetPage.OnlyHost");
        if (!Exists(name)) return string.Format(Tr("PresetStore.NoPreset"), name);
        string text;
        try { text = File.ReadAllText(PathOf(name)); }
        catch (Exception) { text = null; }
        if (!Parse(text, out var values, out var vanilla)) return string.Format(Tr("PresetStore.CouldntRead"), name);
        int changed = Apply(values, vanilla);
        return string.Format(Tr("PresetStore.Loaded"), name, changed);
    }

    public static string Delete(string name)
    {
        if (!Exists(name)) return string.Format(Tr("PresetStore.NoPreset"), name);
        try
        {
            File.Delete(PathOf(name));
            return string.Format(Tr("PresetStore.Deleted"), name);
        }
        catch (Exception e) { return string.Format(Tr("PresetStore.CouldntDelete"), e.Message); }
    }

    public static string Export(string name)
    {
        if (!Exists(name)) return string.Format(Tr("PresetStore.NoPreset"), name);
        try
        {
            Directory.CreateDirectory(ShareDir);
            File.Copy(PathOf(name), Path.Combine(ShareDir, name + ".txt"), true);
            return string.Format(Tr("PresetStore.Exported"), name, ShareDir);
        }
        catch (Exception e) { return string.Format(Tr("PresetStore.CouldntExport"), e.Message); }
    }

    // 受け渡しのフォルダにある物を全部取り込む (同じ名前が既にある物と、プリセットでない物は飛ばす)
    public static string Import()
    {
        try
        {
            Directory.CreateDirectory(ShareDir);
            Directory.CreateDirectory(Dir);
            int added = 0, skipped = 0;
            foreach (var f in Directory.GetFiles(ShareDir, "*.txt"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (!ValidName(name) || File.Exists(PathOf(name)) || new FileInfo(f).Length > MaxText) { skipped++; continue; }
                if (!Parse(File.ReadAllText(f), out _, out _)) { skipped++; continue; }
                File.Copy(f, PathOf(name), false);
                added++;
            }
            return string.Format(Tr("PresetStore.Imported"), added, skipped, ShareDir);
        }
        catch (Exception e) { return string.Format(Tr("PresetStore.CouldntImport"), e.Message); }
    }

    // 受け渡しのフォルダをエクスプローラーで開く
    public static string OpenShare()
    {
        try
        {
            Directory.CreateDirectory(ShareDir);
            System.Diagnostics.Process.Start("explorer.exe", "\"" + ShareDir + "\"");
            return string.Format(Tr("PresetPage.ShareFolder"), ShareDir);
        }
        catch (Exception e) { return e.Message; }
    }

    // ---- 設定のコード (中身を縮めて base64 にし、頭に版の印) ----

    public static string CopyCode()
    {
        try
        {
            using var ms = new MemoryStream();
            using (var z = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                var raw = Encoding.UTF8.GetBytes(MakeText());
                z.Write(raw, 0, raw.Length);
            }
            var code = CodePrefix + Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            GUIUtility.systemCopyBuffer = code;
            return string.Format(Tr("SettingCode.Copied"), code.Length);
        }
        catch (Exception e) { return string.Format(Tr("SettingCode.CouldntMake"), e.Message); }
    }

    // コピーしてあるコードを読む (ロビーのホストだけ)
    public static string PasteCode()
    {
        if (!CanLoad) return Tr("PresetPage.OnlyHost");
        string text;
        try { text = Decode(GUIUtility.systemCopyBuffer); }
        catch (Exception) { text = null; }
        if (!Parse(text, out var values, out var vanilla)) return Tr("SettingCode.NotACode");
        int changed = Apply(values, vanilla);
        return string.Format(Tr("SettingCode.Loaded"), changed);
    }

    // コードを中身の文へ。コードでなければ null
    private static string Decode(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        code = code.Trim();
        if (code.Length > MaxCodeChars || !code.StartsWith(CodePrefix, StringComparison.Ordinal)) return null;
        var b64 = code[CodePrefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        using var z = new DeflateStream(new MemoryStream(Convert.FromBase64String(b64)), CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        var buf = new byte[4096];
        int n;
        while ((n = z.Read(buf, 0, buf.Length)) > 0)
        {
            outMs.Write(buf, 0, n);
            if (outMs.Length > MaxText) return null;
        }
        return Encoding.UTF8.GetString(outMs.ToArray());
    }

    private static string Tr(string key) => Translator.GetString(key);
}
