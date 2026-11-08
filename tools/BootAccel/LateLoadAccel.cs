using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Logging;
using HarmonyLib;

namespace TownOfHostL.BootAccel;

// Based on https://github.com/waffle-ful/Aeterna-End-K-not (EndKnot.BootAccel/LateLoadAccel.cs, GPL-3.0)
// BepInEx は最初の Internal_ActiveSceneChanged の場で plugin の読み込み (Chainloader.Execute) を同期に回すので、
// Unity は 1 フレームも描かないまま 1 秒ほど待たされる。その後に始まる EOS のログインもその分だけ遅れる。
// シーン変更の場では元と同じ下準備 (Unity のログの取り込み・interop の先読み) だけ済ませ、
// Execute は Time.frameCount がシーン変更から 2 進んだ最初の呼び出しで回す。
// フレーム数が読めない・進まない時は 3 秒後の呼び出しで回す。
// 実測 (2026-11-08・Steam 版 x64・Class::Init の保存と合わせて各 3 起動の中央値): メインメニューの到着 6.9s → 6.1s。
// plugin は最初のシーンの Unity 側の受け手より後・最初のフレームの後に読まれる。
// TOHL のパッチは最初のシーンの開始時に走る物を当てにしていない。
internal static class LateLoadAccel
{
    private const int FallbackMs = 3000;

    private static FieldInfo originalInvoke;
    private static PropertyInfo detour;
    private static FieldInfo unityLogging;
    private static MethodInfo preloadInterop;

    private static int state; // 0 = シーン変更待ち, 1 = 2 フレーム待ち, 2 = 済み
    private static int mainThread;
    private static Stopwatch sinceScene;
    private static int frameAtScene;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FrameCountFn();
    private static FrameCountFn frameCount;

