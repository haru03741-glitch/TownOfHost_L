using HarmonyLib;
using TMPro;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// ホストが操作しなくても部屋が回るようにする 2 つの自動進行。
/// ・結果画面から数秒でロビーへ戻る (「もう一度遊ぶ」を押す)
/// ・ロビーで一定時間が経ち、人数が揃って落ち着いたら試合を始める
/// </summary>
/// <remarks>
/// 結果画面の秒読みは EndlessHostRoles (Gurge44/EndlessHostRoles Patches/EndGameManagerPatch.cs AutoPlayAgain) を、
/// NextGame の再試行と、開始前に人数と外見が落ち着くのを待つ条件は TownOfHost-hamo
/// (rar006/TownOfHost-hamo@f7b00a9e Patches/AutoReturnToRoomPatch.cs・AutoStartWhenTimerLowPatch.cs) を元にした。
/// </remarks>
public static class AutoProgress
{
    public static OptionItem ReturnToLobby;
    public static OptionItem ReturnToLobbyDelay;
    public static OptionItem AutoStart;
    public static OptionItem AutoStartWait;
    public static OptionItem AutoStartMinPlayers;
    public static OptionItem AutoStartFullWait;

    public static void SetupCustomOption()
    {
        ReturnToLobby = StringOptionItem.Create("AutoReturnToLobby", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.All);
        ReturnToLobbyDelay = IntegerOptionItem.Create("AutoReturnToLobbyDelay", new(3, 30, 1), 5, TabGroup.ModMainSettings, false).SetParent(ReturnToLobby)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        AutoRehost.SetupEveryGamesOption(ReturnToLobby);
        AutoStart = StringOptionItem.Create("AutoStartGame", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.All);
        AutoStartWait = IntegerOptionItem.Create("AutoStartWait", new(30, 600, 10), 180, TabGroup.ModMainSettings, false).SetParent(AutoStart)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        AutoStartMinPlayers = IntegerOptionItem.Create("AutoStartMinPlayers", new(4, 15, 1), 4, TabGroup.ModMainSettings, false).SetParent(AutoStart)
            .SetValueFormat(OptionFormat.Players)
            .SetGameMode(CustomGameMode.All);
        AutoStartFullWait = IntegerOptionItem.Create("AutoStartFullWait", new(10, 600, 10), 30, TabGroup.ModMainSettings, false).SetParent(AutoStart)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
    }

    // ===== 結果画面からロビーへ =====

