using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 画面の左下のボタン (<see cref="HostPanelButton"/>) から開く、ホストの操作盤 (IMGUI の浮かせた窓)。
/// チャットのコマンドやキーの組み合わせを覚えなくても、ロビーと試合の操作・人の管理・ゲームログを押すだけで使えるようにする。
/// 窓は題の帯をつかんで動かせる。押すと取り返しのつかない操作 (キック・BAN・廃村) は、2 回押した時だけ行う。
/// </summary>
/// <remarks>
/// 浮かせた窓に操作を並べる形と、閉じている間は OnGUI ごと止める作りは EndlessHostRoles (Modules/ClientControlGUI.cs) と End K not (waffle-ful/Aeterna-End-K-not Modules/ImguiHost.cs) を元にした。
/// OnGUI は 1 フレームに何度も呼ばれるので、中身 (文字・大きさ・押した時の処理) は 0.25 秒ごとか押した後にだけ組み直し、
/// 毎回の OnGUI は組んだ物を描くだけにする (文字を本編へ渡し直したり、Rect の値を読んだりしない)。
/// </remarks>
public class HostPanel : MonoBehaviour
{
    public HostPanel(IntPtr ptr) : base(ptr) { }

    public static HostPanel Instance;
    public static bool IsOpen;

    // 窓の位置と大きさ (Rect の値の読み出しは本編を呼ぶので、手元の数で持つ)
    float wx, wy, ww, wh;
    bool windowReady;
    float contentHeight;
    bool dragging;
    float dragDx, dragDy;
    bool showLog;
    float scrollY;
    // 中身を描く枠 (画面の座標)
    float vx, vy, vw, vh;

    // 2 回押しの確かめ: 押した物の名前と時刻
    string armedKey;
    float armedAt;
    const float ArmSeconds = 3f;

    float scale = -1f;
    float sw, sh;
    GUIStyle sWindow, sTitle, sSection, sLabel, sAction, sHost, sDanger, sOn, sSmall;
    GUIContent titleContent;

    float Scale => sw / 1920f;
    int Font => Mathf.Max(12, Mathf.RoundToInt(22f * Scale));
    float RowH => 52f * Scale;
    float Pad => 8f * Scale;
    float Width => 460f * Scale;

    public void Awake()
    {
        Instance = this;
        // GUILayout を使わないので、Layout の回を止める (OnGUI が 1 フレーム 1 回分減る)
        useGUILayout = false;
        // 開くまでは OnGUI を回さない (HostPanelButton が開け閉めに合わせて切り替える)
        enabled = false;
    }

    /// <summary>閉じたら組んだ中身を放す (前の試合のプレイヤーなどを掴み続けず、開き直したら組み直してから押しを受ける)</summary>
    public void OnDisable()
    {
        for (var i = 0; i < items.Count; i++) items[i].Action = null;
        itemCount = 0;
        dirty = true;
    }

    public void OnGUI()
    {
        sw = Screen.width;
        sh = Screen.height;
        if (AmongUsClient.Instance == null) return;
        if (!Mathf.Approximately(scale, Scale)) BuildStyles();
        if (!HostPanelButton.InRoom)
        {
            DrawRehostStatus();
            return;
        }
        if (IsOpen) DrawWindow();
    }

    /// <summary>部屋の作り直しの様子 (メニューにいる間だけ)。作り直し中は取り消し、止まった時は閉じるボタンを付ける</summary>
    void DrawRehostStatus()
    {
        var text = AutoRehost.StatusText();
        if (text == null) return;
        var w = Width * 1.3f;
        var h = RowH * 2.4f;
        // 上の帯 (名前・版) と、メニューのボタンの列に重ならない下の真ん中
        var x = (sw - w) / 2f;
        var y = sh * 0.97f - h;
        GUI.Box(new Rect(x, y, w, h), GUIContent.none, sWindow);
        GUI.Label(new Rect(x + Pad, y + Pad, w - Pad * 2f, RowH), text, sLabel);
        var b = new Rect(x + Pad, y + Pad + RowH * 1.1f, w - Pad * 2f, RowH);
        if (AutoRehost.Pending)
        {
            if (GUI.Button(b, GetString("AutoRehost.Cancel"), sDanger)) AutoRehost.Cancel("取り消した");
        }
        else if (GUI.Button(b, GetString("HostPanel.Close"), sAction)) AutoRehost.DismissFailure();
    }

    // ===== 窓 =====

