using AmongUs.GameOptions;

namespace TownOfHostL.Modules
{
    /// <summary>
    /// 試合の間、ホストの手元の本編の設定 (GameOptionsManager.CurrentGameOptions) は、
    /// ホスト用に組んだ設定 (役職の視界・クール・暗転など) に差し替わる。
    /// 試合を出る時 (試合の終わり・試合の途中で部屋を抜けた時) に、試合の始まりに控えた部屋の設定へ必ず戻す。
    /// 戻さずに抜けると、組んだ設定がそのまま部屋の設定として残り、次に部屋を作った時に保存される
    /// (会議中に抜けると視界 0 が残り、次の試合で全員の姿が見えなくなる)。
    /// </summary>
    public static class RoomOptions
    {
        /// <summary>試合の始まりに控えてから、まだ戻していない</summary>
        public static bool Live { get; private set; }

        /// <summary>今の設定を部屋の設定として控える (試合の始まり・フリープレイの始まり)。差し替えて戻すのはホストだけ</summary>
        public static void Capture()
        {
            Main.RealOptionsData = new OptionBackupData(GameOptionsManager.Instance.CurrentGameOptions);
            Live = AmongUsClient.Instance && AmongUsClient.Instance.AmHost;
        }

        /// <summary>
        /// 控えた部屋の設定へ戻し、人ごとの設定の送り手を捨てる。控えていない時・戻し済みの時は何もしない。
        /// ホストの手元の今の設定は、部屋の設定の器 (GameHostOptions) へ向け直す (ロビーで本編の画面が書く先と保存する先をそろえる)。
        /// </summary>
        public static void Restore(string why)
        {
            if (!Live) return;
            Live = false;
            var real = Main.RealOptionsData;
            var gm = GameOptionsManager.Instance;
            if (real != null && gm != null)
            {
                try
                {
                    var current = gm.CurrentGameOptions;
                    var host = gm.GameHostOptions;
                    if (current != null) real.Restore(current);
                    if (host != null && (current == null || host.Pointer != current.Pointer))
                    {
                        real.Restore(host);
                        gm.CurrentGameOptions = host;
                        if (GameManager.Instance && GameManager.Instance.LogicOptions != null)
                            GameManager.Instance.LogicOptions.SetGameOptions(host);
                    }
                }
                catch (System.Exception ex) { Logger.Exception(ex, "RoomOptions.Restore"); }
            }
            // 人ごとの送り手が残ると、次の更新でホスト用の設定を組み直して今の設定へ書き戻す
            GameOptionsSender.AllSenders.Clear();
            GameOptionsSender.AllSenders.Add(new NormalGameOptionsSender());
            Logger.Info($"部屋の設定を戻した ({why})", "RoomOptions");
        }
    }
}
