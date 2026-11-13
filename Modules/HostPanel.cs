using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// 画面の左下のボタンから開く、ホストの操作盤 (IMGUI の浮かせた窓)。
/// チャットのコマンドやキーの組み合わせを覚えなくても、ロビーと試合の操作・人の管理・ゲームログを押すだけで使えるようにする。
/// 窓は題の帯をつかんで動かせる。押すと取り返しのつかない操作 (キック・BAN・廃村) は、2 回押した時だけ行う。
/// </summary>
/// <remarks>浮かせた窓に操作を並べる形は EndlessHostRoles (Modules/ClientControlGUI.cs) を元にした。</remarks>
public class HostPanel : MonoBehaviour
{
    public HostPanel(IntPtr ptr) : base(ptr) { }

    public static HostPanel Instance;
    public static bool IsOpen;

    Rect window;
    bool windowReady;
    float contentHeight;
    bool dragging;
    Vector2 dragOffset;
    bool showLog;

    // 2 回押しの確かめ: 押した物の名前と時刻
    string armedKey;
    float armedAt;
    const float ArmSeconds = 3f;

    float scale = -1f;
    GUIStyle sToggle, sWindow, sTitle, sSection, sLabel, sAction, sHost, sDanger, sOn, sSmall;

    static float Scale => Screen.width / 1920f;
    static int Font => Mathf.Max(12, Mathf.RoundToInt(22f * Scale));
    static float RowH => 52f * Scale;
    static float Pad => 8f * Scale;
    static float Width => 460f * Scale;

    public void Awake() => Instance = this;

    public void OnGUI()
    {
        if (AmongUsClient.Instance == null || GameStates.IsNotJoined || InMainMenu()) { IsOpen = false; return; }
        if (!Mathf.Approximately(scale, Scale)) BuildStyles();
        DrawToggle();
        if (IsOpen) DrawWindow();
    }

    // 今の場面がメインメニューか (部屋の出入りの途中は IsNotJoined が外れることがあるので、場面の名前でも見る)。1 フレームに 1 回だけ調べる
    int sceneCheckedFrame = -1;
    bool inMainMenu;
    bool InMainMenu()
    {
        if (sceneCheckedFrame != Time.frameCount)
        {
            sceneCheckedFrame = Time.frameCount;
            inMainMenu = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu";
        }
        return inMainMenu;
    }

    // ===== 窓 =====

    void DrawToggle()
    {
        var size = 56f * Scale;
        // 左下の隅 (ロビーで左に出る設定の一覧より下)
        var rect = new Rect(Pad * 2f, Screen.height * 0.90f - size, size, size);
        if (GUI.Button(rect, IsOpen ? "×" : "≡", sToggle)) IsOpen = !IsOpen;
    }

    void DrawWindow()
    {
        if (!windowReady)
        {
            window = new Rect(Pad * 2f + 64f * Scale, Screen.height * 0.12f, Width, Screen.height * 0.70f);
            windowReady = true;
        }
        // 高さは中身に合わせる (会議の札やタスクの一覧をなるべく隠さない)。前のフレームの中身の高さを使う
        window.width = Width;
        window.height = Mathf.Min(Screen.height * 0.70f, RowH * 0.9f + Pad * 3f + Mathf.Max(contentHeight, RowH));
        HandleDrag();
        window.x = Mathf.Clamp(window.x, 0f, Screen.width - window.width);
        window.y = Mathf.Clamp(window.y, 0f, Screen.height - window.height);

        GUI.Box(window, "", sWindow);
        var titleH = RowH * 0.9f;
        GUI.Label(new Rect(window.x, window.y + Pad * 0.5f, window.width, titleH), GetString("HostPanel.Title"), sTitle);

        // 中身は画面の座標で直接描く (GUI のグループやスクロールの枠を使わない)。
        // 枠に全部が入らない部品は描かず、スクロールはホイールと右端のスクロールバーで動かす
        var barW = 18f * Scale;
        view = new Rect(window.x + Pad * 2f, window.y + titleH + Pad, window.width - Pad * 4f - barW - Pad, window.height - titleH - Pad * 2f);
        var maxScroll = Mathf.Max(0f, contentHeight - view.height);
        var e = Event.current;
        if (e.type == EventType.ScrollWheel && window.Contains(e.mousePosition))
        {
            scrollY += e.delta.y * RowH * 0.5f;
            e.Use();
        }
        if (maxScroll > 0f)
            scrollY = GUI.VerticalScrollbar(new Rect(view.xMax + Pad, view.y, barW, view.height), scrollY, view.height, 0f, contentHeight);
        scrollY = Mathf.Clamp(scrollY, 0f, maxScroll);

        var y = 0f;
        try { DrawContent(ref y, view.width); }
        catch (Exception ex) { Logger.Exception(ex, nameof(HostPanel)); }
        contentHeight = y;
    }

