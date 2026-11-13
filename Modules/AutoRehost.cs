using System;
using HarmonyLib;
using InnerNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// ホストが部屋から切断された時 (公式サーバーのキック・通信エラー・ロビーの放置切れなど)、同じ設定で部屋を自動で作り直す。
/// 結果画面から N 試合ごとに作り直すこと (ロビーへ戻る代わり) と、操作盤からの作り直し・番犬に起動し直された後の部屋作りもここを通す。
/// 同じプロセスで作り直せない時は、番犬 (Watchdog) が動いていればゲームごと起動し直す。
/// </summary>
/// <remarks>
/// 流れ: メインメニューへ戻る → メニューが落ち着くまで待つ → 部屋作りの画面を開く → 1 回だけ確定 → 新しい部屋に入ったら終わり。
/// 前の部屋の後片付けの途中で部屋を作るとキックされやすいので、メニューが 3 秒落ち着くまで作らない。確定は 1 回の試行で 1 回だけ。
/// 開くのは MainMenuManager.OpenCreateGame だけを使う (オンラインのメニューへ遷移する道は残った状態で次の試行を詰まらせる)。
/// 考え方と待ち時間は End K not (waffle-ful/Aeterna-End-K-not Modules/AutoRehost.cs) を、LAN の部屋になった時の扱いと
/// N 試合ごとの作り直しは TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/AutoRehost.cs・Patches/PeriodicAutoRehostPatch.cs) を元にした。
/// </remarks>
public static class AutoRehost
{
    public static OptionItem Enabled;
    public static OptionItem Delay;
    public static OptionItem MaxAttempts;
    public static OptionItem EveryGames;

    public static void SetupCustomOption()
    {
        Enabled = StringOptionItem.Create("AutoRehost", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.cyan)
            .SetGameMode(CustomGameMode.All);
        Delay = IntegerOptionItem.Create("AutoRehostDelay", new(3, 60, 1), 5, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds)
            .SetGameMode(CustomGameMode.All);
        MaxAttempts = IntegerOptionItem.Create("AutoRehostMaxAttempts", new(1, 10, 1), 3, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Times)
            .SetGameMode(CustomGameMode.All);
    }

    /// <summary>「結果画面からロビーへ戻る」の子。0 で作り直さない</summary>
    public static void SetupEveryGamesOption(OptionItem parent)
    {
        EveryGames = IntegerOptionItem.Create("AutoRehostEveryGames", new(0, 30, 1), 0, TabGroup.ModMainSettings, false).SetParent(parent)
            .SetValueFormat(OptionFormat.Times)
            .SetGameMode(CustomGameMode.All);
    }

    enum Phase { WaitClean, OpenDialog, WaitDialog, WaitJoin }

    const float PollInterval = 0.5f;
    /// <summary>メニューがこの秒数落ち着いてから部屋を作る</summary>
    const float SettleSeconds = 3f;
    /// <summary>部屋作りの画面が開かなければ開き直す</summary>
    const float DialogTimeout = 8f;
    /// <summary>新しい部屋に入れなければ、その試行をやめて次の試行へ</summary>
    const float AttemptTimeout = 45f;
    /// <summary>新しい部屋がこの秒数もったら、試した回数を戻す (作ってすぐ切れるのを繰り返さないため)</summary>
    const float StabilizeSeconds = 60f;

    public static bool Pending { get; private set; }
    public static int Attempts { get; private set; }
    public static int AttemptLimit => Mathf.Max(1, MaxAttempts?.GetInt() ?? 3);
    /// <summary>最後に止まった理由 (メニューに出す)。作り直せた・取り消した時は null</summary>
    public static string FailureText { get; private set; }

    static int seq;
    static Phase phase;
    static float deadline, cleanSince, openedAt, nextWaitLogAt;
    static int oldGameId;
    static bool wasPublic;
    /// <summary>オンラインの部屋のホストとして入った (入った時に控える。本物のキックでは切断の途中で AmHost が先に倒れることがある)</summary>
    static bool hostingOnline;
    /// <summary>自分で作り直すために抜けている (ExitGame の理由で止めない)</summary>
    static bool selfLeaving;
    static int gamesSinceRehost;

