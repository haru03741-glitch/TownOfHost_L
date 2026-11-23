using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TownOfHostL.SettingsUI;

// 役職のカードの値を絵で見せる: 人数ぶんのクルーの人形と出現率の字、その下に出現率の棒。
// 人形の並べ方は 2 通り。既定は小さめの人形を 5 人ずつの段で 15 人まで並べる。手元の設定で「人数を数字で」を選ぶと、
// 大きめの人形を 3 人まで並べ、それより多い時は 1 体と ×人数 にする。並べきれない人数はどちらも 1 体と ×人数。
// 出ない役職は人形も棒の塗りも無く OFF の字だけ。値が変わったら棒は伸び縮みし、人形は増えた分が弾む。
internal sealed class CrewMeter : Widget
{
    public const int PerRow = 5, MaxRows = 3, CompactMax = 3;
    private const float BigH = 0.27f, SmallH = 0.15f, GapRatio = 0.025f / 0.27f, RowStep = 0.02f, BarH = 0.055f, RowGap = 0.05f;

    public float Width;
    public Color Color = Color.white;
    public float FontSize = 1.15f;

    private readonly bool _compact;
    private readonly int _rows;          // 人形の欄に取った段の数 (隣のカードと高さを揃えるため、要る段より多いことがある)
    private bool _on;
    private int _chance, _count;
    private bool _hasCount;
    private float _shownFrac;

    private GameObject _root;
    private int _layer;
    private Sprite _crew;
    private readonly List<SpriteRenderer> _figs = new();
    private SpriteRenderer _track, _fill;
    private Label _label;
    private float _barY, _areaY;

    public CrewMeter(float width, Color color, bool on, int chance, bool hasCount, int count, bool compact, int rows = 1)
    {
        Width = width;
        Color = color;
        _on = on;
        _chance = chance;
        _hasCount = hasCount;
        _count = count;
        _compact = compact;
        _rows = compact ? 1 : Math.Clamp(rows, 1, MaxRows);
    }

    // 人形の欄に要る段の数 (隣のカードと揃える時に使う)
    public static int RowsFor(bool compact, bool on, bool hasCount, int count)
    {
        if (compact) return 1;
        int n = FiguresFor(false, on, hasCount, count);
        return Math.Max(1, (n + PerRow - 1) / PerRow);
    }

    private static int MaxOf(bool compact) => compact ? CompactMax : PerRow * MaxRows;
    // 並べられる人数までは人数ぶん、それより多い時は 1 体と「×人数」の字にする
    private static int FiguresFor(bool compact, bool on, bool hasCount, int count) =>
        !on || !hasCount ? 0 : count > MaxOf(compact) ? 1 : Math.Max(count, 0);

    public int Rows => _rows;
    public int NeedRows => RowsFor(_compact, _on, _hasCount, _count);

    private float FigH => _compact ? BigH : SmallH;
    private float FigW => FigH * 0.8f;
    private float FigGap => FigH * GapRatio;
    private float AreaH => Math.Max(BigH, _rows * SmallH + (_rows - 1) * RowStep);

    public override GameObject Build(Transform parent, BuildCtx ctx, float maxWidth, out Vector2 size)
    {
        var go = Node("CrewMeter", parent, ctx.Layer);
        _root = go;
        _layer = ctx.Layer;
        _crew = CrewSprite();
        _figs.Clear();
        float h = AreaH + RowGap + BarH;
        size = new Vector2(Width, h);
        _areaY = h / 2f - AreaH / 2f;
        _barY = -h / 2f + BarH / 2f;

        _label = new Label("", FontSize) { Bold = true, Width = Width, MinFontSize = 0.75f, TextAlign = TextAlignmentOptions.Left, NoWrap = true };
        var lgo = _label.Build(go.transform, ctx, Width, out _);
        lgo.transform.localPosition = new Vector3(0f, _areaY, -0.02f);

        var round = MenuArt.Round(BarH / 2f);
        _track = Node(UiScreen.BackName, go.transform, ctx.Layer).AddComponent<SpriteRenderer>();
        _track.sprite = round;
        _track.drawMode = SpriteDrawMode.Sliced;
        _track.size = new Vector2(Width, BarH);
        _track.transform.localPosition = new Vector3(0f, _barY, 0f);
        _fill = Node(UiScreen.FillName, go.transform, ctx.Layer).AddComponent<SpriteRenderer>();
        _fill.sprite = round;
        _fill.drawMode = SpriteDrawMode.Sliced;

        Apply(-1);
        SetBar(Target);
        return go;
    }

