using System.Collections.Generic;
using UnityEngine;

namespace TownOfHostL.Modules;

public static class FloorColorSampler
{
    private const int Size = 16;
    private static readonly string[] SkipLayers = { "Players", "Ghost", "UI", "Shadow", "KillAnimation" };

    // マスごとに読む画素の数 (1 辺)
    private const int CellPixels = 4;
    // 床として読む画素の透明度の下限。床の上に半透明の物が重なると 1 より小さくなる (2026-11-27 実機で 0.84〜0.89)
    private const float MinAlpha = 0.5f;

    /// <summary>
    /// <paramref name="center"/> を中心にした幅 <paramref name="width"/>・高さ <paramref name="height"/> の範囲を
    /// <paramref name="cols"/> × <paramref name="rows"/> のマスに分け、マスごとに一番多く映っている床の色を返す ([列, 行]・行 0 が上)。
    /// 色は似た色をまとめて数え、一番多い組の平均を取る (赤・緑・青を別々に取ると、床と机の境目でどちらでもない色になる)。
    /// 読めなかったマスは、範囲全体で一番多い色にする
    /// </summary>
    public static Color[,] SampleGrid(Vector2 center, float width, float height, int cols, int rows)
    {
        var result = new Color[cols, rows];
        GameObject go = null;
        RenderTexture rt = null;
        Texture2D tex = null;
        var prevActive = RenderTexture.active;
        try
        {
            int w = cols * CellPixels, h = rows * CellPixels;
            go = new GameObject("FloorColorSampler");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = height / 2f;
            cam.transform.position = new Vector3(center.x, center.y, -100f);
            cam.cullingMask = CullingMask();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            rt = new RenderTexture(w, h, 16);
            cam.targetTexture = rt;
            cam.aspect = width / height;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            cam.targetTexture = null;

            var all = new List<Color>();
            var cells = new List<Color>[cols, rows];
            int seen = 0;
            float maxAlpha = 0f;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    var c = tex.GetPixel(x, y);
                    if (c.a > 0f) seen++;
                    if (c.a > maxAlpha) maxAlpha = c.a;
                    if (c.a < MinAlpha) continue;
                    // 半透明に重なった所は、色も透明度の割合で暗く書き込まれるので割り戻す
                    c = new Color(c.r / c.a, c.g / c.a, c.b / c.a, 1f);
                    // 画素の y は下から、行は上から数える
                    int cx = x / CellPixels, cy = rows - 1 - y / CellPixels;
                    (cells[cx, cy] ??= new()).Add(c);
                    all.Add(c);
                }
            if (all.Count < w * h / 4)
                Logger.Info($"床の色を読めた画素が少ない: 不透明 {all.Count}・透明でない {seen}・透明度の最大 {maxAlpha:0.00}・中心 #{ColorUtility.ToHtmlStringRGBA(tex.GetPixel(w / 2, h / 2))}", "FloorColorSampler");
            var overall = all.Count >= w * h / 4 ? Dominant(all) : MapFallback();
            for (int cx = 0; cx < cols; cx++)
                for (int cy = 0; cy < rows; cy++)
                {
                    var list = cells[cx, cy];
                    result[cx, cy] = list != null && list.Count >= CellPixels * CellPixels / 2 ? Dominant(list) : overall;
                }
            Logger.Info($"床の色 (マス {cols}×{rows}): 全体 #{ColorUtility.ToHtmlStringRGB(overall)} ({all.Count}点)", "FloorColorSampler");
        }
        catch (System.Exception e)
        {
            Logger.Warn($"床の色を読めませんでした: {e.Message}", "FloorColorSampler");
            var fallback = MapFallback();
            for (int cx = 0; cx < cols; cx++)
                for (int cy = 0; cy < rows; cy++) result[cx, cy] = fallback;
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (tex != null) Object.Destroy(tex);
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
            if (go != null) Object.Destroy(go);
        }
        return result;
    }

    private static int CullingMask()
    {
        var mask = ~0;
        foreach (var name in SkipLayers)
        {
            var layer = LayerMask.NameToLayer(name);
            if (layer >= 0) mask &= ~(1 << layer);
        }
        return mask;
    }

    // 似た色 (各成分 1/16 の幅) をまとめて数え、一番多い組の平均を返す
    private static Color Dominant(List<Color> colors)
    {
        var groups = new Dictionary<int, (int n, float r, float g, float b)>();
        foreach (var c in colors)
        {
            int key = ((int)(c.r * 15.99f) << 8) | ((int)(c.g * 15.99f) << 4) | (int)(c.b * 15.99f);
            groups.TryGetValue(key, out var e);
            groups[key] = (e.n + 1, e.r + c.r, e.g + c.g, e.b + c.b);
        }
        (int n, float r, float g, float b) best = default;
        foreach (var e in groups.Values)
            if (e.n > best.n) best = e;
        return best.n == 0 ? Color.gray : new Color(best.r / best.n, best.g / best.n, best.b / best.n, 1f);
    }

    private static Color MapFallback()
    {
        var hex = (int)GameMap.Current switch
        {
            0 or 3 => "#7C8494",
            1 => "#A9B0BA",
            2 => "#6E6863",
            4 => "#7D746A",
            5 => "#5F6A45",
            _ => "#808080",
        };
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;
    }
}
