using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 番犬 (Among Us の外で動く見張り役) と、プロセスごとの再起動。
/// 番犬が ON の間は心拍 (TOHL_DATA/Watchdog/heartbeat.txt) を 5 秒ごとに書き直し、番犬 (Resources/TOHLWatchdog.ps1) は
/// Among Us が落ちた・固まった時に起動し直す。起動し直した後のメニューで、前にホストしていたなら部屋を作り直す。
/// 同じプロセスでは部屋を作り直せない時 (ログイン切れ・作り直しの失敗の続き) は、自分で終わって番犬に起動し直してもらう。
/// </summary>
/// <remarks>
/// 番犬をタスク スケジューラから起動して Among Us のプロセスの外に置く形と、再起動の印で番犬の待ち時間を飛ばす形は
/// End K not (waffle-ful/Aeterna-End-K-not Modules/WatchdogLauncher.cs・Modules/AutoRestart.cs・tools/EndKnotWatchdog.ps1) を元にした。
/// </remarks>
public class Watchdog : MonoBehaviour
{
    public Watchdog(IntPtr ptr) : base(ptr) { }

    const string TaskName = "TOHLWatchdog";
    const string MutexName = "Local\\TOHLWatchdog";
    const string ResourceName = "TownOfHostL.Resources.TOHLWatchdog.ps1";
    const float HeartbeatInterval = 5f;
    /// <summary>起動からこの秒数までは再起動しない (起動の直後に落ち続けるのを防ぐ)</summary>
    const float MinUptimeSeconds = 60f;
    const float QuitDelaySeconds = 3f;
    /// <summary>終わるよう頼んでからこの秒数たっても残っていたら、自分で強制終了する</summary>
    const int HardKillAfterSeconds = 10;
    /// <summary>番犬の起動に失敗した時に、次に試すまでの秒数</summary>
    const float StartRetrySeconds = 30f;

    public static bool IsSupported => OperatingSystem.IsWindows();

    static string dir;
    static string Dir => dir ??= Path.GetFullPath(Path.Combine("TOHL_DATA", "Watchdog"));
    static string ScriptPath => Path.Combine(Dir, "TOHLWatchdog.ps1");
    static string TaskXmlPath => Path.Combine(Dir, "task.xml");
    static string HeartbeatPath => Path.Combine(Dir, "heartbeat.txt");
    static string StopFlagPath => Path.Combine(Dir, "stop.flag");
    static string RestartFlagPath => Path.Combine(Dir, "restart.flag");
    static string RelaunchFlagPath => Path.Combine(Dir, "relaunch.flag");
    public static string LogPath => Path.Combine(Dir, "watchdog.log");

    /// <summary>メインメニューに 1 度でも着いたか。着く前の終了 (起動の途中で落ちた) は自分の意思の終了と見ない</summary>
    static bool mainMenuReached;
    /// <summary>再起動のために終わる途中か。戻さない (終わる時に番犬を止めない印)</summary>
    public static bool RestartRequested { get; private set; }
    static volatile bool startInFlight;
    static bool reconcileInit, lastWant;
    static float nextStartAt, nextBeatAt, nextReconcileAt;

    /// <summary>オンラインの部屋をホストしていたいか (入室で立て、自分で抜けたら倒す)。起動し直した後に部屋を作り直すかをこれで決める</summary>
    static bool wantHost;
    static bool wantPublic;
    /// <summary>前のプロセスが最後に書いた心拍の中身</summary>
    static bool prevWantHost, prevWantPublic;

    public static bool Enabled => IsSupported && (Main.CrashWatchdog?.Value ?? false);

    /// <summary>番犬が動いているか (毎秒の見直しで控えた値。操作盤の描画のたびにカーネルの物を開かない)</summary>
    public static bool IsRunning { get; private set; }

    /// <summary>名前付きの Mutex で見るので、起動し直した後のプロセスからでも分かる</summary>
    static bool CheckRunning
    {
        get
        {
            if (!IsSupported) return false;
            try
            {
                if (Mutex.TryOpenExisting(MutexName, out var m)) { m.Dispose(); return true; }
            }
            catch { }
            return false;
        }
    }

