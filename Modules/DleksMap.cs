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
/// 本編のかくれんぼでも選べる (本編のかくれんぼのマップの一覧には無いが、船と配役は通常モードと同じ形で動く・2026-11-26 実機)。
/// かくれんぼのロビーでも 3 を 0 と印に戻すので、どのモードでも当てる束に入れる。
/// </summary>
[TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
public static class DleksMap
{
    public const byte MapId = 3;

    // 印は本編のモード (通常・かくれんぼ) ごとに持つ。マップの設定もモードごとに別の物なので
    static readonly bool[] pending = new bool[2];
    static int ModeIndex => GameOptionsManager.Instance != null && GameOptionsManager.Instance.currentGameMode == GameModes.HideNSeek ? 1 : 0;

    /// <summary>今のモードのロビーの MapId 0 が反転スケルドを指している。</summary>
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

    /// <summary>
    /// メニューで、保存された設定の MapId 3 を 0 と印に戻す (反転スケルドで試合を終えると本編が 3 を保存する)。
    /// 本編の部屋を作る画面とロビーは 3 の札を持たないので、そこへ 3 を持ち込まない
    /// </summary>
    static void NormalizeSaved()
    {
        try
        {
            var gm = GameOptionsManager.Instance;
            if (gm == null) return;
            var normal = gm.currentNormalGameOptions;
            if (normal != null && normal.MapId == MapId)
            {
                normal.MapId = 0;
                pending[0] = true;
                Logger.Info("保存された通常モードの MapId 3 を 0 と反転スケルドの印に戻した", nameof(DleksMap));
            }
            var hns = gm.currentHideNSeekGameOptions;
            if (hns != null && hns.MapId == MapId)
            {
                hns.MapId = 0;
                pending[1] = true;
                Logger.Info("保存されたかくれんぼの MapId 3 を 0 と反転スケルドの印に戻した", nameof(DleksMap));
            }
        }
        catch (Exception ex) { Logger.Warn($"保存された MapId の戻しに失敗: {ex.Message}", nameof(DleksMap)); }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    static class MenuPatch
    {
        static void Postfix() => NormalizeSaved();
    }

    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.Start))]
    static class CreateGamePatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix() => NormalizeSaved();
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

    // ホストの手元は MapId 3 でもスケルドの船 (SkeldShip) を出してしまい、客だけが反転した船 (AprilShip) を出して位置が全部ずれる。
    // 試合を始めるホストのコルーチンの 1 歩目は、MapId が 3 でも AprilFoolsMode.ShouldFlipSkeld が false なら 0 に直してから
    // ShipPrefabs[id] を読み込むので、その 1 歩の間だけ手元の ShipPrefabs の 0 と 3 を入れ替えて、反転した船を読ませる。
    // ShouldFlipSkeld に後置を付けてはいけない: 本編の DLL では「false を返すだけ」の関数が 1 つの機械語に畳まれていて
    // (ImpostorRole.IsDead・ShouldLongAround なども同じ番地)、後置がその全部に効く。付けた役職が全部「死んだ役職」になり
    // かくれんぼでは配役の直後に全員が死ぬ・人が縦に伸びる・タスクの判定が例外を出す (どれも 2026-11-26 実機)
    [HarmonyPatch(typeof(AmongUsClient._CoStartGameHost_d__28), nameof(AmongUsClient._CoStartGameHost_d__28.MoveNext))]
    static class HostStartPatch
    {
        static bool swapped;

        static void Prefix(AmongUsClient._CoStartGameHost_d__28 __instance)
        {
            try
            {
                if (__instance.__1__state != 0) return;
                var o = Opts;
                if (o == null || o.MapId != MapId) return;
                swapped = Swap();
                if (swapped) Logger.Info("ホストの船に反転スケルドを読ませる", nameof(DleksMap));
            }
            catch (Exception ex) { Logger.Error($"ホストの船の差し替えに失敗: {ex.Message}", nameof(DleksMap)); }
        }

        static void Finalizer()
        {
            if (!swapped) return;
            swapped = false;
            try { Swap(); } catch (Exception ex) { Logger.Error($"ShipPrefabs の戻しに失敗: {ex.Message}", nameof(DleksMap)); }
        }

        static bool Swap()
        {
            var list = AmongUsClient.Instance ? AmongUsClient.Instance.ShipPrefabs : null;
            if (list == null || list.Count <= MapId) return false;
            var skeld = list[0];
            list[0] = list[MapId];
            list[MapId] = skeld;
            return true;
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
