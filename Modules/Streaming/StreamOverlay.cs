using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using static TownOfHostL.Translator;
using Object = UnityEngine.Object;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// 配信の重ね表示 (ホストの画面だけ)。左下に観客席、その右にコメントの板、ロビーではその上にマップ投票の札を出す。
/// どれも HUD のカメラに置く絵と文字で、IMGUI は使わない (描く物が無い時も毎フレーム回る IMGUI の処理を足さない)。
/// 文字は記録やポイントが変わった時だけ作り直し、毎フレームは動く物 (クルー・吹き出し) の位置だけを動かす。
/// 置き場所は操作盤の「配置を動かす」の間だけドラッグで動かせる (それ以外の押しはゲームへそのまま渡る)。
/// </summary>
/// <remarks>観客席の右にコメントの板を貼り付け、ロビーでは開始のボタンをよける置き方は End K not (waffle-ful/Aeterna-End-K-not Modules/YouTubeChat/YouTubeChatBubble.cs) を元にした。</remarks>
public class StreamOverlay : MonoBehaviour
{
    public StreamOverlay(IntPtr ptr) : base(ptr) { }

    public static StreamOverlay Instance;
    /// <summary>置き場所を動かしている間 (操作盤から切り替える)</summary>
    public static bool Editing;

    readonly HudSpace space = new();
    readonly AudienceSeats seats = new();
    bool subscribed;
    bool shown;
    bool seatsShown, signEditing;
    bool wantCache;
    float nextWantCheck;

    // 置き場所 (画面の画素・左下から)。ドラッグの間は手元だけで動かし、離した時に保存する
    float dockLeft, dockBottom;
    bool dragging, pressing;
    float pressX, pressY, dragDx, dragDy;
    float startButtonLeft = float.MaxValue;
    float nextStartProbe;

    public void Awake()
    {
        Instance = this;
    }

    public void LateUpdate()
    {
        try { Tick(); }
        catch (Exception ex) { Logger.Exception(ex, nameof(StreamOverlay)); }
    }

    static float Now => Time.realtimeSinceStartup;

    [HideFromIl2Cpp]
    static void OnArrived(StreamHub.Entry e)
    {
        if (Instance != null && StreamHub.ShowSeats.Value) Instance.seats.OnArrived(e, Now);
    }

    [HideFromIl2Cpp]
    static void OnExecuted(StreamHub.Entry e)
    {
        if (Instance != null) Instance.seats.OnExecuted(e, Now);
    }

    void Tick()
    {
        if (!subscribed)
        {
            subscribed = true;
            StreamHub.Arrived += OnArrived;
            StreamHub.Executed += OnExecuted;
        }
        var t = Now;
        if (t >= nextWantCheck)
        {
            nextWantCheck = t + 0.1f;
            wantCache = WantShown();
        }
        var want = wantCache;
        if (!want)
        {
            if (shown) SetVisible(false);
            shown = false;
            dragging = pressing = false;
            return;
        }
        if (!space.Ensure("TOHL_StreamOverlay", 1.5f)) { shown = false; return; }
        var wasShown = shown;
        if (!wasShown && !space.Root.activeSelf) space.Root.SetActive(true);
        var now = Now;
        var s = Scale;
        Layout(s, out var benchLeft, out var benchFloor, out var board, out var vote, out var bounds);
        HandleInput(s, board, bounds);

        var seatsOn = StreamHub.ShowSeats.Value;
        if (seatsOn) seats.Update(space, s, benchLeft, benchFloor, now);
        // 出し入れと札の文字は、変わった時だけ (毎フレーム本編の物に触らない)
        if (seatsOn != seatsShown || !wasShown)
        {
            seatsShown = seatsOn;
            seats.SetVisible(seatsOn);
        }
        if (seatsOn && (Editing != signEditing || seats.SignStale))
        {
            signEditing = Editing;
            seats.SetSign(GetString(Editing ? "Stream.SignMove" : "Stream.SeatsSign"));
        }
        UpdateBoard(s, board, now);
        UpdateVote(s, vote);
        UpdateEditFrame(s, bounds);
        shown = true;
    }

