using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// バグや要望の報告。
/// ・他の人: チャットで /bug 内容 (/report・/req も同じ)
/// ・ホスト: 操作盤の「バグ・要望を報告」から、Windows の入力画面で文と画像・動画を付けて送る
/// 送った物はホストの PC の TOHL_DATA/Reports.txt に時刻・送った人・その時の状況と一緒に書き足し、付けたファイルは TOHL_DATA/Reports/ に写す。
/// ホストが Discord の Webhook を入れていれば、そこへも (付けたファイルごと) 送る
/// </summary>
public static class BugReport
{
    const int MaxLength = 300;
    const int MaxLengthForm = 2000;
    // 1 人が続けて送れる間隔 (秒)
    const float Interval = 20f;
    // Discord の Webhook へ 1 通で送れるファイルの合計 (サーバーのブーストが無い時の上限 10MB より少し下)
    const long MaxUpload = 9_500_000;
    static readonly Dictionary<byte, float> lastAt = new();

    static string Dir => Path.Combine(Environment.CurrentDirectory, "TOHL_DATA");
    static string FilePath => Path.Combine(Dir, "Reports.txt");
    static string AttachDir => Path.Combine(Dir, "Reports");

    public static void Host(ChatCommandContext c) => Handle(c, true);
    public static void Remote(ChatCommandContext c) => Handle(c, false);

    static void Handle(ChatCommandContext c, bool host)
    {
        var to = c.Player != null ? c.Player.PlayerId : byte.MaxValue;
        void Reply(string text)
        {
            if (host) HostNote.Post(text);
            else Utils.SendMessageAutoSplit(text, true, sendTo: to);
        }
        var text = string.Join(" ", c.Args.Skip(1)).Trim();
        if (host && text.ToLowerInvariant() is "open" or "folder")
        {
            OpenFolder();
            return;
        }
        if (host && text.Length == 0)
        {
            OpenForm();
            return;
        }
        if (text.Length == 0)
        {
            Reply(GetString("Report.HowTo"));
            return;
        }
        if (text.Length > MaxLength) text = text[..MaxLength];
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (!host && lastAt.TryGetValue(to, out var last) && now - last < Interval)
        {
            Reply(GetString("Report.TooFast"));
            return;
        }
        lastAt[to] = now;

        var name = Submit(c.Player, text, Array.Empty<string>());
        Reply(GetString("Report.Thanks"));
        // ホストへは、誰から何が届いたかを知らせる
        if (!host) HostNote.Post(string.Format(GetString("Report.ToHost"), name, text));
    }

