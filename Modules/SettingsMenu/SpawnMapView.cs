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
        // 地図の下に、別の行で「ピンを全部消す」「初期の配置にする」 (どちらも 2 回押しで実行)
        float inner = rowW - OptEditor.PlatePad * 2f;
        return new Stack(false,
            OptEditor.Plated(view, rowW, 1),
            OptEditor.Plated(view.ActionRow(Translator.GetString("SpawnPin.Clear"), inner, view.Clear), rowW, 0),
            OptEditor.Plated(view.ActionRow(Translator.GetString("SpawnPin.Reset"), inner, view.ResetToDefault), rowW, 1))
        { Gap = 0.04f, Width = rowW, Align = Align.Start };
    }

    private const float ActBtnW = 1.5f, ActBtnH = 0.32f;
    private static readonly Color ActColor = new(1f, 0.72f, 0.3f, 1f), ConfirmColor = new(1f, 0.38f, 0.38f, 1f);
    private string _pending;   // もう一度押すと実行する操作の名前

    private Widget ActionRow(string name, float width, System.Action run)
    {
        var label = new Label(name, OptEditor.NarrowFont) { Width = width - ActBtnW - 0.08f, Bold = true, NoWrap = true, MinFontSize = 0.8f, Color = Color.white };
        Button btn = null;
        btn = new Button(name, () =>
        {
            if (!SettingsPages.CanEdit()) return;
            if (_pending != name)
            {
                // 1 回目: 確かめる
                _pending = name;
                btn.SetColor(ConfirmColor);
                _info?.SetText(InfoText(Translator.GetString(name == Translator.GetString("SpawnPin.Clear") ? "SpawnPin.ClearConfirm" : "SpawnPin.ResetConfirm")));
                return;
            }
            _pending = null;
            btn.SetColor(ActColor);
            run();
        }, 1.1f) { Size = new Vector2(ActBtnW, ActBtnH), Color = ActColor };
        return new Stack(true, label, btn) { Gap = 0.08f, Align = Align.Start };
    }

    // このマップのピンを全部外す
    private void Clear()
    {
        var points = Points(false);
        points?.Clear();
        SpawnStore.Save();
        DrawPins(true);
        _info?.SetText(InfoText(Translator.GetString("SpawnPin.Cleared")));
    }

    // TownOfHost-hamo のランダムスポーンの部屋の位置 (Patches/RandomSpawnPatch.cs) をピンにする
    private static readonly Dictionary<int, (string name, float x, float y)[]> HamoDefaults = new()
    {
        [0] = new (string, float, float)[] { ("Cafeteria", -1.0f, 3.0f), ("Weapons", 9.3f, 1.0f), ("LifeSupp", 6.5f, -3.8f), ("Nav", 16.5f, -4.8f), ("Shields", 9.3f, -12.3f), ("Comms", 4.0f, -15.5f), ("Storage", -1.5f, -15.5f), ("Admin", 4.5f, -7.9f), ("Electrical", -7.5f, -8.8f), ("LowerEngine", -17.0f, -13.5f), ("UpperEngine", -17.0f, -1.3f), ("Security", -13.5f, -5.5f), ("Reactor", -20.5f, -5.5f), ("MedBay", -9.0f, -4.0f) },
        [1] = new (string, float, float)[] { ("Cafeteria", 25.5f, 2.0f), ("Balcony", 24.0f, -2.0f), ("Storage", 19.5f, 4.0f), ("Junction", 17.8f, 11.5f), ("Comms", 15.3f, 3.8f), ("MedBay", 15.5f, -0.5f), ("LockerRoom", 9.0f, 1.0f), ("Decontamination", 6.1f, 6.0f), ("Laboratory", 9.5f, 12.0f), ("Reactor", 2.5f, 10.5f), ("Launchpad", -4.5f, 2.0f), ("Admin", 21.0f, 17.5f), ("Office", 15.0f, 19.0f), ("Greenhouse", 17.8f, 23.0f) },
        [2] = new (string, float, float)[] { ("OfficeLeft", 19.5f, -18.0f), ("OfficeRight", 26.0f, -17.0f), ("Admin", 24.0f, -22.5f), ("Comms", 12.5f, -16.0f), ("Weapons", 12.0f, -23.5f), ("BoilerRoom", 2.3f, -24.0f), ("LifeSupp", 2.0f, -17.5f), ("Electrical", 9.5f, -12.5f), ("Security", 3.0f, -12.0f), ("Dropship", 16.7f, -3.0f), ("Storage", 20.5f, -12.0f), ("Rocket", 26.7f, -8.5f), ("Laboratory", 36.5f, -7.5f), ("Toilet", 34.0f, -10.0f), ("Specimens", 36.5f, -22.0f) },
        [4] = new (string, float, float)[] { ("Brig", -0.7f, 8.5f), ("Engine", -0.7f, -1.0f), ("Kitchen", -7.0f, -11.5f), ("CargoBay", 33.5f, -1.5f), ("Records", 20.0f, 10.5f), ("MainHall", 15.5f, 0.0f), ("NapRoom", 6.3f, 2.5f), ("MeetingRoom", 17.1f, 14.9f), ("GapRoom", 12.0f, 8.5f), ("VaultRoom", -8.9f, 12.2f), ("Comms", -13.3f, 1.3f), ("Cockpit", -23.5f, -1.6f), ("Armory", -10.3f, -5.9f), ("ViewingDeck", -13.7f, -12.6f), ("Security", 5.8f, -10.8f), ("Electrical", 16.3f, -8.8f), ("Medical", 29.0f, -6.2f), ("Toilet", 30.9f, 6.8f), ("Showers", 21.2f, -0.8f) },
        [5] = new (string, float, float)[] { ("Kitchen", -17.8f, -7.3f), ("Beach", -21.3f, 3.0f), ("Cafeteria", -16.9f, 5.5f), ("RecRoom", -17.7f, 0.0f), ("Bonfire", -9.7f, 2.7f), ("Dropship", -7.6f, 10.4f), ("Storage", 2.3f, 4.3f), ("MeetingRoom", -4.2f, -2.2f), ("SleepingQuarters", 1.7f, -1.4f), ("Laboratory", -4.2f, -7.9f), ("Greenhouse", 9.2f, -11.8f), ("Reactor", 21.8f, -7.2f), ("JungleTop", 4.2f, -5.3f), ("JungleBottom", 15.9f, -14.8f), ("Lookout", 6.4f, 3.1f), ("MiningPit", 12.5f, 9.6f), ("Highlands", 15.5f, 3.9f), ("UpperEngine", 21.9f, 3.2f), ("Precipice", 19.8f, 7.3f), ("Comms", 20.9f, 13.4f) },
    };

    // ピンを hamo のスポーン位置と同じ並びに置き直す
    private void ResetToDefault()
    {
        if (!HamoDefaults.TryGetValue(_sheet.Id, out var defs)) return;
        var list = Points(true);
        list.Clear();
        for (int i = 0; i < defs.Length && i < SpawnPinEditor.MaxPins; i++)
            list.Add(new SpawnPoint { Name = defs[i].name, Color = i + 1, X = defs[i].x, Y = defs[i].y });
        SpawnStore.Save();
        DrawPins(true);
        _info?.SetText(InfoText());
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
