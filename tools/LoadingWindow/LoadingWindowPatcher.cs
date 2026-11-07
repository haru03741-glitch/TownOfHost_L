using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Preloader.Core.Patching;

namespace LoadingWindow;

/// <summary>
/// 起動時に、今どこまで読み込んだかを別の窓で見せる。
/// 窓は PowerShell (WinForms) で出し、この DLL は状態ファイルへ書くだけ。
///   S &lt;段階&gt; start|done &lt;起動からのミリ秒&gt;
///   M &lt;今の状態の文&gt;
/// 段階: 0 環境とBepInExの初期化 / 1 パッチャー / 2 MOD (プラグイン) / 3 ゲームの開始
/// 3 の done は MOD 側 (TOH_L のタイトル画面) が書く。
/// </summary>
[PatcherPluginInfo("haru.loadingwindow", "LoadingWindow", "1.0.0")]
public class LoadingWindowPatcher : BasePatcher
{
    public static string StatusPath => Path.Combine(Paths.CachePath, "loading_window.txt");
    static readonly object Lock = new();

    static long SinceStart()
    {
        try { return (long)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
        catch { return 0; }
    }

    public static void Write(string line)
    {
        try
        {
            lock (Lock) File.AppendAllText(StatusPath, line.Replace('\n', ' ') + "\n", new UTF8Encoding(false));
        }
        catch { }
    }

    public override void Initialize()
    {
        try
        {
            Directory.CreateDirectory(Paths.CachePath);
            File.WriteAllText(StatusPath, "", new UTF8Encoding(false));
            Write("S 0 start 0");
            Write($"S 0 done {SinceStart()}");
            Write($"S 1 start {SinceStart()}");
            Write("M パッチャーを読み込んでいます");
            StartWindow();
            Logger.Listeners.Add(new Listener());
        }
        catch (Exception ex)
        {
            Log.LogWarning($"読み込み画面を出せませんでした: {ex.Message}");
        }
    }

    public override void Finalizer()
    {
        Write($"S 1 done {SinceStart()}");
        Write($"S 2 start {SinceStart()}");
        Write("M MOD を読み込んでいます");
    }

    static void StartWindow()
    {
        // 窓の中身 (PowerShell の台本) を書き出して、隠した PowerShell から開く
        var script = Path.Combine(Paths.CachePath, "loading_window.ps1");
        using (var s = typeof(LoadingWindowPatcher).Assembly.GetManifestResourceStream("loader.ps1"))
        using (var r = new StreamReader(s, Encoding.UTF8))
            File.WriteAllText(script, r.ReadToEnd(), new UTF8Encoding(true)); // PowerShell 5.1 は BOM が無いと日本語を読めない

        var pid = Process.GetCurrentProcess().Id;
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -STA -File \"{script}\" -StatusFile \"{StatusPath}\" -GamePid {pid}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(psi);
    }

    sealed class Listener : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.All;
        bool chainDone;

        public void LogEvent(object sender, LogEventArgs e)
        {
            if (chainDone) return;
            var msg = e.Data?.ToString() ?? "";
            if (msg.StartsWith("Loading [", StringComparison.Ordinal))
            {
                var name = msg.Substring(9).TrimEnd(']');
                Write($"M {name} を読み込んでいます");
            }
            else if (msg.Contains("Chainloader startup complete"))
            {
                chainDone = true;
                Write($"S 2 done {SinceStart()}");
                Write($"S 3 start {SinceStart()}");
                Write("M ゲームを起動しています");
            }
            else if (e.Level is LogLevel.Error or LogLevel.Fatal && e.Source?.SourceName is "BepInEx" or "Preloader")
            {
                Write($"M エラー: {msg}");
            }
        }

        public void Dispose() { }
    }
}
