using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using LogLevel = BepInEx.Logging.LogLevel;
using TownOfHostL.Modules;

namespace TownOfHostL
{
    class Webhook
    {
        public static void Send(string text)
        {
            if (Main.WebhookURL.Value == "none") return;
            HttpClient httpClient = new();
            Dictionary<string, string> strs = new()
            {
                { "content", text },
                { "username", "TownOfHost-Debugger" },
                { "avatar_url", "https://cdn.discordapp.com/avatars/336095904320716800/95243b1468018a24f7ae03d7454fd5f2.webp?size=40" }
            };
            TaskAwaiter<HttpResponseMessage> awaiter = httpClient.PostAsync(
                Main.WebhookURL.Value, new FormUrlEncodedContent(strs)).GetAwaiter();
            awaiter.GetResult();
        }
    }
    class Logger
    {
        static Logger()
        {
            try
            {
                System.Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch { }
        }
        public static bool isEnable;
        public static HashSet<string> disableList = new();
        public static HashSet<string> sendToGameList = new();
        /// <summary>各行に呼び出し元 (ファイル・メソッド・行) を付ける。開発者のときだけ効く。</summary>
        public static bool isDetail = false;
        public static bool isAlsoInGame = false;

        /// <summary>起動時に止めておくタグ。BepInEx の設定 [Logging] Disabled Tags に保存される。</summary>
        public const string DefaultDisabledTags = "NotifyRoles,SendRPC,ReceiveRPC,SwitchSystem,CustomRpcSender,Sniper";
        private static ConfigEntry<string> disabledTagsConfig;

        public static void Enable() => isEnable = true;
        public static void Disable() => isEnable = false;
        public static void Enable(string tag, bool toGame = false)
        {
            disableList.Remove(tag);
            if (toGame) sendToGameList.Add(tag);
            else sendToGameList.Remove(tag);
        }
        public static void Disable(string tag) => disableList.Add(tag);

        /// <summary>止めるタグを設定から読み込む。以後 SetTagEnabled で変えた分は設定へ書き戻す。</summary>
        public static void LoadDisabledTags(ConfigEntry<string> config)
        {
            disabledTagsConfig = config;
            foreach (var tag in config.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                disableList.Add(tag);
        }
        /// <summary>タグの有効・無効を切り替えて、次の起動にも残す。</summary>
        public static void SetTagEnabled(string tag, bool enabled, bool toGame = false)
        {
            if (enabled) Enable(tag, toGame);
            else
            {
                Disable(tag);
                sendToGameList.Remove(tag);
            }
            if (disabledTagsConfig != null)
                disabledTagsConfig.Value = string.Join(",", disableList.OrderBy(t => t, StringComparer.Ordinal));
        }

        /// <summary>このタグのログが出るか。出ないなら文字列を組み立てる前に止められる。</summary>
        public static bool IsTagEnabled(string tag) => isEnable && !disableList.Contains(tag);
        public static void SendInGame(string text, bool isAlways = false)
        {
            if (!isEnable) return;
            if (DestroyableSingleton<HudManager>._instance) DestroyableSingleton<HudManager>.Instance.Notifier.AddDisconnectMessage(text);
        }

        // 時刻の文字列は秒が変わった時だけ作り直す。別スレッドから呼ばれても、組ごと差し替えるので食い違わない
        private sealed class Stamp
        {
            public readonly long Second;
            public readonly string Text;
            public Stamp(long second, string text) { Second = second; Text = text; }
        }
        private static Stamp stamp = new(-1, "");
        private static string TimeStamp()
        {
            var now = DateTime.Now;
            long second = now.Ticks / TimeSpan.TicksPerSecond;
            var s = stamp;
            if (s.Second != second) stamp = s = new Stamp(second, now.ToString("HH:mm:ss"));
            return s.Text;
        }

        private static void SendToFile(string text, LogLevel level, string tag, bool escapeCRLF, int lineNumber, string fileName, string memberName)
        {
            if (!isEnable || disableList.Contains(tag)) return;
            var logger = Main.Logger;
            string t = TimeStamp();
            if (sendToGameList.Contains(tag) || isAlsoInGame) SendInGame($"[{tag}]{text}");
            if (escapeCRLF && text.AsSpan().IndexOfAny('\r', '\n') >= 0)
                text = text.Replace("\r", "\\r").Replace("\n", "\\n");
            string log_text = isDetail && DebugModeManager.AmDebugger
                ? $"[{t}][{Path.GetFileNameWithoutExtension(fileName)}.{memberName}:{lineNumber}][{tag}]{text}"
                : $"[{t}][{tag}]{text}";
            switch (level)
            {
                case LogLevel.Info:
                    logger.LogInfo(log_text);
                    break;
                case LogLevel.Warning:
                    logger.LogWarning(log_text);
                    break;
                case LogLevel.Error:
                    logger.LogError(log_text);
                    break;
                case LogLevel.Fatal:
                    logger.LogFatal(log_text);
                    break;
                case LogLevel.Message:
                    logger.LogMessage(log_text);
                    break;
                default:
                    logger.LogWarning("Error:Invalid LogLevel");
                    logger.LogInfo(log_text);
                    break;
            }
        }
        public static void Info(string text, string tag, bool escapeCRLF = true, [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fileName = "", [CallerMemberName] string memberName = "")
        {
            // タグの判定を先にして、出さないログでタグ除去の正規表現を走らせない。タグを含まない行も正規表現を通さない
            if (!IsTagEnabled(tag)) return;
            if (text.Contains('<')) text = text.RemoveHtmlTags();
            SendToFile(text, LogLevel.Info, tag, escapeCRLF, lineNumber, fileName, memberName);
        }
        public static void Warn(string text, string tag, bool escapeCRLF = true, [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fileName = "", [CallerMemberName] string memberName = "") =>
            SendToFile(text, LogLevel.Warning, tag, escapeCRLF, lineNumber, fileName, memberName);
        public static void Error(string text, string tag, bool escapeCRLF = true, [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fileName = "", [CallerMemberName] string memberName = "") =>
            SendToFile(text, LogLevel.Error, tag, escapeCRLF, lineNumber, fileName, memberName);
        public static void Fatal(string text, string tag, bool escapeCRLF = true, [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fileName = "", [CallerMemberName] string memberName = "") =>
            SendToFile(text, LogLevel.Fatal, tag, escapeCRLF, lineNumber, fileName, memberName);
        public static void Msg(string text, string tag, bool escapeCRLF = true, [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fileName = "", [CallerMemberName] string memberName = "") =>
            SendToFile(text, LogLevel.Message, tag, escapeCRLF, lineNumber, fileName, memberName);

        // ---- 例外の重複抑制 ----
        // 毎フレームの処理で同じ例外が出続けると、1 行約 0.25ms の書き出しが 1 秒に数十回積もり、ログも埋まる。
        // 同じ例外 (タグ・型・投げた場所) は、前に書いてから ExceptionRepeatWindow の間は数えるだけにして、
        // 次に書く時に省略した回数を添える。止んだ後に残った回数は FlushSuppressedExceptions で書き出す。
        private static readonly TimeSpan ExceptionRepeatWindow = TimeSpan.FromSeconds(10);
        private const int MaxExceptionKeys = 256;
        private sealed class RepeatCount
        {
            public DateTime LastWritten;
            public int Suppressed;
            public string Summary;
        }
        private static readonly Dictionary<string, RepeatCount> exceptionRepeats = new();

        public static void Exception(Exception ex, string tag, [CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fileName = "", [CallerMemberName] string memberName = "")
        {
            if (!IsTagEnabled(tag)) return;
            var stack = ex.StackTrace;
            int newline = stack?.IndexOf('\n') ?? -1;
            string firstFrame = newline >= 0 ? stack.Substring(0, newline) : stack;
            string key = $"{tag}|{ex.GetType().FullName}|{firstFrame}";
            var now = DateTime.Now;
            int suppressed;
            lock (exceptionRepeats)
            {
                if (exceptionRepeats.TryGetValue(key, out var entry))
                {
                    if (now - entry.LastWritten < ExceptionRepeatWindow)
                    {
                        entry.Suppressed++;
                        return;
                    }
                    suppressed = entry.Suppressed;
                    entry.Suppressed = 0;
                    entry.LastWritten = now;
                }
                else
                {
                    if (exceptionRepeats.Count >= MaxExceptionKeys) FlushSuppressedExceptionsLocked();
                    exceptionRepeats[key] = new RepeatCount { LastWritten = now, Summary = $"{ex.GetType().Name}: {ex.Message}" };
                    suppressed = 0;
                }
            }
            string text = suppressed > 0 ? $"(同じ例外を前回から {suppressed} 回省略) {ex}" : ex.ToString();
            SendToFile(text, LogLevel.Error, tag, false, lineNumber, fileName, memberName);
        }
        /// <summary>省略したまま書いていない例外の回数を書き出し、数え直す。</summary>
        public static void FlushSuppressedExceptions()
        {
            lock (exceptionRepeats) FlushSuppressedExceptionsLocked();
        }
        private static void FlushSuppressedExceptionsLocked()
        {
            foreach (var (key, entry) in exceptionRepeats)
            {
                if (entry.Suppressed <= 0) continue;
                string tag = key.Substring(0, key.IndexOf('|'));
                SendToFile($"(同じ例外を前回から {entry.Suppressed} 回省略) {entry.Summary}", LogLevel.Error, tag, true, 0, "Debugger", nameof(FlushSuppressedExceptions));
            }
            exceptionRepeats.Clear();
        }

        public static void CurrentMethod([CallerLineNumber] int lineNumber = 0, [CallerFilePath] string fileName = "", [CallerMemberName] string memberName = "")
        {
            Logger.Msg($"\"{memberName}\" Called in \"{Path.GetFileName(fileName)}({lineNumber})\"", "Method");
        }

        public static LogHandler Handler(string tag)
            => new(tag);
    }
}