    Rect view;
    float scrollY;

    /// <summary>中身の座標 (枠の左上が 0,0) を画面の座標にする。枠に全部が入らなければ false</summary>
    bool Place(float x, float y, float w, float h, out Rect rect)
    {
        rect = new Rect(view.x + x, view.y + y - scrollY, w, h);
        return rect.yMin >= view.yMin - 0.5f && rect.yMax <= view.yMax + 0.5f;
    }

    void HandleDrag()
    {
        var e = Event.current;
        var title = new Rect(window.x, window.y, window.width, RowH);
        switch (e.type)
        {
            case EventType.MouseDown when title.Contains(e.mousePosition):
                dragging = true;
                dragOffset = e.mousePosition - window.position;
                e.Use();
                break;
            case EventType.MouseDrag when dragging:
                window.position = e.mousePosition - dragOffset;
                e.Use();
                break;
            case EventType.MouseUp:
                dragging = false;
                break;
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
            {
                if (Button(ref y, w, GetString("HostPanel.StartGame"), sHost)) gsm.BeginGame();
            }
            else
            {
                Row(ref y, w,
                    (GetString("HostPanel.StartNow"), sHost, () => gsm.countDownTimer = 0f),
                    (GetString("HostPanel.CancelStart"), sDanger, () => gsm.ResetStartState()));
            }
        }

        // 自動スタート
        var auto = AutoProgress.AutoStart.GetBool();
        var autoText = GetString("AutoStartGame") + ": " + GetString(auto ? "ColoredOn" : "ColoredOff");
        if (auto) autoText += $"  ({Mathf.Max(0, AutoProgress.AutoStartWait.GetInt() - (int)AutoProgress.LobbySeconds)}s)";
        if (Button(ref y, w, autoText, auto ? sOn : sAction)) AutoProgress.AutoStart.SetValue(auto ? 0 : 1);

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
            {
                if (Button(ref y, w, Armed("kpall", GetString("HostPanel.KickAll")), sDanger)) Arm("kpall", JoinGuard.KickDetected);
            }
            else if (!JoinGuard.AllowedThisLobby && Button(ref y, w, GetString("HostPanel.AllowThisLobby"), sAction)) JoinGuard.AllowThisLobby();
        }

