using System;
using System.Runtime.CompilerServices;

namespace TownOfHostL.Modules
{
    public interface ILogHandler
    {
        public void Info(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
        public void Warn(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
        public void Error(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
        public void Fatal(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
        public void Msg(string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
        public void Exception(Exception ex, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "");
    }
}