    void DrawWindow()
    {
        var e = Event.current;
        var type = e.type;
        if (!windowReady)
        {
            wx = Pad * 2f + 64f * Scale;
            wy = sh * 0.12f;
            windowReady = true;
        }
        var titleH = RowH * 0.9f;
        ww = Width;
        vw = ww - Pad * 4f - 18f * Scale - Pad;
        // 中身は描く回 (Repaint) の頭でだけ組み直す。入力の回は前に描いた物と同じ並びで判定する (開き直した直後でまだ組んでいなければ入力の回でも組む)
        if ((type == EventType.Repaint || itemCount == 0) && (dirty || Time.unscaledTime >= nextBuild || !Mathf.Approximately(builtWidth, vw)))
            Build();
        // 高さは中身に合わせる (会議の札やタスクの一覧をなるべく隠さない)
        wh = Mathf.Min(sh * 0.70f, titleH + Pad * 3f + Mathf.Max(contentHeight, RowH));
        HandleDrag(e, type);
        wx = Mathf.Clamp(wx, 0f, sw - ww);
        wy = Mathf.Clamp(wy, 0f, sh - wh);

        GUI.Box(new Rect(wx, wy, ww, wh), GUIContent.none, sWindow);
        GUI.Label(new Rect(wx, wy + Pad * 0.5f, ww, titleH), titleContent, sTitle);
        // 題の帯の右の閉じるボタン (左下の開閉ボタンが出ない結果の画面でも閉じられるように)
        var cs = titleH * 0.8f;
        if (GUI.Button(new Rect(wx + ww - cs - Pad, wy + (titleH - cs) / 2f + Pad * 0.5f, cs, cs), "×", sAction))
        {
            IsOpen = false;
            return;
        }

        // 中身は画面の座標で直接描く (GUI のグループやスクロールの枠を使わない)。
        // 枠に全部が入らない部品は描かず、スクロールはホイールと右端のスクロールバーで動かす
        var barW = 18f * Scale;
        vx = wx + Pad * 2f;
        vy = wy + titleH + Pad;
        vh = wh - titleH - Pad * 2f;
        var maxScroll = Mathf.Max(0f, contentHeight - vh);
        if (type == EventType.ScrollWheel && Inside(e.mousePosition, wx, wy, ww, wh))
        {
            scrollY += e.delta.y * RowH * 0.5f;
            e.Use();
        }
        if (maxScroll > 0f)
            scrollY = GUI.VerticalScrollbar(new Rect(vx + vw + Pad, vy, barW, vh), scrollY, vh, 0f, contentHeight);
        scrollY = Mathf.Clamp(scrollY, 0f, maxScroll);

        try { DrawItems(); }
        catch (Exception ex) { Logger.Exception(ex, nameof(HostPanel)); }
    }

    static bool Inside(Vector2 p, float x, float y, float w, float h) => p.x >= x && p.x <= x + w && p.y >= y && p.y <= y + h;

    /// <summary>画面の座標 (Input.mousePosition・左下から) が、開いている操作盤の窓の上か</summary>
    public static bool ContainsScreen(Vector2 inputPos)
    {
        var self = Instance;
        if (!IsOpen || self == null || !self.windowReady) return false;
        return Inside(new Vector2(inputPos.x, Screen.height - inputPos.y), self.wx, self.wy, self.ww, self.wh);
    }

    void HandleDrag(Event e, EventType type)
    {
        switch (type)
        {
            // 題の帯のうち、右の閉じるボタンを除いた所
            case EventType.MouseDown when Inside(e.mousePosition, wx, wy, ww - RowH - Pad * 2f, RowH):
                {
                    var p = e.mousePosition;
                    dragging = true;
                    dragDx = p.x - wx;
                    dragDy = p.y - wy;
                    e.Use();
                    break;
                }
            case EventType.MouseDrag when dragging:
                {
                    var p = e.mousePosition;
                    wx = p.x - dragDx;
                    wy = p.y - dragDy;
                    e.Use();
                    break;
                }
            case EventType.MouseUp:
                dragging = false;
                break;
        }
    }

    // ===== 組んだ中身 =====

    enum Kind : byte { Text, Button }

    /// <summary>中身の 1 つ。中身の座標 (枠の左上が 0,0) と、本編へ渡し済みの文字を持つ</summary>
    sealed class Item
    {
        public Kind Kind;
        public float X, Y, W, H;
        public GUIStyle Style;
        public Action Action;
        public string Text;
        public readonly GUIContent Content = new();
        /// <summary>前に測った幅 (文字と幅が同じなら高さを測り直さない)</summary>
        public float MeasuredW = -1f, MeasuredH;