    /// <summary>報告を残して送る。送った人の名前を返す</summary>
    static string Submit(PlayerControl pc, string text, IReadOnlyList<string> files)
    {
        var name = pc?.Data?.PlayerName ?? "?";
        var stamp = DateTime.Now;
        var line = $"[{stamp:yyyy-MM-dd HH:mm:ss}] {name} ({Situation(pc)}) {text.Replace("\r", "").Replace("\n", " / ")}";

        // 付けたファイルを写す (元のファイルが消えても残るように)
        var saved = new List<string>();
        if (files.Count > 0)
        {
            try
            {
                var dir = Path.Combine(AttachDir, stamp.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(dir);
                foreach (var f in files)
                {
                    if (!File.Exists(f)) continue;
                    var dest = Path.Combine(dir, Path.GetFileName(f));
                    File.Copy(f, dest, true);
                    saved.Add(dest);
                }
                if (saved.Count > 0) line += $"  [{GetString("Report.Attached")}: {string.Join(", ", saved.Select(Path.GetFileName))} → Reports/{Path.GetFileName(dir)}]";
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
        }

        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
        Logger.Info(line, "Report");

        if (DiscordWebhook.HasUrl)
        {
            // 大きすぎるファイルは Discord へは送らず、フォルダに残したことを書き添える
            var upload = new List<string>();
            var skipped = new List<string>();
            long total = 0;
            foreach (var f in saved)
            {
                long len = new FileInfo(f).Length;
                if (total + len <= MaxUpload) { upload.Add(f); total += len; }
                else skipped.Add(Path.GetFileName(f));
            }
            var body = $"📝 {GetString("Report.WebhookTitle")}\n{line}";
            if (skipped.Count > 0) body += "\n" + string.Format(GetString("Report.TooBig"), string.Join(", ", skipped));
            DiscordWebhook.PostWithFiles(body, upload);
        }
        return name;
    }

    // 送った時の状況: ロビー / 試合中 (マップ・役職・生死)
    static string Situation(PlayerControl pc)
    {
        try
        {
            if (!GameStates.IsInGame) return "Lobby";
            var map = ((MapNames)Main.NormalOptions.MapId).ToString();
            if (pc == null) return map;
            return $"{map} {pc.GetCustomRole()}{(pc.IsAlive() ? "" : " dead")}{(GameStates.IsMeeting ? " meeting" : "")}";
        }
        catch { return "?"; }
    }

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            if (!File.Exists(FilePath)) File.WriteAllText(FilePath, "");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
    }

    // ---- ホストの入力画面 (Windows の画面。本編の中の入力欄は使えないので、PowerShell の画面を別に開く) ----

    static volatile bool formOpen;
    public static bool FormOpen => formOpen;
    static readonly ConcurrentQueue<(string text, string[] files)> formResults = new();
    static readonly ConcurrentQueue<string> formErrors = new();

    /// <summary>操作盤のボタンから: 報告の入力画面を開く (閉じるまで待たない)</summary>
    public static void OpenForm()
    {
        if (formOpen) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var script = Path.Combine(Dir, "ReportForm.ps1");
            var outPath = Path.Combine(Dir, "ReportForm.out.json");
            File.WriteAllText(script, FormScript(), new UTF8Encoding(true));
            if (File.Exists(outPath)) File.Delete(outPath);
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -Out \"{outPath}\"")
            { UseShellExecute = false, CreateNoWindow = true };
            var p = Process.Start(psi);
            if (p == null) return;
            formOpen = true;
            Task.Run(() =>
            {
                try
                {
                    p.WaitForExit();
                    if (!File.Exists(outPath)) return;
                    using var doc = JsonDocument.Parse(File.ReadAllText(outPath, Encoding.UTF8));
                    var root = doc.RootElement;
                    var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                    var files = new List<string>();
                    if (root.TryGetProperty("files", out var fs))
                    {
                        if (fs.ValueKind == JsonValueKind.Array) foreach (var f in fs.EnumerateArray()) { var s = f.GetString(); if (!string.IsNullOrEmpty(s)) files.Add(s); }
                        else if (fs.ValueKind == JsonValueKind.String) files.Add(fs.GetString());
                    }
                    formResults.Enqueue((text, files.ToArray()));
                }
                catch (Exception ex) { formErrors.Enqueue(ex.Message); }
                finally { formOpen = false; }
            });
        }
        catch (Exception ex)
        {
            formOpen = false;
            Logger.Exception(ex, nameof(BugReport));
        }
    }

    /// <summary>毎フレーム呼ぶ: 入力画面で送られた報告を、本編の側 (メインのスレッド) で残して送る</summary>
    public static void Pump()
    {
        while (formErrors.TryDequeue(out var err)) Logger.Warn($"報告の画面の読み取りに失敗: {err}", nameof(BugReport));
        while (formResults.TryDequeue(out var r))
        {
            try
            {
                var text = r.text.Trim();
                if (text.Length > MaxLengthForm) text = text[..MaxLengthForm];
                if (text.Length == 0 && r.files.Length == 0) continue;
                Submit(PlayerControl.LocalPlayer, text, r.files);
                var note = DiscordWebhook.HasUrl ? GetString("Report.SentWebhook") : GetString("Report.Saved");
                if (AmongUsClient.Instance && AmongUsClient.Instance.AmConnected) HostNote.Post(note);
                else Logger.SendInGame(note);
            }
            catch (Exception ex) { Logger.Exception(ex, nameof(BugReport)); }
        }
    }

    static string Q(string key) => GetString(key).Replace("'", "''");

    static string FormScript() => $@"param([string]$Out)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$font = New-Object System.Drawing.Font('Yu Gothic UI', 10)
