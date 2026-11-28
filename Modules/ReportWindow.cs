using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// ゲームの中で書いて送る「バグ・要望の報告」の窓。ロビーと試合中のボタン (役職辞典の隣)・ホストの操作盤から開く。
/// 見た目は Google の入力欄 (枠の線・上に浮く見出し・字数・青い光) に寄せ、役職辞典と同じく IMGUI で一から描く。
/// 字の入力は自前: どこにでもカーソルを置いて書き足し・消せる (←→↑↓・Home/End・Backspace/Delete の押しっぱなし)、
/// 日本語の変換中の字・Ctrl+A/C/X/V/Z・Enter で改行・Ctrl+Enter で送る。
/// 画像・動画は「画面を撮る」「クリップボードから貼る」「最近の画像・動画から選ぶ」で、ゲームを離れずに付けられる
/// </summary>
public class ReportWindow : MonoBehaviour
{
    public ReportWindow(IntPtr ptr) : base(ptr) { }

    public static ReportWindow Instance;
    static bool open;
    public static bool IsOpen => open;
    /// <summary>入力欄に字を打っている間 (ゲームのキー操作を止める)</summary>
    public static bool Typing => open && focused;

    public const int MaxLen = 2000;
    public const int MaxFiles = 5;
    const long MaxBytes = 9_500_000;

    public static void Toggle() => SetOpen(!open);
    public static void Open() => SetOpen(true);
    public static void SetOpen(bool value)
    {
        open = value;
        if (Instance != null) Instance.enabled = value;
        if (value)
        {
            pickerOpen = false;
            closeAt = -1f;
            SetFocus(true);
            caretMoved = true;
        }
        else SetFocus(false);
    }

    public void Awake()
    {
        Instance = this;
        useGUILayout = false;
        enabled = false;
    }

    // ---- 書いている中身 (閉じても残す。送ったら消す) ----
    static string text = "";
    static int cursor;
    static bool selAll;
    static bool isBug = true;
    static readonly List<string> files = new();
    static readonly List<(string text, int cursor)> undo = new();
    static float lastUndoAt;

    static bool focused, wasComposing, caretMoved;
    static float blinkStart;

    static void SetFocus(bool v)
    {
        focused = v;
        Input.imeCompositionMode = v ? IMECompositionMode.On : IMECompositionMode.Auto;
        if (v) blinkStart = Time.unscaledTime;
    }

    // ---- お知らせ (窓の下に少しの間だけ出す) ----
    static string snack = "";
    static bool snackBad;
    static float snackUntil;
    static float closeAt = -1f;
    static void Snack(string s, bool bad = false) { snack = s; snackBad = bad; snackUntil = Time.unscaledTime + 3.5f; }

    // =====================================================================
    // 字の入力
    // =====================================================================

    static void Snapshot(bool force = false)
    {
        var now = Time.unscaledTime;
        var quick = now - lastUndoAt < 0.8f;
        lastUndoAt = now;
        if (!force && quick && undo.Count > 0) return;
        if (undo.Count > 0 && undo[^1].text == text) return;
        undo.Add((text, cursor));
        if (undo.Count > 100) undo.RemoveAt(0);
    }

    static void Changed()
    {
        caretMoved = true;
        blinkStart = Time.unscaledTime;
    }

    static void Insert(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        s = s.Replace("\r\n", "\n").Replace('\r', '\n');
        s = new string(s.Where(c => c == '\n' || !char.IsControl(c)).ToArray());
        if (s.Length == 0) return;
        Snapshot(s.Length > 1);
        if (selAll) { text = ""; cursor = 0; selAll = false; }
        var room = Limit - text.Length;
        if (room <= 0) { Snack(string.Format(GetString("Report.WinFull"), Limit), true); return; }
        if (s.Length > room) s = s[..room];
        cursor = Mathf.Clamp(cursor, 0, text.Length);
        text = text.Insert(cursor, s);
        cursor += s.Length;
        Changed();
    }

    /// <summary>DevBridge から: 入力欄へ字を入れる (確かめる用)</summary>
    public static void DevInsert(string s) { if (!focused) SetFocus(true); Insert(s); }
    /// <summary>DevBridge から: ボタンの代わり (shot / pick / clip / send / req / bug)</summary>
    public static string DevAct(string act)
    {
        switch (act)
        {
            case "shot": SetFocus(false); shotState = 1; break;
            case "pick": OpenPicker(); break;
            case "pickfirst": if (recent.Count > 0) AddFile(recent[0].path); break;
            case "clip": PasteClipboardImage(); break;
            case "send": Send(); break;
            case "req": isBug = false; break;
            case "bug": isBug = true; break;
        }
        return $"files={files.Count} [{string.Join(", ", files.Select(Path.GetFileName))}] recent={recent.Count} snack={snack}";
    }

    static void Delete(bool back)
    {
        if (selAll)
        {
            Snapshot(true);
            text = ""; cursor = 0; selAll = false;
            Changed();
            return;
        }
        if (back)
        {
            if (cursor <= 0) return;
            Snapshot();
            var n = cursor >= 2 && char.IsLowSurrogate(text[cursor - 1]) && char.IsHighSurrogate(text[cursor - 2]) ? 2 : 1;
            text = text.Remove(cursor - n, n);
            cursor -= n;
        }
        else
        {
            if (cursor >= text.Length) return;
            Snapshot();
            var n = cursor + 1 < text.Length && char.IsHighSurrogate(text[cursor]) ? 2 : 1;
            text = text.Remove(cursor, n);
        }
        Changed();
    }

    static void Move(int d)
    {
        selAll = false;
        var c = cursor + d;
        // 絵文字など 2 つで 1 字の物の間には止まらない
        if (c > 0 && c < text.Length && char.IsLowSurrogate(text[c]) && char.IsHighSurrogate(text[c - 1])) c += d;
        cursor = Mathf.Clamp(c, 0, text.Length);
        Changed();
    }

    static int LineStart(int i) => i <= 0 ? 0 : text.LastIndexOf('\n', i - 1) + 1;
    static int LineEnd(int i) { var n = text.IndexOf('\n', Mathf.Clamp(i, 0, text.Length)); return n < 0 ? text.Length : n; }

    static void MoveLine(int dir)
    {
        selAll = false;
        var ls = LineStart(cursor);
        var col = cursor - ls;
        if (dir < 0)
        {
            if (ls == 0) cursor = 0;
            else { var ps = LineStart(ls - 1); cursor = Mathf.Min(ps + col, ls - 1); }
        }
        else
        {
            var le = LineEnd(cursor);
            if (le >= text.Length) cursor = text.Length;
            else { var ns = le + 1; cursor = Mathf.Min(ns + col, LineEnd(ns)); }
        }
        Changed();
    }

    static void Undo()
    {
        if (undo.Count == 0) return;
        var (t, c) = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        text = t;
        cursor = Mathf.Clamp(c, 0, text.Length);
        selAll = false;
        lastUndoAt = 0f;
        Changed();
    }

    // 押しっぱなしで続けて動かす・消す
    static KeyCode repeatKey = KeyCode.None;
    static float repeatTimer;
    static void Repeat(KeyCode key, Action act)
    {
        if (Input.GetKeyDown(key)) { act(); repeatKey = key; repeatTimer = 0f; return; }
        if (repeatKey != key) return;
        if (!Input.GetKey(key)) { repeatKey = KeyCode.None; return; }
        repeatTimer += Time.unscaledDeltaTime;
        if (repeatTimer >= 0.42f) { repeatTimer -= 0.04f; act(); }
    }