        public void SetText(string text)
        {
            if (Text == text) return;
            Text = text;
            Content.text = text;
            MeasuredW = -1f;
        }
    }

    readonly List<Item> items = new();
    int itemCount;
    bool dirty = true;
    float nextBuild;
    float builtWidth = -1f;
    const float BuildInterval = 0.25f;

    Item Add(Kind kind, string text, GUIStyle style, Action action, float x, float y, float w, float h)
    {
        if (itemCount == items.Count) items.Add(new Item());
        var it = items[itemCount++];
        it.Kind = kind;
        it.SetText(text);
        it.Style = style;
        it.Action = action;
        it.X = x; it.Y = y; it.W = w; it.H = h;
        return it;
    }

    void Build()
    {
        dirty = false;
        nextBuild = Time.unscaledTime + BuildInterval;
        builtWidth = vw;
        itemCount = 0;
        var y = 0f;
        try { DrawContent(ref y, vw); }
        catch (Exception ex) { Logger.Exception(ex, nameof(HostPanel)); }
        contentHeight = y;
        // 使わなくなった物は押した時の処理を放す (古い試合の物を掴まない)
        for (var i = itemCount; i < items.Count; i++) items[i].Action = null;
    }

    void DrawItems()
    {
        var top = vy - 0.5f;
        var bottom = vy + vh + 0.5f;
        for (var i = 0; i < itemCount; i++)
        {
            var it = items[i];
            var y = vy + it.Y - scrollY;
            // 枠に全部が入らない物は描かない
            if (y < top || y + it.H > bottom) continue;
            var rect = new Rect(vx + it.X, y, it.W, it.H);
            if (it.Kind == Kind.Text)
            {
                GUI.Label(rect, it.Content, it.Style);
                continue;
            }
            if (it.Action == null)
            {
                GUI.enabled = false;
                GUI.Button(rect, it.Content, it.Style);
                GUI.enabled = true;
                continue;
            }
            if (!GUI.Button(rect, it.Content, it.Style)) continue;
            dirty = true;
            try { it.Action(); }
            catch (Exception ex) { Logger.Exception(ex, nameof(HostPanel)); }
        }
    }

    // ===== 中身 =====

    void DrawContent(ref float y, float w)
    {
        var amHost = AmongUsClient.Instance.AmHost;
        if (!amHost)
        {
            Label(ref y, w, GetString("HostPanel.NotHost"));
            LogSection(ref y, w);
            return;
        }
        if (GameStates.IsLobby) LobbySection(ref y, w);
        if (GameStates.IsInGame) GameSection(ref y, w);
        StreamSection(ref y, w);
        PlayersSection(ref y, w);
        LogSection(ref y, w);
    }