    public bool On => _on;

    private float Target => _on ? Math.Clamp(_chance / 100f, 0f, 1f) : 0f;
    private int Figures => FiguresFor(_compact, _on, _hasCount, _count);

    // 値を書き換える。animate なら棒を伸び縮みさせ、増えた人形を弾ませる
    public void Set(Color color, bool on, int chance, int count, bool animate)
    {
        if (color == Color && on == _on && chance == _chance && count == _count) return;
        int before = Figures;
        Color = color;
        _on = on;
        _chance = chance;
        _count = count;
        if (!_root) return;
        Apply(animate ? before : -1);
        float from = _shownFrac, to = Target;
        if (!animate || Math.Abs(from - to) < 0.001f) { SetBar(to); return; }
        UiFx.Play(_fill, UiFx.ChannelBar, 0.22f, p => SetBar(from + (to - from) * UiFx.EaseOut(p)));
    }

    // 開いた時に棒を 0 から伸ばす (delay 秒待ってから)
    public void GrowIn(float delay)
    {
        if (!_fill) return;
        float to = Target;
        if (to <= 0f) return;
        SetBar(0f);
        float total = delay + 0.25f;
        UiFx.Play(_fill, UiFx.ChannelBar, total, p =>
        {
            float q = Math.Clamp((p * total - delay) / 0.25f, 0f, 1f);
            SetBar(to * UiFx.EaseOut(q));
        });
    }

    // 人形は要る数だけ作る (一覧のカードが多いので、15 体ぶんを先に作っておかない)
    private SpriteRenderer Fig(int i)
    {
        while (_figs.Count <= i)
        {
            // 弾ませる時は入れ物の大きさを変える (絵の大きさ合わせは中の絵に持たせる)
            var holder = Node("Crew", _root.transform, _layer);
            holder.SetActive(false);
            var sr = Node("Body", holder.transform, _layer).AddComponent<SpriteRenderer>();
            sr.sprite = _crew;
            var b = _crew ? _crew.bounds.size : Vector3.one;
            float sc = FigH / Math.Max(b.y, 0.0001f);
            sr.transform.localScale = new Vector3(sc, sc, 1f);
            if (_track)
            {
                sr.sortingOrder = _track.sortingOrder + 4;
                sr.maskInteraction = _track.maskInteraction;
            }
            _figs.Add(sr);
        }
        return _figs[i];
    }

    private void Apply(int popFrom)
    {
        int n = Figures;
        int perRow = _compact ? CompactMax : PerRow;
        int usedRows = Math.Max(1, (n + perRow - 1) / perRow);
        // 使う段のまとまりを欄の真ん中に置く (隣に合わせて広げた欄でも字と人形の高さが揃う)
        float blockH = usedRows * FigH + (usedRows - 1) * RowStep;
        float topY = _areaY + blockH / 2f - FigH / 2f;
        var figColor = Color.Lerp(Color, Color.white, 0.25f);
        int upto = Math.Max(n, _figs.Count);
        for (int i = 0; i < upto; i++)
        {
            bool show = i < n;
            var f = Fig(i);
            if (!f) continue;
            var holder = f.transform.parent;
            if (show)
            {
                int r = i / perRow, c = i % perRow;
                holder.localPosition = new Vector3(-Width / 2f + FigW / 2f + c * (FigW + FigGap), topY - r * (FigH + RowStep), -0.01f);
                f.color = figColor;
            }
            if (holder.gameObject.activeSelf != show) holder.gameObject.SetActive(show);
            if (show && popFrom >= 0 && i >= popFrom) UiFx.Pop(holder, 0.35f, 0.22f);
        }
        // 人形の後ろに字 (並べきれない時は ×人数)。出ない役職は OFF だけ
        // 字の欄は人形の右から端まで (長い字はその幅に入るよう縮める)
        string text = !_on ? "OFF" : _hasCount && _count > MaxOf(_compact) ? $"×{_count}  {_chance}%" : $"{_chance}%";
        int cols = Math.Min(n, perRow);
        float left = cols > 0 ? cols * (FigW + FigGap) + 0.04f : 0f;
        float labelW = Math.Max(Width - left, 0.3f);
        if (text != _label.Content || labelW != _label.Width)
        {
            _label.Width = labelW;
            _label.SetText(text);
        }
        if (_label.Built)
        {
            _label.Built.color = _on ? Color.white : new Color(0.42f, 0.42f, 0.45f, 1f);
            var rt = _label.Built.rectTransform;
            rt.sizeDelta = new Vector2(labelW, rt.sizeDelta.y);
            var p = rt.localPosition;
            rt.localPosition = new Vector3(left / 2f, p.y, p.z);
        }
        if (_track) _track.color = _on ? Color.Lerp(new Color(0.16f, 0.17f, 0.22f, 1f), Color, 0.18f) : new Color(0.2f, 0.2f, 0.22f, 1f);
        if (_fill) _fill.color = Color;
    }

