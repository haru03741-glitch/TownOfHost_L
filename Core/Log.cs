using System;
using System.Runtime.CompilerServices;

using TownOfHostL;
using TownOfHostL.Modules;

namespace TownOfHostL.Core;

/// <summary>
/// タグを先に受け取るログ。
/// 補間文字列 ($"...") を渡すと、タグが無効なら文字列を組み立てず、埋め込んだ式も評価しない。
/// 止めてあるタグ (NotifyRoles・SendRPC など) を毎フレームの処理から呼んでも、費用は判定 1 回だけになる。
/// </summary>
internal static class Log
{
    public static bool IsEnabled(string tag) => TownOfHostL.Logger.IsTagEnabled(tag);

    public static void Info(string tag, [InterpolatedStringHandlerArgument("tag")] ref LogText text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
    {
        if (text.Enabled) TownOfHostL.Logger.Info(text.ToStringAndClear(), tag, true, line, file, member);
    }
    public static void Info(string tag, string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "") => TownOfHostL.Logger.Info(text, tag, true, line, file, member);

    public static void Warn(string tag, [InterpolatedStringHandlerArgument("tag")] ref LogText text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "")
    {
        if (text.Enabled) TownOfHostL.Logger.Warn(text.ToStringAndClear(), tag, true, line, file, member);
    }
    public static void Warn(string tag, string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "") => TownOfHostL.Logger.Warn(text, tag, true, line, file, member);

    public static void Error(string tag, string text, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "") => TownOfHostL.Logger.Error(text, tag, true, line, file, member);
    public static void Exception(string tag, Exception ex, [CallerLineNumber] int line = 0, [CallerFilePath] string file = "", [CallerMemberName] string member = "") => TownOfHostL.Logger.Exception(ex, tag, line, file, member);
}

/// <summary>タグが無効なら何も組み立てない補間文字列。</summary>
[InterpolatedStringHandler]
internal ref struct LogText
{
    private DefaultInterpolatedStringHandler inner;
    public bool Enabled { get; }

    public LogText(int literalLength, int formattedCount, string tag, out bool shouldAppend)
    {
        Enabled = shouldAppend = TownOfHostL.Logger.IsTagEnabled(tag);
        inner = Enabled ? new DefaultInterpolatedStringHandler(literalLength, formattedCount) : default;
    }

    public LogText(int literalLength, int formattedCount, LogHandler handler, out bool shouldAppend)
        : this(literalLength, formattedCount, handler.Tag, out shouldAppend) { }

    public void AppendLiteral(string value) => inner.AppendLiteral(value);
    public void AppendFormatted<T>(T value) => inner.AppendFormatted(value);
    public void AppendFormatted<T>(T value, string format) => inner.AppendFormatted(value, format);
    public void AppendFormatted<T>(T value, int alignment) => inner.AppendFormatted(value, alignment);
    public void AppendFormatted<T>(T value, int alignment, string format) => inner.AppendFormatted(value, alignment, format);
    public void AppendFormatted(ReadOnlySpan<char> value) => inner.AppendFormatted(value);
    public void AppendFormatted(string value) => inner.AppendFormatted(value);

    public string ToStringAndClear() => Enabled ? inner.ToStringAndClear() : string.Empty;
}