    void LobbySection(ref float y, float w)
    {
        Section(ref y, w, GetString("HostPanel.Lobby"));
        // .Instance は無い時に空の物を作ってしまうので、在る時だけ読む
        var gsm = GameStartManager.InstanceExists ? GameStartManager.Instance : null;
        if (gsm != null)
        {
            if (!GameStates.IsCountDown)
                Button(ref y, w, GetString("HostPanel.StartGame"), sHost, () => Lobby()?.BeginGame());
            else
            {
                Row(ref y, w,
                    (GetString("HostPanel.StartNow"), sHost, () => { if (Lobby() is { } g) g.countDownTimer = 0f; }),
                    (GetString("HostPanel.CancelStart"), sDanger, () => Lobby()?.ResetStartState()));
            }
        }

        // 自動スタート
        var auto = AutoProgress.AutoStart.GetBool();
        var autoText = GetString("AutoStartGame") + ": " + GetString(auto ? "ColoredOn" : "ColoredOff");
        if (auto) autoText += $"  ({Mathf.Max(0, AutoProgress.AutoStartWait.GetInt() - (int)AutoProgress.LobbySeconds)}s)";
        Button(ref y, w, autoText, auto ? sOn : sAction, () => AutoProgress.AutoStart.SetValue(AutoProgress.AutoStart.GetBool() ? 0 : 1));

        // 前の試合に居た人
        if (JoinGuard.PreviousPlayers.GetValue() != 0)
        {
            var detected = JoinGuard.Detected;
            Label(ref y, w, string.Format(GetString("HostPanel.PreviousPlayers"), detected.Count)
                + (JoinGuard.AllowedThisLobby ? "  " + GetString("HostPanel.AllowedNow") : ""));
            foreach (var (clientId, name) in new List<KeyValuePair<int, string>>(detected))
            {
                var id = clientId;
                Row(ref y, w, (name, sSmall, null), (Armed($"kp{id}", GetString("HostPanel.Kick")), sDanger, () => Arm($"kp{id}", () => JoinGuard.KickDetected(id))));
            }
            if (detected.Count > 0 && !JoinGuard.AllowedThisLobby)
                Row(ref y, w,
                    (Armed("kpall", GetString("HostPanel.KickAll")), sDanger, () => Arm("kpall", JoinGuard.KickDetected)),
                    (GetString("HostPanel.AllowThisLobby"), sAction, JoinGuard.AllowThisLobby));
            else if (detected.Count > 0)
                Button(ref y, w, Armed("kpall", GetString("HostPanel.KickAll")), sDanger, () => Arm("kpall", JoinGuard.KickDetected));
            else if (!JoinGuard.AllowedThisLobby)
                Button(ref y, w, GetString("HostPanel.AllowThisLobby"), sAction, JoinGuard.AllowThisLobby);
        }

        PollRows(ref y, w);

        Row(ref y, w,
            (GetString("HostPanel.ShowSettings"), sAction, () => { Main.isChatCommand = true; Utils.ShowActiveSettings(); }),
            (GetString("HostPanel.ShowLastResult"), sAction, () => Utils.ShowLastResult()));
        Button(ref y, w, GetString("HostPanel.PostStats"), sAction, PlayerStats.PostAll);
        if (GameStates.IsOnlineGame)
            Button(ref y, w, Armed("rehost", GetString("HostPanel.Rehost")), sDanger, () => Arm("rehost", () => AutoRehost.RehostNow("操作盤から")));
        WatchdogRows(ref y, w);
        WebhookRows(ref y, w);
    }

    /// <summary>押した時のロビーの開始の管理 (場面が替わって無ければ null。.Instance は無い時に空の物を作ってしまうので、在る時だけ読む)</summary>
    static GameStartManager Lobby() => GameStartManager.InstanceExists ? GameStartManager.Instance : null;

    // 番犬: 切り替えと今の様子。動いている時だけ、ゲームごと再起動する (起動し直した後に部屋を作り直す)
    void WatchdogRows(ref float y, float w)
    {
        // 番犬の再起動は自動の作り直しが ON の時の切断から上がるので、切り替えを隣に置く
        var rehost = AutoRehost.Enabled.GetBool();
        Button(ref y, w, GetString("AutoRehost") + ": " + GetString(rehost ? "ColoredOn" : "ColoredOff"), rehost ? sOn : sAction,
            () => AutoRehost.Enabled.SetValue(AutoRehost.Enabled.GetBool() ? 0 : 1));
        if (!Watchdog.IsSupported) return;
        var on = Watchdog.Enabled;
        Row(ref y, w,
            (GetString("HostPanel.Watchdog") + ": " + Watchdog.StatusText(), on ? sOn : sAction, Watchdog.Toggle),
            (Armed("restart", GetString("HostPanel.RestartGame")), sDanger, Watchdog.CanRestart ? () => Arm("restart", () => Watchdog.RequestRestart("操作盤から")) : null));
    }

    // Discord の Webhook: URL が無ければクリップボードから貼るボタン、あれば送るかの切り替えと試しの送信
    // (IMGUI の入力欄は本編で削られていて使えないので、貼り付けで入れる)
    string webhookNote;

    void WebhookRows(ref float y, float w)
    {
        if (!DiscordWebhook.HasUrl)
        {
            Label(ref y, w, webhookNote ?? GetString("HostPanel.WebhookHint"));
            Button(ref y, w, GetString("HostPanel.WebhookPaste"), sAction, () =>
            {
                var ok = DiscordWebhook.SetUrl(ReadClipboard());
                webhookNote = ok ? null : GetString("Webhook.BadUrl");
                if (ok) Main.WebhookSendResult.Value = true;
            });
            return;
        }
        var on = Main.WebhookSendResult.Value;
        Row(ref y, w,
            (GetString("HostPanel.WebhookSend") + ": " + GetString(on ? "ColoredOn" : "ColoredOff"), on ? sOn : sAction, () => Main.WebhookSendResult.Value = !Main.WebhookSendResult.Value),
            (GetString("HostPanel.WebhookTest"), sAction, () => DiscordWebhook.Command(new ChatCommandContext(new[] { "/webhook", "test" }, PlayerControl.LocalPlayer, null))),
            (Armed("whclear", GetString("HostPanel.WebhookClear")), sDanger, () => Arm("whclear", () => { Main.WebhookURL.Value = "none"; Main.WebhookSendResult.Value = false; })));
    }