    static void EditUpdate()
    {
        var comp = Input.compositionString ?? "";
        var composing = comp.Length > 0;
        var ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        var enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        // 変換を決めた Enter で改行しないよう、前のフレームまで変換中だった時の Enter は使わない
        if (enter && !composing && !wasComposing)
        {
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            // やりとりは Enter で送る (Shift+Enter で改行)。報告は Ctrl+Enter で送る
            if (ctrl || chatMode && !shift) { Send(); wasComposing = composing; return; }
            Insert("\n");
        }
        if (ctrl)
        {
            if (Input.GetKeyDown(KeyCode.A)) selAll = text.Length > 0;
            if (Input.GetKeyDown(KeyCode.C) && selAll) GUIUtility.systemCopyBuffer = text;
            if (Input.GetKeyDown(KeyCode.X) && selAll) { GUIUtility.systemCopyBuffer = text; Delete(true); }
            if (Input.GetKeyDown(KeyCode.V)) Insert(GUIUtility.systemCopyBuffer ?? "");
            if (Input.GetKeyDown(KeyCode.Z)) Undo();
        }
        else
        {
            foreach (var c in Input.inputString ?? "")
            {
                if (c == '\b' || c == '\n' || c == '\r' || char.IsControl(c)) continue;
                Insert(c.ToString());
            }
        }
        if (!composing)
        {
            Repeat(KeyCode.LeftArrow, () => { if (selAll) { selAll = false; cursor = 0; Changed(); } else Move(-1); });
            Repeat(KeyCode.RightArrow, () => { if (selAll) { selAll = false; cursor = text.Length; Changed(); } else Move(1); });
            Repeat(KeyCode.UpArrow, () => MoveLine(-1));
            Repeat(KeyCode.DownArrow, () => MoveLine(1));
            Repeat(KeyCode.Backspace, () => Delete(true));
            Repeat(KeyCode.Delete, () => Delete(false));
            if (Input.GetKeyDown(KeyCode.Home)) { selAll = false; cursor = ctrl ? 0 : LineStart(cursor); Changed(); }
            if (Input.GetKeyDown(KeyCode.End)) { selAll = false; cursor = ctrl ? text.Length : LineEnd(cursor); Changed(); }
        }
        else repeatKey = KeyCode.None;
        wasComposing = composing;
    }