    /// <summary>出すか (ホストで、部屋の中で、配信を読んでいるか記録があるか、配置を動かしている間)</summary>
    static bool WantShown()
    {
        if (!HostPanelButton.InRoom || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !HudManager.InstanceExists) return false;
        if (!StreamHub.ShowBoard.Value && !StreamHub.ShowSeats.Value && !StreamHub.MapVoteOn.Value) return false;
        // 試合の始めの演出の間は出さない
        if (IntroCutscene.Instance != null) return false;
        if (StreamHub.HideInMeeting.Value && (MeetingHud.Instance != null || ExileController.Instance != null)) return false;
        return Editing || YouTubeChat.Active || StreamHub.Feed.Count > 0 || MapVote.Total > 0;
    }

    /// <summary>大きさ (1080 幅の画面で 0.5・ホストの選んだ倍率を掛ける)</summary>
    float Scale => space.ScreenW / 1080f * 0.5f * Fx.Clamp(StreamHub.UiScale.Value, 50, 200) / 100f;

    // ===== 置き場所 =====

    /// <summary>
    /// 既定は左下の開閉ボタンの右 (下端をそろえる)。観客席を出さない時は板が左に寄る。
    /// ロビーでは板の右端が開始のボタンに掛からないよう幅を詰め、詰めきれない時は観客席の上に積む
    /// </summary>
    [HideFromIl2Cpp]
    void Layout(float s, out float benchLeft, out float benchFloor, out Area board, out Area vote, out Area bounds)
    {
        float sw = space.ScreenW, sh = space.ScreenH, u = sw / 1920f;
        if (!dragging)
        {
            var fx = StreamHub.DockX.Value;
            var fy = StreamHub.DockY.Value;
            dockLeft = fx >= 0f ? fx * sw : 92f * u;
            dockBottom = fy >= 0f ? fy * sh : sh * 0.10f;
        }
        var seatsOn = StreamHub.ShowSeats.Value;
        var benchW = seats.BenchWidth;
        dockLeft = Fx.Clamp(dockLeft, 0f, Fx.Max(0f, sw - (seatsOn ? benchW : 400f * s)));
        dockBottom = Fx.Clamp(dockBottom, 0f, Fx.Max(0f, sh - 300f * s));

        benchLeft = dockLeft;
        benchFloor = dockBottom + 53f * s;
        var top = seatsOn ? seats.Top : dockBottom;

        // 板
        var gap = 12f * s;
        var bw = 400f * s;
        var stacked = false;
        var bh = StreamHub.BoardCollapsed.Value ? HeaderH(s) : HeaderH(s) + BodyH(s) + 10f * s;
        var bx = seatsOn ? benchLeft + benchW + gap : dockLeft;
        var by = dockBottom;
        if (GameStates.IsLobby && startButtonLeft < sw)
        {
            var room = startButtonLeft - gap - bx;
            if (room < bw) bw = Fx.Max(room, 0f);
            if (bw < 240f * s && seatsOn)
            {
                // 横に入らない時は観客席の上へ (吹き出しは板の文を隠すので出さない)
                bw = Fx.Min(400f * s, benchW);
                bx = benchLeft;
                by = top + gap;
                stacked = true;
            }
        }
        bw = Fx.Max(bw, 200f * s);
        board = StreamHub.ShowBoard.Value ? new Area(bx, by, bw, bh) : new Area(bx, by, 0f, 0f);
        seats.Bubbles = !(stacked && StreamHub.ShowBoard.Value);

        // マップ投票の札は板の上 (板が無ければ観客席の上)
        var vh = VoteH(s);
        var vy = board.H > 0f ? board.YMax + gap * 0.7f : top + gap;
        var vx = board.H > 0f ? board.X : benchLeft;
        var vw = board.H > 0f ? board.W : 320f * s;
        vote = MapVote.Active ? new Area(vx, vy, vw, vh) : new Area(vx, vy, 0f, 0f);

        // ドラッグでつかめる範囲
        var minX = seatsOn ? benchLeft : bx;
        var maxX = Fx.Max(seatsOn ? benchLeft + benchW : bx, board.W > 0f ? board.XMax : 0f, vote.W > 0f ? vote.XMax : 0f);
        var maxY = Fx.Max(top, board.H > 0f ? board.YMax : 0f, vote.H > 0f ? vote.YMax : 0f);
        bounds = new Area(minX - 6f * s, dockBottom - 6f * s, maxX - minX + 12f * s, maxY - dockBottom + 12f * s);

        // 開始のボタンの左端は 1 秒ごとに測る (画面の大きさで動くので)
        if (Now >= nextStartProbe)
        {
            nextStartProbe = Now + 1f;
            startButtonLeft = ProbeStartButton();
        }
    }

