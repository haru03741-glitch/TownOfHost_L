using AmongUs.GameOptions;

namespace TownOfHostL.Modules
{
    /// <summary>
    /// 試合の間だけ使う本編の設定の持ち主。
    /// 部屋の設定の器 (GameOptionsManager.GameHostOptions・本編が保存する物) には試合の間は書かない。
    /// 試合に入る時 (秒読みの始まり) に器の写しを作って今の設定 (CurrentGameOptions) をそれへ向け、
    /// 試合の始まりの書き換え (本編の役職の出現率 0・変身のクール 0・モードごとのインポスター数など) と
    /// ホスト用に組んだ設定 (役職の視界・クール・会議中の暗転) は写しか、その後の差し替え先にだけ入る。
    /// 試合を出る時 (試合の終わり・試合の途中の切断・秒読みの取り消し) に、今の設定を器へ向け直す。
    /// </summary>
    public static class RoomOptions
    {
        /// <summary>試合に入っていて、まだ出ていない (ホストだけ)</summary>
        public static bool Entered { get; private set; }
        // 入った時の器の値。本編が切断の途中で今の設定を器へ写すことがあるので、出る時に書き戻す
        static OptionBackupData saved;

        /// <summary>試合に入る (ホスト・秒読みの始まり)。器の写しを今の設定にする</summary>
        public static void Enter()
        {
            if (Entered || !AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
            var gm = GameOptionsManager.Instance;
            var host = gm?.GameHostOptions;
            if (host == null) return;
            var current = gm.CurrentGameOptions;
            // 試合の基は今の設定から作る (フリープレイなどで器と別の物を指していても、試合の値はそれまでと同じ)
            var source = current ?? host;
            if (source.Pointer != host.Pointer)
                Logger.Warn("試合に入る時の今の設定が部屋の設定の器と別の物でした。今の設定の写しで始めます", "RoomOptions");
            RepairVision(gm, host);
            if (source.Pointer != host.Pointer) RepairVision(gm, source);
            saved = new OptionBackupData(host);
            var work = gm.gameOptionsFactory.FromBytes(gm.gameOptionsFactory.ToBytes(source, false));
            if (work == null)
            {
                Logger.Warn("試合用の設定の写しを作れませんでした。部屋の設定の器のまま始めます", "RoomOptions");
                return;
            }
            Point(gm, work);
            Entered = true;
        }

        /// <summary>
        /// 試合の始まり (CoStartGame の後・フリープレイの始まり) に、試合の基の設定を控える。
        /// 秒読みを通らずに始まった時はここで入る
        /// </summary>
        public static void Capture()
        {
            Enter();
            Main.RealOptionsData = new OptionBackupData(GameOptionsManager.Instance.CurrentGameOptions);
        }

        /// <summary>試合を出る。今の設定を部屋の設定の器へ向け直し、人ごとの設定の送り手を捨てる。入っていなければ何もしない</summary>
        public static void Exit(string why)
        {
            if (!Entered) return;
            Entered = false;
            var gm = GameOptionsManager.Instance;
            try
            {
                var host = gm?.GameHostOptions;
                if (host != null)
                {
                    saved?.Restore(host);
                    Point(gm, host);
                }
            }
            catch (System.Exception ex) { Logger.Exception(ex, "RoomOptions.Exit"); }
            saved = null;
            // 人ごとの送り手が残ると、次の更新でホスト用の設定を組み直して今の設定へ書き戻す
            GameOptionsSender.AllSenders.Clear();
            GameOptionsSender.AllSenders.Add(new NormalGameOptionsSender());
            Logger.Info($"部屋の設定へ戻した ({why})", "RoomOptions");
        }

        // 本編の画面で選べる視界は 0.25 から。0 以下は、試合の途中で抜けると会議中の暗転の値が部屋の設定として
        // 保存されていた頃の名残なので、本編の初期値へ直して保存し直す (残すと、試合中に誰の姿も見えない)
        static void RepairVision(GameOptionsManager gm, IGameOptions host)
        {
            bool fixedAny = false;
            if (host.TryGetFloat(FloatOptionNames.CrewLightMod, out var crew) && crew <= 0f) { host.SetFloat(FloatOptionNames.CrewLightMod, 1f); fixedAny = true; }
            if (host.TryGetFloat(FloatOptionNames.ImpostorLightMod, out var imp) && imp <= 0f) { host.SetFloat(FloatOptionNames.ImpostorLightMod, 1.5f); fixedAny = true; }
            if (!fixedAny) return;
            Logger.Warn($"部屋の設定の視界が 0 だったので初期値へ直した (クルー {crew} → {host.GetFloat(FloatOptionNames.CrewLightMod)}・インポスター {imp} → {host.GetFloat(FloatOptionNames.ImpostorLightMod)})", "RoomOptions");
            try
            {
                if (host.GameMode == GameModes.HideNSeek) gm.SaveHideNSeekHostOptions();
                else gm.SaveNormalHostOptions();
            }
            catch (System.Exception ex) { Logger.Exception(ex, "RoomOptions.RepairVision"); }
        }

        static void Point(GameOptionsManager gm, IGameOptions opt)
        {
            gm.CurrentGameOptions = opt;
            if (GameManager.Instance && GameManager.Instance.LogicOptions != null)
                GameManager.Instance.LogicOptions.SetGameOptions(opt);
        }
    }
}
