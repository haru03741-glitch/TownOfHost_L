using System.Collections.Generic;
using UnityEngine;

namespace TownOfHostL.Modules;

internal static class ImguiTex
{
    static readonly Dictionary<Texture2D, GUIStyle> styles = new();

    public static void Draw(Rect rect, Texture2D tex)
    {
        if (tex == null) return;
        if (!styles.TryGetValue(tex, out var style) || style == null)
        {
            style = new GUIStyle { normal = { background = tex } };
            styles[tex] = style;
        }
        GUI.Box(rect, GUIContent.none, style);
    }
}