    static float ProbeStartButton()
    {
        try
        {
            if (!GameStates.IsLobby || !GameStartManager.InstanceExists) return float.MaxValue;
            var btn = GameStartManager.Instance.StartButton;
            var cam = HudSpace.FindCamera();
            if (btn == null || cam == null || !btn.gameObject.activeInHierarchy) return float.MaxValue;
            var sr = btn.GetComponentInChildren<SpriteRenderer>();
            if (sr == null) return float.MaxValue;
            return cam.WorldToScreenPoint(sr.bounds.min).x;
        }
        catch { return float.MaxValue; }
    }

    // ===== 押し =====

    [HideFromIl2Cpp]
    void HandleInput(float s, Area board, Area bounds)
    {
        // 板を畳む・開く (チャットを打っている間は効かせない)
        if (Input.GetKeyDown(KeyCode.Y) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && !ChatOpen())
            StreamHub.BoardCollapsed.Value = !StreamHub.BoardCollapsed.Value;

        var down = Input.GetMouseButtonDown(0);
        var held = dragging && Input.GetMouseButton(0);
        var up = Input.GetMouseButtonUp(0);
        if (!down && !held && !up) return;
        var mp = Input.mousePosition;
        float mx = mp.x, my = mp.y;
        if (down)
        {
            pressing = !HostPanel.ContainsScreen(Fx.V2(mx, my));
            pressX = mx;
            pressY = my;
            if (pressing && Editing && bounds.Contains(mx, my))
            {
                dragging = true;
                dragDx = mx - dockLeft;
                dragDy = my - dockBottom;
            }
        }
        if (held)
        {
            dockLeft = mx - dragDx;
            dockBottom = my - dragDy;
        }
        if (!up) return;
        if (dragging)
        {
            dragging = false;
            StreamHub.DockX.Value = dockLeft / space.ScreenW;
            StreamHub.DockY.Value = dockBottom / space.ScreenH;
        }
        else if (pressing && !Editing && (mx - pressX) * (mx - pressX) + (my - pressY) * (my - pressY) < 36f)
        {
            // 札か板の見出しを押すと板を畳む・開く
            var header = new Area(board.X, board.YMax - HeaderH(s), board.W, HeaderH(s));
            if ((StreamHub.ShowSeats.Value && seats.SignRect.Contains(mx, my)) || (!board.Empty && header.Contains(mx, my)))
                StreamHub.BoardCollapsed.Value = !StreamHub.BoardCollapsed.Value;
        }
        pressing = false;
    }

    static bool ChatOpen() => HudManager.InstanceExists && HudManager.Instance.Chat != null && HudManager.Instance.Chat.IsOpenOrOpening;

    /// <summary>置き場所を既定へ戻す</summary>
    public static void ResetPosition()
    {
        StreamHub.DockX.Value = -1f;
        StreamHub.DockY.Value = -1f;
    }

    // ===== コメントの板 =====

    static float HeaderH(float s) => 34f * s;
    static float BodyH(float s) => 168f * s;

    static readonly Color Navy = new(0.06f, 0.07f, 0.15f, 0.92f);
    static readonly Color Rim = new(0.36f, 0.48f, 0.78f, 0.55f);
    static readonly Color LiveRed = new(0.91f, 0.10f, 0.24f, 1f);
    static readonly Color Muted = new(0.62f, 0.68f, 0.80f, 1f);

    GameObject boardRoot;
    SpriteRenderer boardRim, boardFill, livePill, headerRule;
    TextMeshPro liveText, titleText, metaText, bodyText;
    float boardScale = -1f;
    int boardVersion = -1;
    float nextMeta;
    Area boardRect;
    bool boardCollapsedBuilt;
    bool boardEditing;
    bool boardActive, voteActive, editFrameOn;
    float boardRetryAt, voteRetryAt;
    int seenWhenCollapsed;
    readonly List<string> lines = new();