    /// <summary>Main.Load から 1 度だけ呼ぶ。前のプロセスの心拍は、こちらが書き直す前に読む</summary>
    public static void Init()
    {
        if (!IsSupported) return;
        try
        {
            if (!File.Exists(HeartbeatPath)) return;
            var text = File.ReadAllText(HeartbeatPath);
            prevWantHost = text.Contains("want=1");
            prevWantPublic = text.Contains("pub=1");
            // 番犬に起動し直された時は、部屋に入るまで前の「ホストしていたい」を心拍に残す
            // (作り直しの途中でまた落ちても、次の起動し直しで部屋を作り直せるように)
            if (File.Exists(RelaunchFlagPath) && (DateTime.Now - File.GetLastWriteTime(RelaunchFlagPath)).TotalMinutes <= 10)
            {
                wantHost = prevWantHost;
                wantPublic = prevWantPublic;
            }
        }
        catch (Exception ex) { Logger.Warn($"前の心拍を読めなかった: {ex.Message}", "Watchdog"); }
    }

    // ===== 毎フレーム =====

    public void Update()
    {
        if (!IsSupported) return;
        var now = Time.realtimeSinceStartup;
        if (now >= nextReconcileAt)
        {
            nextReconcileAt = now + 1f;
            Reconcile();
        }
        if (now >= nextBeatAt && Enabled && !RestartRequested)
        {
            nextBeatAt = now + HeartbeatInterval;
            WriteHeartbeat();
        }
    }

    public void OnApplicationQuit() => OnGameQuit("quit");
    public void OnDestroy() => OnGameQuit("destroy");

    static void WriteHeartbeat()
    {
        try
        {
            var c = AmongUsClient.Instance;
            string state;
            if (c == null || GameStates.IsNotJoined) state = "Menu";
            else if (GameStates.IsLobby) state = "Lobby";
            else if (GameStates.IsInGame) state = "Game";
            else state = "Other";
            // ロビーで公開を切り替えた分も拾う
            if (wantHost && c != null && c.AmHost && GameStates.IsLobby) wantPublic = c.IsGamePublic;
            Directory.CreateDirectory(Dir);
            File.WriteAllText(HeartbeatPath, $"pid={Environment.ProcessId} state={state} want={(wantHost ? 1 : 0)} pub={(wantPublic ? 1 : 0)}");
        }
        catch (Exception ex) { Logger.Warn($"心拍を書けなかった: {ex.Message}", "Watchdog"); }
    }

    // ===== 部屋の状態 (AutoRehost から) =====

    /// <summary>オンラインの部屋にホストとして入った</summary>
    public static void NoteHosting(bool isPublic)
    {
        wantHost = true;
        wantPublic = isPublic;
    }

    /// <summary>自分で部屋を抜けた (起動し直しても部屋は作らない)</summary>
    public static void NoteLeft() => wantHost = false;

    // ===== 設定と番犬の起動・停止 =====

    /// <summary>設定の ON/OFF を番犬に映す。切り替わった時だけ止める (手で立てた番犬を毎秒止めないため)</summary>
    static void Reconcile()
    {
        IsRunning = CheckRunning;
        var want = Enabled;
        if (!reconcileInit)
        {
            reconcileInit = true;
            lastWant = want;
        }
        else if (want != lastWant)
        {
            lastWant = want;
            if (!want) { Stop(); return; }
        }
        if (want && !RestartRequested && !startInFlight && !IsRunning && Time.realtimeSinceStartup >= nextStartAt)
        {
            nextStartAt = Time.realtimeSinceStartup + StartRetrySeconds;
            Start();
        }
    }

