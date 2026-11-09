using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TownOfHostL.Core;
using TownOfHostL.Core.Modes;

namespace TownOfHostL.Modules;

/// <summary>
/// BepInEx のログファイル (LogOutput.log) の扱い。
/// 書き出しは BepInEx が溜めてまとめて行うので、写す前に Flush して末尾の取りこぼしを防ぐ。
/// </summary>
public static class LogFile
{
    private static string fallbackPath => $"{Environment.CurrentDirectory}/BepInEx/LogOutput.log";

    private static BepInEx.Logging.DiskLogListener FindListener()
        => BepInEx.Logging.Logger.Listeners.OfType<BepInEx.Logging.DiskLogListener>().FirstOrDefault();

    /// <summary>今書いているログファイルのパス。同時に複数起動して LogOutput.log.1 などになっている時もそちらを返す。</summary>
    public static string CurrentPath
    {
        get
        {
            try
            {
                var writer = FindListener()?.LogWriter;
                // BepInEx は書き手を TextWriter.Synchronized で包むので、中の StreamWriter を取り出す
                if (writer != null && writer is not StreamWriter)
                    writer = writer.GetType().GetField("_out", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(writer) as TextWriter ?? writer;
                if (writer is StreamWriter { BaseStream: FileStream fs }) return fs.Name;
            }
            catch { }
            return fallbackPath;
        }
    }

    public static void Flush()
    {
        Logger.FlushSuppressedExceptions();
        try { FindListener()?.LogWriter?.Flush(); }
        catch (Exception ex) { Logger.Warn($"ログの Flush に失敗: {ex.Message}", "LogFile"); }
    }

    /// <summary>今のファイルの長さ。試合の始まりの位置を控えるのに使う。Flush しないので、溜まっている分だけ手前になる (切り出しが少し前から始まるだけ)。</summary>
    public static long Length
    {
        get
        {
            try { return new FileInfo(CurrentPath).Length; }
            catch { return 0; }
        }
    }

    /// <summary>offset から末尾までを destPath へ写す。BepInEx が書き込み中のファイルなので共有で開く。</summary>
    public static void CopyTail(long offset, string destPath, string header)
    {
        Flush();
        using var src = new FileStream(CurrentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (offset < 0 || offset > src.Length) offset = 0;
        src.Seek(offset, SeekOrigin.Begin);
        using var dst = new FileStream(destPath, FileMode.CreateNew, FileAccess.Write);
        var headerBytes = new UTF8Encoding(false).GetBytes(header);
        dst.Write(headerBytes, 0, headerBytes.Length);
        src.CopyTo(dst);
    }
}

/// <summary>試合ごとのログの切り出し。試合開始でファイルの位置を控えるだけなので、試合中の費用は無い。</summary>
public static class GameLogMark
{
    public static int SessionId { get; private set; }
    public static long Offset { get; private set; } = -1;
    public static DateTime StartedAt { get; private set; }

    internal static void Mark(int sessionId)
    {
        SessionId = sessionId;
        Offset = LogFile.Length;
        StartedAt = DateTime.Now;
    }

    /// <summary>最後に始まった試合からのログを保存してフォルダを開く。まだ試合をしていなければ false。</summary>
    public static bool Dump()
    {
        if (Offset < 0) return false;
        var folder = Utils.GetLogFolder();
        string t = DateTime.Now.ToString("yyyy-MM-dd_HH.mm.ss");
        string stem = $"{folder.FullName}/TownOfHost-v{Main.PluginVersion}-{t}-game{SessionId}";
        // 同じ秒に続けて打つと名前が重なり、CreateNew が IOException になるので番号を足す
        string path = $"{stem}.log";
        for (int n = 2; File.Exists(path); n++) path = $"{stem}-{n}.log";
        string header =
            $"# TownOfHost_L v{Main.PluginVersion} ({ThisAssembly.Git.Branch}@{ThisAssembly.Git.Commit})\n" +
            $"# 試合 #{SessionId} の開始 ({StartedAt:yyyy-MM-dd HH:mm:ss}) から {DateTime.Now:HH:mm:ss} まで\n";
        LogFile.CopyTail(Offset, path, header);
        Utils.OpenDirectory(path);
        return true;
    }
}

/// <summary>開発者用のチャットコマンド。どれも自分の画面とログにだけ書き、通信はしない。</summary>
public static class DevCommands
{
    private static void Reply(ChatCommandContext c, string text)
    {
        if (c.Chat != null) c.Chat.AddChat(PlayerControl.LocalPlayer, text);
    }

    private static bool RequireDeveloper(ChatCommandContext c)
    {
        if (DebugModeManager.AmDebugger) return true;
        Reply(c, "開発者向けのコマンドです (デバッグビルドかデバッグキーの認証が要ります)");
        return false;
    }

    /// <summary>/dump [game]</summary>
    public static void Dump(ChatCommandContext c)
    {
        if (c.Arg(1) is "game" or "g")
        {
            try
            {
                if (!GameLogMark.Dump()) Reply(c, "まだ試合をしていないので、切り出せるログがありません");
                else Reply(c, $"試合 #{GameLogMark.SessionId} のログを保存しました");
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "DevCommands");
                Reply(c, $"ログの保存に失敗しました: {ex.Message}");
            }
            return;
        }
        Utils.DumpLog();
    }

    /// <summary>/log [list | detail | ingame | &lt;tag&gt; on|off|game]</summary>
    public static void LogCommand(ChatCommandContext c)
    {
        if (!RequireDeveloper(c)) return;
        string sub = c.Arg(1);
        switch (sub.ToLowerInvariant())
        {
            case "":
            case "list":
                Reply(c,
                    $"止めているタグ: {(Logger.disableList.Count == 0 ? "なし" : string.Join(", ", Logger.disableList.OrderBy(t => t, StringComparer.Ordinal)))}\n" +
                    $"画面にも出すタグ: {(Logger.sendToGameList.Count == 0 ? "なし" : string.Join(", ", Logger.sendToGameList))}\n" +
                    $"呼び出し元の表示 (detail): {(Logger.isDetail ? "ON" : "OFF")} / 全ログを画面にも (ingame): {(Logger.isAlsoInGame ? "ON" : "OFF")}\n" +
                    "使い方: /log <タグ> on|off|game ・ /log detail ・ /log ingame");
                return;
            case "detail":
                Logger.isDetail = !Logger.isDetail;
                Reply(c, $"呼び出し元の表示: {(Logger.isDetail ? "ON" : "OFF")}");
                return;
            case "ingame":
                Logger.isAlsoInGame = !Logger.isAlsoInGame;
                Reply(c, $"全ログを画面にも出す: {(Logger.isAlsoInGame ? "ON" : "OFF")}");
                return;
        }
        // タグは大文字小文字を区別するので、打ち間違いは止めているタグの中から拾い直す
        string tag = Logger.disableList.FirstOrDefault(t => string.Equals(t, sub, StringComparison.OrdinalIgnoreCase)) ?? sub;
        switch (c.Arg(2).ToLowerInvariant())
        {
            case "on":
                Logger.SetTagEnabled(tag, true);
                Reply(c, $"{tag}: 出す");
                break;
            case "off":
                Logger.SetTagEnabled(tag, false);
                Reply(c, $"{tag}: 止める (次の起動にも残ります)");
                break;
            case "game":
                Logger.SetTagEnabled(tag, true, toGame: true);
                Reply(c, $"{tag}: 出して、画面にも表示する");
                break;
            default:
                Reply(c, $"{tag}: {(Logger.IsTagEnabled(tag) ? "出している" : "止めている")}\n使い方: /log {tag} on|off|game");
                break;
        }
    }

    /// <summary>/dev state</summary>
    public static void DevCommand(ChatCommandContext c)
    {
        if (!RequireDeveloper(c)) return;
        switch (c.Arg(1).ToLowerInvariant())
        {
            case "state":
            case "s":
                Logger.Info(BuildStateDump(), "DevState", escapeCRLF: false);
                Reply(c, "今の状態をログ (タグ DevState) に書きました");
                break;
            default:
                Reply(c, "/dev state : 全員の役職・生死・タスク・位置などをログへ書く");
                break;
        }
    }

    /// <summary>不具合報告の時に今の状態を 1 回の書き込みで残す。</summary>
    public static string BuildStateDump()
    {
        var sb = new StringBuilder(1024);
        var client = AmongUsClient.Instance;
        string phase = !GameStates.IsInGame ? (GameStates.IsLobby ? "ロビー" : "試合外") : GameStates.IsMeeting ? "会議" : "タスク中";
        sb.Append("---- 状態 ----\n");
        sb.Append($"フェーズ: {phase} / 試合: {(GameSession.Current != null ? $"#{GameSession.Current.Id}" : "なし")} / モード: {ModeCatalog.Current.GetType().Name}");
        if (Main.NormalOptions != null) sb.Append($" / マップ: {(MapNames)Main.NormalOptions.MapId}");
        sb.Append($" / ホスト: {client?.AmHost} / 通信: {client?.NetworkMode}\n");
        if (GameStates.IsInGame && Main.IntroEndTime != DateTime.MinValue)
            sb.Append($"イントロ後の経過: {(DateTime.Now - Main.IntroEndTime).TotalSeconds:F0} 秒\n");

        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc == null) continue;
            var data = pc.Data;
            var cd = pc.GetClient();
            var state = PlayerState.GetByPlayerId(pc.PlayerId);
            sb.Append($"{pc.PlayerId,2} {data?.PlayerName} cid:{cd?.Id.ToString() ?? "-"} {cd?.PlatformData?.Platform.ToString() ?? "-"} 色:{data?.DefaultOutfit?.ColorId}");
            if (state != null)
            {
                sb.Append($" 役職:{state.GetNowMainRole()}");
                if (state.SubRoles.Count > 0) sb.Append($"+{string.Join("+", state.SubRoles)}");
                if (state.IsDead)
                {
                    sb.Append($" 死亡:{state.DeathReason}");
                    byte killer = state.GetRealKiller();
                    if (killer != byte.MaxValue) sb.Append($"(by {killer})");
                }
                var task = state.taskState;
                if (task != null && task.hasTasks) sb.Append($" タスク:{task.CompletedTasksCount}/{task.AllTasksCount}");
            }
            if (data != null)
            {
                if (data.IsDead) sb.Append(" [Data.IsDead]");
                if (data.Disconnected) sb.Append(" [切断]");
                sb.Append($" 本編役職:{data.RoleType}");
            }
            if (GameStates.IsInGame)
            {
                var pos = pc.GetTruePosition();
                var room = pc.GetPlainShipRoom();
                sb.Append($" 位置:({pos.x:F2},{pos.y:F2}){(room != null ? $"@{room.RoomId}" : "")}");
            }
            sb.Append('\n');
        }
        sb.Append("--------------");
        return sb.ToString();
    }
}