$f = New-Object System.Windows.Forms.Form
$f.Text = '{Q("Report.FormTitle")}'
$f.ClientSize = New-Object System.Drawing.Size(560, 470)
$f.StartPosition = 'CenterScreen'
$f.TopMost = $true
$f.Font = $font
$f.FormBorderStyle = 'FixedDialog'
$f.MaximizeBox = $false

$l1 = New-Object System.Windows.Forms.Label
$l1.Text = '{Q("Report.FormText")}'
$l1.Location = '12,10'; $l1.Size = '536,22'
$f.Controls.Add($l1)

$t = New-Object System.Windows.Forms.TextBox
$t.Multiline = $true; $t.ScrollBars = 'Vertical'; $t.AcceptsReturn = $true
$t.Location = '12,36'; $t.Size = '536,200'; $t.MaxLength = {MaxLengthForm}
$f.Controls.Add($t)

$l2 = New-Object System.Windows.Forms.Label
$l2.Text = '{Q("Report.FormFiles")}'
$l2.Location = '12,246'; $l2.Size = '536,22'
$f.Controls.Add($l2)

$lb = New-Object System.Windows.Forms.ListBox
$lb.Location = '12,270'; $lb.Size = '536,110'; $lb.SelectionMode = 'MultiExtended'; $lb.HorizontalScrollbar = $true
$f.Controls.Add($lb)

$add = New-Object System.Windows.Forms.Button
$add.Text = '{Q("Report.FormAdd")}'; $add.Location = '12,388'; $add.Size = '170,32'
$add.Add_Click({{
    $d = New-Object System.Windows.Forms.OpenFileDialog
    $d.Multiselect = $true
    $d.Filter = '{Q("Report.FormFilter")}|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.mp4;*.mov;*.webm;*.mkv;*.avi|*.*|*.*'
    if ($d.ShowDialog($f) -eq 'OK') {{ foreach ($n in $d.FileNames) {{ if (-not $lb.Items.Contains($n)) {{ [void]$lb.Items.Add($n) }} }} }}
}})
$f.Controls.Add($add)

$paste = New-Object System.Windows.Forms.Button
$paste.Text = '{Q("Report.FormPaste")}'; $paste.Location = '190,388'; $paste.Size = '190,32'
$paste.Add_Click({{
    if ([System.Windows.Forms.Clipboard]::ContainsImage()) {{
        $img = [System.Windows.Forms.Clipboard]::GetImage()
        $p = Join-Path ([System.IO.Path]::GetTempPath()) ('tohl_report_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.png')
        $img.Save($p, [System.Drawing.Imaging.ImageFormat]::Png)
        [void]$lb.Items.Add($p)
    }} else {{ [void][System.Windows.Forms.MessageBox]::Show($f, '{Q("Report.FormNoImage")}') }}
}})
$f.Controls.Add($paste)

$rm = New-Object System.Windows.Forms.Button
$rm.Text = '{Q("Report.FormRemove")}'; $rm.Location = '388,388'; $rm.Size = '160,32'
$rm.Add_Click({{ foreach ($s in @($lb.SelectedItems)) {{ $lb.Items.Remove($s) }} }})
$f.Controls.Add($rm)

$ok = New-Object System.Windows.Forms.Button
$ok.Text = '{Q("Report.FormSend")}'; $ok.Location = '336,430'; $ok.Size = '100,32'
$ok.Add_Click({{
    if ($t.Text.Trim().Length -eq 0 -and $lb.Items.Count -eq 0) {{ [void][System.Windows.Forms.MessageBox]::Show($f, '{Q("Report.FormEmpty")}'); return }}
    $json = ConvertTo-Json -Compress -InputObject @{{ text = $t.Text; files = [string[]]@($lb.Items) }}
    [System.IO.File]::WriteAllText($Out, $json, (New-Object System.Text.UTF8Encoding($false)))
    $f.Close()
}})
$f.Controls.Add($ok)

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = '{Q("Report.FormCancel")}'; $cancel.Location = '448,430'; $cancel.Size = '100,32'
$cancel.Add_Click({{ $f.Close() }})
$f.Controls.Add($cancel)

$f.Add_Shown({{ $f.Activate(); $t.Focus() }})
[void]$f.ShowDialog()
";
}
