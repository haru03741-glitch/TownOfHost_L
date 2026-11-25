using System;
using AmongUs.GameOptions;
using HarmonyLib;

namespace TownOfHostL;

/// <summary>
/// 反転スケルド (dlekS・MapId 3) を選べるようにする。
/// 本編のロビーの GameStartManager は、MapId 3 だとマップの札 (AllMapIcons) が無く毎フレーム例外を出して開始もできない
/// (2026-11-26 実機)。なのでロビーの間は設定の MapId を 0 (スケルド) のまま持ち、「0 は反転スケルドを指す」印を立てておく。
/// 試合を始める電文の直前 (AmongUsClient.StartGame) に 3 へ書き換えて全員へ送り、ロビーへ戻ったら 0 と印に戻す。
/// 船は本編の物をそのまま使うので、素の客もそのまま反転した船で遊べる。
/// </summary>
public static class DleksMap
{
    public const byte MapId = 3;

    // 印は本編のモード (通常・かくれんぼ) ごとに持つ。マップの設定もモードごとに別の物なので
    static readonly bool[] pending = new bool[2];
    static int ModeIndex => GameOptionsManager.Instance != null && GameOptionsManager.Instance.currentGameMode == GameModes.HideNSeek ? 1 : 0;

    /// <summary>今のモードのロビーの MapId 0 が反転スケルドを指している</summary>
    /// ホストとしてオンラインの部屋にいる間だけ効く (客として他の部屋に入った時やフリープレイの MapId 0 はスケルド)
    public static bool Pending => pending[ModeIndex] && AmongUsClient.Instance && AmongUsClient.Instance.AmHost
        && AmongUsClient.Instance.NetworkMode != NetworkModes.FreePlay;

    /// <summary>ホストが選んだ (ロビーで 0 の代わりに反転スケルドを指す)</summary>
    public static void Select(bool on)
    {
        int i = ModeIndex;
        if (pending[i] == on) return;
        pending[i] = on;
        Logger.Info(on ? "反転スケルドを選んだ" : "反転スケルドを外した", nameof(DleksMap));
    }

    static IGameOptions Opts => GameOptionsManager.Instance?.CurrentGameOptions;

    /// <summary>
    /// ロビーでマップを決める時の入口 (設定画面・ランダムマップ・投票)。3 は 0 と印で持つ。
    /// Main.NormalOptions へ直に書くと、印が残ってスケルドが反転スケルドになる
    /// </summary>
    public static void SetLobbyMap(IGameOptions o, byte id)
    {
        if (o == null) return;
        Select(id == MapId);
        o.SetByte(ByteOptionNames.MapId, id == MapId ? (byte)0 : id);
    }

    /// <summary>ロビーで MapId 3 を見つけたら (試合の後・保存値・他の経路で書かれた時)、0 と印に戻す</summary>
    public static void NormalizeLobby()
    {
        try
        {
            if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
            var o = Opts;
            if (o == null || o.MapId != MapId) return;
            o.SetByte(ByteOptionNames.MapId, 0);
            pending[ModeIndex] = true;
            GameManager.Instance?.LogicOptions?.SyncOptions();
            Logger.Info("ロビーの MapId 3 を 0 と反転スケルドの印に戻した", nameof(DleksMap));
        }
        catch (Exception ex) { Logger.Warn($"ロビーの MapId の戻しに失敗: {ex.Message}", nameof(DleksMap)); }
    }

    // 試合を始める電文を送る直前。秒読み (自動スタートも) の終わりはここを通る。
    // GameStartManager.FinallyBegin は本編の Update に畳み込まれていて、前置きが呼ばれない (2026-11-26 実機)
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.StartGame))]
    static class StartGamePatch
    {
        static void Prefix()
        {
            try
            {
                if (!Pending || !AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
                var o = Opts;
                if (o == null || o.MapId != 0) return;
                o.SetByte(ByteOptionNames.MapId, MapId);
                // 試合の処理が読む LogicOptions の設定が部屋の設定と別の物なら、そちらにも書く
                var logic = GameManager.Instance ? GameManager.Instance.LogicOptions : null;
                IGameOptions logicOpts = null;
                try { logicOpts = logic?.currentGameOptions; } catch { }
                bool separate = logicOpts != null && logicOpts.Pointer != o.Pointer;
                if (separate) logicOpts.SetByte(ByteOptionNames.MapId, MapId);
                logic?.SyncOptions();
                Logger.Info($"反転スケルドで始める (MapId 3 を送った・試合の設定は別の物: {separate})", nameof(DleksMap));
            }
            catch (Exception ex) { Logger.Error($"反転スケルドへの切り替えに失敗: {ex.Message}", nameof(DleksMap)); }
        }
    }

    // 本編はスケルドの船を左右反転するかをここで決める。ホストの手元で、始まった試合の MapId が 3 なら反転させる。
    // メニューでも呼ばれ、保存された設定が 3 のまま true を返すとメニューの立ち上げが終わらずログインできない (2026-11-26 実機)。
    // なのでオンラインの試合が始まってからだけ見る
    [HarmonyPatch(typeof(AprilFoolsMode), nameof(AprilFoolsMode.ShouldFlipSkeld))]
    static class ShouldFlipSkeldPatch
    {
        static void Postfix(ref bool __result)
        {
            if (__result) return;
            try
            {
                var client = AmongUsClient.Instance;
                if (!client || !client.AmHost || client.NetworkMode == NetworkModes.FreePlay
                    || client.GameState != InnerNet.InnerNetClient.GameStates.Started) return;
                var o = Opts;
                if (o != null && o.MapId == MapId) __result = true;
            }
            catch { }
        }
    }

    // 反転させる所が本編の更新で別の道を通るようになると、黙って反転しない船で始まる。その時はログに残す
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    static class CheckMirrorPatch
    {
        static void Postfix(ShipStatus __instance)
        {
            try
            {
                var o = Opts;
                if (__instance && o != null && o.MapId == MapId && __instance.transform.lossyScale.x > 0f)
                    Logger.Error("MapId 3 なのに船が反転していない", nameof(DleksMap));
            }
            catch { }
        }
    }

    // ロビーが作られた時 (試合の後も)。設定の読み込みは Start より前に済んでいる
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    static class StartPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix() => NormalizeLobby();
    }

    // 秒読みの取り消し・開始の失敗で戻った時。始めた後 (Starting) に呼ばれた時は、送った 3 を戻さない
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.ResetStartState))]
    static class ResetPatch
    {
        static void Prefix(GameStartManager __instance)
        {
            if (__instance && __instance.startState == GameStartManager.StartingStates.Starting) return;
            NormalizeLobby();
        }
    }
}