    // ===== 切断 =====

    /// <summary>InnerNetClient.DisconnectInternal の前から呼ぶ (この時点ならまだ AmHost・NetworkMode・GameId が正しい)</summary>
    public static void OnDisconnect(InnerNetClient client, DisconnectReasons reason)
    {
        if (client is not AmongUsClient c) return;
        var wasHosting = hostingOnline || (c.AmHost && GameStates.IsOnlineGame);
        hostingOnline = false;

        if (selfLeaving)
        {
            selfLeaving = false;
            if (Pending) Logger.Info($"作り直すために部屋を抜けた ({reason})", "AutoRehost");
            return;
        }
        // 自分で抜けた時は、番犬に起動し直されても部屋を作らない (自動の作り直しが OFF でも)
        var leftByUser = reason is DisconnectReasons.ExitGame or DisconnectReasons.Destroy or DisconnectReasons.Banned or DisconnectReasons.IncorrectVersion;
        if (leftByUser) Watchdog.NoteLeft();
        if (!Enabled.GetBool() || !wasHosting) return;

        if (leftByUser)
        {
            if (Pending) Cancel($"自分で抜けた ({reason})");
            return;
        }
        // ログイン・サーバーが死んでいる時は、同じプロセスで作り直しても通らない
        if (reason is DisconnectReasons.NotAuthorized or DisconnectReasons.NoServersAvailable or DisconnectReasons.ServerNotFound
            or DisconnectReasons.MatchmakerInactivity or DisconnectReasons.InternalConnectionToken or DisconnectReasons.InternalNonceFailure)
        {
            Logger.Warn($"作り直せない切断 ({reason})。ゲームの再起動が要る", "AutoRehost");
            if (Pending) ResetState();
            FailureText = Watchdog.RequestRestart($"切断 ({reason})")
                ? GetString("Watchdog.Restarting")
                : string.Format(GetString("AutoRehost.Fatal"), reason);
            return;
        }
        if (Pending) return;
        Begin(c.GameId, c.IsGamePublic, $"切断 ({reason})");
    }

    /// <summary>今の部屋を抜けて作り直す (操作盤・N 試合ごと)</summary>
    /// <returns>作り直しを始めたか (始めなかった時は呼んだ側がいつもの流れに戻す)</returns>
    public static bool RehostNow(string why)
    {
        var c = AmongUsClient.Instance;
        if (Pending || c == null || !c.AmHost || !GameStates.IsOnlineGame || GameStates.IsNotJoined) return false;
        Begin(c.GameId, c.IsGamePublic, why);
        selfLeaving = true;
        try
        {
            c.ExitGame(DisconnectReasons.ExitGame);
            return true;
        }
        catch (Exception ex)
        {
            // 抜けられなかった時に印を残すと、次の本物の切断を「自分で抜けた」と取り違える
            Logger.Warn($"部屋を抜けられなかったので作り直しをやめる: {ex.Message}", "AutoRehost");
            selfLeaving = false;
            ResetState();
            return false;
        }
    }

    /// <summary>メインメニューから部屋を作る (番犬に起動し直された後)</summary>
    public static void HostFromMenu(bool isPublic, string why)
    {
        if (Pending) return;
        Attempts = 0;
        Begin(-1, isPublic, why);
    }

    static void Begin(int gameId, bool isPublic, string why)
    {
        Pending = true;
        FailureText = null;
        oldGameId = gameId;
        wasPublic = isPublic;
        var delay = Mathf.Max(1, Delay?.GetInt() ?? 5);
        Logger.Info($"{why}。{delay} 秒後に部屋を作り直す ({Attempts + 1}/{AttemptLimit}) 前の部屋={oldGameId} 公開={wasPublic}", "AutoRehost");
        var mySeq = ++seq;
        _ = new LateTask(() => { if (seq == mySeq) BeginAttempt(); }, delay, "AutoRehost.Start", true);
    }

    // ===== 試行 =====

