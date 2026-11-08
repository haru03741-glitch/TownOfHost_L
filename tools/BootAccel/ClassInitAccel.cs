using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace TownOfHostL.BootAccel;

// Based on https://github.com/waffle-ful/Aeterna-End-K-not (EndKnot.BootAccel/ClassInitAccel.cs, GPL-3.0)
// Il2CppInterop は起動のたびに Class::Init の署名を GameAssembly (数十 MB) の全域から 1 バイトずつ探す
// (MemoryUtils.FindSignatureInModule)。今の版では署名が当たらず、全域の走査が 2 回空振りしてから
// export の代わりの関数へ落ちる。走査の答えは GameAssembly が同じなら毎回同じなので、
// 署名ごとの答え (当たらなかったことも含む) を残して返す。外れを返せば呼び出し元が
// 自分で代わりの関数を探すので、その先の振る舞いは元のまま。
// 実測 (2026-11-08・Steam 版 x64): 走査 785ms → 2 回目以降 3ms。
internal static class ClassInitAccel
{
    private static bool gaChecked;
    private static int calls, hits;
    private static long ticks;

    [ThreadStatic] private static string key;
    [ThreadStatic] private static bool served;
    [ThreadStatic] private static IntPtr moduleBase;
    [ThreadStatic] private static long t0;

    internal static void Install(Harmony harmony)
    {
        try
        {
            Assembly asm = typeof(Il2CppInterop.Runtime.IL2CPP).Assembly;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            MethodInfo find = asm.GetType("Il2CppInterop.Runtime.MemoryUtils")?.GetMethod("FindSignatureInModule", all);
            MethodInfo setup = asm.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers")?.GetMethod("Setup", all, null, Type.EmptyTypes, null);
            if (find == null || find.ReturnType != typeof(IntPtr) || find.GetParameters().Length != 2)
            {
                BootAccelPatcher.Warn("FindSignatureInModule not recognised; Class::Init scan runs every boot");
                return;
            }

            harmony.Patch(find,
                new HarmonyMethod(AccessTools.Method(typeof(ClassInitAccel), nameof(Prefix))),
                new HarmonyMethod(AccessTools.Method(typeof(ClassInitAccel), nameof(Postfix))));
            if (setup != null)
                harmony.Patch(setup, postfix: new HarmonyMethod(AccessTools.Method(typeof(ClassInitAccel), nameof(SetupPostfix))));
        }
        catch (Exception ex)
        {
            BootAccelPatcher.Warn("classinit install failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // 32bit では IntPtr.ToInt64 が符号拡張するので、差を取る前に符号なしの値へ直す
    private static long Linear(IntPtr p) => IntPtr.Size == 4 ? (uint)p.ToInt32() : p.ToInt64();

    private static bool TryPointer(long linear, out IntPtr p)
    {
        p = IntPtr.Zero;
        if (IntPtr.Size == 4)
        {
            if (linear < 0 || linear > uint.MaxValue) return false;
            p = new IntPtr(unchecked((int)(uint)linear));
            return true;
        }
        p = new IntPtr(linear);
        return true;
    }

    // SignatureDefinition (internal) のフィールドを名前順に並べ、値は 16 進にして 1 つの鍵にする
    private static string SigKey(object sig)
    {
        if (sig == null) return null;
        var fields = sig.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
        var sb = new StringBuilder(128);
        foreach (FieldInfo f in fields)
        {
            sb.Append(f.Name).Append('=');
            string v = Convert.ToString(f.GetValue(sig), CultureInfo.InvariantCulture) ?? "";
            foreach (char c in v) sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            sb.Append(';');
        }
        return sb.ToString();
    }

    public static bool Prefix(object[] __args, ref IntPtr __result)
    {
        key = null;
        served = false;
        t0 = Stopwatch.GetTimestamp();
        try
        {
            if (__args == null || __args.Length != 2 || __args[0] is not ProcessModule module) return true;
            moduleBase = module.BaseAddress;

            if (!gaChecked)
            {
                gaChecked = true;
                var fi = new FileInfo(module.FileName);
                if (fi.Exists) SigCache.EnsureGameAssembly(fi.Length, fi.LastWriteTimeUtc.Ticks);
                else SigCache.EnsureGameAssembly(-1, -1);
            }

            key = SigKey(__args[1]);
            if (key == null || !SigCache.Sigs.TryGetValue(key, out var e)) return true;

            IntPtr cached = IntPtr.Zero;
            if (e.Found && !TryPointer(Linear(moduleBase) + e.Rva, out cached)) return true;
            __result = cached;
            served = true;
            return false;
        }
        catch (Exception ex)
        {
            BootAccelPatcher.Warn("classinit prefix: " + ex.GetType().Name + ": " + ex.Message);
            key = null;
            served = false;
            return true;
        }
    }

    public static void Postfix(IntPtr __result)
    {
        try
        {
            calls++;
            ticks += Stopwatch.GetTimestamp() - t0;
            if (served) { hits++; return; }
            if (key == null) return;
            SigCache.Put(key, __result == IntPtr.Zero
                ? new SigCache.Entry(false, 0)
                : new SigCache.Entry(true, Linear(__result) - Linear(moduleBase)));
        }
        catch (Exception ex)
        {
            BootAccelPatcher.Warn("classinit postfix: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // 走査は最初の InjectorHelpers.Setup の中で済むので、ここで結果を書き出し、1 行だけ報告する
    // (Setup は型を il2cpp へ登録するたびに呼ばれる)
    private static bool reported;
    public static void SetupPostfix()
    {
        if (reported) return;
        reported = true;
        SigCache.Save();
        BootAccelPatcher.AccelLog.LogInfo(string.Format(CultureInfo.InvariantCulture,
            "classinit: {0}/{1} from cache, scan took {2:F1}ms", hits, calls, ticks * 1000.0 / Stopwatch.Frequency));
    }
}