    /// <summary>Unity の systemCopyBuffer はこの本編では空を返すことがあるので、本編の Windows のクリップボードの読み取りを先に使う</summary>
    static string ReadClipboard()
    {
        string clip = null;
        try { clip = ClipboardHelper.GetClipboardString(); } catch { }
        if (string.IsNullOrWhiteSpace(clip)) clip = GUIUtility.systemCopyBuffer;
        return clip;
    }

    // 配信: コメントを読む YouTube の配信 (URL はクリップボードから貼る)・画面の表示・視聴者の干渉・マップ投票・OBS 用のページ
    string youtubeNote;

    void StreamSection(ref float y, float w)
    {
        Section(ref y, w, GetString("HostPanel.Stream"));
        if (!Streaming.YouTubeChat.Active)
        {
            if (youtubeNote != null) Label(ref y, w, youtubeNote);
            Button(ref y, w, GetString("HostPanel.YouTubePaste"), sAction,
                () => youtubeNote = Streaming.YouTubeChat.Start(ReadClipboard()) ? null : GetString("YouTube.BadUrl"));
        }
        else
            Row(ref y, w,
                (Streaming.Audience.YouTubeStatus(), sSmall, null),
                (Armed("ytstop", GetString("HostPanel.YouTubeStop")), sDanger, () => Arm("ytstop", () => Streaming.YouTubeChat.Stop())));

        // 画面の表示
        Row(ref y, w,
            Toggle("HostPanel.StreamBoard", Streaming.StreamHub.ShowBoard),
            Toggle("HostPanel.StreamSeats", Streaming.StreamHub.ShowSeats));
        Row(ref y, w,
            Toggle("HostPanel.StreamHideMeeting", Streaming.StreamHub.HideInMeeting),
            Toggle("HostPanel.StreamSound", Streaming.StreamHub.Sound));
        var scale = Streaming.StreamHub.UiScale.Value;
        Row(ref y, w,
            ($"{GetString("HostPanel.StreamScale")}: {scale}%", sAction, () =>
            {
                var list = Streaming.StreamHub.Scales;
                var i = Array.IndexOf(list, Streaming.StreamHub.UiScale.Value);
                Streaming.StreamHub.UiScale.Value = list[(i + 1) % list.Length];
            }),
            (GetString("HostPanel.StreamLayoutReset"), sAction, Streaming.StreamOverlay.ResetPosition));
        Button(ref y, w, GetString(Streaming.StreamOverlay.Editing ? "HostPanel.StreamLayoutDone" : "HostPanel.StreamLayout"), Streaming.StreamOverlay.Editing ? sOn : sAction,
            () => Streaming.StreamOverlay.Editing = !Streaming.StreamOverlay.Editing);

        // 視聴者の干渉とマップ投票
        var on = Streaming.Audience.Enabled.GetBool();
        var text = GetString("HostPanel.StreamAudience") + ": " + GetString(on ? "ColoredOn" : "ColoredOff");
        if (on && Streaming.Audience.QueueCount > 0) text += $"  ({Streaming.Audience.QueueCount})";
        Row(ref y, w,
            (text, on ? sOn : sAction, () => Streaming.Audience.Enabled.SetValue(Streaming.Audience.Enabled.GetBool() ? 0 : 1)),
            Toggle("HostPanel.StreamMapVote", Streaming.StreamHub.MapVoteOn));

        // OBS 用のページ
        var obs = Streaming.StreamHub.ObsOn.Value;
        if (!obs)
            Button(ref y, w, GetString("HostPanel.StreamObs") + ": " + GetString("ColoredOff"), sAction, () => Streaming.StreamHub.ObsOn.Value = true);
        else
        {
            Row(ref y, w,
                (GetString("HostPanel.StreamObs") + ": " + GetString("ColoredOn"), sOn, () => Streaming.StreamHub.ObsOn.Value = false),
                (obsNote ?? GetString("HostPanel.StreamObsCopy"), sAction, () =>
                {
                    GUIUtility.systemCopyBuffer = Streaming.ObsServer.Url;
                    obsNote = GetString("HostPanel.Copied");
                    obsNoteUntil = Time.unscaledTime + 2f;
                }));
            if (Streaming.ObsServer.Error != null) Label(ref y, w, Streaming.ObsServer.Error);
        }
        if (obsNote != null && Time.unscaledTime > obsNoteUntil) obsNote = null;
        if (Streaming.StreamHub.Feed.Count > 0)
            Button(ref y, w, GetString("HostPanel.StreamClear"), sAction, Streaming.StreamHub.ClearFeed);
    }