    [HideFromIl2Cpp]
    void UpdateBoard(float s, Area r, float now)
    {
        var on = !r.Empty;
        if (!on)
        {
            if (boardActive && boardRoot != null) boardRoot.SetActive(false);
            boardActive = false;
            return;
        }
        if ((boardRoot == null && Now >= boardRetryAt) || (boardRoot != null && !Fx.Same(boardScale, s))) { BuildBoard(s); boardActive = true; }
        if (boardRoot == null) return;
        if (!boardActive) { boardActive = true; boardRoot.SetActive(true); }

        var collapsed = StreamHub.BoardCollapsed.Value;
        if (!r.Same(boardRect) || collapsed != boardCollapsedBuilt)
        {
            if (collapsed && !boardCollapsedBuilt) seenWhenCollapsed = CommentCount();
            boardRect = r;
            boardCollapsedBuilt = collapsed;
            PlaceBoard(s, r, collapsed);
            boardVersion = -1;
        }
        if (boardVersion != StreamHub.Version || boardEditing != Editing)
        {
            boardEditing = Editing;
            boardVersion = StreamHub.Version;
            if (!collapsed) bodyText.text = BuildBody(s, r.W - 28f * s);
            nextMeta = 0f;
        }
        // 見出しの右 (毎分の数と様子) は 1 秒ごと
        if (now >= nextMeta)
        {
            nextMeta = now + 1f;
            metaText.text = BuildMeta(collapsed);
            // 読めている間は LIVE の印を赤く、それ以外は灰色に
            livePill.color = YouTubeChat.Active && YouTubeChat.LastStatus == "Reading" ? LiveRed : Fx.Rgba(0.35f, 0.38f, 0.48f, 1f);
        }
    }

    void BuildBoard(float s)
    {
        if (boardRoot != null) Object.Destroy(boardRoot);
        boardRoot = new GameObject("TOHL_StreamBoard") { layer = 5 };
        boardRoot.transform.SetParent(space.Root.transform, false);
        boardScale = s;
        boardRect = default;
        var t = boardRoot.transform;
        boardRim = Art.Panel(t, 16f * s, Rim, 0f, 0f, 10f, 10f, 40);
        boardFill = Art.Panel(t, 15f * s, Navy, 0f, 0f, 10f, 10f, 41);
        headerRule = Art.Quad(t, Fx.Rgba(1f, 1f, 1f, 0.08f), 0f, 0f, 1f, 1f, 42);
        livePill = Art.Panel(t, 9f * s, LiveRed, 0f, 0f, 10f, 10f, 43);
        liveText = Art.Text(t, "Live", 13f * s, Color.white, TextAlignmentOptions.Center, 44);
        titleText = Art.Text(t, "Title", 15f * s, Fx.Rgba(0.86f, 0.91f, 1f), TextAlignmentOptions.Left, 44);
        metaText = Art.Text(t, "Meta", 13f * s, Muted, TextAlignmentOptions.Right, 44);
        bodyText = Art.Text(t, "Body", 17f * s, Fx.Rgba(0.93f, 0.95f, 1f), TextAlignmentOptions.BottomLeft, 44);
        if (liveText == null || titleText == null || metaText == null || bodyText == null)
        {
            // 本編の文字の元がまだ無い時など。毎フレーム作って壊さないよう間を空ける
            Object.Destroy(boardRoot);
            boardRoot = null;
            boardRetryAt = Now + 5f;
            return;
        }
        liveText.fontStyle = FontStyles.Bold;
        liveText.text = "● " + GetString("Stream.Live");
        titleText.fontStyle = FontStyles.Bold;
        titleText.text = GetString("Stream.BoardTitle");
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        bodyText.enableWordWrapping = true;
        bodyText.lineSpacing = 6f;
        boardVersion = -1;
    }

