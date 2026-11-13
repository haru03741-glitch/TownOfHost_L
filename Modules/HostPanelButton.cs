using System;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 操作盤を開け閉めする左下のボタン。IMGUI ではなく、カメラに付けた絵 (SpriteRenderer) で出し、押しは Input から読む。
/// こうすると操作盤を閉じている間は <see cref="HostPanel"/> の OnGUI を止められる (OnGUI を持つ部品が 1 つでも動いていると、描く物が無くても毎フレーム IMGUI の処理が回る)。
/// </summary>
/// <remarks>閉じた時のボタンを IMGUI から絵に替える作りは End K not (waffle-ful/Aeterna-End-K-not Modules/ClientControlGUI.cs) を元にした。</remarks>
public class HostPanelButton : MonoBehaviour
{
    public HostPanelButton(IntPtr ptr) : base(ptr) { }

    /// <summary>部屋の中 (ロビー・試合・結果) にいるか。メインメニューでは操作盤を出さない</summary>
    public static bool InRoom { get; private set; }

    GameObject root;
    SpriteRenderer back, glyph;
    Camera boundCamera;
    bool shownOpen;
    bool hover, pressing;
    float placedW = -1f, placedH, placedOrtho;
    float nextSceneCheck;
    bool inMainMenu;
    Camera cachedCamera;

    static Sprite backSprite, menuSprite, closeSprite;
    static Material material;
    /// <summary>絵が作れなかった (以後はボタンを出さない。毎フレーム同じ失敗を記録しない)</summary>
    static bool failed;

    static readonly Color BackNormal = new(0.10f, 0.10f, 0.28f, 0.95f);
    static readonly Color BackHover = new(0.20f, 0.30f, 0.55f, 0.95f);

    static HostPanelButton instance;
    public void Awake() => instance = this;

    /// <summary>Mod を外す時。ボタンの絵を消し、操作盤ごと止める</summary>
    public static void Shutdown()
    {
        HostPanel.IsOpen = false;
        if (HostPanel.Instance != null) HostPanel.Instance.enabled = false;
        if (instance == null) return;
        instance.enabled = false;
        if (instance.root != null) Destroy(instance.root);
        instance.root = null;
    }

    public void LateUpdate()
    {
        try { Tick(); }
        catch (Exception ex) { Logger.Exception(ex, nameof(HostPanelButton)); }
    }

    void Tick()
    {
        // 部屋の出入りの途中は IsNotJoined が外れることがあるので、場面の名前でも見る。
        // 名前やカメラを引くと本編の側に物が作られるので、どちらも 0.25 秒ごと
        var now = Time.unscaledTime;
        var recheck = now >= nextSceneCheck;
        if (recheck)
        {
            nextSceneCheck = now + 0.25f;
            inMainMenu = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu";
        }
        InRoom = AmongUsClient.Instance != null && !GameStates.IsNotJoined && !inMainMenu;
        if (!InRoom) HostPanel.IsOpen = false;

        var panel = HostPanel.Instance;
        if (panel != null)
        {
            var want = InRoom ? HostPanel.IsOpen : AutoRehost.Pending || AutoRehost.FailureText != null;
            if (panel.enabled != want) panel.enabled = want;
        }

        if (!InRoom) cachedCamera = null;
        else if (recheck || cachedCamera == null) cachedCamera = FindCamera();
        var cam = cachedCamera;
        if (cam == null || failed)
        {
            if (root != null) root.SetActive(false);
            pressing = false;
            return;
        }
        if (root == null || boundCamera != cam)
        {
            Create(cam);
            if (root == null) return;
        }
        if (!root.activeSelf) root.SetActive(true);

        // 置き場所 (左下の隅・ロビーで左に出る設定の一覧より下)。画面の大きさかカメラの寄りが変わった時だけ置き直す
        float sw = Screen.width, sh = Screen.height;
        var ortho = cam.orthographicSize;
        var scale = sw / 1920f;
        var size = 56f * scale;
        var left = 16f * scale;
        var bottom = sh * 0.10f;
        if (!Mathf.Approximately(placedW, sw) || !Mathf.Approximately(placedH, sh) || !Mathf.Approximately(placedOrtho, ortho))
        {
            placedW = sw; placedH = sh; placedOrtho = ortho;
            var perPx = 2f * ortho / sh;
            var cx = left + size / 2f - sw / 2f;
            var cy = bottom + size / 2f - sh / 2f;
            var t = root.transform;
            t.localPosition = new Vector3(cx * perPx, cy * perPx, Depth(cam));
            // 絵は 1 枚 1.28 (128 画素・100 画素で 1)
            var s = size * perPx / 1.28f;
            t.localScale = new Vector3(s, s, 1f);
        }

        // 押し: 押し下げと離しの両方がボタンの上の時だけ開け閉めする
        var m = Input.mousePosition;
        var over = m.x >= left && m.x <= left + size && m.y >= bottom && m.y <= bottom + size;
        if (Input.GetMouseButtonDown(0)) pressing = over;
        if (Input.GetMouseButtonUp(0))
        {
            if (pressing && over) HostPanel.IsOpen = !HostPanel.IsOpen;
            pressing = false;
        }
        if (over != hover)
        {
            hover = over;
            back.color = over ? BackHover : BackNormal;
        }
        if (shownOpen != HostPanel.IsOpen)
        {
            shownOpen = HostPanel.IsOpen;
            glyph.sprite = shownOpen ? closeSprite : menuSprite;
        }
    }