    string obsNote;
    float obsNoteUntil;

    /// <summary>手元の設定の入り切りのボタン (押した時の値を読み直して反転する)</summary>
    (string, GUIStyle, Action) Toggle(string key, BepInEx.Configuration.ConfigEntry<bool> entry)
        => ($"{GetString(key)}: {GetString(entry.Value ? "ColoredOn" : "ColoredOff")}", entry.Value ? sOn : sAction, () => entry.Value = !entry.Value);

    // ロビーの投票: 投票中は選択肢ごとの票 (押すと自分の票) と締めるボタン、それ以外はプリセットの投票を始めるボタン
    void PollRows(ref float y, float w)
    {
        if (!LobbyPoll.Active)
        {
            Button(ref y, w, GetString("HostPanel.StartPresetVote"), sAction, LobbyPoll.StartPresetVote);
            return;
        }
        Label(ref y, w, $"{LobbyPoll.Question}  ({(int)LobbyPoll.Remaining}s)");
        var mine = LobbyPoll.VoteOf(PlayerControl.LocalPlayer.PlayerId);
        for (var i = 0; i < LobbyPoll.Choices.Count; i++)
        {
            var choice = i;
            var text = $"{i + 1}: {LobbyPoll.Choices[i]}  [{LobbyPoll.CountOf(i)}]";
            Button(ref y, w, text, mine == i ? sOn : sAction, () => LobbyPoll.VoteAsHost(choice));
        }
        Button(ref y, w, GetString("HostPanel.EndPoll"), sHost, LobbyPoll.Finish);
    }

    void GameSection(ref float y, float w)
    {
        Section(ref y, w, GetString("HostPanel.InGame"));
        if (GameStates.IsMeeting)
            Button(ref y, w, GetString("HostPanel.EndMeeting"), sHost, () => { if (MeetingHud.Instance != null) MeetingHud.Instance.RpcClose(); });
        else if (GameStates.IsInTask && Main.IsSetRoleFinished)
        {
            // 緊急会議として開く (ボタンの回数やサボタージュに関係なく開く)
            Button(ref y, w, GetString("HostPanel.CallMeeting"), sHost, () => PlayerControl.LocalPlayer.NoCheckStartMeeting(null));
        }
        if (HudManager.InstanceExists && HudManager.Instance.Chat && !HudManager.Instance.Chat.IsOpenOrOpening)
            Button(ref y, w, GetString("HostPanel.OpenChat"), sAction, () => { if (HudManager.InstanceExists) HudManager.Instance.Chat.SetVisible(true); });
        if (Main.IsSetRoleFinished)
            Button(ref y, w, Armed("abort", GetString("HostPanel.Abort")), sDanger, () => Arm("abort", () =>
            {
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
                GameManager.Instance.LogicFlow.CheckEndCriteria();
            }));
    }