    public void Update()
    {
        if (!open) return;
        RelayUpdate();
        ReportChat.Pump();
        if (BugReport.SendsToDeveloper)
        {
            var now = Time.unscaledTime;
            ReportChat.RefreshAllSometimes(now, 45f);
            if (chatMode && chatSel != null && now >= nextPollAt) { nextPollAt = now + 6f; ReportChat.Refresh(chatSel); }
        }
        if (closeAt > 0f && Time.unscaledTime >= closeAt) { closeAt = -1f; SetOpen(false); return; }
        ShotUpdate();
        PumpBackground();
        LoadThumbs();

        var composing = !string.IsNullOrEmpty(Input.compositionString);
        if (Input.GetKeyDown(KeyCode.Escape) && !composing)
        {
            if (pickerOpen) pickerOpen = false;
            else if (focused) SetFocus(false);
            else SetOpen(false);
            return;
        }
        if (pickerOpen || shotState > 0) return;
        if (focused) EditUpdate();
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) SetFocus(true);
    }

    [HarmonyLib.HarmonyPatch(typeof(PassiveButtonManager), nameof(PassiveButtonManager.Update))]
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    static class BlockButtonsPatch
    {
        public static bool Prefix(PassiveButtonManager __instance)
        {
            if (!open) return true;
            __instance.HandleMouseOut();
            return false;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(KeyboardJoystick), nameof(KeyboardJoystick.Update))]
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    static class MovePatch
    {
        // 開いている間は字を打つので、自分のキャラは動かさない
        public static bool Prefix() => !open;
    }

    // =====================================================================
    // 送る
    // =====================================================================

    static bool CanSend => text.Trim().Length > 0;

    static void Send()
    {
        if (chatMode) { SendChat(); return; }
        if (closeAt > 0f) return;
        if (!CanSend) { Snack(GetString("Report.WinEmpty"), true); return; }
        if (BugReport.LocalFriendCode().Length == 0) { Snack(GetString("Report.NoFriendCode"), true); return; }
        if (relaying != null) return;
        if (BugReport.SendFromWindow(text.Trim(), files.ToArray(), isBug, out var message, out var relay))
        {
            // 開発者へ送る時は、中継の返事 (1 日の回数の上限など) を待ってから閉じる
            if (relay != null) { relaying = relay; relayMessage = message; Snack(GetString("Report.Sending")); return; }
            Sent(message);
        }
        else Snack(message, true);
    }

    static System.Threading.Tasks.Task<string> relaying;
    static string relayMessage;

    static void Sent(string message)
    {
        text = ""; cursor = 0; selAll = false;
        files.Clear();
        undo.Clear();
        SetFocus(false);
        Snack(message);
        closeAt = Time.unscaledTime + 2.2f;
        try { HostNote.Post(message); } catch { }
    }

    static void RelayUpdate()
    {
        if (relaying == null || !relaying.IsCompleted) return;
        string error = relaying.IsFaulted ? "network" : relaying.Result;
        relaying = null;
        if (error == null) Sent(relayMessage);
        else Snack(BugReport.RelayMessage(error), true);
    }

    // =====================================================================
    // 付けるファイル
    // =====================================================================

    static string ShotDir => Path.Combine(Environment.CurrentDirectory, "TOHL_DATA", "Reports", "Shots");
    static readonly HashSet<string> imageExt = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    static readonly HashSet<string> videoExt = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".webm", ".mkv", ".avi" };
    static bool IsMedia(string p) { var x = Path.GetExtension(p); return imageExt.Contains(x) || videoExt.Contains(x); }

    static long SizeOf(string p) { try { return new FileInfo(p).Length; } catch { return 0; } }
    static long TotalBytes() => files.Sum(SizeOf);

    static string Bytes(long b) => b >= 1_000_000 ? $"{b / 1_000_000f:0.0}MB" : $"{Math.Max(1, b / 1000)}KB";

    static bool AddFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        if (files.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase))) return true;
        if (files.Count >= MaxFiles) { Snack(string.Format(GetString("Report.WinTooMany"), MaxFiles), true); return false; }
        files.Add(path);
        if (TotalBytes() > MaxBytes) Snack(string.Format(GetString("Report.WinTooBig"), Bytes(TotalBytes())), true);
        return true;
    }

    // 画面を撮る: 窓を隠したフレームを撮ってから、窓を戻す
    static int shotState; // 0 何もしない / 1 次のフレームで撮る / 2 書き終わるのを待つ
    static string shotPath;
    static float shotDeadline;
    static void ShotUpdate()
    {
        if (shotState == 1)
        {
            try
            {
                Directory.CreateDirectory(ShotDir);
                shotPath = Path.Combine(ShotDir, $"shot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                ScreenCapture.CaptureScreenshot(shotPath);
                shotState = 2;
                shotDeadline = Time.unscaledTime + 6f;
            }
            catch (Exception ex)
            {
                shotState = 0;
                Snack(GetString("Report.WinShotFail"), true);
                Logger.Warn($"画面を撮れなかった: {ex.Message}", nameof(ReportWindow));
            }
        }
        else if (shotState == 2)
        {
            if (File.Exists(shotPath) && SizeOf(shotPath) > 0)
            {
                shotState = 0;
                if (AddFile(shotPath)) Snack(GetString("Report.WinShotDone"));
            }
            else if (Time.unscaledTime > shotDeadline)
            {
                shotState = 0;
                Snack(GetString("Report.WinShotFail"), true);
            }
        }
    }

    // クリップボードの画像は Unity から読めないので、見えない PowerShell に png へ書き出させる
    static volatile bool clipBusy;
    static readonly ConcurrentQueue<(string path, bool ok)> clipResults = new();
    static void PasteClipboardImage()
    {
        if (clipBusy) return;
        clipBusy = true;
        string path;
        try
        {
            Directory.CreateDirectory(ShotDir);
            path = Path.Combine(ShotDir, $"clip_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        }
        catch { clipBusy = false; return; }
        var cmd = "Add-Type -AssemblyName System.Windows.Forms; Add-Type -AssemblyName System.Drawing; "
            + "$i = [System.Windows.Forms.Clipboard]::GetImage(); "
            + "if ($i -eq $null) { $f = [System.Windows.Forms.Clipboard]::GetFileDropList(); if ($f.Count -gt 0) { [Console]::Out.Write('FILES:' + ($f -join '|')); exit 0 }; exit 3 }; "
            + $"$i.Save('{path.Replace("'", "''")}', [System.Drawing.Imaging.ImageFormat]::Png); exit 0";
        Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"{cmd.Replace("\"", "\\\"")}\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit(15000);
                if (output.StartsWith("FILES:"))
                {
                    // エクスプローラーでコピーしたファイル: 画像・動画だけを付ける
                    foreach (var f in output[6..].Split('|')) if (IsMedia(f)) clipResults.Enqueue((f.Trim(), true));
                    clipResults.Enqueue((null, true));
                }
                else clipResults.Enqueue((path, p != null && p.ExitCode == 0 && File.Exists(path)));
            }
            catch { clipResults.Enqueue((path, false)); }
            finally { clipBusy = false; }
        });
    }

    static void PumpBackground()
    {
        while (clipResults.TryDequeue(out var r))
        {
            if (r.path == null) continue;
            if (r.ok && AddFile(r.path)) Snack(GetString("Report.WinClipDone"));
            else if (!r.ok) Snack(GetString("Report.FormNoImage"), true);
        }
        if (recentTask != null && recentTask.IsCompleted)
        {
            recent = recentTask.Status == TaskStatus.RanToCompletion ? recentTask.Result : new();
            recentTask = null;
        }
    }

    // 最近の画像・動画 (スクリーンショット・キャプチャ・デスクトップなどの直下)
    static Task<List<(string path, DateTime time, long size)>> recentTask;
    static List<(string path, DateTime time, long size)> recent = new();
    static bool pickerOpen;
    static float pickerScroll;

    static void OpenPicker()
    {
        pickerOpen = true;
        pickerScroll = 0f;
        SetFocus(false);
        if (recentTask != null) return;
        var shotDir = ShotDir;
        recentTask = Task.Run(() =>
        {
            var dirs = new List<string>();
            void Add(string d) { if (!string.IsNullOrEmpty(d) && Directory.Exists(d) && !dirs.Contains(d, StringComparer.OrdinalIgnoreCase)) dirs.Add(d); }
            var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            var vids = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add(shotDir);
            foreach (var root in new[] { pics, vids, Path.Combine(home, "OneDrive", "Pictures"), Path.Combine(home, "OneDrive", "画像") })
            {
                if (string.IsNullOrEmpty(root)) continue;
                Add(Path.Combine(root, "Screenshots"));
                Add(Path.Combine(root, "スクリーンショット"));
                Add(Path.Combine(root, "Captures"));
                Add(Path.Combine(root, "キャプチャ"));
                Add(root);
            }
            Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Add(Path.Combine(home, "Downloads"));
            var list = new List<(string, DateTime, long)>();
            foreach (var d in dirs)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(d))
                    {
                        if (!IsMedia(f)) continue;
                        try { var fi = new FileInfo(f); list.Add((f, fi.LastWriteTime, fi.Length)); } catch { }
                    }
                }
                catch { }
            }
            return list.OrderByDescending(x => x.Item2).Take(60).ToList();
        });
    }

    // 小さな見本の絵 (読み込みは 1 フレームに 1 枚・縮めてから持つ)
    static readonly Dictionary<string, Texture2D> thumbs = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> thumbFailed = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> thumbWanted = new();

    static Texture2D Thumb(string path)
    {
        if (thumbs.TryGetValue(path, out var t) && t != null) return t;
        if (!thumbFailed.Contains(path) && imageExt.Contains(Path.GetExtension(path)) && !thumbWanted.Contains(path)) thumbWanted.Add(path);
        return null;
    }

    static void LoadThumbs()
    {
        if (thumbWanted.Count == 0) return;
        var path = thumbWanted[0];
        thumbWanted.RemoveAt(0);
        Texture2D big = null;
        try
        {
            if (SizeOf(path) > 25_000_000) throw new IOException("too large");
            big = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(big, File.ReadAllBytes(path), false)) throw new InvalidDataException();
            const int tw = 256;
            var th = Mathf.Clamp(Mathf.RoundToInt(tw * big.height / (float)Mathf.Max(1, big.width)), 16, 512);
            var rt = RenderTexture.GetTemporary(tw, th, 0);
            Graphics.Blit(big, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(tw, th, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            small.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            small.Apply(false, true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            thumbs[path] = small;
        }
        catch { thumbFailed.Add(path); }
        finally { if (big != null) Destroy(big); }
    }

    // =====================================================================
    // 描く (Google の暗い画面の色)
    // =====================================================================

    static readonly Color cBg = Hex(0x202124), cSurface = Hex(0x2d2e31), cLine = Hex(0x5f6368), cLineHover = Hex(0x9aa0a6),
        cBlue = Hex(0x8ab4f8), cBlueHover = Hex(0xaecbfa), cText = Hex(0xe8eaed), cSub = Hex(0x9aa0a6), cRed = Hex(0xf28b82),
        cChipOn = Hex(0x394457), cHover = Hex(0x35363a), cDisabled = Hex(0x3c4043), cDark = Hex(0x202124);
    static Color Hex(int v) => new(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);

    static float sw, sh, scale = -1f;
    static float S => sw / 1920f;
    static int F(float px) => Mathf.Max(9, Mathf.RoundToInt(px * S));
    static GUIStyle sTitle, sSub, sSubRight, sLabelSmall, sPlaceholder, sText, sChip, sBtn, sBtnDark, sTileName, sTileSub, sSnack, sCenter;
    static readonly Dictionary<(Color, int), GUIStyle> fills = new();

    static void Fill(Rect r, Color c, float radius)
    {
        if (Event.current.type != EventType.Repaint) return;
        var px = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(radius, Mathf.Min(r.width, r.height) / 2f)));
        if (!fills.TryGetValue((c, px), out var st) || st == null)
        {
            st = new GUIStyle { normal = { background = HostPanel.RoundTex(c, px) }, border = HostPanel.Border(px) };
            fills[(c, px)] = st;
        }
        GUI.Box(r, GUIContent.none, st);
    }

    // 枠の線 (外を線の色・内を中の色で塗る)
    static void Outline(Rect r, Color line, Color inside, float radius, float width)
    {
        Fill(r, line, radius);
        Fill(new Rect(r.x + width, r.y + width, r.width - width * 2f, r.height - width * 2f), inside, Mathf.Max(1f, radius - width));
    }

    static GUIStyle Style(int size, Color color, TextAnchor anchor, bool bold = false, bool wrap = false, bool rich = false)
        => new() { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = anchor, wordWrap = wrap, richText = rich, clipping = TextClipping.Clip, normal = { textColor = color } };

    static void BuildStyles()
    {
        scale = S;
        sTitle = Style(F(30), cText, TextAnchor.MiddleLeft);
        sSub = Style(F(17), cSub, TextAnchor.MiddleLeft, wrap: true);
        sSubRight = Style(F(16), cSub, TextAnchor.MiddleRight);
        sLabelSmall = Style(F(16), cSub, TextAnchor.MiddleLeft);
        sPlaceholder = Style(F(22), cSub, TextAnchor.UpperLeft);
        sText = Style(F(22), cText, TextAnchor.UpperLeft, wrap: true);
        sText.padding = new RectOffset();
        sChip = Style(F(18), cText, TextAnchor.MiddleCenter, bold: true);
        sBtn = Style(F(18), cBlue, TextAnchor.MiddleCenter, bold: true);
        sBtnDark = Style(F(19), cDark, TextAnchor.MiddleCenter, bold: true);
        sTileName = Style(F(15), cText, TextAnchor.MiddleLeft);
        sTileSub = Style(F(13), cSub, TextAnchor.MiddleLeft);
        sSnack = Style(F(18), cDark, TextAnchor.MiddleCenter, wrap: true);
        sCenter = Style(F(18), cSub, TextAnchor.MiddleCenter, wrap: true);
    }

    static Event ev;
    static bool live; // このフレームで押す・打つを受け付けるか (重なった画面がある時は下を止める)

    static bool Hover(Rect r) => live && r.Contains(ev.mousePosition);
    static bool Click(Rect r)
    {
        if (!live || ev.type != EventType.MouseDown || ev.button != 0 || !r.Contains(ev.mousePosition)) return false;
        ev.Use();
        return true;
    }

    static float Width(GUIStyle st, string s) => st.CalcSize(new GUIContent(s)).x;

    /// <summary>丸い押しボタン。kind 0: 枠の線 / 1: 青く塗る / 2: 字だけ</summary>
    static bool Button(Rect r, string label, int kind, bool enabled = true)
    {
        var hover = enabled && Hover(r);
        switch (kind)
        {
            case 1:
                Fill(r, !enabled ? cDisabled : hover ? cBlueHover : cBlue, r.height / 2f);
                var st = enabled ? sBtnDark : sChip;
                var old = st.normal.textColor;
                if (!enabled) st.normal.textColor = cSub;
                GUI.Label(r, label, st);
                st.normal.textColor = old;
                break;
            case 0:
                Outline(r, hover ? cLineHover : cLine, hover ? cHover : cBg, r.height / 2f, Mathf.Max(1f, S));
                GUI.Label(r, label, sBtn);
                break;
            default:
                if (hover) Fill(r, cHover, r.height / 2f);
                GUI.Label(r, label, sBtn);
                break;
        }
        return enabled && Click(r);
    }

    static float wx = -1f, wy, ww, wh;
    static bool dragging;
    static float dragDx, dragDy;
    static float fieldScroll;
    static bool caretFallback;

    public void OnGUI()
    {
        sw = Screen.width; sh = Screen.height;
        if (!open) { enabled = false; return; }
        if (shotState > 0) return; // 撮っている間は窓を出さない
        if (!Mathf.Approximately(scale, S)) BuildStyles();
        ev = Event.current;
        var type = ev.type;

        ww = Mathf.Min(sw * 0.92f, 1040f * S);
        wh = Mathf.Min(sh * 0.92f, 880f * S);
        if (wx < 0 || type == EventType.Layout && (wx + ww > sw || wy + wh > sh)) { wx = (sw - ww) / 2f; wy = (sh - wh) / 2f; }

        var pad = 32f * S;
        var titleH = 64f * S;
        live = !pickerOpen;
        if (live)
        {
            if (type == EventType.MouseDown && ev.button == 0 && new Rect(wx, wy, ww - 90f * S, titleH + pad * 0.5f).Contains(ev.mousePosition)) { dragging = true; dragDx = ev.mousePosition.x - wx; dragDy = ev.mousePosition.y - wy; ev.Use(); }
            else if (type == EventType.MouseDrag && dragging) { wx = ev.mousePosition.x - dragDx; wy = ev.mousePosition.y - dragDy; ev.Use(); }
            else if (type == EventType.MouseUp) dragging = false;
        }
        wx = Mathf.Clamp(wx, 0f, Mathf.Max(0f, sw - ww));
        wy = Mathf.Clamp(wy, 0f, Mathf.Max(0f, sh - wh));

        // 影と窓
        Fill(new Rect(wx - 6f * S, wy + 2f * S, ww + 12f * S, wh + 12f * S), new Color(0f, 0f, 0f, 0.35f), 30f * S);
        Fill(new Rect(wx, wy, ww, wh), cBg, 24f * S);

        // 見出し
        var x0 = wx + pad; var cw = ww - pad * 2f;
        var y = wy + pad * 0.75f;
        GUI.Label(new Rect(x0, y, cw - 60f * S, titleH * 0.75f), GetString("Report.WinTitle"), sTitle);
        var closeR = new Rect(wx + ww - pad - 44f * S, y + 2f * S, 44f * S, 44f * S);
        if (Hover(closeR)) Fill(closeR, cHover, 22f * S);
        GUI.Label(closeR, "×", Style(F(30), cSub, TextAnchor.MiddleCenter));
        if (Click(closeR)) { SetOpen(false); return; }
        y += titleH * 0.75f;
        var note = BugReport.SendsToDeveloper ? GetString("Report.DevNote") : GetString("Report.WinLocalOnly");
        GUI.Label(new Rect(x0, y, cw, 30f * S), note, sSub);
        y += 44f * S;

        // 書く / やりとり (開発者へ送る時だけ)
        if (BugReport.SendsToDeveloper)
        {
            var tabH = 46f * S;
            var tx = x0;
            for (int i = 0; i < 3; i++)
            {
                bool chat = i == 1;
                var unread = chat ? ReportChat.TotalUnread : 0;
                var label = GetString(i switch { 1 => "Report.TabChat", 2 => "Report.TabList", _ => "Report.TabWrite" }) + (unread > 0 ? $"  ({unread})" : "");
                var r = new Rect(tx, y, Width(sChip, label) + 48f * S, tabH);
                bool on = tab == i;
                if (on) Fill(r, cChipOn, 12f * S);
                else if (Hover(r)) Fill(r, cHover, 12f * S);
                var st = sChip; var old = st.normal.textColor;
                st.normal.textColor = on ? Hex(0xd2e3fc) : unread > 0 ? cRed : cSub;
                GUI.Label(r, label, st);
                st.normal.textColor = old;
                if (on) Fill(new Rect(r.x + 12f * S, r.yMax - 4f * S, r.width - 24f * S, 4f * S), cBlue, 2f * S);
                if (Click(r) && !on) SetTab(i);
                tx = r.xMax + 8f * S;
            }
            y += tabH + 18f * S;
        }

        if (tab == 2)
        {
            DrawList(new Rect(x0, y, cw, wy + wh - pad * 0.8f - y));
            DrawSnack(cw, wy + wh - pad * 0.8f);
            return;
        }
        if (chatMode)
        {
            DrawChat(new Rect(x0, y, cw, wy + wh - pad * 0.8f - y));
            if (live && ev.type == EventType.MouseDown && ev.button == 0) SetFocus(false);
            DrawSnack(cw, wy + wh - pad * 0.8f - 120f * S);
            return;
        }

        // バグ / 要望 (Google の選ぶチップ)
        var chipH = 42f * S;
        var cx = x0;
        foreach (var bug in new[] { true, false })
        {
            var on = isBug == bug;
            var label = (on ? "✓  " : "") + GetString(bug ? "Report.KindBug" : "Report.FormRequest");
            var r = new Rect(cx, y, Width(sChip, label) + 40f * S, chipH);
            if (on) Fill(r, Hover(r) ? Hex(0x41506a) : cChipOn, chipH / 2f);
            else Outline(r, cLine, Hover(r) ? cHover : cBg, chipH / 2f, Mathf.Max(1f, S));
            var st = sChip;
            var old = st.normal.textColor;
            st.normal.textColor = on ? Hex(0xd2e3fc) : cText;
            GUI.Label(r, label, st);
            st.normal.textColor = old;
            if (Click(r) && isBug != bug) { isBug = bug; caretMoved = true; }
            cx = r.xMax + 12f * S;
        }
        GUI.Label(new Rect(cx + 8f * S, y, wx + ww - pad - cx - 8f * S, chipH), isBug ? GetString("Report.WinLog") : "", sLabelSmall);
        y += chipH + 30f * S;

        // 下から: ボタンの列・付けるファイル・入力欄の下の案内
        var footH = 52f * S;
        var footY = wy + wh - pad * 0.8f - footH;
        var fileChipH = 64f * S;
        var fileChipW = (cw - 24f * S) / 3f;
        var fileRows = files.Count == 0 ? 0 : (files.Count + 2) / 3;
        var filesH = 30f * S + 10f * S + 44f * S + (fileRows > 0 ? 14f * S + fileRows * (fileChipH + 10f * S) : 0f);
        var filesY = footY - 24f * S - filesH;
        var helperH = 28f * S;
        var fieldY = y;
        var fieldH = Mathf.Max(110f * S, filesY - 22f * S - helperH - fieldY);

        DrawField(new Rect(x0, fieldY, cw, fieldH));
        var hy = fieldY + fieldH + 6f * S;
        GUI.Label(new Rect(x0 + 16f * S, hy, cw * 0.7f, helperH), GetString("Report.WinHint"), sLabelSmall);
        var counter = $"{text.Length} / {Limit}";
        var sc = sSubRight; var oc = sc.normal.textColor;
        if (text.Length >= Limit) sc.normal.textColor = cRed;
        GUI.Label(new Rect(x0, hy, cw - 16f * S, helperH), counter, sc);
        sc.normal.textColor = oc;

        DrawFiles(new Rect(x0, filesY, cw, filesH), fileChipW, fileChipH);

        // 一番下: フレンドコード・やめる・送る
        var fc = BugReport.LocalFriendCode();
        var fcText = fc.Length > 0 ? string.Format(GetString("Report.WinFc"), fc) : GetString("Report.NoFriendCode");
        var fs = sLabelSmall; var ofc = fs.normal.textColor;
        if (fc.Length == 0) fs.normal.textColor = cRed;
        GUI.Label(new Rect(x0, footY, cw * 0.55f, footH), fcText, fs);
        fs.normal.textColor = ofc;
        var sendW = Mathf.Max(150f * S, Width(sBtnDark, GetString("Report.FormSend")) + 64f * S);
        var sendR = new Rect(x0 + cw - sendW, footY, sendW, footH);
        if (Button(sendR, GetString("Report.FormSend"), 1, CanSend && fc.Length > 0 && closeAt < 0f)) Send();
        var cancelW = Width(sBtn, GetString("Report.FormCancel")) + 48f * S;
        if (Button(new Rect(sendR.x - 12f * S - cancelW, footY, cancelW, footH), GetString("Report.FormCancel"), 2)) { SetOpen(false); return; }

        // 窓の中の何も無い所を押したら入力をやめる
        if (live && ev.type == EventType.MouseDown && ev.button == 0) SetFocus(false);

        if (pickerOpen) DrawPicker();

        DrawSnack(cw, footY);
    }

    // お知らせ
    static void DrawSnack(float cw, float footY)
    {
        if (Time.unscaledTime >= snackUntil || snack.Length == 0) return;
        var sw2 = Mathf.Min(cw, Width(sSnack, snack) + 64f * S);
        var sr = new Rect(wx + (ww - sw2) / 2f, footY - 70f * S, sw2, 52f * S);
        Fill(sr, snackBad ? Hex(0xf6aea9) : cText, 10f * S);
        GUI.Label(sr, snack, sSnack);
    }

    // =====================================================================
    // やりとり (自分の報告の中で、運営と話す)
    // =====================================================================

    // 0: 書く / 1: やりとり / 2: みんなの報告の一覧
    static int tab;
    static bool chatMode => tab == 1;
    // 書く欄に今入っているのが、やりとりの欄の中身か
    static bool chatBuf;
    static int Limit => chatMode ? ChatMaxLen : MaxLen;
    const int ChatMaxLen = 500;
    // 書く欄の中身は、書く / やりとり で別々に持つ
    static string otherText = "";
    static int otherCursor;
    static ReportChat.Report chatSel;
    static float nextPollAt, listScroll, chatBack;
    static bool chatSending;

    static void SetTab(int t)
    {
        if (tab == t) return;
        bool wantChat = t == 1;
        if (t != 2 && wantChat != chatBuf)
        {
            (text, otherText) = (otherText, text);
            (cursor, otherCursor) = (otherCursor, cursor);
            chatBuf = wantChat;
            undo.Clear();
        }
        tab = t;
        selAll = false;
        fieldScroll = 0f;
        caretMoved = true;
        SetFocus(t != 2);
        if (t == 1 && chatSel == null && ReportChat.Reports.Count > 0) Select(ReportChat.Reports[0]);
        if (t == 2) ReportChat.RefreshList(Time.unscaledTime, true);
    }

    // ---- みんなの報告の一覧 ----
    static int listKind; // 0: 全部 / 1: バグ / 2: 要望
    static bool listShowFixed;
    static float listAllScroll;
    static string listOpenId;

    static void DrawList(Rect area)
    {
        ReportChat.RefreshList(Time.unscaledTime);
        // 絞り込み
        var chipH = 38f * S;
        var cx = area.x;
        for (int i = 0; i < 3; i++)
        {
            var label = GetString(i switch { 1 => "Report.KindBug", 2 => "Report.KindRequest", _ => "Report.ListAll" });
            var r = new Rect(cx, area.y, Width(sChip, label) + 36f * S, chipH);
            bool on = listKind == i;
            if (on) Fill(r, cChipOn, chipH / 2f); else Outline(r, cLine, Hover(r) ? cHover : cBg, chipH / 2f, Mathf.Max(1f, S));
            GUI.Label(r, label, sChip);
            if (Click(r)) { listKind = i; listAllScroll = 0f; }
            cx = r.xMax + 8f * S;
        }
        {
            var label = (listShowFixed ? "✓  " : "") + GetString("Report.ListShowFixed");
            var r = new Rect(cx + 8f * S, area.y, Width(sChip, label) + 36f * S, chipH);
            if (listShowFixed) Fill(r, cChipOn, chipH / 2f); else Outline(r, cLine, Hover(r) ? cHover : cBg, chipH / 2f, Mathf.Max(1f, S));
            GUI.Label(r, label, sChip);
            if (Click(r)) { listShowFixed = !listShowFixed; listAllScroll = 0f; }
            var rr = new Rect(area.xMax - 120f * S, area.y, 120f * S, chipH);
            if (Button(rr, ReportChat.ListBusy ? "…" : GetString("Report.ListReload"), 2, !ReportChat.ListBusy)) ReportChat.RefreshList(Time.unscaledTime, true);
        }

        var box = new Rect(area.x, area.y + chipH + 14f * S, area.width, area.height - chipH - 14f * S);
        Outline(box, cLine, cBg, 12f * S, Mathf.Max(1f, S));
        var items = ReportChat.List.Where(x => (listKind == 0 || (listKind == 1) == x.IsBug) && (listShowFixed || x.Status != "fixed"))
            .OrderBy(x => x.Status == "fixed" ? 1 : 0).ThenByDescending(x => x.At).ToList();
        if (items.Count == 0)
        {
            var msg = !ReportChat.ListLoaded ? GetString(ReportChat.ListBusy ? "Report.ChatLoading" : "Report.ListFailed") : GetString("Report.ListEmpty");
            GUI.Label(box, msg, sCenter);
            return;
        }
        var inner = new Rect(box.x + 8f * S, box.y + 8f * S, box.width - 16f * S, box.height - 16f * S);
        var sTitle2 = Style(F(18), cText, TextAnchor.MiddleLeft, bold: true);
        var sBody = Style(F(16), cSub, TextAnchor.UpperLeft, wrap: true);
        var heights = new float[items.Count];
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float h = 64f * S;
            if (items[i].Id == listOpenId && items[i].Summary.Length > 0) h += sBody.CalcHeight(new GUIContent(items[i].Summary), inner.width - 40f * S) + 12f * S;
            heights[i] = h;
            total += h + 6f * S;
        }
        if (Hover(inner) && ev.type == EventType.ScrollWheel) { listAllScroll += ev.delta.y * 30f * S; ev.Use(); }
        listAllScroll = Mathf.Clamp(listAllScroll, 0f, Mathf.Max(0f, total - inner.height));
        GUI.BeginGroup(inner);
        var mouse = ev.mousePosition;
        float y = -listAllScroll;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var r = new Rect(0f, y, inner.width, heights[i]);
            y += heights[i] + 6f * S;
            if (r.yMax < 0f || r.y > inner.height) continue;
            bool hover = live && r.Contains(mouse) && mouse.y >= 0f && mouse.y <= inner.height;
            bool opened = it.Id == listOpenId;
            Fill(r, opened ? cChipOn : hover ? cHover : cSurface, 10f * S);
            var st = StatusText(it.Status);
            var stW = Width(sChip, st) + 28f * S;
            var when = DateTimeOffset.FromUnixTimeMilliseconds(it.At).ToLocalTime().ToString("MM/dd");
            GUI.Label(new Rect(r.x + 16f * S, r.y, r.width - stW - 110f * S, 64f * S), (it.IsBug ? "🐞 " : "💡 ") + it.Title, sTitle2);
            GUI.Label(new Rect(r.xMax - stW - 90f * S, r.y, 70f * S, 64f * S), when, sSubRight);
            var stR = new Rect(r.xMax - stW - 12f * S, r.y + 14f * S, stW, 36f * S);
            Outline(stR, StatusColor(it.Status), opened ? cChipOn : hover ? cHover : cSurface, 18f * S, Mathf.Max(1f, S));
            GUI.Label(stR, st, Style(F(15), StatusColor(it.Status), TextAnchor.MiddleCenter, bold: true));
            if (opened && it.Summary.Length > 0)
                GUI.Label(new Rect(r.x + 20f * S, r.y + 60f * S, r.width - 40f * S, r.height - 64f * S), it.Summary, sBody);
            if (hover && ev.type == EventType.MouseDown && ev.button == 0) { ev.Use(); listOpenId = opened ? null : it.Id; }
        }
        GUI.EndGroup();
    }

    static void Select(ReportChat.Report r)
    {
        chatSel = r;
        chatBack = 0f;
        nextPollAt = 0f;
    }

    static void SendChat()
    {
        if (chatSel == null || chatSending) return;
        var t = text.Trim();
        if (t.Length == 0) return;
        chatSending = true;
        var target = chatSel;
        ReportChat.Send(target, t, error =>
        {
            chatSending = false;
            if (error == null)
            {
                if (chatMode) { text = ""; cursor = 0; } else { otherText = ""; otherCursor = 0; }
                undo.Clear();
                chatBack = 0f;
                caretMoved = true;
            }
            else Snack(error == "too_fast" ? GetString("Report.TooFast") : string.Format(GetString("Report.ChatFailed"), error), true);
        });
    }

    static string StatusText(string s) => GetString("Report.St." + (s is "read" or "doing" or "fixed" ? s : "open"));
    static Color StatusColor(string s) => s switch
    {
        "read" => Hex(0x8ab4f8),
        "doing" => Hex(0xfdd663),
        "fixed" => Hex(0x81c995),
        _ => Hex(0x9aa0a6),
    };

    static void DrawChat(Rect area)
    {
        var reports = ReportChat.Reports;
        var gap = 16f * S;
        var listW = Mathf.Round(area.width * 0.34f);
        var list = new Rect(area.x, area.y, listW, area.height);
        var right = new Rect(list.xMax + gap, area.y, area.width - listW - gap, area.height);

        // ---- 左: 自分の報告 ----
        Outline(list, cLine, cBg, 12f * S, Mathf.Max(1f, S));
        if (reports.Count == 0)
        {
            GUI.Label(new Rect(list.x + 16f * S, list.y, list.width - 32f * S, list.height), GetString("Report.ChatNone"), sCenter);
        }
        else
        {
            var itemH = 84f * S;
            var inner = new Rect(list.x + 6f * S, list.y + 6f * S, list.width - 12f * S, list.height - 12f * S);
            var totalH = reports.Count * (itemH + 6f * S);
            if (Hover(inner) && ev.type == EventType.ScrollWheel) { listScroll += ev.delta.y * 30f * S; ev.Use(); }
            listScroll = Mathf.Clamp(listScroll, 0f, Mathf.Max(0f, totalH - inner.height));
            GUI.BeginGroup(inner);
            var mouse = ev.mousePosition;
            for (int i = 0; i < reports.Count; i++)
            {
                var rep = reports[i];
                var r = new Rect(0f, i * (itemH + 6f * S) - listScroll, inner.width, itemH);
                if (r.yMax < 0f || r.y > inner.height) continue;
                bool sel = rep == chatSel;
                bool hover = live && r.Contains(mouse) && mouse.y >= 0f && mouse.y <= inner.height;
                if (sel) Fill(r, cChipOn, 10f * S);
                else if (hover) Fill(r, cHover, 10f * S);
                var title = (rep.IsBug ? "🐞 " : "💡 ") + (rep.Title.Length > 0 ? rep.Title : "-");
                GUI.Label(new Rect(r.x + 14f * S, r.y + 8f * S, r.width - 60f * S, 34f * S), title, Style(F(18), cText, TextAnchor.MiddleLeft, bold: true));
                var st = Style(F(15), StatusColor(rep.Status), TextAnchor.MiddleLeft, bold: true);
                GUI.Label(new Rect(r.x + 14f * S, r.y + 44f * S, r.width * 0.5f, 28f * S), StatusText(rep.Status), st);
                var when = DateTimeOffset.FromUnixTimeMilliseconds(rep.At).ToLocalTime().ToString("MM/dd HH:mm");
                GUI.Label(new Rect(r.x, r.y + 44f * S, r.width - 14f * S, 28f * S), when, sSubRight);
                var unread = rep.Unread;
                if (unread > 0)
                {
                    var b = new Rect(r.xMax - 44f * S, r.y + 12f * S, 30f * S, 26f * S);
                    Fill(b, cRed, 13f * S);
                    GUI.Label(b, unread > 9 ? "9+" : unread.ToString(), Style(F(14), cDark, TextAnchor.MiddleCenter, bold: true));
                }
                if (hover && ev.type == EventType.MouseDown && ev.button == 0) { ev.Use(); Select(rep); }
            }
            GUI.EndGroup();
        }

        // ---- 右: やりとり ----
        if (chatSel == null)
        {
            GUI.Label(right, GetString(reports.Count == 0 ? "Report.ChatHowTo" : "Report.ChatPick"), sCenter);
            return;
        }
        var head = new Rect(right.x, right.y, right.width, 40f * S);
        GUI.Label(new Rect(head.x, head.y, head.width - 150f * S, head.height), (chatSel.IsBug ? "🐞 " : "💡 ") + chatSel.Title, Style(F(20), cText, TextAnchor.MiddleLeft, bold: true));
        var stText = StatusText(chatSel.Status);
        var stW = Width(sChip, stText) + 32f * S;
        var stR = new Rect(head.xMax - stW, head.y + 4f * S, stW, head.height - 8f * S);
        Outline(stR, StatusColor(chatSel.Status), cBg, stR.height / 2f, Mathf.Max(1f, S));
        GUI.Label(stR, stText, Style(F(15), StatusColor(chatSel.Status), TextAnchor.MiddleCenter, bold: true));

        var inputH = 96f * S;
        var sendW = Mathf.Max(110f * S, Width(sBtnDark, GetString("Report.FormSend")) + 48f * S);
        var input = new Rect(right.x, right.yMax - inputH, right.width - sendW - 12f * S, inputH);
        var msgs = new Rect(right.x, head.yMax + 10f * S, right.width, input.y - 26f * S - head.yMax - 10f * S);
        Fill(msgs, cSurface, 12f * S);
        DrawMessages(new Rect(msgs.x + 12f * S, msgs.y + 10f * S, msgs.width - 24f * S, msgs.height - 20f * S));
        GUI.Label(new Rect(right.x + 6f * S, input.y - 24f * S, right.width, 20f * S), GetString("Report.ChatHint"), Style(F(14), cSub, TextAnchor.MiddleLeft));

        DrawField(input);
        var sendR = new Rect(right.xMax - sendW, input.yMax - 52f * S, sendW, 52f * S);
        if (Button(sendR, chatSending ? "…" : GetString("Report.FormSend"), 1, text.Trim().Length > 0 && !chatSending)) SendChat();
        ReportChat.MarkSeen(chatSel);
    }

    static void DrawMessages(Rect view)
    {
        var list = chatSel.Messages;
        var bubbleMax = view.width * 0.74f;
        var sMsg = Style(F(18), cText, TextAnchor.UpperLeft, wrap: true);
        var sName = Style(F(14), cBlue, TextAnchor.MiddleLeft, bold: true);
        var sSys = Style(F(14), cSub, TextAnchor.MiddleCenter, wrap: true);
        var sTime = Style(F(12), cSub, TextAnchor.MiddleLeft);
        var padX = 14f * S; var padY = 10f * S; var gapY = 12f * S;

        // 高さを測る
        var heights = new float[list.Count];
        float total = 0f;
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            float h;
            if (m.From == "system") h = sSys.CalcHeight(new GUIContent(m.Text), view.width) + 4f * S;
            else
            {
                var w = Mathf.Min(bubbleMax, sMsg.CalcSize(new GUIContent(m.Text)).x + padX * 2f);
                h = sMsg.CalcHeight(new GUIContent(m.Text), w - padX * 2f) + padY * 2f + 20f * S;
            }
            heights[i] = h;
            total += h + gapY;
        }
        if (list.Count == 0)
        {
            GUI.Label(view, GetString(ReportChat.Busy(chatSel) ? "Report.ChatLoading" : "Report.ChatEmpty"), sCenter);
            return;
        }
        // 下に付けて並べる (chatBack = 下から戻した量)
        if (Hover(view) && ev.type == EventType.ScrollWheel) { chatBack -= ev.delta.y * 30f * S; ev.Use(); }
        var maxBack = Mathf.Max(0f, total - view.height);
        chatBack = Mathf.Clamp(chatBack, 0f, maxBack);
        float y = view.height - total + chatBack;
        if (total < view.height) y = 0f;

        GUI.BeginGroup(view);
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            var h = heights[i];
            if (y + h >= 0f && y <= view.height)
            {
                var time = m.At > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(m.At).ToLocalTime().ToString("MM/dd HH:mm") : "";
                if (m.From == "system")
                {
                    GUI.Label(new Rect(0f, y, view.width, h), m.Text.Replace("**", "") + (time.Length > 0 ? "  " + time : ""), sSys);
                }
                else
                {
                    bool me = m.From == "me";
                    var w = Mathf.Min(bubbleMax, sMsg.CalcSize(new GUIContent(m.Text)).x + padX * 2f);
                    var bx = me ? view.width - w : 0f;
                    var label = me ? GetString("Report.ChatMe") : m.Name;
                    GUI.Label(new Rect(bx + 4f * S, y, Mathf.Max(w, 200f * S), 18f * S), $"{label}  <color=#9aa0a6>{time}</color>", new GUIStyle(me ? sTime : sName) { richText = true, alignment = me ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, fixedWidth = 0 });
                    var br = new Rect(bx, y + 20f * S, w, h - 20f * S);
                    Fill(br, me ? cChipOn : Hex(0x3c4043), 14f * S);
                    GUI.Label(new Rect(br.x + padX, br.y + padY, br.width - padX * 2f, br.height - padY * 2f), m.Text, sMsg);
                }
            }
            y += h + gapY;
        }
        GUI.EndGroup();
    }

    // ---- Google の枠の線の入力欄 ----
    static void DrawField(Rect r)
    {
        var hover = Hover(r);
        var lineW = focused ? 2f * Mathf.Max(1f, S) : Mathf.Max(1f, S);
        Outline(r, focused ? cBlue : hover ? cText : cLine, cBg, 10f * S, lineW);

        var comp = focused ? Input.compositionString ?? "" : "";
        var inner = new Rect(r.x + 18f * S, r.y + 20f * S, r.width - 18f * S - 26f * S, r.height - 32f * S);
        var labelText = GetString(chatMode ? "Report.ChatLabel" : isBug ? "Report.WinLabelBug" : "Report.WinLabelReq");
        var floating = focused || text.Length > 0 || comp.Length > 0;
        if (floating)
        {
            // 枠の上の線に乗った見出し (後ろを窓の色で塗って線を切る)
            var ls = sLabelSmall;
            var oc = ls.normal.textColor;
            ls.normal.textColor = focused ? cBlue : cSub;
            var lw = Width(ls, labelText) + 12f * S;
            var lr = new Rect(r.x + 12f * S, r.y - 12f * S, lw, 24f * S);
            Fill(lr, cBg, 1f);
            GUI.Label(new Rect(lr.x + 6f * S, lr.y, lw, lr.height), labelText, ls);
            ls.normal.textColor = oc;
        }
        else GUI.Label(inner, labelText, sPlaceholder);

        if (Click(r))
        {
            if (!focused) SetFocus(true);
            selAll = false;
            // 押した所へカーソルを置く (Unity の測りが使えない時は最後へ)
            try
            {
                var local = ev.mousePosition - new Vector2(inner.x, inner.y - fieldScroll);
                var idx = sText.GetCursorStringIndex(new Rect(0, 0, inner.width, 100000f), new GUIContent(text), local);
                cursor = Mathf.Clamp(idx, 0, text.Length);
            }
            catch { cursor = text.Length; }
            Changed();
        }

        var shown = text.Insert(Mathf.Clamp(cursor, 0, text.Length), comp);
        var caretIdx = cursor + comp.Length;
        var caretOn = focused && (Time.unscaledTime - blinkStart) % 1.06f < 0.53f;
        if (caretFallback && caretOn) shown = shown.Insert(caretIdx, "|");
        var content = new GUIContent(shown);
        var contentH = sText.CalcHeight(content, inner.width);
        var lineH = sText.lineHeight;

        Vector2 caretPos = Vector2.zero;
        if (!caretFallback)
        {
            try { caretPos = sText.GetCursorPixelPosition(new Rect(0, 0, inner.width, 100000f), content, caretIdx); }
            catch { caretFallback = true; }
        }
        if (caretFallback) caretPos = new Vector2(0f, Mathf.Max(0f, sText.CalcHeight(new GUIContent(shown[..caretIdx] + "|"), inner.width) - lineH));

        if (hover && ev.type == EventType.ScrollWheel) { fieldScroll += ev.delta.y * lineH; ev.Use(); }
        if (caretMoved && ev.type == EventType.Repaint)
        {
            caretMoved = false;
            if (caretPos.y < fieldScroll) fieldScroll = caretPos.y;
            else if (caretPos.y + lineH > fieldScroll + inner.height) fieldScroll = caretPos.y + lineH - inner.height;
        }
        fieldScroll = Mathf.Clamp(fieldScroll, 0f, Mathf.Max(0f, contentH - inner.height));

        GUI.BeginGroup(inner);
        if (selAll && text.Length > 0) Fill(new Rect(-4f * S, -fieldScroll - 2f * S, inner.width + 8f * S, contentH + 4f * S), new Color(cBlue.r, cBlue.g, cBlue.b, 0.25f), 6f * S);
        GUI.Label(new Rect(0, -fieldScroll, inner.width, contentH + lineH), content, sText);
        if (!caretFallback)
        {
            if (comp.Length > 0)
            {
                // 変換中の字に下線
                try
                {
                    var a = sText.GetCursorPixelPosition(new Rect(0, 0, inner.width, 100000f), content, cursor);
                    if (Mathf.Approximately(a.y, caretPos.y)) Fill(new Rect(a.x, caretPos.y - fieldScroll + lineH - 2f * S, caretPos.x - a.x, 2f * S), cBlue, 1f);
                }
                catch { }
            }
            if (caretOn) Fill(new Rect(caretPos.x, caretPos.y - fieldScroll + 2f * S, Mathf.Max(2f, 2f * S), lineH - 2f * S), cBlue, 1f);
        }
        GUI.EndGroup();

        // 長い時の細いスクロールの棒
        if (contentH > inner.height + 1f)
        {
            var trackH = r.height - 24f * S;
            var barH = Mathf.Max(30f * S, trackH * inner.height / contentH);
            var barY = r.y + 12f * S + (trackH - barH) * (fieldScroll / Mathf.Max(1f, contentH - inner.height));
            Fill(new Rect(r.xMax - 14f * S, barY, 6f * S, barH), cLine, 3f * S);
        }

        // 日本語の変換の候補をカーソルのそばへ出す
        if (focused) Input.compositionCursorPos = GUIUtility.GUIToScreenPoint(new Vector2(inner.x + caretPos.x, inner.y + caretPos.y - fieldScroll + lineH));
    }

    // ---- 付けるファイル ----
    static void DrawFiles(Rect r, float chipW, float chipH)
    {
        var y = r.y;
        var total = TotalBytes();
        var head = string.Format(GetString("Report.WinFiles"), files.Count, MaxFiles) + (files.Count > 0 ? $"   {Bytes(total)}" : "");
        var hs = sLabelSmall; var oc = hs.normal.textColor;
        if (total > MaxBytes) hs.normal.textColor = cRed;
        GUI.Label(new Rect(r.x, y, r.width, 30f * S), head, hs);
        hs.normal.textColor = oc;
        y += 40f * S;

        var bh = 44f * S;
        var bx = r.x;
        var full = files.Count >= MaxFiles;
        foreach (var (label, act, always) in new (string, Action, bool)[]
        {
            ("＋  " + GetString("Report.WinPick"), OpenPicker, true),
            ("◎  " + GetString("Report.WinShot"), () => { SetFocus(false); shotState = 1; }, false),
            ("⎘  " + (clipBusy ? GetString("Report.WinClipBusy") : GetString("Report.WinClip")), PasteClipboardImage, false),
        })
        {
            var w = Width(sBtn, label) + 44f * S;
            if (Button(new Rect(bx, y, w, bh), label, 0, always || !full)) act();
            bx += w + 12f * S;
        }
        y += bh + 14f * S;

        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var cr = new Rect(r.x + (i % 3) * (chipW + 12f * S), y + (i / 3) * (chipH + 10f * S), chipW, chipH);
            Fill(cr, cSurface, 12f * S);
            var tr = new Rect(cr.x + 8f * S, cr.y + 8f * S, chipH - 16f * S, chipH - 16f * S);
            DrawThumb(tr, f, 8f * S);
            var tx = tr.xMax + 12f * S;
            var xr = new Rect(cr.xMax - 40f * S, cr.y + (chipH - 32f * S) / 2f, 32f * S, 32f * S);
            GUI.Label(new Rect(tx, cr.y + 8f * S, xr.x - tx - 4f * S, 26f * S), Path.GetFileName(f), sTileName);
            GUI.Label(new Rect(tx, cr.y + 34f * S, xr.x - tx - 4f * S, 22f * S), Bytes(SizeOf(f)), sTileSub);
            if (Hover(xr)) Fill(xr, cHover, 16f * S);
            GUI.Label(xr, "×", Style(F(22), cSub, TextAnchor.MiddleCenter));
            if (Click(xr)) { files.RemoveAt(i); break; }
        }
    }

    static void DrawThumb(Rect r, string path, float radius)
    {
        var t = Thumb(path);
        if (t != null)
        {
            // 縦横の比を保ったまま、枠いっぱいに収める
            Fill(r, Hex(0x17181a), radius);
            var k = Mathf.Min(r.width / t.width, r.height / t.height);
            var w = t.width * k; var h = t.height * k;
            ImguiTex.Draw(new Rect(r.x + (r.width - w) / 2f, r.y + (r.height - h) / 2f, w, h), t);
            return;
        }
        var video = videoExt.Contains(Path.GetExtension(path));
        Fill(r, video ? Hex(0x3b2f4a) : Hex(0x2f3b4a), radius);
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        GUI.Label(r, (video ? "▶\n" : "") + ext, Style(F(Mathf.Clamp(r.height / S * 0.22f, 11f, 22f)), cText, TextAnchor.MiddleCenter, bold: true, wrap: true));
    }

    // ---- 最近の画像・動画から選ぶ画面 ----
    static void DrawPicker()
    {
        live = true;
        Fill(new Rect(wx, wy, ww, wh), new Color(0f, 0f, 0f, 0.55f), 24f * S);
        var pad = 28f * S;
        var p = new Rect(wx + ww * 0.05f, wy + wh * 0.05f, ww * 0.9f, wh * 0.9f);
        Fill(p, cSurface, 20f * S);
        GUI.Label(new Rect(p.x + pad, p.y + pad * 0.6f, p.width - pad * 2f, 48f * S), GetString("Report.WinPickTitle"), Style(F(26), cText, TextAnchor.MiddleLeft));
        var closeR = new Rect(p.xMax - pad - 44f * S, p.y + pad * 0.6f + 2f * S, 44f * S, 44f * S);
        if (Hover(closeR)) Fill(closeR, cHover, 22f * S);
        GUI.Label(closeR, "×", Style(F(30), cSub, TextAnchor.MiddleCenter));
        if (Click(closeR)) { pickerOpen = false; return; }

        var footH = 52f * S;
        var gx = p.x + pad; var gw = p.width - pad * 2f;
        var gy = p.y + pad * 0.6f + 64f * S;
        var gh = p.yMax - pad * 0.8f - footH - 16f * S - gy;
        var footY = p.yMax - pad * 0.8f - footH;

        var doneW = Mathf.Max(150f * S, Width(sBtnDark, GetString("Report.WinPickDone")) + 64f * S);
        if (Button(new Rect(p.xMax - pad - doneW, footY, doneW, footH), GetString("Report.WinPickDone"), 1)) { pickerOpen = false; return; }
        GUI.Label(new Rect(gx, footY, gw * 0.6f, footH), string.Format(GetString("Report.WinFiles"), files.Count, MaxFiles), sLabelSmall);

        if (recentTask != null)
        {
            GUI.Label(new Rect(gx, gy, gw, gh), GetString("Report.WinPickLoading"), sCenter);
            return;
        }
        if (recent.Count == 0)
        {
            GUI.Label(new Rect(gx, gy, gw, gh), GetString("Report.WinPickNone"), sCenter);
            return;
        }

        const int cols = 4;
        var gap = 14f * S;
        var tw = (gw - 12f * S - gap * (cols - 1)) / cols;
        var imgH = tw * 0.62f;
        var th = imgH + 58f * S;
        var rows = (recent.Count + cols - 1) / cols;
        var totalH = rows * (th + gap);
        var area = new Rect(gx, gy, gw, gh);
        if (area.Contains(ev.mousePosition) && ev.type == EventType.ScrollWheel) { pickerScroll += ev.delta.y * th * 0.35f; ev.Use(); }
        pickerScroll = Mathf.Clamp(pickerScroll, 0f, Mathf.Max(0f, totalH - gh));

        GUI.BeginGroup(area);
        var mouse = ev.mousePosition;
        for (int i = 0; i < recent.Count; i++)
        {
            var (path, time, size) = recent[i];
            var tr = new Rect((i % cols) * (tw + gap), (i / cols) * (th + gap) - pickerScroll, tw, th);
            if (tr.yMax < 0f || tr.y > gh) continue;
            var on = files.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
            var hover = tr.Contains(mouse) && mouse.y >= 0f && mouse.y <= gh;
            if (on) Outline(tr, cBlue, hover ? cHover : cBg, 12f * S, 3f * S);
            else Fill(tr, hover ? cHover : cBg, 12f * S);
            DrawThumb(new Rect(tr.x + 8f * S, tr.y + 8f * S, tr.width - 16f * S, imgH), path, 8f * S);
            GUI.Label(new Rect(tr.x + 12f * S, tr.y + imgH + 12f * S, tr.width - 24f * S, 22f * S), Path.GetFileName(path), sTileName);
            GUI.Label(new Rect(tr.x + 12f * S, tr.y + imgH + 34f * S, tr.width - 24f * S, 20f * S), $"{time:M/d HH:mm}  ・  {Bytes(size)}", sTileSub);
            if (on)
            {
                var b = new Rect(tr.xMax - 40f * S, tr.y + 14f * S, 28f * S, 28f * S);
                Fill(b, cBlue, 14f * S);
                GUI.Label(b, "✓", Style(F(18), cDark, TextAnchor.MiddleCenter, bold: true));
            }
            if (ev.type == EventType.MouseDown && ev.button == 0 && hover)
            {
                ev.Use();
                if (on) files.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
                else AddFile(path);
            }
        }
        GUI.EndGroup();
        if (totalH > gh)
        {
            var barH = Mathf.Max(30f * S, gh * gh / totalH);
            var barY = gy + (gh - barH) * (pickerScroll / Mathf.Max(1f, totalH - gh));
            Fill(new Rect(p.xMax - pad * 0.6f, barY, 6f * S, barH), cLine, 3f * S);
        }
    }
}
