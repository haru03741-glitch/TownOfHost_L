using System;
using System.Runtime.CompilerServices;
using TownOfHostL.Core;

namespace TownOfHostL.Modules
{
    class LogHandler : ILogHandler
    {
        public string Tag { get; }
        public LogHandler(string tag)
        {
            Tag = tag;
        }

        public void Info(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
            => Logger.Info(text, Tag, true, line, file, member);
        // 補間文字列で呼んだ時は、タグが無効なら文字列を組み立てない
        public void Info([InterpolatedStringHandlerArgument("")] ref LogText text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
        {
            if (text.Enabled) Logger.Info(text.ToStringAndClear(), Tag, true, line, file, member);
        }
        public void Warn(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
            => Logger.Warn(text, Tag, true, line, file, member);
        public void Warn([InterpolatedStringHandlerArgument("")] ref LogText text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
        {
            if (text.Enabled) Logger.Warn(text.ToStringAndClear(), Tag, true, line, file, member);
        }
        public void Error(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
            => Logger.Error(text, Tag, true, line, file, member);
        public void Fatal(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
            => Logger.Fatal(text, Tag, true, line, file, member);
        public void Msg(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
            => Logger.Msg(text, Tag, true, line, file, member);
        public void Exception(Exception ex, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
            => Logger.Exception(ex, Tag, line, file, member);
    }
}
