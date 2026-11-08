using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TownOfHostL.BootAccel;

// Based on https://github.com/waffle-ful/Aeterna-End-K-not (EndKnot.BootAccel/AccelCache.cs, GPL-3.0)
// Class::Init の走査結果の置き場。GameAssembly.dll の大きさと更新時刻が一致する間だけ使う。
// 書式は 1 行 1 項目のタブ区切り:
//   v1
//   ga <size> <mtime ticks>
//   sig <signature key> <found 0|1> <rva>
// 読めない・版が違う・壊れている時は空として扱い、次の起動で作り直す。
internal static class SigCache
{
    internal readonly struct Entry
    {
        internal readonly bool Found;
        internal readonly long Rva;
        internal Entry(bool found, long rva) { Found = found; Rva = rva; }
    }

    private const string Version = "v1";

    internal static readonly Dictionary<string, Entry> Sigs = new(StringComparer.Ordinal);

    private static string path;
    private static long gaSize = -1, gaMtime = -1;
    private static bool dirty;
    private static readonly object gate = new();

    internal static void Load(string file)
    {
        path = file;
        try
        {
            if (!File.Exists(file)) return;
            string[] lines = File.ReadAllLines(file, Encoding.UTF8);
            if (lines.Length == 0 || lines[0] != Version) return;
            foreach (string line in lines)
            {
                string[] f = line.Split('\t');
                if (f[0] == "ga" && f.Length == 3)
                {
                    gaSize = long.Parse(f[1], CultureInfo.InvariantCulture);
                    gaMtime = long.Parse(f[2], CultureInfo.InvariantCulture);
                }
                else if (f[0] == "sig" && f.Length == 4)
                {
                    Sigs[f[1]] = new Entry(f[2] == "1", long.Parse(f[3], CultureInfo.InvariantCulture));
                }
            }
        }
        catch (Exception ex)
        {
            Sigs.Clear();
            gaSize = gaMtime = -1;
            BootAccelPatcher.Warn("cache load failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // GameAssembly.dll が変わっていたら (ゲームの更新) 走査結果を捨てる
    internal static void EnsureGameAssembly(long size, long mtime)
    {
        if (gaSize == size && gaMtime == mtime) return;
        Sigs.Clear();
        gaSize = size;
        gaMtime = mtime;
        dirty = true;
    }

    internal static void Put(string key, Entry entry)
    {
        Sigs[key] = entry;
        dirty = true;
    }

    internal static void Save()
    {
        if (path == null) return;
        try
        {
            lock (gate)
            {
                if (!dirty) return;
                var sb = new StringBuilder();
                sb.Append(Version).Append('\n');
                sb.Append("ga\t").Append(gaSize.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(gaMtime.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (var kv in Sigs)
                    sb.Append("sig\t").Append(kv.Key).Append('\t').Append(kv.Value.Found ? '1' : '0').Append('\t')
                      .Append(kv.Value.Rva.ToString(CultureInfo.InvariantCulture)).Append('\n');

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                File.Move(tmp, path, true);
                dirty = false;
            }
        }
        catch (Exception ex)
        {
            BootAccelPatcher.Warn("cache save failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