    static void BeginAttempt()
    {
        if (!Pending) return;
        if (++Attempts > AttemptLimit)
        {
            GiveUp();
            return;
        }
        var mySeq = ++seq;
        phase = Phase.WaitClean;
        cleanSince = openedAt = nextWaitLogAt = 0f;
        deadline = Time.realtimeSinceStartup + AttemptTimeout;
        Logger.Info($"作り直し {Attempts}/{AttemptLimit} 回目", "AutoRehost");
        if (SceneManager.GetActiveScene().name != "MainMenu")
        {
            try { SceneChanger.ChangeScene("MainMenu"); }
            catch (Exception ex) { Logger.Warn($"メインメニューへ戻れなかった: {ex.Message}", "AutoRehost"); }
        }
        _ = new LateTask(() => Tick(mySeq), 1f, "", true);
    }

    static void Tick(int mySeq)
    {
        if (!Pending || seq != mySeq) return;
        if (Time.realtimeSinceStartup > deadline)
        {
            Logger.Warn($"{Attempts} 回目は新しい部屋に入れなかった ({phase})。やり直す", "AutoRehost");
            BeginAttempt();
            return;
        }
        try { Step(); }
        catch (Exception ex) { Logger.Warn($"作り直しの途中で例外: {ex.Message}", "AutoRehost"); }
        if (!Pending || seq != mySeq) return;
        _ = new LateTask(() => Tick(mySeq), PollInterval, "", true);
    }

    static void Step()
    {
        var now = Time.realtimeSinceStartup;
        switch (phase)
        {
            case Phase.WaitClean:
                {
                    var notJoined = GameStates.IsNotJoined;
                    var noLobby = LobbyBehaviour.Instance == null;
                    var menu = UnityEngine.Object.FindObjectOfType<MainMenuManager>() != null;
                    var noOnlineMenu = UnityEngine.Object.FindObjectOfType<MMOnlineManager>() == null;
                    var eos = EOSManager.InstanceExists ? EOSManager.Instance : null;
                    var login = eos != null && eos.loginFlowFinished;
                    if (!(notJoined && noLobby && menu && noOnlineMenu && login && Options.IsLoaded))
                    {
                        cleanSince = 0f;
                        if (now >= nextWaitLogAt)
                        {
                            nextWaitLogAt = now + 2f;
                            Logger.Info($"メニューが落ち着くのを待つ (未参加={notJoined} ロビー無し={noLobby} メニュー={menu} オンライン画面無し={noOnlineMenu} ログイン={login})", "AutoRehost");
                        }
                        return;
                    }
                    if (cleanSince == 0f) { cleanSince = now; return; }
                    if (now - cleanSince < SettleSeconds) return;
                    phase = Phase.OpenDialog;
                    return;
                }
            case Phase.OpenDialog:
                {
                    var mm = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
                    if (mm == null) { phase = Phase.WaitClean; cleanSince = 0f; return; }
                    // 起動直後のメニューは LAN のまま。メニューの「オンライン」を通らずに部屋作りを開くので、ここで合わせる
                    AmongUsClient.Instance.NetworkMode = NetworkModes.OnlineGame;
                    mm.OpenCreateGame();
                    Logger.Info("部屋作りの画面を開いた", "AutoRehost");
                    openedAt = now;
                    phase = Phase.WaitDialog;
                    return;
                }
            case Phase.WaitDialog:
                {
                    var cgo = UnityEngine.Object.FindObjectOfType<CreateGameOptions>();
                    if (cgo != null && cgo.isActiveAndEnabled)
                    {
                        Logger.Info($"部屋作りを確定する (地域={ServerManager.Instance?.CurrentRegion?.Name})", "AutoRehost");
                        phase = Phase.WaitJoin; // 確定は 1 回の試行で 1 回だけ
                        cgo.Confirm();
                        return;
                    }
                    if (now - openedAt > DialogTimeout)
                    {
                        Logger.Warn("部屋作りの画面が開かない。開き直す", "AutoRehost");
                        phase = Phase.OpenDialog;
                    }
                    return;
                }
            case Phase.WaitJoin:
                return; // 入れたかは OnGameJoined で見る。来なければ AttemptTimeout でやり直す
        }
    }

