using System;

namespace TownOfHostL.Modules.Net;

// Innersloth 公式サーバーにいるかどうか。公式サーバーは送信の形や量を検査して Hacking 理由でホストを切断するので、
// 送信まわりの安全装置はこの判定で切り替える。
// リージョンの表示名ではなく PingServer のホスト名で見る。表示名は招待リンク等で任意の名前が入るため当てにならない。
// 公式は matchmaker(-eu/-as).among.us、非公式サーバーは among.us で終わらない。
public static class OfficialServer
{
    // 判定できない時は公式とみなす。公式扱いは制限が強まるだけで、通信が壊れることはない。
    public static bool IsActive
    {
        get
        {
            try
            {
                if (AmongUsClient.Instance == null || AmongUsClient.Instance.NetworkMode != NetworkModes.OnlineGame) return false;
                return IsOfficialRegion;
            }
            catch
            {
                return true;
            }
        }
    }

    // 選んでいるリージョンが公式かどうか。まだ接続していない画面 (部屋探しなど) でも使える。
    public static bool IsOfficialRegion
    {
        get
        {
            try
            {
                var region = ServerManager.Instance?.CurrentRegion;
                if (region == null) return true;
                var ping = region.PingServer;
                return string.IsNullOrEmpty(ping) || ping.EndsWith("among.us", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true;
            }
        }
    }
}
