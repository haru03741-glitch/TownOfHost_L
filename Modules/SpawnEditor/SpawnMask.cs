using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using UnityEngine;

namespace TownOfHostL.Modules.SpawnEditor;

/// <summary>
/// カスタムスポーンのピンを立てられる所を、実際のマップの当たりから覚える。
/// 地図の絵だけでは置物 (机・箱など) の上や、床はあっても入れない所 (エアシップの薄い色の所など) が分からないので、
/// マップに入った時に一度だけ、地図の絵の点ごとに「立てるか」を当たりで調べ、さらに入れる所 (タスクの機械・ベント・はしご・自分の居場所)
/// から歩いてつながる所だけを残す。結果は TOHL_DATA に置き、次からはピンの画面とスポーンの両方で使う
/// </summary>
internal static class SpawnMask
{
    // 地図の絵の何点ごとに調べるか
    const int Step = 2;
    // 1 フレームで調べる数 (重くしない)
    const int PerFrame = 1500;
    // 保存の形を変えたら上げる (古いのは調べ直す)
    const int Version = 1;

    sealed class Learned
    {
        public int Cols, Rows;
        public bool[] Cells;
        public bool At(int px, int py)
        {
            int cx = px / Step, cy = py / Step;
            if (cx < 0 || cy < 0 || cx >= Cols || cy >= Rows) return false;
            return Cells[cy * Cols + cx];
        }
    }

    static readonly Dictionary<int, Learned> cache = new();
    static readonly HashSet<int> missing = new();
    static bool scanning;

    static string PathOf(int id) => $"./TOHL_DATA/SpawnMask_v{Version}_{id}.bin";

    static SpawnPinEditor.MapSheet SheetOf(int id)
    {
        foreach (var s in SpawnPinEditor.Sheets) if (s.Id == id) return s;
        return null;
    }

    static Learned Get(int id)
    {
        if (cache.TryGetValue(id, out var l)) return l;
        if (missing.Contains(id)) return null;
        try
        {
            var path = PathOf(id);
            if (!File.Exists(path)) { missing.Add(id); return null; }
            using var br = new BinaryReader(File.OpenRead(path));
            int cols = br.ReadInt32(), rows = br.ReadInt32();
            var bytes = br.ReadBytes(cols * rows);
            if (bytes.Length != cols * rows) throw new InvalidDataException(path);
            l = new Learned { Cols = cols, Rows = rows, Cells = new bool[cols * rows] };
            for (int i = 0; i < bytes.Length; i++) l.Cells[i] = bytes[i] != 0;
            cache[id] = l;
            return l;
        }
        catch (Exception ex)
        {
            Logger.Warn($"スポーンの立てる所の読み込みに失敗: {ex.Message}", nameof(SpawnMask));
            missing.Add(id);
            return null;
        }
    }

    public static bool Has(int id) => Get(id) != null;

    /// <summary>地図の絵の点 (左上から) が、覚えた立てる所に入っているか。まだ覚えていなければ true</summary>
    public static bool Allows(int id, int px, int py)
    {
        var l = Get(id);
        return l == null || l.At(px, py);
    }

    /// <summary>今のマップの船の座標が、覚えた立てる所に入っているか (スポーンの時に使う)。まだ覚えていなければ true</summary>
    public static bool AllowsShip(Vector2 pos)
    {
        int mapId = (int)GameMap.Current;
        if (mapId == 3) pos.x = -pos.x; // 逆スケルドはスケルドを左右に返した物
        int id = TohlSpawnEditor.ToStorageMapId(mapId);
        var s = SheetOf(id);
        if (s == null || Get(id) == null) return true;
        var p = s.ToPixel(pos.x, pos.y);
        return Allows(id, Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
    }

    /// <summary>今のマップをまだ覚えていなければ調べ始める (force で調べ直す)</summary>
    public static bool ScanIfNeeded(bool force = false)
    {
        if (scanning || !ShipStatus.Instance) return false;
        int mapId = (int)GameMap.Current;
        if (mapId == 3) return false; // 逆スケルドの当たりは左右が返っているので、スケルドで覚える
        var s = SheetOf(mapId);
        if (s == null || !SpawnPinEditor.Load(s)) return false;
        if (!force && Has(mapId)) return false;
        scanning = true;
        ShipStatus.Instance.StartCoroutine(Scan(s, mapId).WrapToIl2Cpp());
        return true;
    }

    static IEnumerator Scan(SpawnPinEditor.MapSheet s, int mapId)
    {
        var ship = ShipStatus.Instance;
        // 入った直後は物が揃っていないので少し待つ
        float until = Time.realtimeSinceStartup + 2f;
        while (Time.realtimeSinceStartup < until) yield return null;

        int cols = (s.W + Step - 1) / Step, rows = (s.H + Step - 1) / Step;
        var stand = new bool[cols * rows];
        int done = 0, standCount = 0;
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                int px = Mathf.Min(cx * Step + Step / 2, s.W - 1), py = Mathf.Min(cy * Step + Step / 2, s.H - 1);
                if (s.Inside(px, py, false))
                {
                    var pos = s.ToShip(px + 0.5f, py + 0.5f);
                    if (TohlSpawnEditor.CanStand(pos)) { stand[cy * cols + cx] = true; standCount++; }
                }
                if (++done % PerFrame == 0)
                {
                    yield return null;
                    if (!ship || ShipStatus.Instance != ship) { scanning = false; yield break; }
                }
            }
        }