    // ===== 入室 =====

    /// <summary>AmongUsClient.OnGameJoined の後から呼ぶ</summary>
    public static void OnGameJoined()
    {
        var c = AmongUsClient.Instance;
        if (c == null) return;
        hostingOnline = c.AmHost && GameStates.IsOnlineGame;
        if (hostingOnline) Watchdog.NoteHosting(c.IsGamePublic || (Pending && wasPublic));
        if (!Pending || !c.AmHost || c.GameId == oldGameId) return;

        if (!GameStates.IsOnlineGame)
        {
            // ログインが切れていると LAN の部屋になり、誰も入れない。居続けずに抜けて止める
            Logger.Warn($"オンラインでない部屋 ({c.GameId}, {c.NetworkMode}) になったので抜けて止める", "AutoRehost");
            ResetState();
            selfLeaving = true;
            try { c.ExitGame(DisconnectReasons.ExitGame); } catch (Exception ex) { Logger.Warn(ex.Message, "AutoRehost"); }
            // ログインはこのプロセスでは戻らないので、番犬が動いていればゲームごと起動し直す
            FailureText = Watchdog.RequestRestart("オンラインでない部屋になった")
                ? GetString("Watchdog.Restarting")
                : GetString("AutoRehost.NotOnline");
            return;
        }

        Logger.Info($"新しい部屋 {c.GameId} に入った ({Attempts} 回目)", "AutoRehost");
        Pending = false;
        var mySeq = ++seq;
        gamesSinceRehost = 0;
        var restorePublic = wasPublic;
        _ = new LateTask(() =>
        {
            if (seq != mySeq || !GameStates.IsLobby || !AmongUsClient.Instance.AmHost) return;
            if (restorePublic && !AmongUsClient.Instance.IsGamePublic) AmongUsClient.Instance.ChangeGamePublic(true);
            Logger.SendInGame(GetString("AutoRehost.Done"));
        }, 2f, "AutoRehost.Done", true);
        _ = new LateTask(() => { if (seq == mySeq && !Pending) Attempts = 0; }, StabilizeSeconds, "AutoRehost.Stabilize", true);
    }

    // ===== N 試合ごと =====

    /// <summary>試合が終わった時に数える (廃村は数えない)</summary>
    public static void OnGameEnded()
    {
        if (CustomWinnerHolder.WinnerTeam is CustomWinner.Draw or CustomWinner.None) return;
        gamesSinceRehost++;
    }

    /// <summary>結果画面からロビーへ戻る代わりに作り直すか</summary>
    public static bool DueAfterGame()
    {
        var every = EveryGames?.GetInt() ?? 0;
        return every > 0 && gamesSinceRehost >= every && GameStates.IsOnlineGame;
    }

    // ===== 止める =====

    public static void Cancel(string why)
    {
        if (!Pending) return;
        // 取り消した部屋は、番犬に起動し直されても作らない
        Watchdog.NoteLeft();
        Logger.Info($"作り直しをやめた ({why})", "AutoRehost");
        ResetState();
    }

    static void GiveUp()
    {
        Logger.Warn($"{AttemptLimit} 回試しても作り直せなかった", "AutoRehost");
        ResetState();
        // 同じプロセスで作り直せない (ログインや通信が壊れている) ので、番犬が動いていればゲームごと起動し直す
        FailureText = Watchdog.RequestRestart("作り直しの失敗が続いた")
            ? GetString("Watchdog.Restarting")
            : string.Format(GetString("AutoRehost.GaveUp"), AttemptLimit);
    }

    static void ResetState()
    {
        Pending = false;
        Attempts = 0;
        seq++;
    }

    public static void DismissFailure() => FailureText = null;

    /// <summary>メニューに出す今の様子</summary>
    public static string StatusText()
    {
        if (Pending) return string.Format(GetString("AutoRehost.Status"), Mathf.Max(1, Attempts), AttemptLimit);
        return FailureText;
    }

    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.Start))]
    static class EndGameStartPatch
    {
        public static void Postfix()
        {
            if (AmongUsClient.Instance.AmHost) OnGameEnded();
        }
    }
}
