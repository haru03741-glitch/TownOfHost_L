// Based on https://github.com/waffle-ful/Aeterna-End-K-not Modules/SpawnEditor/EkSpawnEditor.cs (GPL-3.0)
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SpawnEditorKit;
using UnityEngine;

namespace TownOfHostL.Modules.SpawnEditor;

internal static class TohlSpawnEditor
{
    public const string SavePath = "./TOHL_DATA/SpawnPoints.json";
    private const string OldPath = "./TOHL_DATA/CustomSpawns.json";

    public static void Init()
    {
        Migrate();
        SpawnStore.Init(SavePath, m => Logger.Warn(m, "SpawnEditor"));
    }

    public static int ToStorageMapId(int mapId) => mapId switch
    {
        0 or 3 => 0,
        1 or 2 or 4 or 5 => mapId,
        _ => -1
    };

    public static RandomSpawn.SpawnMap MapOf(int mapId) => RandomSpawn.ForMap((MapNames)mapId);

    public static (OptionItem map, OptionItem random, OptionItem custom, OptionItem only)? Opts(int mapId) => mapId switch
    {
        0 or 3 => (Options.MapOption_Skeld, Options.RandomSpawn_Skeld, Options.CustomSpawn_Skeld, Options.CustomSpawnOnly_Skeld),
        1 => (Options.MapOption_MiraHQ, Options.RandomSpawn_MiraHQ, Options.CustomSpawn_MiraHQ, Options.CustomSpawnOnly_MiraHQ),
        2 => (Options.MapOption_Polus, Options.RandomSpawn_Polus, Options.CustomSpawn_Polus, Options.CustomSpawnOnly_Polus),
        4 => (Options.MapOption_Airship, Options.RandomSpawn_Airship, Options.CustomSpawn_Airship, Options.CustomSpawnOnly_Airship),
        5 => (Options.MapOption_Fungle, Options.RandomSpawn_Fungle, Options.CustomSpawn_Fungle, Options.CustomSpawnOnly_Fungle),
        _ => null
    };

    public static int GetMode(int mapId)
    {
        if (Opts(mapId) is not { } o || o.custom == null) return 0;
        bool on = o.map.GetBool() && o.random.GetBool() && o.custom.GetBool();
        return !on ? 0 : o.only.GetValue() == 1 ? 2 : 1;
    }

    public static void SetMode(int mapId, int mode)
    {
        if (Opts(mapId) is not { } o || o.custom == null) return;
        if (mode > 0)
        {
            o.map.SetValue(1, true, false);
            o.random.SetValue(1, true, false);
            o.only.SetValue(mode == 2 ? 1 : 0, true, false);
        }
        o.custom.SetValue(mode > 0 ? 1 : 0, true, true);
    }

    public static bool TryPick(RandomSpawn.SpawnMap map, out Vector2 position)
    {
        position = default;
        int mapId = (int)GameMap.Current;
        int storageId = ToStorageMapId(mapId);
        if (storageId < 0 || Opts(mapId) is not { } o || !o.custom.GetBool()) return false;

        var points = SpawnStore.GetEnabledPoints(storageId);
        bool only = o.only.GetValue() == 1;
        if (!SpawnPicker.TryPick(points, map.RoomPool().Count(), only, out var picked)) return false;

        var raw = new Vector2(mapId == 3 ? -picked.X : picked.X, picked.Y);
        if (!TrySnap(raw, out position))
        {
            Logger.Info($"カスタムスポーンの近くに立てる所が無いので部屋から選ぶ: {raw}", "RandomSpawn");
            return false;
        }
        Logger.Info($"カスタムスポーン: {raw} → {position}", "RandomSpawn");
        return true;
    }

    private const float FeetOffset = 0.3636f;
    private const float SnapStep = 0.25f;
    private const float SnapRadius = 4f;