    internal static void Install(Harmony harmony)
    {
        try
        {
            Type cl = typeof(IL2CPPChainloader);
            const BindingFlags all = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo target = cl.GetMethod("OnInvokeMethod", all, null,
                new[] { typeof(IntPtr), typeof(IntPtr), typeof(IntPtr), typeof(IntPtr) }, null);
            originalInvoke = cl.GetField("originalInvoke", all);
            detour = cl.GetProperty("RuntimeInvokeDetour", all);
            unityLogging = cl.GetField("ConfigUnityLogging", all);
            preloadInterop = cl.Assembly.GetType("BepInEx.Unity.IL2CPP.Il2CppInteropManager")?.GetMethod("PreloadInteropAssemblies", all);

            if (target == null || originalInvoke == null || detour == null || preloadInterop == null)
            {
                BootAccelPatcher.Warn("lateload: chainloader not recognised; plugins load at the stock point");
                return;
            }
            harmony.Patch(target, new HarmonyMethod(AccessTools.Method(typeof(LateLoadAccel), nameof(Prefix))));
        }
        catch (Exception ex)
        {
            BootAccelPatcher.Warn("lateload install failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // IL2CPPChainloader.OnInvokeMethod の Prefix。true を返すと元の処理が走る
    // (シーン変更をこちらで受けた後は、元の処理は何もせず il2cpp_runtime_invoke へ渡すだけ)。
    public static bool Prefix(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc, ref IntPtr __result)
    {
        if (state == 2) return true;
        bool forwarded = false; // il2cpp_runtime_invoke をこちらで呼んだ (ここから先の例外で元の処理をもう一度走らせない)
        try
        {
            // 起動中は数千回通るので、名前は先頭が 'I' の時だけ文字列にする
            IntPtr namePtr = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_name(method);
            string name = namePtr != IntPtr.Zero && Marshal.ReadByte(namePtr) == (byte)'I' ? Marshal.PtrToStringAnsi(namePtr) : null;

            if (state == 0)
            {
                if (name != "Internal_ActiveSceneChanged") return true;
                mainThread = Environment.CurrentManagedThreadId;
                sinceScene = Stopwatch.StartNew();
                frameCount = ResolveFrameCount();
                frameAtScene = frameCount?.Invoke() ?? 0;
                state = 1;
                OnSceneChanged();
                forwarded = true;
                __result = CallOriginal(method, obj, parameters, exc);
                return false;
            }

            if (Environment.CurrentManagedThreadId != mainThread) return true;

            bool framesPassed = frameCount != null && frameCount() >= frameAtScene + 2;
            // Unity は最初のフレームの前にシーン変更を 2 回出す。元の処理に渡すとそこで Execute が回るので、こちらで流す
            if (name == "Internal_ActiveSceneChanged" && !framesPassed)
            {
                forwarded = true;
                __result = CallOriginal(method, obj, parameters, exc);
                return false;
            }
            if (!framesPassed && sinceScene.ElapsedMilliseconds <= FallbackMs) return true;

            state = 2;
            RunExecute(framesPassed);
            forwarded = true;
            try { __result = CallOriginal(method, obj, parameters, exc); }
            finally { DisposeDetour(); }
            return false;
        }
        catch (Exception ex)
        {
            // plugin を読まないまま進むことだけは避ける
            ex = ex.InnerException ?? ex;
            BootAccelPatcher.Warn("lateload: prefix failed, falling back to the stock path: " + ex.GetType().Name + ": " + ex.Message);
            if (state != 1) return !forwarded;
            state = 2;
            try { RunExecute(false); } catch { }
            // ここで plugin を読んだので、元の処理 (シーン変更なら Execute をもう一度回す) には渡さず、こちらで流す
            if (!forwarded)
            {
                try { __result = CallOriginal(method, obj, parameters, exc); }
                catch (Exception e) { BootAccelPatcher.Warn("lateload: runtime invoke failed: " + (e.InnerException ?? e).Message); }
            }
            DisposeDetour();
            return false;
        }
    }

    private static FrameCountFn ResolveFrameCount()
    {
        try
        {
            IntPtr p = Il2CppInterop.Runtime.IL2CPP.il2cpp_resolve_icall("UnityEngine.Time::get_frameCount()");
            if (p != IntPtr.Zero) return Marshal.GetDelegateForFunctionPointer<FrameCountFn>(p);
            BootAccelPatcher.Warn("lateload: Time.frameCount not found; waiting by time instead");
        }
        catch (Exception ex)
        {
            BootAccelPatcher.Warn("lateload: frameCount resolve failed: " + ex.GetType().Name + ": " + ex.Message);
        }
        return null;
    }

    // 元の OnInvokeMethod がシーン変更の場で Execute の前にする事
    private static void OnSceneChanged()
    {
        try
        {
            if (unityLogging?.GetValue(null) is not BepInEx.Configuration.ConfigEntry<bool> entry || entry.Value)
                BepInEx.Logging.Logger.Sources.Add(new IL2CPPUnityLogSource());
            preloadInterop.Invoke(null, null);
        }
        catch (Exception ex)
        {
            BootAccelPatcher.Warn("lateload: scene-change setup failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void RunExecute(bool byFrames)
    {
        var sw = Stopwatch.StartNew();
        long waited = sinceScene?.ElapsedMilliseconds ?? -1;
        try
        {
            IL2CPPChainloader.Instance.Execute();
        }
        catch (Exception ex)
        {
            BootAccelPatcher.AccelLog.LogFatal("Unable to execute IL2CPP chainloader (late load)");
            BootAccelPatcher.AccelLog.LogError(ex);
        }
        BootAccelPatcher.AccelLog.LogInfo($"lateload: plugins loaded {waited}ms after the first scene change ({(byFrames ? "frame +2" : "time fallback")}), took {sw.ElapsedMilliseconds}ms");
    }

    private static IntPtr CallOriginal(IntPtr method, IntPtr obj, IntPtr parameters, IntPtr exc)
        => (IntPtr)((Delegate)originalInvoke.GetValue(null)).DynamicInvoke(method, obj, parameters, exc);

    private static void DisposeDetour()
    {
        try { (detour.GetValue(null) as IDisposable)?.Dispose(); }
        catch (Exception ex) { BootAccelPatcher.Warn("lateload: detour dispose failed: " + ex.GetType().Name + ": " + ex.Message); }
    }
}