    static void Start()
    {
        string exe;
        try { exe = Process.GetCurrentProcess().MainModule?.FileName ?? ""; }
        catch { exe = ""; }
        // Epic 版かは番犬が Epic のマニフェストの入れ先と exe のフォルダを比べて決める (別のライブラリに入れた Epic 版も拾う)
        var platform = exe.Contains(@"\steamapps\", StringComparison.OrdinalIgnoreCase) ? "Steam" : "Other";
        startInFlight = true;
        // schtasks を待つ間にゲームを止めないよう、別のスレッドで起動する
        new Thread(() => StartWorker(exe, platform)) { IsBackground = true, Name = "TOHLWatchdogStart" }.Start();
    }

    static void StartWorker(string exe, string platform)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            // 前の止めの印が残っていると、起動した番犬がすぐ終わってしまう
            TryDelete(StopFlagPath);
            if (!WriteScript()) return;
            var args = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Minimized -File \"{ScriptPath}\" -DataDir \"{Dir}\" -ExePath \"{exe}\" -Platform {platform}";
            WriteTaskXml(args);
            // タスク スケジューラに起動させ、Among Us のプロセスの外に置く (Among Us を強制終了しても道連れにならない)
            if (RunSchtasks($"/Create /F /TN {TaskName} /XML \"{TaskXmlPath}\"") && RunSchtasks($"/Run /TN {TaskName}"))
            {
                Logger.Info($"番犬を起動した ({platform})", "Watchdog");
                return;
            }
            // タスク スケジューラが使えない時は直接起動する (この時は Among Us の強制終了に巻き込まれうる)
            Logger.Warn("タスク スケジューラで起動できなかったので、番犬を直接起動する", "Watchdog");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = args,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
                WorkingDirectory = Dir,
            });
        }
        catch (Exception ex) { Logger.Warn($"番犬を起動できなかった: {ex.Message}", "Watchdog"); }
        finally { startInFlight = false; }
    }

    /// <summary>番犬に止めの印を渡す (番犬は次の見回りで終わる)</summary>
    static void Stop()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(StopFlagPath, DateTime.Now.ToString("o"));
            Logger.Info("番犬に止めの印を置いた", "Watchdog");
            new Thread(() => RunSchtasks($"/Delete /F /TN {TaskName}")) { IsBackground = true }.Start();
        }
        catch (Exception ex) { Logger.Warn($"番犬を止められなかった: {ex.Message}", "Watchdog"); }
    }

    static bool quitHandled;

    /// <summary>ゲームを終える時。自分で閉じたなら番犬も止める (落ちた・強制終了ではここを通らないので、番犬が起動し直す)</summary>
    public static void OnGameQuit(string why)
    {
        if (quitHandled || !IsSupported) return;
        quitHandled = true;
        if (RestartRequested) return;
        // メニューに着く前の終了は、起動の途中で落ちたもの。番犬は残して起動し直してもらう
        if (!mainMenuReached)
        {
            Logger.Warn($"メニューに着く前に終わる ({why})。番犬は残す", "Watchdog");
            return;
        }
        if (CheckRunning) Stop();
    }

    // ===== プロセスごとの再起動 =====

    /// <summary>再起動できる状態か (番犬が動いていて、起動の直後でない)</summary>
    public static bool CanRestart => Enabled && IsRunning && !RestartRequested && Time.realtimeSinceStartup >= MinUptimeSeconds;

    /// <summary>ゲームを終え、番犬に起動し直してもらう。できない時は false (呼んだ側がいつもの知らせを出す)</summary>
    public static bool RequestRestart(string why)
    {
        if (!CanRestart)
        {
            if (Enabled) Logger.Warn($"再起動できない ({why}) 番犬={IsRunning} 起動から={Time.realtimeSinceStartup:F0}秒 済={RestartRequested}", "Watchdog");
            return false;
        }
        Logger.Warn($"ゲームごと再起動する ({why})", "Watchdog");
        // 終わる直前の部屋の様子を残す (起動し直した後に作り直すか)
        WriteHeartbeat();
        RestartRequested = true;
        try { File.WriteAllText(RestartFlagPath, DateTime.Now.ToString("o")); }
        catch (Exception ex) { Logger.Warn($"再起動の印を置けなかった: {ex.Message}", "Watchdog"); }

        _ = new LateTask(() =>
        {
            try { Application.Quit(); }
            catch (Exception ex) { Logger.Warn($"終われなかった: {ex.Message}", "Watchdog"); }
        }, QuitDelaySeconds, "Watchdog.Quit", true);
        // メインスレッドが固まって終われない時の保険
        new Thread(() =>
        {
            Thread.Sleep((int)(QuitDelaySeconds * 1000) + HardKillAfterSeconds * 1000);
            try { Process.GetCurrentProcess().Kill(); } catch { }
        }) { IsBackground = true, Name = "TOHLRestartKill" }.Start();
        return true;
    }

    public static string StatusText()
    {
        if (!IsSupported) return null;
        if (RestartRequested) return GetString("Watchdog.Restarting");
        if (!Enabled) return GetString("Watchdog.Off");
        return GetString(IsRunning ? "Watchdog.Running" : "Watchdog.Starting");
    }

    public static void Toggle()
    {
        if (!IsSupported || Main.CrashWatchdog == null) return;
        Main.CrashWatchdog.Value = !Main.CrashWatchdog.Value;
    }

    // ===== 起動し直した後 =====

    // どのモードでも番犬は動くので、いつも当てる束に置く
    [HarmonyPatch, Core.Modes.PatchBundle(Core.Modes.PatchBundles.Core)]
    static class MainMenuStartPatch
    {
        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix]
        static void Postfix() => OnMainMenuStart();
    }

    static void OnMainMenuStart()
    {
        if (mainMenuReached || !IsSupported) return;
        mainMenuReached = true;
        try
        {
            if (!File.Exists(RelaunchFlagPath)) return;
            var age = (DateTime.Now - File.GetLastWriteTime(RelaunchFlagPath)).TotalMinutes;
            TryDelete(RelaunchFlagPath);
            if (age > 10) { Logger.Info("古い起動し直しの印を捨てた", "Watchdog"); return; }
            if (!prevWantHost) { Logger.Info("番犬に起動し直された (前はホストしていなかった)", "Watchdog"); return; }
            Logger.Info($"番犬に起動し直されたので部屋を作り直す (公開={prevWantPublic})", "Watchdog");
            AutoRehost.HostFromMenu(prevWantPublic, "番犬の起動し直し");
        }
        catch (Exception ex) { Logger.Warn($"起動し直しの印を読めなかった: {ex.Message}", "Watchdog"); }
    }

    // ===== 下回り =====

    /// <summary>埋め込んだ番犬を書き出す (起動のたびに上書きして、DLL と同じ版にする)。Windows PowerShell 5.1 は BOM が無いと日本語を読めない</summary>
    static bool WriteScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream == null)
        {
            Logger.Warn($"番犬のスクリプトが DLL に無い: {ResourceName}", "Watchdog");
            return false;
        }
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        using var fs = File.Create(ScriptPath);
        if (!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)) fs.Write(new byte[] { 0xEF, 0xBB, 0xBF });
        fs.Write(bytes);
        return true;
    }

    /// <summary>タスクの定義。引数を別の要素に持つので、パスの空白や日本語でクオートが崩れない</summary>
    static void WriteTaskXml(string args)
    {
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
            "  <RegistrationInfo><Description>Town Of Host_L watchdog</Description></RegistrationInfo>\r\n" +
            "  <Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\r\n" +
            // Settings の子は並びが決まっている (違うと厳しい Windows では登録を断られる)
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <AllowHardTerminate>false</AllowHardTerminate>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "    <Enabled>true</Enabled><Hidden>false</Hidden>\r\n" +
            "  </Settings>\r\n" +
            $"  <Actions Context=\"Author\"><Exec><Command>powershell.exe</Command><Arguments>{SecurityElement.Escape(args)}</Arguments></Exec></Actions>\r\n" +
            "</Task>";
        File.WriteAllText(TaskXmlPath, xml, Encoding.Unicode);
    }

    static bool RunSchtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (p == null) return false;
            if (!p.WaitForExit(8000))
            {
                try { p.Kill(); } catch { }
                return false;
            }
            if (p.ExitCode != 0) Logger.Warn($"schtasks {args.Split(' ')[0]} が {p.ExitCode} で終わった: {p.StandardError.ReadToEnd().Trim()}", "Watchdog");
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Logger.Warn($"schtasks を動かせなかった: {ex.Message}", "Watchdog");
            return false;
        }
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