    /// <summary>
    /// HUD を描くカメラ (ズームはこれと本編のカメラの両方の寄りを変える)。
    /// 結果の画面には HUD が無く、左下に本編の「終了」ボタンがあって重なるので、ボタンを出さない (開いた操作盤はそのまま出る)
    /// </summary>
    static Camera FindCamera() => HudManager.InstanceExists ? HudManager.Instance.UICamera : null;

    static float Depth(Camera cam)
    {
        // HUD と同じ奥行き。手前に出すのは描く順 (renderQueue・sortingOrder) で決める
        float near = cam.nearClipPlane, far = cam.farClipPlane;
        var d = HudManager.InstanceExists ? HudManager.Instance.transform.position.z - cam.transform.position.z : float.NaN;
        return d > near && d < far ? d : (near + far) / 2f;
    }

    void Create(Camera cam)
    {
        if (root != null) Destroy(root);
        root = null;
        boundCamera = cam;
        try
        {
            EnsureAssets();
            root = new GameObject("TOHL_HostPanelButton") { layer = 5 };
            root.transform.SetParent(cam.transform, false);
            back = AddSprite(root.transform, backSprite, 0);
            back.color = BackNormal;
            glyph = AddSprite(root.transform, HostPanel.IsOpen ? closeSprite : menuSprite, 1);
        }
        catch (Exception ex)
        {
            // 絵の作り方が本編で使えない時は、ボタンを出さずに諦める (同じ失敗を毎フレーム記録しない)
            failed = true;
            if (root != null) Destroy(root);
            root = null;
            Logger.Exception(ex, nameof(HostPanelButton));
            return;
        }
        shownOpen = HostPanel.IsOpen;
        hover = false;
        placedW = -1f;
    }

    static SpriteRenderer AddSprite(Transform parent, Sprite sprite, int order)
    {
        var go = new GameObject("img") { layer = 5 };
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sharedMaterial = material;
        // 会議の札などより手前
        sr.sortingOrder = short.MaxValue - 1 + order;
        return sr;
    }

    // ===== 絵 (起動して最初に出す時に 1 回だけ作る) =====

    const int N = 128;

    static void EnsureAssets()
    {
        if (backSprite != null && menuSprite != null && closeSprite != null && material != null) return;
        var shader = Shader.Find("Sprites/Default") ?? throw new InvalidOperationException("Sprites/Default が無い");
        material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 4000 };
        backSprite = MakeSprite((x, y) => RoundedSquare(x, y, 30f));
        menuSprite = MakeSprite(Bars);
        closeSprite = MakeSprite(Cross);
    }

    static Sprite MakeSprite(Func<float, float, float> alpha)
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (var y = 0; y < N; y++)
            for (var x = 0; x < N; x++)
                px[y * N + x] = new Color32(255, 255, 255, (byte)(255f * Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f))));
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sprite = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    /// <summary>角を丸めた四角 (r は角の半径)。縁は 1 画素ぼかす</summary>
    static float RoundedSquare(float x, float y, float r)
    {
        var dx = Mathf.Max(0f, Mathf.Abs(x - N / 2f) - (N / 2f - r));
        var dy = Mathf.Max(0f, Mathf.Abs(y - N / 2f) - (N / 2f - r));
        return r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f;
    }

    /// <summary>≡ (横棒 3 本・端は丸い)</summary>
    static float Bars(float x, float y)
    {
        const float half = 30f, thick = 6f, gap = 20f;
        var best = 0f;
        for (var i = -1; i <= 1; i++)
            best = Mathf.Max(best, Capsule(x, y, N / 2f - half, N / 2f + i * gap, N / 2f + half, N / 2f + i * gap, thick));
        return best;
    }

    /// <summary>× (斜めの棒 2 本)</summary>
    static float Cross(float x, float y)
    {
        const float d = 26f, thick = 6.5f;
        float c = N / 2f;
        return Mathf.Max(Capsule(x, y, c - d, c - d, c + d, c + d, thick), Capsule(x, y, c - d, c + d, c + d, c - d, thick));
    }

    /// <summary>線分 (ax,ay)-(bx,by) から半径 r の太さの棒</summary>
    static float Capsule(float x, float y, float ax, float ay, float bx, float by, float r)
    {
        float vx = bx - ax, vy = by - ay;
        var t = Mathf.Clamp01(((x - ax) * vx + (y - ay) * vy) / (vx * vx + vy * vy));
        float ex = x - (ax + vx * t), ey = y - (ay + vy * t);
        return r - Mathf.Sqrt(ex * ex + ey * ey) + 0.5f;
    }
}