    private void SetBar(float frac)
    {
        _shownFrac = frac;
        if (!_fill) return;
        // 丸い端の分より短い時は隠す (縮めた丸が潰れて見えるため)
        float w = Width * frac;
        bool show = w >= BarH;
        if (_fill.enabled != show) _fill.enabled = show;
        if (!show) return;
        _fill.size = new Vector2(w, BarH);
        _fill.transform.localPosition = new Vector3(-Width / 2f + w / 2f, _barY, -0.005f);
    }

    // ---- クルーの人形 (白・色は SpriteRenderer.color で付ける)。右向き・左に背負い袋・顔の窓は半透明 ----

    private static Sprite _crewSprite;
    private const int CW = 32, CH = 40;

    internal static Sprite CrewSprite()
    {
        if (_crewSprite) return _crewSprite;
        var px = new byte[CW * CH * 4];
        for (int y = 0; y < CH; y++)
        for (int x = 0; x < CW; x++)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            // 胴 (上も下も丸い)・後ろと前の脚 (先が丸い短い柱)・背負い袋。脚の間は胴の丸い裾がアーチになる
            float torso = RoundBox(fx, fy, 8f, 27.5f, 8f, 38f, 9.75f);
            float back = RoundBox(fx, fy, 8f, 16.5f, 1.5f, 17f, 3.5f);
            float front = RoundBox(fx, fy, 19f, 27.5f, 1.5f, 17f, 3.5f);
            float pack = RoundBox(fx, fy, 3f, 10f, 14f, 31f, 3f);
            float d = Math.Min(Math.Min(torso, pack), Math.Min(back, front));
            float a = Math.Clamp(0.5f - d, 0f, 1f);
            // 顔の窓 = 右寄りの横長の楕円。中は薄く (地の色が透けて窓に見える)
            float vx = (fx - 22.5f) / 6.8f, vy = (fy - 28f) / 4.4f;
            float visor = (MathF.Sqrt(vx * vx + vy * vy) - 1f) * 4.4f;
            float inVisor = Math.Clamp(0.5f - visor, 0f, 1f);
            a *= 1f - 0.62f * inVisor;
            int i = (y * CW + x) * 4;
            px[i] = px[i + 1] = px[i + 2] = 255;
            px[i + 3] = (byte)(a * 255f + 0.5f);
        }
        var tex = new Texture2D(CW, CH, TextureFormat.RGBA32, false) { name = "TohlMenuCrew" };
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.LoadRawTextureData(px);
        tex.Apply(false, true);
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        _crewSprite = Sprite.Create(tex, new Rect(0, 0, CW, CH), new Vector2(0.5f, 0.5f), 100f);
        _crewSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return _crewSprite;
    }

    // 角の丸い四角 (x0..x1, y0..y1・角の半径 r) までの距離 (内側が負)
    private static float RoundBox(float x, float y, float x0, float x1, float y0, float y1, float r)
    {
        float cx = (x0 + x1) / 2f, cy = (y0 + y1) / 2f;
        float hx = (x1 - x0) / 2f - r, hy = (y1 - y0) / 2f - r;
        float qx = Math.Abs(x - cx) - hx, qy = Math.Abs(y - cy) - hy;
        float ox = Math.Max(qx, 0f), oy = Math.Max(qy, 0f);
        return MathF.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0f) - r;
    }
}