    /// <summary>板の部品を置く (板の左下を (0,0) とした画素で)</summary>
    [HideFromIl2Cpp]
    void PlaceBoard(float s, Area r, bool collapsed)
    {
        boardRoot.transform.localPosition = space.Px(r.X, r.Y, -0.2f);
        float w = r.W, h = r.H, hh = HeaderH(s), pad = 14f * s;
        boardRim.size = Fx.V2(w + 3f * s, h + 3f * s);
        boardRim.transform.localPosition = Fx.V3(w / 2f, h / 2f, 0f);
        boardFill.size = Fx.V2(w, h);
        boardFill.transform.localPosition = Fx.V3(w / 2f, h / 2f, 0f);

        var hy = h - hh / 2f;
        var pillW = liveText.GetPreferredValues(liveText.text).x + 16f * s;
        var pillH = 20f * s;
        livePill.size = Fx.V2(pillW, pillH);
        livePill.transform.localPosition = Fx.V3(pad + pillW / 2f, hy, 0f);
        liveText.rectTransform.sizeDelta = Fx.V2(pillW, pillH);
        liveText.transform.localPosition = Fx.V3(pad + pillW / 2f, hy, 0f);

        // 右の様子 (毎分の数・▾) は中身の幅だけ取り、残りを題に回す。狭い時は題を出さない
        var metaW = Fx.Min(150f * s, w * 0.42f);
        var titleX = pad + pillW + 8f * s;
        var titleW = w - titleX - metaW - pad;
        titleText.gameObject.SetActive(titleW > 70f * s);
        titleText.rectTransform.sizeDelta = Fx.V2(Fx.Max(10f, titleW), hh);
        titleText.transform.localPosition = Fx.V3(titleX + titleW / 2f, hy, 0f);
        metaText.rectTransform.sizeDelta = Fx.V2(metaW, hh);
        metaText.transform.localPosition = Fx.V3(w - pad - metaW / 2f, hy, 0f);

        headerRule.enabled = !collapsed;
        headerRule.transform.localPosition = Fx.V3(w / 2f, h - hh, 0f);
        headerRule.transform.localScale = Fx.V3(w - pad * 2f, Fx.Max(1f, 1.2f * s), 1f);

        bodyText.gameObject.SetActive(!collapsed);
        var bh = BodyH(s);
        bodyText.rectTransform.sizeDelta = Fx.V2(w - pad * 2f, bh);
        bodyText.transform.localPosition = Fx.V3(w / 2f, 5f * s + bh / 2f, 0f);
    }

    static int CommentCount()
    {
        var n = 0;
        foreach (var e in StreamHub.Feed) if (e.Kind != StreamHub.EntryKind.Executed) n++;
        return n;
    }

    string BuildMeta(bool collapsed)
    {
        var sb = new StringBuilder();
        if (collapsed)
        {
            var unread = CommentCount() - seenWhenCollapsed;
            if (unread > 0) sb.Append("<color=#FFD54A>+").Append(unread).Append("</color>  ");
        }
        else
        {
            var status = YouTubeChat.Active ? YouTubeChat.LastStatus : null;
            if (status is not (null or "Reading"))
                sb.Append(GetString(status is "Connecting" or "Ended" ? $"Stream.Status.{status}" : "Stream.Status.Retry")).Append("  ");
            sb.Append(string.Format(GetString("Stream.PerMinute"), StreamHub.PerMinute)).Append("  ");
        }
        sb.Append(collapsed ? "＋" : "－");
        return sb.ToString();
    }

    /// <summary>記録の新しい方から、板に入るだけの行を作る (古い物が上・新しい物が下)</summary>
    string BuildBody(float s, float width)
    {
        var feed = StreamHub.Feed;
        if (feed.Count == 0) return $"<color=#{Art.Hex(Muted)}>{GetString(Editing ? "Stream.Drag" : "Stream.Empty")}</color>";
        lines.Clear();
        // 新しい方から 1 行ずつ足し、つないだ全体が板に入らなくなったら止める
        var budget = BodyH(s);
        for (var i = feed.Count - 1; i >= 0; i--)
        {
            lines.Insert(0, Line(feed[i]));
            if (lines.Count > 1 && bodyText.GetPreferredValues(string.Join("\n", lines), width, 0f).y > budget)
            {
                lines.RemoveAt(0);
                break;
            }
        }
        return string.Join("\n", lines);
    }

    [HideFromIl2Cpp]
    static string Line(StreamHub.Entry e)
    {
        var name = $"<color=#{ViewerLook.Hex(e.Look.BrightColor)}><b>{e.Author}</b></color>";
        switch (e.Kind)
        {
            case StreamHub.EntryKind.Executed:
                return $"<color=#{Art.Hex(e.Accent)}>▶ {string.Format(GetString("Stream.Executed"), $"<b>{e.Author}</b>", $"<b>{e.Text}</b>")}</color>";
            case StreamHub.EntryKind.Paid:
                var paidBody = e.Text.Length > 0 ? $"<color=#7F8AA3>：</color>{e.Text}" : "";
                return $"<mark=#FFC1073A><color=#FFD54A><b>{e.Paid}</b></color> {name}{paidBody}</mark>";
        }
        if (e.Accent.a > 0f)
            return $"{name}<color=#7F8AA3>：</color><color=#{Art.Hex(e.Accent)}><b>{e.Text}</b></color>";
        return $"{name}<color=#7F8AA3>：</color>{e.Text}";
    }