    const int MaxNextGameAttempts = 3;
    static int returnGeneration;
    static TextMeshPro countdownText;

    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.ShowButtons))]
    static class EndGameShowButtonsPatch
    {
        public static void Postfix(EndGameManager __instance)
        {
            if (!AmongUsClient.Instance.AmHost || !ReturnToLobby.GetBool()) return;
            var generation = ++returnGeneration;
            Countdown(__instance, generation, ReturnToLobbyDelay.GetInt());
        }
    }

    static void Countdown(EndGameManager manager, int generation, int seconds)
    {
        if (generation != returnGeneration || manager == null || GameStates.IsLobby) { ClearText(); return; }
        if (countdownText == null)
        {
            // 勝ち負けの大きな字と重ならないよう、下の説明の更に下に小さく出す
            var go = new GameObject("AutoReturnCountdown");
            go.transform.SetParent(manager.transform, false);
            go.transform.localPosition = new(0f, -0.9f, -10f);
            countdownText = go.AddComponent<TextMeshPro>();
            countdownText.alignment = TextAlignmentOptions.Center;
            countdownText.fontSize = 3f;
            countdownText.color = Color.white;
        }
        if (countdownText != null) countdownText.text = string.Format(GetString("AutoReturnCountdown"), seconds);
        if (seconds > 0)
        {
            _ = new LateTask(() => Countdown(manager, generation, seconds - 1), 1f, "AutoReturnToLobby", true);
            return;
        }
        ClearText();
        if (AutoRehost.DueAfterGame() && AutoRehost.RehostNow($"{AutoRehost.EveryGames.GetInt()} 試合ごとの作り直し")) return;
        TryNextGame(manager, generation, 1);
    }

    /// <summary>NextGame は遷移の途中だと例外を出すことがあるので、間を空けて数回やり直す</summary>
    static void TryNextGame(EndGameManager manager, int generation, int attempt)
    {
        if (generation != returnGeneration || !AmongUsClient.Instance.AmHost || GameStates.IsLobby || manager == null) return;
        // 「もう一度遊ぶ」の本体は結果画面が持っている (シングルトンとしては登録されていない)
        var nav = manager.Navigation;
        if (nav == null) return;
        try
        {
            nav.NextGame();
        }
        catch (System.Exception ex)
        {
            if (attempt >= MaxNextGameAttempts)
            {
                Logger.Error($"NextGame が {MaxNextGameAttempts} 回とも失敗した: {ex}", "AutoReturnToLobby");
                return;
            }
            Logger.Warn($"NextGame が失敗した ({attempt}/{MaxNextGameAttempts})。3 秒後にやり直す: {ex.Message}", "AutoReturnToLobby");
            _ = new LateTask(() => TryNextGame(manager, generation, attempt + 1), 3f, "AutoReturnToLobby.Retry", true);
        }
    }

    static void ClearText()
    {
        if (countdownText != null) Object.Destroy(countdownText.gameObject);
        countdownText = null;
    }

    // ===== ロビーで自動スタート =====

    /// <summary>人数が変わってから、この秒数は始めない (入ってきた人の準備を待つ)</summary>
    const float JoinSettleSeconds = 5f;
    static float lobbyTimer;
    static int lastPlayerCount;
    static int lastGameId;
    static float countChangedAt;

    /// <summary>ロビーにいる時間 (秒)。表示用</summary>
    public static float LobbySeconds => lobbyTimer;

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]
    static class LobbyUpdatePatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            // 部屋が変わったら数え直す (前の部屋の時間が残ると、作り直した直後に始まってしまう)
            var gameId = AmongUsClient.Instance.GameId;
            if (gameId != lastGameId)
            {
                lastGameId = gameId;
                lobbyTimer = 0f;
                lastPlayerCount = 0;
                countChangedAt = Time.time;
            }
            if (!AutoStart.GetBool())
            {
                lobbyTimer = 0f;
                return;
            }
            var count = PlayerControl.AllPlayerControls.Count;
            if (count != lastPlayerCount)
            {
                lastPlayerCount = count;
                countChangedAt = Time.time;
            }
            lobbyTimer += Time.deltaTime;

            var full = Main.NormalOptions != null && count >= Main.NormalOptions.MaxPlayers;
            var limit = full ? Mathf.Min(AutoStartFullWait.GetInt(), AutoStartWait.GetInt()) : AutoStartWait.GetInt();
            if (lobbyTimer < limit || !IsReadyToStart(count)) return;

            lobbyTimer = 0f;
            Logger.Info($"ロビーで {limit} 秒経ち {count} 人が揃ったので始める", "AutoStart");
            if (!GameStartManager.InstanceExists) return;
            var gsm = GameStartManager.Instance;
            // 自動スタートは BeginGame を通らないので、配信のマップ投票はここで決めて送る
            if (Streaming.MapVote.ApplyAtGameStart()) Streaming.MapVote.SyncSettings();
            gsm.countDownTimer = 5f;
            gsm.startState = GameStartManager.StartingStates.Countdown;
        }
    }

    static bool IsReadyToStart(int count)
    {
        if (count < AutoStartMinPlayers.GetInt()) return false;
        if (Time.time - countChangedAt < JoinSettleSeconds) return false;
        if (!GameStartManager.InstanceExists) return false;
        var gsm = GameStartManager.Instance;
        if (gsm == null || gsm.startState != GameStartManager.StartingStates.NotStarting) return false;
        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc == null || pc.Data == null || pc.Data.Disconnected) return false;
            if (pc.Data.DefaultOutfit == null || pc.Data.DefaultOutfit.ColorId < 0) return false;
        }
        return true;
    }
}