    public static bool TrySnap(Vector2 spawn, out Vector2 result)
    {
        result = spawn;
        if (!ShipStatus.Instance) return true;
        if (CanStand(spawn) && SpawnMask.AllowsShip(spawn)) return true;
        // まず壁を越えずに行ける所を探す (壁の向こうの別の部屋や、壁の中の狭いすき間へ飛ばないように)。
        // 見つからない時だけ壁越しも許す (置いた点が壁の中そのものの時など)
        for (int pass = 0; pass < 2; pass++)
        {
            for (float r = SnapStep; r <= SnapRadius; r += SnapStep)
            {
                int n = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / SnapStep));
                for (int i = 0; i < n; i++)
                {
                    float a = i * 2f * Mathf.PI / n;
                    var p = spawn + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!CanStand(p) || !SpawnMask.AllowsShip(p)) continue;
                    if (pass == 0 && WallBetween(spawn, p)) continue;
                    // 周りにも少し余裕がある所だけ (ふちぎりぎりだと動けず埋まったように見える)
                    if (pass == 0 && !HasRoom(p)) continue;
                    result = p;
                    return true;
                }
            }
        }
        return false;
    }

    static bool WallBetween(Vector2 a, Vector2 b)
    {
        try
        {
            var off = new Vector2(0f, FeetOffset);
            return PhysicsHelpers.AnythingBetween(a - off, b - off, Constants.ShipAndObjectsMask, false);
        }
        catch { return false; }
    }

    // 上下左右の少し先にも立てるか (動き出せる広さがあるか)
    static bool HasRoom(Vector2 p)
    {
        const float d = 0.3f;
        int ok = 0;
        if (CanStand(p + new Vector2(d, 0))) ok++;
        if (CanStand(p + new Vector2(-d, 0))) ok++;
        if (CanStand(p + new Vector2(0, d))) ok++;
        if (CanStand(p + new Vector2(0, -d))) ok++;
        return ok >= 2;
    }

    private static readonly Collider2D[] wallBuffer = new Collider2D[16];

    internal static bool CanStand(Vector2 spawn)
    {
        var feet = new Vector2(spawn.x, spawn.y - FeetOffset);
        try
        {
            if (!TempGhost.IsStandable(feet)) return false;
            bool triggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesHitTriggers = false;
            int hits;
            try { hits = Physics2D.OverlapCircleNonAlloc(feet, 0.22f, wallBuffer, Constants.ShipAndObjectsMask); }
            finally { Physics2D.queriesHitTriggers = triggers; }
            for (int i = 0; i < hits; i++)
                if (wallBuffer[i] && !wallBuffer[i].isTrigger) return false;
            return true;
        }
        catch { return true; }
    }

    private static void Migrate()
    {
        try
        {
            if (File.Exists(SavePath) || !File.Exists(OldPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(OldPath));
            var lib = new SpawnLibrary();
            if (doc.RootElement.TryGetProperty("CurrentPresetId", out var cur)) lib.CurrentPreset = cur.GetInt32();
            if (doc.RootElement.TryGetProperty("Presets", out var presets))
            {
                foreach (var pr in presets.EnumerateArray())
                {
                    var preset = new SpawnPreset { Name = pr.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "" };
                    if (pr.TryGetProperty("SpawnMaps", out var maps))
                    {
                        foreach (var m in maps.EnumerateObject())
                        {
                            if (!Enum.TryParse<MapNames>(m.Name, out var mapName)) continue;
                            int id = ToStorageMapId((int)mapName);
                            if (id < 0 || !m.Value.TryGetProperty("Points", out var pts)) continue;
                            var list = preset.GetPoints(id, true);
                            foreach (var p in pts.EnumerateArray())
                            {
                                list.Add(new SpawnPoint
                                {
                                    Name = p.TryGetProperty("Name", out var pn) ? pn.GetString() ?? "" : "",
                                    X = p.GetProperty("X").GetSingle(),
                                    Y = p.GetProperty("Y").GetSingle(),
                                    Color = list.Count,
                                });
                            }
                        }
                    }
                    lib.Presets.Add(preset);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            File.WriteAllText(SavePath, JsonSerializer.Serialize(lib, new JsonSerializerOptions { WriteIndented = true }));
            Logger.Info($"カスタムスポーンを新しい形へ移しました ({lib.Presets.Count} プリセット)", "SpawnEditor");
        }
        catch (Exception e)
        {
            Logger.Warn($"カスタムスポーンの引き継ぎに失敗: {e.Message}", "SpawnEditor");
        }
    }
}