    // ===== マップ投票の札 =====

    static float VoteH(float s) => 30f * s + MapVote.Maps.Length * 22f * s + 12f * s;

    GameObject voteRoot;
    SpriteRenderer voteRim, voteFill;
    TextMeshPro voteTitle, voteHint;
    readonly TextMeshPro[] voteNames = new TextMeshPro[MapVote.Maps.Length];
    readonly TextMeshPro[] voteCounts = new TextMeshPro[MapVote.Maps.Length];
    readonly SpriteRenderer[] voteTracks = new SpriteRenderer[MapVote.Maps.Length];
    readonly SpriteRenderer[] voteBars = new SpriteRenderer[MapVote.Maps.Length];
    float voteScale = -1f;
    Area voteRect;
    int voteVersion = -1;

    [HideFromIl2Cpp]
    void UpdateVote(float s, Area r)
    {
        var on = !r.Empty;
        if (!on)
        {
            if (voteActive && voteRoot != null) voteRoot.SetActive(false);
            voteActive = false;
            return;
        }
        if ((voteRoot == null && Now >= voteRetryAt) || (voteRoot != null && !Fx.Same(voteScale, s))) { BuildVote(s); voteActive = true; }
        if (voteRoot == null) return;
        if (!voteActive) { voteActive = true; voteRoot.SetActive(true); }
        if (!r.Same(voteRect))
        {
            voteRect = r;
            voteRoot.transform.localPosition = space.Px(r.X, r.Y, -0.2f);
            voteRim.size = Fx.V2(r.W + 3f * s, r.H + 3f * s);
            voteRim.transform.localPosition = Fx.V3(r.W / 2f, r.H / 2f, 0f);
            voteFill.size = Fx.V2(r.W, r.H);
            voteFill.transform.localPosition = Fx.V3(r.W / 2f, r.H / 2f, 0f);
            voteVersion = -1;
        }
        if (voteVersion == MapVote.Version) return;
        voteVersion = MapVote.Version;

        float w = r.W, h = r.H, pad = 14f * s, rowH = 22f * s;
        var ty = h - 17f * s;
        voteTitle.rectTransform.sizeDelta = Fx.V2(w * 0.5f, 24f * s);
        voteTitle.transform.localPosition = Fx.V3(pad + w * 0.25f, ty, 0f);
        voteHint.text = MapVote.Total > 0 ? string.Format(GetString("Stream.MapVoteTotal"), MapVote.Total) : GetString("Stream.MapVoteHint");
        voteHint.rectTransform.sizeDelta = Fx.V2(w * 0.5f - pad, 24f * s);
        voteHint.transform.localPosition = Fx.V3(w - pad - (w * 0.5f - pad) / 2f, ty, 0f);

        var max = 0;
        for (var i = 0; i < MapVote.Maps.Length; i++) max = Math.Max(max, MapVote.CountAt(i));
        float nameW = 92f * s, countW = 34f * s;
        var trackX = pad + nameW;
        var trackW = w - trackX - countW - pad;
        for (var i = 0; i < MapVote.Maps.Length; i++)
        {
            var y = h - 34f * s - rowH * (i + 0.5f);
            var c = MapVote.CountAt(i);
            var lead = c > 0 && c == max;
            voteNames[i].rectTransform.sizeDelta = Fx.V2(nameW, rowH);
            voteNames[i].transform.localPosition = Fx.V3(pad + nameW / 2f, y, 0f);
            voteNames[i].color = lead ? Fx.Rgba(1f, 0.84f, 0.30f) : Fx.Rgba(0.86f, 0.91f, 1f);
            voteTracks[i].transform.localPosition = Fx.V3(trackX + trackW / 2f, y, 0f);
            voteTracks[i].size = Fx.V2(trackW, 10f * s);
            var bw = max > 0 ? trackW * c / max : 0f;
            voteBars[i].enabled = bw > 1f;
            voteBars[i].size = Fx.V2(Fx.Max(bw, 10f * s), 10f * s);
            voteBars[i].transform.localPosition = Fx.V3(trackX + Fx.Max(bw, 10f * s) / 2f, y, 0f);
            voteBars[i].color = lead ? Fx.Rgba(1f, 0.78f, 0.20f) : Fx.Rgba(0.40f, 0.70f, 1f);
            voteCounts[i].text = c.ToString();
            voteCounts[i].rectTransform.sizeDelta = Fx.V2(countW, rowH);
            voteCounts[i].transform.localPosition = Fx.V3(w - pad - countW / 2f, y, 0f);
            voteCounts[i].color = lead ? Fx.Rgba(1f, 0.84f, 0.30f) : Muted;
        }
    }