        // 入れる所から歩いてつながる所だけを残す (壁の向こうの閉じた所・置物に囲まれた所を外す)
        var seeds = new List<Vector2>();
        if (PlayerControl.LocalPlayer) seeds.Add(PlayerControl.LocalPlayer.GetTruePosition() + new Vector2(0f, 0.3636f));
        foreach (var v in ship.AllVents) if (v) seeds.Add(v.transform.position);
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Console>()) if (c) seeds.Add(c.transform.position);
        foreach (var l in UnityEngine.Object.FindObjectsOfType<Ladder>()) if (l) seeds.Add(l.transform.position);
        // 部屋ごとのスポーンの位置 (入れる所として選んである)
        try { seeds.AddRange(RandomSpawn.ForMap((MapNames)mapId).Positions.Values); } catch { }

        var reached = new bool[cols * rows];
        var queue = new Queue<int>();
        float cellShip = Mathf.Abs(s.Ax) * Step;
        int radius = Mathf.CeilToInt(1.2f / cellShip);
        foreach (var seed in seeds)
        {
            var p = s.ToPixel(seed.x, seed.y);
            int sx = Mathf.FloorToInt(p.x) / Step, sy = Mathf.FloorToInt(p.y) / Step;
            int best = -1, bestD = int.MaxValue;
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = sx + dx, y = sy + dy;
                    if (x < 0 || y < 0 || x >= cols || y >= rows || !stand[y * cols + x]) continue;
                    int d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = y * cols + x; }
                }
            if (best >= 0 && !reached[best]) { reached[best] = true; queue.Enqueue(best); }
        }
        int reachedCount = queue.Count;
        // エアシップ以外は薄い色の所 (廊下・外など) も歩ける所なので、つながりでは削らず、置物などで立てない所だけを外す
        if (mapId != 4)
        {
            queue.Clear();
            reached = stand;
            reachedCount = standCount;
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % cols, y = i / cols;
            void Try(int nx, int ny)
            {
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) return;
                int j = ny * cols + nx;
                if (!stand[j] || reached[j]) return;
                reached[j] = true;
                reachedCount++;
                queue.Enqueue(j);
            }
            Try(x + 1, y); Try(x - 1, y); Try(x, y + 1); Try(x, y - 1);
        }

        scanning = false;
        if (!ship || ShipStatus.Instance != ship) yield break;
        try
        {
            Directory.CreateDirectory("./TOHL_DATA");
            using (var bw = new BinaryWriter(File.Create(PathOf(mapId))))
            {
                bw.Write(cols);
                bw.Write(rows);
                var bytes = new byte[reached.Length];
                for (int i = 0; i < reached.Length; i++) bytes[i] = reached[i] ? (byte)1 : (byte)0;
                bw.Write(bytes);
            }
            cache[mapId] = new Learned { Cols = cols, Rows = rows, Cells = reached };
            missing.Remove(mapId);
            Logger.Info($"スポーンの立てる所を覚えた: map={mapId} 立てる={standCount} つながる={reachedCount} 種={seeds.Count}", nameof(SpawnMask));
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(SpawnMask)); }
    }

    // マップに入った時 (試合・フリープレイ) に、まだ覚えていなければ調べる
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
    [TownOfHostL.Core.Modes.PatchBundle(TownOfHostL.Core.Modes.PatchBundles.Core)]
    static class ShipStartPatch
    {
        public static void Postfix()
        {
            scanning = false;
            try { ScanIfNeeded(); }
            catch (Exception ex) { Logger.Exception(ex, nameof(SpawnMask)); }
        }
    }
}
