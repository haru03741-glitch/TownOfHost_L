using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SpawnEditorKit;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules.SpawnEditor;

public class SpawnPinEditor : MonoBehaviour
{
    public SpawnPinEditor(IntPtr ptr) : base(ptr) { }

    sealed class MapSheet
    {
        public int Id;
        public string Label;
        public string Resource;
        public float Ax, Bx, Ay, By;
        public Texture2D Tex;
        public bool[] Mask;
        public int W, H;
        public bool Failed;

        public Vector2 ToShip(float px, float py) => new(Ax * px + Bx, Ay * py + By);
        public Vector2 ToPixel(float x, float y) => new((x - Bx) / Ax, (y - By) / Ay);

        public bool Inside(int px, int py)
        {
            if (Mask == null || px < 0 || py < 0 || px >= W || py >= H) return false;
            return Mask[(H - 1 - py) * W + px];
        }
    }

    static readonly MapSheet[] Sheets =
    {
        new() { Id = 0, Label = "The Skeld", Resource = "SpawnMap_Skeld", Ax = 0.04402f, Bx = -23.623f, Ay = -0.04637f, By = 7.523f },
        new() { Id = 1, Label = "MIRA HQ", Resource = "SpawnMap_MiraHQ", Ax = 0.05420f, Bx = -12.094f, Ay = -0.05438f, By = 25.732f },
        new() { Id = 2, Label = "Polus", Resource = "SpawnMap_Polus", Ax = 0.04954f, Bx = 0.041f, Ay = -0.04933f, By = 1.264f },
        new() { Id = 4, Label = "Airship", Resource = "SpawnMap_Airship", Ax = 0.08369f, Bx = -25.978f, Ay = -0.08508f, By = 18.442f },
        new() { Id = 5, Label = "Fungle", Resource = "SpawnMap_Fungle", Ax = 0.06058f, Bx = -28.243f, Ay = -0.06036f, By = 16.501f },
    };

    const int MaxPins = 40;
    const float PickRadius = 17f;
    const float ArmSeconds = 3f;

    public static SpawnPinEditor Instance;
    static bool open;
    public static bool IsOpen => open;

    static int sheet;
    static int dragging = -1;
    static bool dragMoved;
    static int hovered = -1;
    static string message;
    static float messageUntil;
    static float clearArmedAt = -10f;

    public static void Toggle() => SetOpen(!open);

    public static void SetOpen(bool value)
    {
        open = value;
        dragging = -1;
        if (value)
        {
            int cur = TohlSpawnEditor.ToStorageMapId(Main.NormalOptions?.MapId ?? 0);
            for (int i = 0; i < Sheets.Length; i++) if (Sheets[i].Id == cur) sheet = i;
            SpawnStore.Load();
        }
        if (Instance != null) Instance.enabled = value;
    }

    public void Awake()
    {
        Instance = this;
        useGUILayout = false;
        enabled = false;
    }

    public void Update()
    {
        if (!open) return;
        if (Input.GetKeyDown(KeyCode.Escape) || !HostPanelButton.InRoom) SetOpen(false);
    }

    [HarmonyLib.HarmonyPatch(typeof(PassiveButtonManager), nameof(PassiveButtonManager.Update))]
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    static class BlockButtonsPatch
    {
        public static bool Prefix(PassiveButtonManager __instance)
        {
            if (!open) return true;
            __instance.HandleMouseOut();
            return false;
        }
    }

    static void Say(string key, params object[] args)
    {
        message = args.Length == 0 ? GetString(key) : string.Format(GetString(key), args);
        messageUntil = Time.unscaledTime + 2.5f;
    }