    void BuildVote(float s)
    {
        if (voteRoot != null) Object.Destroy(voteRoot);
        voteRoot = new GameObject("TOHL_MapVote") { layer = 5 };
        voteRoot.transform.SetParent(space.Root.transform, false);
        voteScale = s;
        voteRect = default;
        var t = voteRoot.transform;
        voteRim = Art.Panel(t, 16f * s, Rim, 0f, 0f, 10f, 10f, 40);
        voteFill = Art.Panel(t, 15f * s, Navy, 0f, 0f, 10f, 10f, 41);
        voteTitle = Art.Text(t, "Title", 15f * s, Fx.Rgba(0.86f, 0.91f, 1f), TextAlignmentOptions.Left, 44);
        voteHint = Art.Text(t, "Hint", 12f * s, Muted, TextAlignmentOptions.Right, 44);
        if (voteTitle == null || voteHint == null) { Object.Destroy(voteRoot); voteRoot = null; voteRetryAt = Now + 5f; return; }
        voteTitle.fontStyle = FontStyles.Bold;
        voteTitle.text = GetString("Stream.MapVoteTitle");
        voteHint.enableWordWrapping = false;
        voteHint.overflowMode = TextOverflowModes.Ellipsis;
        for (var i = 0; i < MapVote.Maps.Length; i++)
        {
            voteNames[i] = Art.Text(t, "Map", 14f * s, Color.white, TextAlignmentOptions.Left, 44);
            voteCounts[i] = Art.Text(t, "Count", 14f * s, Muted, TextAlignmentOptions.Right, 44);
            if (voteNames[i] == null || voteCounts[i] == null) { Object.Destroy(voteRoot); voteRoot = null; voteRetryAt = Now + 5f; return; }
            voteNames[i].text = MapVote.Maps[i].Name;
            voteNames[i].fontStyle = FontStyles.Bold;
            voteCounts[i].fontStyle = FontStyles.Bold;
            voteTracks[i] = Art.Panel(t, 5f * s, Fx.Rgba(1f, 1f, 1f, 0.08f), 0f, 0f, 10f, 10f, 42);
            voteBars[i] = Art.Panel(t, 5f * s, Color.white, 0f, 0f, 10f, 10f, 43);
        }
        voteVersion = -1;
    }

    // ===== 配置を動かしている間の枠 =====

    SpriteRenderer editFrame;
    float editScale = -1f;

    [HideFromIl2Cpp]
    void UpdateEditFrame(float s, Area bounds)
    {
        if (!Editing)
        {
            if (editFrameOn && editFrame != null) editFrame.enabled = false;
            editFrameOn = false;
            return;
        }
        if (editFrame == null || !Fx.Same(editScale, s))
        {
            if (editFrame != null) Object.Destroy(editFrame.gameObject);
            editScale = s;
            editFrame = Art.Panel(space.Root.transform, 14f * s, Fx.Rgba(0.35f, 0.85f, 1f, 0.16f), 0f, 0f, 10f, 10f, -60);
            editFrameOn = true;
        }
        if (!editFrameOn) { editFrameOn = true; editFrame.enabled = true; }
        // 枠を少し脈打たせて、動かせる状態だと分かるようにする
        var a = 0.12f + 0.08f * Fx.Sin(Now * 4f);
        editFrame.color = Fx.Rgba(0.35f, 0.85f, 1f, a);
        editFrame.size = Fx.V2(bounds.W, bounds.H);
        editFrame.transform.localPosition = space.Px(bounds.X + bounds.W / 2f, bounds.Y + bounds.H / 2f, 0.2f);
    }

    // ===== 出し入れと片付け =====

    void SetVisible(bool on)
    {
        if (space.Root != null && space.Root.activeSelf != on) space.Root.SetActive(on);
        seats.SetVisible(on && StreamHub.ShowSeats.Value);
    }

    /// <summary>Mod を外す時。絵を消して止める</summary>
    public static void Shutdown()
    {
        var self = Instance;
        if (self == null) return;
        self.enabled = false;
        self.seats.Destroy();
        self.space.Destroy();
    }

    public void OnDestroy() => Shutdown();
}
