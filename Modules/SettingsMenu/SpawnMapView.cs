using System.Collections.Generic;
using SpawnEditorKit;
using TownOfHostL.Modules.SpawnEditor;
using UnityEngine;
using UnityEngine.Events;

namespace TownOfHostL.SettingsUI;

internal sealed class SpawnMapView : Widget
{
    private const float PinSize = 0.17f;

    private static Sprite pinSprite;
    private static readonly Dictionary<int, Sprite> mapSprites = new();

    private readonly SpawnPinEditor.MapSheet _sheet;
    private readonly float _width;
    private Label _info;
    private SpriteRenderer _map;
    private Transform _pins;
    private float _scale;

    private SpawnMapView(SpawnPinEditor.MapSheet sheet, float width)
    {
        _sheet = sheet;
        _width = width;
    }

    public static int MapOf(OptionItem item)
    {
        if (item == null) return -1;
        if (item == Options.CustomSpawn_Skeld) return 0;
        if (item == Options.CustomSpawn_MiraHQ) return 1;
        if (item == Options.CustomSpawn_Polus) return 2;
        if (item == Options.CustomSpawn_Airship) return 4;
        if (item == Options.CustomSpawn_Fungle) return 5;
        return -1;
    }

    public static Widget After(MenuOpt opt, float rowW, bool edit)
    {
        if (!edit || opt?.Item == null) return null;
        int id = MapOf(opt.Item);
        if (id < 0 || !opt.Item.GetBool()) return null;
        SpawnPinEditor.MapSheet sheet = null;
        foreach (var s in SpawnPinEditor.Sheets) if (s.Id == id) sheet = s;
        if (sheet == null || !SpawnPinEditor.Load(sheet)) return null;
        var view = new SpawnMapView(sheet, rowW - OptEditor.PlatePad * 2f);
        return OptEditor.Plated(view, rowW, 1);
    }

    private List<SpawnPoint> Points(bool create) => SpawnStore.Library.Current.GetPoints(_sheet.Id, create);

    private string InfoText(string tail = null)
    {
        int n = Points(false)?.Count ?? 0;
        return $"<color=#ffd36b>{string.Format(Translator.GetString("SpawnPin.Count"), n, SpawnPinEditor.MaxPins)}</color>  <size=85%><color=#B8C0CC>{tail ?? Translator.GetString("SpawnPin.SettingsHint")}</color></size>";
    }

    public override GameObject Build(Transform parent, BuildCtx ctx, float maxWidth, out Vector2 size)
    {
        var go = Node("SpawnMap", parent, ctx.Layer);
        live.Add(this);
        float w = Mathf.Min(_width, maxWidth);
        float mapW = w;
        float mapH = mapW * _sheet.H / _sheet.W;

        _info = new Label(InfoText(), 1.25f) { Width = w };
        var infoGo = _info.Build(go.transform, ctx, w, out var infoSize);
        float total = infoSize.y + 0.06f + mapH;
        infoGo.transform.localPosition = new Vector3(0f, total / 2f - infoSize.y / 2f, 0f);

        var mapGo = Node("Map", go.transform, ctx.Layer);
        mapGo.transform.localPosition = new Vector3(0f, -total / 2f + mapH / 2f, 0f);
        _map = mapGo.AddComponent<SpriteRenderer>();
        _map.sprite = MapSprite(_sheet);
        var b = _map.sprite.bounds.size;
        _scale = mapW / b.x;
        mapGo.transform.localScale = new Vector3(_scale, _scale, 1f);

        var col = mapGo.AddComponent<BoxCollider2D>();
        col.size = new Vector2(b.x, b.y);
        var pb = mapGo.AddComponent<PassiveButton>();
        pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        pb.OnMouseOver = new UnityEvent();
        pb.OnMouseOut = new UnityEvent();
        if (ctx.ClickMask) pb.ClickMask = ctx.ClickMask;
        pb.OnClick.AddListener(UiActions.Take(Clicked, ctx.Slots));
        pb.ClickSound = UiAssets.ClickSound;

        _pins = Node("Pins", mapGo.transform, ctx.Layer).transform;
        _pins.localPosition = new Vector3(0f, 0f, -0.01f);
        DrawPins(false);

        size = new Vector2(w, total);
        return go;
    }

    private static Sprite MapSprite(SpawnPinEditor.MapSheet s)
    {
        if (mapSprites.TryGetValue(s.Id, out var sp) && sp) return sp;
        sp = Sprite.Create(s.Tex, new Rect(0, 0, s.W, s.H), new Vector2(0.5f, 0.5f), 100f);
        sp.hideFlags = HideFlags.HideAndDontSave;
        mapSprites[s.Id] = sp;
        return sp;
    }