        Row(ref y, w,
            (GetString("HostPanel.ShowSettings"), sAction, () => { Main.isChatCommand = true; Utils.ShowActiveSettings(); }),
            (GetString("HostPanel.ShowLastResult"), sAction, () => Utils.ShowLastResult()));
    }

    void GameSection(ref float y, float w)
    {
        Section(ref y, w, GetString("HostPanel.InGame"));
        if (GameStates.IsMeeting)
        {
            if (Button(ref y, w, GetString("HostPanel.EndMeeting"), sHost)) MeetingHud.Instance.RpcClose();
        }
        else if (GameStates.IsInTask && Main.IsSetRoleFinished)
        {
            if (Button(ref y, w, GetString("HostPanel.CallMeeting"), sHost))
            {
                // 緊急会議として開く (ボタンの回数やサボタージュに関係なく開く)
                PlayerControl.LocalPlayer.NoCheckStartMeeting(null);
            }
        }
        if (HudManager.InstanceExists && HudManager.Instance.Chat && !HudManager.Instance.Chat.IsOpenOrOpening)
            if (Button(ref y, w, GetString("HostPanel.OpenChat"), sAction)) HudManager.Instance.Chat.SetVisible(true);
        if (Main.IsSetRoleFinished && Button(ref y, w, Armed("abort", GetString("HostPanel.Abort")), sDanger))
            Arm("abort", () =>
            {
                CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);
                GameManager.Instance.LogicFlow.CheckEndCriteria();
            });
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
            Row(ref y, w,
                (name, sSmall, null),
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
        if (Button(ref y, w, GetString(showLog ? "HostPanel.HideLog" : "HostPanel.ShowLog"), sAction)) showLog = !showLog;
        if (!showLog) return;
        if (!ReferenceEquals(logCacheSource, GameLog.LastText))
        {
            logCacheSource = GameLog.LastText;
            // TextMeshPro だけのタグは IMGUI では字のまま出るので外す
            logCacheLines = ShortColor.Replace(UnsupportedTags.Replace(GameLog.LastText, ""), "<color=#$1>").Replace("\t", "  ").Split('\n');
        }
        // 1 行ずつ描く (枠からはみ出す行は描かない)
        foreach (var line in logCacheLines)
        {
            var h = sLabel.CalcHeight(new GUIContent(line), w);
            if (Place(0f, y, w, h, out var r)) GUI.Label(r, line, sLabel);
            y += h;
        }
        y += Pad;
    }

    // ===== 部品 =====

    void Section(ref float y, float w, string title)
    {
        y += Pad;
        if (Place(0f, y, w, RowH * 0.6f, out var r)) GUI.Label(r, title, sSection);
        y += RowH * 0.6f + Pad * 0.5f;
    }

    void Label(ref float y, float w, string text)
    {
        var h = Mathf.Max(RowH * 0.6f, sLabel.CalcHeight(new GUIContent(text), w));
        if (Place(0f, y, w, h, out var r)) GUI.Label(r, text, sLabel);
        y += h + Pad * 0.5f;
    }

    bool Button(ref float y, float w, string text, GUIStyle style)
    {
        var pressed = Place(0f, y, w, RowH, out var r) && GUI.Button(r, text, style);
        y += RowH + Pad * 0.6f;
        return pressed;
    }

    /// <summary>横に並べる。action が null の物は字だけ (押せない)。最初の欄を広く取る</summary>
    void Row(ref float y, float w, params (string text, GUIStyle style, Action action)[] cells)
    {
        var gap = Pad * 0.6f;
        var first = cells.Length >= 3 ? 0.5f : 0.6f;
        if (cells[0].action != null) first = 1f / cells.Length;
        var x = 0f;
        for (var i = 0; i < cells.Length; i++)
        {
            var cw = i == 0 ? (w - gap * (cells.Length - 1)) * first : (w - gap * (cells.Length - 1)) * (1f - first) / (cells.Length - 1);
            var (text, style, action) = cells[i];
            if (!Place(x, y, cw, RowH, out var rect)) { x += cw + gap; continue; }
            if (action == null && style == sSmall) GUI.Label(rect, text, sSmall);
            else
            {
                var enabled = GUI.enabled;
                GUI.enabled = action != null;
                if (GUI.Button(rect, text, style)) action?.Invoke();
                GUI.enabled = enabled;
            }
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
        sToggle = Btn(new Color(0.10f, 0.10f, 0.28f), new Color(0.20f, 0.30f, 0.55f), Font + 6);
        sWindow = new GUIStyle { normal = { background = Tex(new Color(0.06f, 0.07f, 0.15f, 1f)) } };
        sTitle = Text(Font + 3, FontStyle.BoldAndItalic, TextAnchor.MiddleCenter, new Color(0.72f, 0.90f, 1f));
        sSection = Text(Font, FontStyle.BoldAndItalic, TextAnchor.MiddleLeft, new Color(0.58f, 0.82f, 1f));
        sLabel = Text(Font - 2, FontStyle.Normal, TextAnchor.UpperLeft, Color.white);
        sLabel.wordWrap = true;
        sSmall = Text(Font - 2, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white);
        sSmall.clipping = TextClipping.Clip;
        sAction = Btn(new Color(0.15f, 0.19f, 0.35f), new Color(0.24f, 0.30f, 0.52f), Font);
        sHost = Btn(new Color(0.07f, 0.22f, 0.40f), new Color(0.12f, 0.36f, 0.62f), Font);
        sOn = Btn(new Color(0.08f, 0.36f, 0.22f), new Color(0.12f, 0.50f, 0.32f), Font);
        sDanger = Btn(new Color(0.38f, 0.07f, 0.07f), new Color(0.60f, 0.12f, 0.12f), Font);
    }

    static GUIStyle Text(int size, FontStyle fs, TextAnchor anchor, Color color)
        => new() { fontSize = size, fontStyle = fs, alignment = anchor, richText = true, normal = { textColor = color } };

    static GUIStyle Btn(Color normal, Color hover, int size) => new()
    {
        fontSize = size,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        richText = true,
        wordWrap = true,
        normal = { background = Tex(normal), textColor = Color.white },
        hover = { background = Tex(hover), textColor = Color.white },
        active = { background = Tex(normal * 0.7f), textColor = Color.white },
    };

    static readonly Dictionary<Color, Texture2D> textures = new();
    static Texture2D Tex(Color c)
    {
        if (textures.TryGetValue(c, out var t) && t != null) return t;
        t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        textures[c] = t;
        return t;
    }
}