    static bool Load(MapSheet s)
    {
        if (s.Tex != null) return true;
        if (s.Failed) return false;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"TownOfHost_L.Resources.{s.Resource}.png");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(tex, ms.ToArray(), false)) throw new InvalidDataException(s.Resource);
            var px = tex.GetPixels32();
            s.W = tex.width;
            s.H = tex.height;
            s.Mask = new bool[px.Length];
            for (int i = 0; i < px.Length; i++) s.Mask[i] = Walkable(px[i]);
            tex.Apply(false, true);
            s.Tex = tex;
            return true;
        }
        catch (Exception ex)
        {
            s.Failed = true;
            Logger.Exception(ex, nameof(SpawnPinEditor));
            return false;
        }
    }

    static bool Walkable(Color32 c)
    {
        if (c.a < 128) return false;
        if (c.r >= 230 && c.g <= 235 && c.b <= 235) return true;
        if (c.r >= 200 && c.g >= 190 && c.b >= 130 && c.b <= 190 && c.r - c.b >= 40) return true;
        return c.b > c.r + 8 && c.g > c.r + 8 && c.r >= 110;
    }

    static List<SpawnPoint> Points(bool create) => SpawnStore.Library.Current.GetPoints(Sheets[sheet].Id, create);

    static float sw, sh, scale = -1f;
    static float Scale => sw / 1920f;
    static int Font => Mathf.Max(12, Mathf.RoundToInt(22f * Scale));
    static float Pad => 10f * Scale;
    static GUIStyle sWindow, sTitle, sSmall, sTab, sTabOn, sClose, sDanger, sPinText, sPanel;
    static Texture2D pinTex, ringTex;

    public void OnGUI()
    {
        sw = Screen.width; sh = Screen.height;
        if (!open) { enabled = false; return; }
        if (!Mathf.Approximately(scale, Scale)) BuildStyles();
        var e = Event.current;
        var type = e.type;

        float ww = Mathf.Min(sw * 0.92f, 1500f * Scale), wh = Mathf.Min(sh * 0.9f, 980f * Scale);
        float wx = (sw - ww) / 2f, wy = (sh - wh) / 2f;
        GUI.Box(new Rect(wx, wy, ww, wh), GUIContent.none, sWindow);

        float titleH = 56f * Scale;
        GUI.Label(new Rect(wx + Pad * 2f, wy, ww * 0.5f, titleH), GetString("SpawnPin.Title"), sTitle);
        float cs = titleH * 0.7f;
        if (GUI.Button(new Rect(wx + ww - cs - Pad * 1.5f, wy + (titleH - cs) / 2f, cs, cs), "×", sClose)) { SetOpen(false); return; }

        float rowH = 44f * Scale;
        float y = wy + titleH;
        float tw = (ww - Pad * 2f) / Sheets.Length;
        for (int i = 0; i < Sheets.Length; i++)
        {
            int count = SpawnStore.Library.Current.GetPoints(Sheets[i].Id, false)?.Count ?? 0;
            string label = count > 0 ? $"{Sheets[i].Label}  <color=#ffd36b>{count}</color>" : Sheets[i].Label;
            if (GUI.Button(new Rect(wx + Pad + tw * i + 2f, y, tw - 4f, rowH), label, i == sheet ? sTabOn : sTab)) { sheet = i; dragging = -1; }
        }
        y += rowH + Pad;

        var s = Sheets[sheet];
        int mode = TohlSpawnEditor.GetMode(s.Id);
        string[] modes = { "SpawnPin.ModeOff", "SpawnPin.ModeMix", "SpawnPin.ModeOnly" };
        float mw = 200f * Scale;
        GUI.Label(new Rect(wx + Pad * 2f, y, 190f * Scale, rowH), GetString("SpawnPin.Mode"), sSmall);
        float mx0 = wx + Pad * 2f + 190f * Scale;
        for (int i = 0; i < modes.Length; i++)
            if (GUI.Button(new Rect(mx0 + (mw + Pad * 0.5f) * i, y, mw, rowH), GetString(modes[i]), mode == i ? sTabOn : sTab) && mode != i)
                TohlSpawnEditor.SetMode(s.Id, i);

        var points = Points(false);
        int n = points?.Count ?? 0;
        bool armed = Time.unscaledTime - clearArmedAt <= ArmSeconds;
        float cw = 230f * Scale;
        if (n > 0 && GUI.Button(new Rect(wx + ww - Pad * 2f - cw, y, cw, rowH), GetString(armed ? "SpawnPin.ClearConfirm" : "SpawnPin.Clear"), sDanger))
        {
            if (armed)
            {
                points.Clear();
                SpawnStore.Save();
                clearArmedAt = -10f;
                Say("SpawnPin.Cleared");
            }
            else clearArmedAt = Time.unscaledTime;
        }
        GUI.Label(new Rect(wx + ww - Pad * 3f - cw - 220f * Scale, y, 210f * Scale, rowH), string.Format(GetString("SpawnPin.Count"), n, MaxPins), sSmall);
        y += rowH + Pad;

        float footH = rowH;
        float areaX = wx + Pad * 2f, areaY = y, areaW = ww - Pad * 4f, areaH = wy + wh - y - footH - Pad * 2f;
        GUI.Box(new Rect(areaX, areaY, areaW, areaH), GUIContent.none, sPanel);
        if (!Load(s))
        {
            GUI.Label(new Rect(areaX + Pad, areaY + Pad, areaW, rowH), GetString("SpawnPin.LoadFailed"), sSmall);
            return;
        }

        float k = Mathf.Min((areaW - Pad * 2f) / s.W, (areaH - Pad * 2f) / s.H);
        float iw = s.W * k, ih = s.H * k;
        float ix = areaX + (areaW - iw) / 2f, iy = areaY + (areaH - ih) / 2f;
        var imgRect = new Rect(ix, iy, iw, ih);
        ImguiTex.Draw(imgRect, s.Tex);

        var mouse = e.mousePosition;
        hovered = -1;
        if (points != null)
        {
            float best = PickRadius * Scale * 1.4f;
            for (int i = 0; i < points.Count; i++)
            {
                var pp = s.ToPixel(points[i].X, points[i].Y);
                float d = Vector2.Distance(mouse, new Vector2(ix + pp.x * k, iy + pp.y * k));
                if (d >= best) continue;
                best = d;
                hovered = i;
            }
        }

        bool overImage = imgRect.Contains(mouse);
        int mpx = Mathf.FloorToInt((mouse.x - ix) / k), mpy = Mathf.FloorToInt((mouse.y - iy) / k);
        bool inside = overImage && s.Inside(mpx, mpy);

        if (type == EventType.MouseDown && overImage)
        {
            if (e.button == 1)
            {
                if (hovered >= 0)
                {
                    points.RemoveAt(hovered);
                    SpawnStore.Save();
                    hovered = -1;
                    Say("SpawnPin.Removed");
                }
                e.Use();
            }
            else if (e.button == 0)
            {
                if (hovered >= 0) { dragging = hovered; dragMoved = false; }
                else if (!inside) Say("SpawnPin.Outside");
                else if (n >= MaxPins) Say("SpawnPin.Full", MaxPins);
                else
                {
                    var list = Points(true);
                    var ship = s.ToShip(mpx + 0.5f, mpy + 0.5f);
                    list.Add(new SpawnPoint { Name = (list.Count + 1).ToString(), Color = list.Count + 1, X = ship.x, Y = ship.y });
                    SpawnStore.Save();
                    if (TohlSpawnEditor.GetMode(s.Id) == 0)
                    {
                        TohlSpawnEditor.SetMode(s.Id, 2);
                        Say("SpawnPin.AutoOn");
                    }
                    else Say("SpawnPin.Added", list.Count);
                }
                e.Use();
            }
        }
        else if (type == EventType.MouseDrag && dragging >= 0 && points != null && dragging < points.Count)
        {
            if (inside)
            {
                var ship = s.ToShip(mpx + 0.5f, mpy + 0.5f);
                points[dragging].X = ship.x;
                points[dragging].Y = ship.y;
                dragMoved = true;
            }
            e.Use();
        }
        else if (type == EventType.MouseUp && dragging >= 0)
        {
            if (dragMoved) SpawnStore.Save();
            else if (points != null && dragging < points.Count)
            {
                points.RemoveAt(dragging);
                SpawnStore.Save();
                Say("SpawnPin.Removed");
            }
            dragging = -1;
            e.Use();
        }

        if (type == EventType.Repaint)
        {
            if (overImage && hovered < 0 && dragging < 0)
            {
                float r = PickRadius * Scale;
                var old = GUI.color;
                GUI.color = inside ? new Color(1f, 1f, 1f, 0.55f) : new Color(0.2f, 0.2f, 0.2f, 0.5f);
                ImguiTex.Draw(new Rect(mouse.x - r, mouse.y - r, r * 2f, r * 2f), ringTex);
                GUI.color = old;
            }
            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    var pp = s.ToPixel(points[i].X, points[i].Y);
                    float cx = ix + pp.x * k, cy = iy + pp.y * k;
                    float r = PickRadius * Scale * (i == hovered || i == dragging ? 1.25f : 1f);
                    var (cr, cg, cb) = SpawnPalette.Colors[2 + ((points[i].Color - 1) % 7 + 7) % 7];
                    var old = GUI.color;
                    GUI.color = new Color32(cr, cg, cb, 255);
                    ImguiTex.Draw(new Rect(cx - r, cy - r, r * 2f, r * 2f), pinTex);
                    GUI.color = old;
                    GUI.Label(new Rect(cx - r, cy - r, r * 2f, r * 2f), (i + 1).ToString(), sPinText);
                }
            }
        }

        string foot = Time.unscaledTime < messageUntil ? message : GetString("SpawnPin.Hint");
        GUI.Label(new Rect(wx + Pad * 2f, wy + wh - footH - Pad, ww - Pad * 4f, footH), foot, sSmall);
    }

    static void BuildStyles()
    {
        scale = Scale;
        var k = Mathf.Max(0.5f, scale);
        var r = Mathf.RoundToInt(10f * k) * 2;
        var br = Mathf.RoundToInt(6f * k) * 2;
        sWindow = new GUIStyle { normal = { background = HostPanel.RoundTex(new Color(0.05f, 0.06f, 0.13f, 0.97f), r) }, border = HostPanel.Border(r) };
        sPanel = new GUIStyle { normal = { background = HostPanel.RoundTex(new Color(0.93f, 0.94f, 0.97f, 1f), br) }, border = HostPanel.Border(br) };
        sTitle = Text(Font + 6, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.45f, 0.92f, 1f));
        sSmall = Text(Font - 2, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.82f, 0.86f, 0.95f));
        sPinText = Text(Mathf.Max(10, Font - 6), FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.08f, 0.08f, 0.12f));
        sTab = Btn(new Color(0.12f, 0.15f, 0.28f), new Color(0.2f, 0.26f, 0.45f), br);
        sTabOn = Btn(new Color(0.20f, 0.40f, 0.68f), new Color(0.26f, 0.48f, 0.78f), br);
        sClose = Btn(new Color(0.38f, 0.08f, 0.08f), new Color(0.6f, 0.12f, 0.12f), br);
        sDanger = Btn(new Color(0.34f, 0.10f, 0.10f), new Color(0.55f, 0.14f, 0.14f), br);
        pinTex ??= Disc(true);
        ringTex ??= Disc(false);
    }

    static Texture2D Disc(bool filled)
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - N / 2f) * (x + 0.5f - N / 2f) + (y + 0.5f - N / 2f) * (y + 0.5f - N / 2f));
                float outer = Mathf.Clamp01(N / 2f - 1f - d);
                float edge = Mathf.Clamp01(d - (N / 2f - 7f));
                byte v = (byte)(255f * (1f - edge * 0.85f));
                float a = filled ? outer : outer * edge;
                px[y * N + x] = new Color32(v, v, v, (byte)(255f * a));
            }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return tex;
    }

    static GUIStyle Text(int size, FontStyle fs, TextAnchor anchor, Color color)
        => new() { fontSize = size, fontStyle = fs, alignment = anchor, richText = true, normal = { textColor = color } };

    static GUIStyle Btn(Color normal, Color hover, int r) => new()
    {
        fontSize = Font - 1,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        richText = true,
        clipping = TextClipping.Clip,
        border = HostPanel.Border(r),
        padding = HostPanel.Border(Mathf.Max(4, r / 2)),
        normal = { background = HostPanel.RoundTex(normal, r), textColor = Color.white },
        hover = { background = HostPanel.RoundTex(hover, r), textColor = Color.white },
        active = { background = HostPanel.RoundTex(new Color(normal.r * 0.7f, normal.g * 0.7f, normal.b * 0.7f, 1f), r), textColor = Color.white },
    };
}