    private static Sprite PinSprite()
    {
        if (pinSprite) return pinSprite;
        var tex = SpawnPinEditor.Disc(true);
        pinSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        pinSprite.hideFlags = HideFlags.HideAndDontSave;
        return pinSprite;
    }

    private Vector2 PixelToLocal(float px, float py) => new((px / _sheet.W - 0.5f) * _sheet.W / 100f, (0.5f - py / _sheet.H) * _sheet.H / 100f);

    private void DrawPins(bool live)
    {
        if (!_pins) return;
        for (int i = _pins.childCount - 1; i >= 0; i--)
        {
            var c = _pins.GetChild(i).gameObject;
            c.transform.SetParent(null, false);
            Object.Destroy(c);
        }
        var points = Points(false);
        if (points == null) return;
        var sprite = PinSprite();
        float k = PinSize / (sprite.bounds.size.x * _scale);
        for (int i = 0; i < points.Count; i++)
        {
            var pp = _sheet.ToPixel(points[i].X, points[i].Y);
            var lp = PixelToLocal(pp.x, pp.y);
            var pin = Node("Pin", _pins, _map.gameObject.layer);
            pin.transform.localPosition = new Vector3(lp.x, lp.y, 0f);
            pin.transform.localScale = new Vector3(k, k, 1f);
            var sr = pin.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            var (cr, cg, cb) = SpawnPalette.Colors[2 + ((points[i].Color - 1) % 7 + 7) % 7];
            sr.color = new Color32(cr, cg, cb, 255);
            if (live)
            {
                sr.sortingOrder = _map.sortingOrder + 2;
                sr.maskInteraction = _map.maskInteraction;
            }
        }
    }

    private static readonly List<SpawnMapView> live = new();

    internal static bool ClickScreen(Vector3 screen)
    {
        var cam = Camera.main;
        if (!cam) return false;
        Vector2 world = cam.ScreenToWorldPoint(screen);
        live.RemoveAll(v => !v._map);
        foreach (var v in live)
        {
            var b = v._map.bounds;
            if (world.x < b.min.x || world.x > b.max.x || world.y < b.min.y || world.y > b.max.y) continue;
            v.ClickAt(screen);
            return true;
        }
        return false;
    }

    private void Clicked() => ClickAt(Input.mousePosition);

    private void ClickAt(Vector3 screen)
    {
        if (!_map || !SettingsPages.CanEdit()) return;
        var cam = Camera.main;
        if (!cam) return;
        Vector2 world = cam.ScreenToWorldPoint(screen);
        var local = _map.transform.InverseTransformPoint(world);
        float px = (local.x * 100f / _sheet.W + 0.5f) * _sheet.W;
        float py = (0.5f - local.y * 100f / _sheet.H) * _sheet.H;

        var points = Points(false);
        int hit = -1;
        if (points != null)
        {
            float best = PinSize * 0.75f / (_scale / 100f);
            for (int i = 0; i < points.Count; i++)
            {
                var pp = _sheet.ToPixel(points[i].X, points[i].Y);
                float d = Vector2.Distance(new Vector2(px, py), pp);
                if (d >= best) continue;
                best = d;
                hit = i;
            }
        }
        string msg;
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (hit >= 0 && !shift) msg = Translator.GetString("SpawnPin.ShiftToRemove");
        else if (hit >= 0)
        {
            points.RemoveAt(hit);
            SpawnStore.Save();
            msg = Translator.GetString("SpawnPin.Removed");
        }
        else if (!_sheet.Inside(Mathf.FloorToInt(px), Mathf.FloorToInt(py))) msg = Translator.GetString("SpawnPin.Outside");
        else if ((points?.Count ?? 0) >= SpawnPinEditor.MaxPins) msg = string.Format(Translator.GetString("SpawnPin.Full"), SpawnPinEditor.MaxPins);
        else
        {
            var list = Points(true);
            var ship = _sheet.ToShip(Mathf.Floor(px) + 0.5f, Mathf.Floor(py) + 0.5f);
            list.Add(new SpawnPoint { Name = (list.Count + 1).ToString(), Color = list.Count + 1, X = ship.x, Y = ship.y });
            SpawnStore.Save();
            msg = string.Format(Translator.GetString("SpawnPin.Added"), list.Count);
        }
        DrawPins(true);
        _info?.SetText(InfoText(msg));
    }
}
