using System;
using System.IO;
using BepInEx.Logging;
using BepInEx.Preloader.Core.Patching;
using HarmonyLib;

namespace TownOfHostL.BootAccel;

// Based on https://github.com/waffle-ful/Aeterna-End-K-not (EndKnot.BootAccel, GPL-3.0)
// BepInEx が起動のたびに払う 2 つの待ちを減らす preloader patcher。
//   ・Class::Init を探すための GameAssembly 全体のバイト走査 → 結果を BepInEx/cache に残して次から使う
//   ・最初のシーン変更の場で同期に回る plugin の読み込み → 最初の 2 フレームを描いた後へ送る
// どちらも、少しでも想定と違えば元の処理に任せる。ここで起動を止めることはしない。
// BepInEx/cache に tohl-bootaccel.disabled を置くと全部を、tohl-lateload.disabled を置くと後者だけを切る。
[PatcherPluginInfo(Guid, "TownOfHost_L Boot Accelerator", "1.0.0")]
public class BootAccelPatcher : BasePatcher
{
    internal const string Guid = "tohl.bootaccel";

    internal static readonly ManualLogSource AccelLog = Logger.CreateLogSource("BootAccel");

    public override void Initialize()
    {
        try
        {
            string cacheDir = BepInEx.Paths.CachePath;
            if (File.Exists(Path.Combine(cacheDir, "tohl-bootaccel.disabled")))
            {
                AccelLog.LogInfo("tohl-bootaccel.disabled found; boot runs unaccelerated");
                return;
            }

            var harmony = new Harmony(Guid);
            SigCache.Load(Path.Combine(cacheDir, "tohl-bootaccel.txt"));
            ClassInitAccel.Install(harmony);

            if (File.Exists(Path.Combine(cacheDir, "tohl-lateload.disabled")))
                AccelLog.LogInfo("tohl-lateload.disabled found; plugins load at the stock point");
            else
                LateLoadAccel.Install(harmony);
        }
        catch (Exception ex)
        {
            Warn("initialize failed, boot runs unaccelerated: " + ex);
        }
    }

    public override void Finalizer() => SigCache.Save();

    internal static void Warn(string message)
    {
        try { AccelLog.LogWarning(message); }
        catch { }
    }
}
