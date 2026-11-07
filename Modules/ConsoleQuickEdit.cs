using System;
using System.Runtime.InteropServices;

namespace TownOfHostL.Modules;

// BepInEx のコンソールへのログはゲームのメインスレッドが同期で書く。Windows のコンソールは QuickEdit が既定で有効で、
// ウィンドウを左クリックしただけで選択の状態に入り、解除されるまで書き込みが止まる。次にログを 1 行出した所でゲームごと固まる。
// 起動時に QuickEdit を切っておく (TOHL はコンソールを作り直さないので 1 回で足りる)。
public static class ConsoleQuickEdit
{
    const uint GenericRead = 0x80000000;
    const uint GenericWrite = 0x40000000;
    const uint FileShareReadWrite = 0x3;
    const uint OpenExisting = 3;
    const uint EnableQuickEditMode = 0x0040;
    const uint EnableExtendedFlags = 0x0080;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    public static void Disable()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return;
            // 標準入力はコンソールを指していないことがあるので、コンソールの入力を名前で開く
            var handle = CreateFileW("CONIN$", GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return; // コンソールが無い
            try
            {
                if (GetConsoleMode(handle, out var mode) && (mode & EnableQuickEditMode) != 0)
                {
                    SetConsoleMode(handle, (mode & ~EnableQuickEditMode) | EnableExtendedFlags);
                    Logger.Info("コンソールの QuickEdit を切りました", "ConsoleQuickEdit");
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"コンソールの QuickEdit を切れませんでした: {ex.Message}", "ConsoleQuickEdit");
        }
    }
}