    void PlayersSection(ref float y, float w)
    {
        // 結果画面などで誰も居ない時は見出しも出さない
        if (PlayerControl.AllPlayerControls.Count == 0) return;
        Section(ref y, w, GetString("HostPanel.Players"));
        var afkOn = AfkDetector.Enabled.GetBool();
        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
            // 試合中の PlayerName は役職の表示などで飾られているので、元の名前を使う
            var rawName = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(pc.PlayerId, out var n) ? n : pc.Data.PlayerName;
            var name = Utils.ColorString(Palette.PlayerColors.Length > pc.Data.DefaultOutfit.ColorId && pc.Data.DefaultOutfit.ColorId >= 0
                ? (Color)Palette.PlayerColors[pc.Data.DefaultOutfit.ColorId] : Color.white, rawName);
            if (GameStates.IsInGame && !pc.IsAlive()) name = $"<i>{name}</i>";
            if (afkOn && !pc.AmOwner)
            {
                var still = AfkDetector.StillSeconds(pc.PlayerId);
                if (still >= 30f) name += $"  <color=#ffcc55>AFK {(int)still}s</color>";
            }
            if (pc.AmOwner)
            {
                Label(ref y, w, name + "  " + GetString("HostPanel.You"));
                continue;
            }
            var clientId = pc.GetClientId();
            var target = pc;
            var isMod = Moderators.Is(pc);
            Row(ref y, w,
                (name, sSmall, null),
                (GetString(isMod ? "HostPanel.ModeratorOn" : "HostPanel.ModeratorOff"), isMod ? sOn : sAction, () => Moderators.Toggle(target)),
                (Armed($"kick{clientId}", GetString("HostPanel.Kick")), sDanger, () => Arm($"kick{clientId}", () => AmongUsClient.Instance.KickPlayer(clientId, false))),
                (Armed($"ban{clientId}", GetString("HostPanel.Ban")), sDanger, () => Arm($"ban{clientId}", () => AmongUsClient.Instance.KickPlayer(clientId, true))));
        }
    }

    static readonly Regex UnsupportedTags = new(@"</?(align|size|pos|line-height|voffset|font|mark|cspace|indent|margin|width)[^>]*>", RegexOptions.Compiled);
    static readonly Regex ShortColor = new(@"<#([0-9a-fA-F]{6,8})>", RegexOptions.Compiled);
    string logCacheSource;
    string[] logCacheLines = Array.Empty<string>();

    void LogSection(ref float y, float w)
    {
        if (string.IsNullOrEmpty(GameLog.LastText)) return;
        Section(ref y, w, GetString("GameLog"));
        Button(ref y, w, GetString(showLog ? "HostPanel.HideLog" : "HostPanel.ShowLog"), sAction, () => showLog = !showLog);
        if (!showLog) return;
        if (!ReferenceEquals(logCacheSource, GameLog.LastText))
        {
            logCacheSource = GameLog.LastText;
            // TextMeshPro だけのタグは IMGUI では字のまま出るので外す
            logCacheLines = ShortColor.Replace(UnsupportedTags.Replace(GameLog.LastText, ""), "<color=#$1>").Replace("\t", "  ").Split('\n');
        }
        // 1 行ずつ (行の文字は同じ物を使い回すので、高さは文字か幅が変わった時だけ測る)
        foreach (var line in logCacheLines)
        {
            var it = Add(Kind.Text, line, sLabel, null, 0f, y, w, 0f);
            y += it.H = Measure(it, w);
        }
        y += Pad;
    }

    // ===== 部品 =====

    float Measure(Item it, float w)
    {
        if (!Mathf.Approximately(it.MeasuredW, w))
        {
            it.MeasuredW = w;
            it.MeasuredH = it.Style.CalcHeight(it.Content, w);
        }
        return it.MeasuredH;
    }

    void Section(ref float y, float w, string title)
    {
        y += Pad;
        Add(Kind.Text, title, sSection, null, 0f, y, w, RowH * 0.6f);
        y += RowH * 0.6f + Pad * 0.5f;
    }

    void Label(ref float y, float w, string text)
    {
        var it = Add(Kind.Text, text, sLabel, null, 0f, y, w, 0f);
        it.H = Mathf.Max(RowH * 0.6f, Measure(it, w));
        y += it.H + Pad * 0.5f;
    }

    void Button(ref float y, float w, string text, GUIStyle style, Action action)
    {
        Add(Kind.Button, text, style, action, 0f, y, w, RowH);
        y += RowH + Pad * 0.6f;
    }

    /// <summary>横に並べる。action が null の物は字だけ (sSmall) か押せないボタン。最初の欄を広く取る</summary>
    void Row(ref float y, float w, params (string text, GUIStyle style, Action action)[] cells)
    {
        var gap = Pad * 0.6f;
        var first = cells.Length >= 4 ? 0.4f : cells.Length == 3 ? 0.5f : 0.6f;
        if (cells[0].action != null) first = 1f / cells.Length;
        var x = 0f;
        for (var i = 0; i < cells.Length; i++)
        {
            var cw = i == 0 ? (w - gap * (cells.Length - 1)) * first : (w - gap * (cells.Length - 1)) * (1f - first) / (cells.Length - 1);
            var (text, style, action) = cells[i];
            var kind = action == null && style == sSmall ? Kind.Text : Kind.Button;
            Add(kind, text, style, action, x, y, cw, RowH);
            x += cw + gap;
        }
        y += RowH + Pad * 0.6f;
    }

    /// <summary>1 回目は構えるだけ、3 秒以内の 2 回目で行う</summary>
    void Arm(string key, Action action)
    {
        if (armedKey == key && Time.time - armedAt <= ArmSeconds)
        {
            armedKey = null;
            try { action(); }
            catch (Exception ex) { Logger.Exception(ex, nameof(HostPanel)); }
            return;
        }
        armedKey = key;
        armedAt = Time.time;
    }

    string Armed(string key, string text)
        => armedKey == key && Time.time - armedAt <= ArmSeconds ? GetString("HostPanel.Confirm") : text;

    // ===== 見た目 =====

    void BuildStyles()
    {
        scale = Scale;
        var k = Mathf.Max(0.5f, scale);
        // 半径は 2 画素刻み (窓の大きさを変えている間に、少しずつ違う絵が溜まらないように)
        var winR = Mathf.RoundToInt(11f * k) * 2;
        var btnR = Mathf.RoundToInt(RowH * 0.15f) * 2;
        sWindow = new GUIStyle { normal = { background = RoundTex(new Color(0.06f, 0.07f, 0.15f, 1f), winR) }, border = Border(winR) };
        sTitle = Text(Font + 3, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter, new Color(0.72f, 0.90f, 1f));
        sSection = Text(Font, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft, new Color(0.58f, 0.82f, 1f));
        sLabel = Text(Font - 2, FontStyle.Normal, TextAnchor.UpperLeft, Color.white);
        sLabel.wordWrap = true;
        sSmall = Text(Font - 2, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
        sSmall.clipping = TextClipping.Clip;
        sAction = Btn(new Color(0.15f, 0.19f, 0.35f), new Color(0.24f, 0.30f, 0.52f), Font, btnR);
        sHost = Btn(new Color(0.07f, 0.22f, 0.40f), new Color(0.12f, 0.36f, 0.62f), Font, btnR);
        sOn = Btn(new Color(0.08f, 0.36f, 0.22f), new Color(0.12f, 0.50f, 0.32f), Font, btnR);
        sDanger = Btn(new Color(0.38f, 0.07f, 0.07f), new Color(0.60f, 0.12f, 0.12f), Font, btnR);
        titleContent = new GUIContent(GetString("HostPanel.Title"));
        // 字の大きさが変わったので測り直す
        foreach (var it in items) it.MeasuredW = -1f;
        dirty = true;
    }

    static RectOffset Border(int r)
    {
        // 4 つの値をとる作り方は interop に無いので、1 つずつ入れる
        var o = new RectOffset();
        o.left = o.right = o.top = o.bottom = r;
        return o;
    }

    static GUIStyle Text(int size, FontStyle fs, TextAnchor anchor, Color color)
        => new() { fontSize = size, fontStyle = fs, alignment = anchor, richText = true, normal = { textColor = color } };

    static GUIStyle Btn(Color normal, Color hover, int size, int r) => new()
    {
        fontSize = size,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        richText = true,
        wordWrap = true,
        border = Border(r),
        padding = Border(Mathf.Max(2, r / 3)),
        normal = { background = RoundTex(normal, r), textColor = Color.white },
        hover = { background = RoundTex(hover, r), textColor = Color.white },
        active = { background = RoundTex(new Color(normal.r * 0.7f, normal.g * 0.7f, normal.b * 0.7f, normal.a), r), textColor = Color.white },
    };

    static readonly Dictionary<(Color, int), Texture2D> textures = new();

    /// <summary>角を丸めた 1 色の絵 (9 分割で引き伸ばす。角の半径 r の外側は透明・縁は 1 画素ぼかす)</summary>
    static Texture2D RoundTex(Color c, int r)
    {
        r = Mathf.Max(1, r);
        if (textures.TryGetValue((c, r), out var t) && t != null) return t;
        var size = r * 2 + 2;
        t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        var col = (Color32)c;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // 角の円の中心からの距離 (角の外では 0)
                var dx = Mathf.Max(0f, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r)));
                var dy = Mathf.Max(0f, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
                var a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * size + x] = new Color32(col.r, col.g, col.b, (byte)(col.a * a));
            }
        t.SetPixels32(px);
        t.Apply(false, true);
        textures[(c, r)] = t;
        return t;
    }
}
