using System;
using TMPro;
using TownOfHostL.SettingsUI;
using TownOfHostL.Templates;
using UnityEngine;

namespace TownOfHostL.Modules.Streaming;

/// <summary>
/// 配信の重ね表示の部品。どれも HUD の層 (5) に置き、重なりは sortingOrder で決める。
/// 座標と大きさは <see cref="HudSpace"/> の根の中の画素で渡す (色は SpriteRenderer.color で塗るので、絵は白い物を使い回す)。
/// </summary>
static class Art
{
    static Sprite white, glow;

    /// <summary>白い四角 (大きさ 1 で 1 画素)</summary>
    public static Sprite WhiteSprite => white != null ? white : white = Bake(4, (_, _) => 1f, 4f);

    /// <summary>光の輪 (中心が明るく外へ薄れる・大きさ 1 で 1 画素)</summary>
    public static Sprite GlowSprite => glow != null ? glow : glow = Bake(64, (x, y) =>
    {
        float dx = (x - 31.5f) / 32f, dy = (y - 31.5f) / 32f;
        var d = 1f - Mathf.Sqrt(dx * dx + dy * dy);
        return d <= 0f ? 0f : d * d;
    }, 64f);

    static Sprite Bake(int n, Func<float, float, float> alpha, float ppu)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[n * n];
        for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255f));
        tex.SetPixels32(px);
        tex.Apply(false, true);
        var sp = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), ppu);
        sp.hideFlags = HideFlags.HideAndDontSave;
        return sp;
    }

    /// <summary>中心 (x,y)・大きさ (w,h) の四角</summary>
    public static SpriteRenderer Quad(Transform parent, Color color, float x, float y, float w, float h, int order)
    {
        var go = new GameObject("Quad") { layer = 5 };
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(x, y, 0f);
        go.transform.localScale = new Vector3(w, h, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = WhiteSprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    /// <summary>中心 (x,y)・大きさ (w,h)・角の半径 radius の角丸の板 (9 分割で伸ばす)</summary>
    public static SpriteRenderer Panel(Transform parent, float radius, Color color, float x, float y, float w, float h, int order)
    {
        // 半径は 1 画素刻み (大きさを変えている間に、少しずつ違う絵が溜まらないように)
        var sr = MenuArt.Panel(parent, "Panel", Mathf.Max(1f, Mathf.Round(radius)), new Vector2(w, h), new Vector3(x, y, 0f), color, null);
        sr.gameObject.layer = 5;
        sr.sortingOrder = order;
        return sr;
    }

    /// <summary>文字。size は字の大きさ (画素)。本編の文字の元が無い時は null</summary>
    public static TextMeshPro Text(Transform parent, string name, float size, Color color, TextAlignmentOptions align, int order)
    {
        TextMeshPro t;
        try { t = TMPTemplate.Create(name, "", color, size * 10f, align, true, parent); }
        catch (Exception ex)
        {
            Logger.Warn($"文字を作れなかった: {ex.Message}", "Stream");
            return null;
        }
        t.gameObject.layer = 5;
        var tr = t.transform;
        tr.localPosition = Vector3.zero;
        tr.localScale = Vector3.one;
        tr.localRotation = Quaternion.identity;
        t.enableAutoSizing = false;
        t.richText = true;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        t.outlineWidth = 0f;
        t.sortingOrder = order;
        t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        return t;
    }

    public static void SetLayer(Transform t)
    {
        t.gameObject.layer = 5;
        for (var i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i));
    }

    public static string Hex(Color c) => ViewerLook.Hex(c);
}

/// <summary>コメントが来た合図の「ピコッ」(その場で作る短い音)</summary>
static class StreamSound
{
    static AudioClip pico;

    public static void Pico(float pitch)
    {
        try
        {
            if (SoundManager.Instance == null) return;
            pico ??= Build();
            var src = SoundManager.Instance.PlaySound(pico, false, 0.55f);
            if (src != null) src.pitch = pitch;
        }
        catch (Exception ex) { Logger.Warn($"音を鳴らせなかった: {ex.Message}", "Stream"); }
    }

    static AudioClip Build()
    {
        const int rate = 22050;
        const float length = 0.09f;
        var n = (int)(rate * length);
        var data = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (float)rate;
            // 高めの音から少し上がる短い音。頭は 3 ms で立ち上げ、後は早めに消す
            var f = 1320f + 900f * t / length;
            var env = Mathf.Min(1f, t / 0.003f) * Mathf.Exp(-t * 38f);
            data[i] = Mathf.Sin(2f * Mathf.PI * f * t) * env * 0.6f;
        }
        var clip = AudioClip.Create("TOHL_StreamPico", n, 1, rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}

/// <summary>
/// 毎フレーム通る所の計算。UnityEngine の Mathf・Quaternion.Euler・Vector3 などの作成は interop 越しに本編を呼び、
/// 値を返すたびに il2cpp の側で箱詰めの確保が起きるので、ここでは .NET の側だけで計算する
/// </summary>
static class Fx
{
    public const float PI = MathF.PI;
    public static float Min(float a, float b) => a < b ? a : b;
    public static float Max(float a, float b) => a > b ? a : b;
    public static float Max(float a, float b, float c) => Max(Max(a, b), c);
    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    public static float Clamp01(float v) => Clamp(v, 0f, 1f);
    public static float Abs(float v) => v < 0f ? -v : v;
    public static float Sin(float v) => MathF.Sin(v);
    public static float Round(float v) => MathF.Round(v);
    public static bool Same(float a, float b) => Abs(a - b) < 1e-4f;
    public static Vector3 V3(float x, float y, float z) => new() { x = x, y = y, z = z };
    public static Vector2 V2(float x, float y) => new() { x = x, y = y };
    public static Color Rgba(float r, float g, float b, float a = 1f) => new() { r = r, g = g, b = b, a = a };
    /// <summary>Z 軸まわりに deg 度</summary>
    public static Quaternion RotZ(float deg)
    {
        var h = deg * (PI / 360f);
        return new Quaternion { x = 0f, y = 0f, z = MathF.Sin(h), w = MathF.Cos(h) };
    }
    static readonly System.Random rng = new();
    public static float Range(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
    public static float Value => (float)rng.NextDouble();
}

/// <summary>画面の画素の四角 (左下・幅・高さ)。UnityEngine.Rect の値の読み出しは本編を呼ぶので、毎フレーム使う所はこちら</summary>
readonly struct Area
{
    public readonly float X, Y, W, H;
    public Area(float x, float y, float w, float h) { X = x; Y = y; W = w; H = h; }
    public float XMax => X + W;
    public float YMax => Y + H;
    public bool Empty => W <= 0f || H <= 0f;
    public bool Contains(float px, float py) => px >= X && px <= X + W && py >= Y && py <= Y + H;
    public bool Same(Area o) => Fx.Same(X, o.X) && Fx.Same(Y, o.Y) && Fx.Same(W, o.W) && Fx.Same(H, o.H);
}
