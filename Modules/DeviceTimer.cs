using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Attributes;
using static TownOfHostL.Translator;

namespace TownOfHostL.Modules;

/// <summary>
/// アドミン・カメラ (ドアログを含む)・バイタルを使える時間の上限。全員で 1 つの持ち時間を使い、尽きたら誰も使えない。
/// 使っているかは、装置の前で止まっているかで見る (本編は客が装置を開いたことをホストに知らせないため)。
/// 尽きた装置は、装置の無効化と同じく、近づいた人にだけ通信妨害を送って使えなくする。持ち時間は試合ごと、または会議ごとに戻す。
/// </summary>
/// <remarks>TownOfHost-hamo (rar006/TownOfHost-hamo@f7b00a9e Modules/DisableDevice.cs TimeLimitDevices) を元にした。</remarks>
public static class DeviceTimer
{
    public enum Kind { Admin, Camera, Vital }
    public enum ResetMode { PerGame, PerMeeting }

    public static OptionItem Enabled;
    static OptionItem resetMode;
    static OptionItem adminLimit;
    static OptionItem cameraLimit;
    static OptionItem vitalLimit;
    static OptionItem showInMeeting;

    public static void SetupCustomOption()
    {
        Enabled = StringOptionItem.Create("DeviceTimeLimit", new string[] { "ColoredOff", "ColoredOn" }, 0, TabGroup.ModMainSettings, false)
            .SetColor(Color.yellow)
            .SetGameMode(CustomGameMode.Standard);
        resetMode = StringOptionItem.Create("DeviceTimeLimitReset", new string[] { "DeviceTimeLimit.PerGame", "DeviceTimeLimit.PerMeeting" }, 0, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetGameMode(CustomGameMode.Standard);
        adminLimit = FloatOptionItem.Create("DeviceTimeLimitAdmin", new(0f, 300f, 5f), 60f, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity)
            .SetGameMode(CustomGameMode.Standard);
        cameraLimit = FloatOptionItem.Create("DeviceTimeLimitCamera", new(0f, 300f, 5f), 60f, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity)
            .SetGameMode(CustomGameMode.Standard);
        vitalLimit = FloatOptionItem.Create("DeviceTimeLimitVital", new(0f, 300f, 5f), 60f, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity)
            .SetGameMode(CustomGameMode.Standard);
        showInMeeting = StringOptionItem.Create("DeviceTimeLimitShowInMeeting", new string[] { "ColoredOff", "ColoredOn" }, 1, TabGroup.ModMainSettings, false).SetParent(Enabled)
            .SetGameMode(CustomGameMode.Standard);
    }

    public static bool IsActive => Enabled != null && Enabled.GetBool() && Options.CurrentGameMode == CustomGameMode.Standard;

    // 種類ごとの使った秒数
    static readonly float[] used = new float[3];
    // 人ごとの、前に見た位置と時刻
    static readonly Dictionary<byte, (Vector2 pos, float at)> lastSeen = new();
    // 尽きたと知らせた種類
    static readonly bool[] announced = new bool[3];

    [GameModuleInitializer]
    public static void Init() => ResetAll();

    static void ResetAll()
    {
        for (var i = 0; i < used.Length; i++) { used[i] = 0f; announced[i] = false; }
        lastSeen.Clear();
    }

    static float Limit(Kind kind) => (kind switch { Kind.Admin => adminLimit, Kind.Camera => cameraLimit, _ => vitalLimit }).GetFloat();

    /// <summary>上限が有り、使い切った</summary>
    public static bool IsExhausted(Kind kind)
    {
        if (!IsActive) return false;
        var limit = Limit(kind);
        return limit > 0f && used[(int)kind] >= limit;
    }

    static float Remaining(Kind kind) => Mathf.Max(0f, Limit(kind) - used[(int)kind]);

    /// <summary>その位置の近くにある装置。無ければ null</summary>
    static Kind? NearKind(Vector2 pos)
    {
        var map = Main.NormalOptions.MapId;
        if (map != devicesMap) BuildDevices(map);
        var distance = DisableDevice.UsableDistance();
        foreach (var (devicePos, kind) in devices)
            if (Vector2.Distance(pos, devicePos) <= distance) return kind;
        return null;
    }

    // 今のマップの装置の位置と種類 (マップが変わった時だけ作り直す)
    static readonly List<(Vector2 pos, Kind kind)> devices = new();
    static int devicesMap = -1;

    static void BuildDevices(int map)
    {
        devicesMap = map;
        devices.Clear();
        var prefix = ((MapNames)map).ToString();
        foreach (var (name, devicePos) in DisableDevice.DevicePos)
        {
            if (!name.StartsWith(prefix)) continue;
            if (name.EndsWith("Admin")) devices.Add((devicePos, Kind.Admin));
            else if (name.EndsWith("Camera") || name.EndsWith("DoorLog")) devices.Add((devicePos, Kind.Camera));
            else if (name.EndsWith("Vital")) devices.Add((devicePos, Kind.Vital));
        }
    }

    /// <summary>
    /// DisableDevice.FixedUpdate から 1 人ずつ呼ぶ。装置の前で止まっている間を持ち時間から引く。
    /// 近くの装置が尽きていれば true (使えなくする)
    /// </summary>
    public static bool Track(PlayerControl pc, Vector2 pos)
    {
        if (!IsActive || pc == null || !pc.IsAlive()) return false;
        var kind = NearKind(pos);
        if (kind == null)
        {
            lastSeen.Remove(pc.PlayerId);
            return false;
        }
        var k = kind.Value;
        if (IsExhausted(k))
        {
            if (pc.AmOwner) CloseHostDevice(k);
            return true;
        }
        var now = Time.time;
        // ホストは開いているかを自分の画面で見る。客は分からないので、装置の前で止まっている間を使っているとみなす
        var inUse = pc.AmOwner ? IsHostDeviceOpen(k) : (lastSeen.TryGetValue(pc.PlayerId, out var seen) && (seen.pos - pos).sqrMagnitude < 0.0001f);
        if (inUse && lastSeen.TryGetValue(pc.PlayerId, out var last)
            && !pc.inVent && GameStates.IsInTask && !Utils.IsActive(SystemTypes.Comms))
        {
            used[(int)k] += now - last.at;
            if (IsExhausted(k) && !announced[(int)k])
            {
                announced[(int)k] = true;
                Utils.SendMessageAutoSplit(string.Format(GetString("DeviceTimeLimit.Exhausted"), KindName(k)), true, GetString("DeviceTimeLimit.Title"));
                Logger.Info($"{k} の持ち時間が尽きた", nameof(DeviceTimer));
            }
        }
        lastSeen[pc.PlayerId] = (pos, now);
        return false;
    }

    // ホストの画面でその種類の装置が開いているか
    static bool IsHostDeviceOpen(Kind kind)
    {
        if (kind == Kind.Admin)
            return MapBehaviour.Instance != null && MapBehaviour.Instance.IsOpen && MapBehaviour.Instance.countOverlay != null
                && MapBehaviour.Instance.countOverlay.gameObject.activeSelf;
        var game = Minigame.Instance;
        if (game == null) return false;
        if (kind == Kind.Vital) return game.TryCast<VitalsMinigame>() != null;
        return game.TryCast<SurveillanceMinigame>() != null || game.TryCast<PlanetSurveillanceMinigame>() != null
            || game.TryCast<FungleSurveillanceMinigame>() != null || game.TryCast<SecurityLogGame>() != null;
    }

    // ホストは自分の画面で装置を開いているので、尽きたら閉じる
    static void CloseHostDevice(Kind kind)
    {
        if (!IsHostDeviceOpen(kind)) return;
        if (kind == Kind.Admin) MapBehaviour.Instance.Close();
        else Minigame.Instance.Close();
    }

    static string KindName(Kind kind) => GetString(kind switch
    {
        Kind.Admin => "DeviceTimeLimit.Admin",
        Kind.Camera => "DeviceTimeLimit.Camera",
        _ => "DeviceTimeLimit.Vital",
    });

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    static class MeetingStartPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost || !IsActive) return;
            lastSeen.Clear();
            if (showInMeeting.GetBool())
            {
                var lines = new List<string>();
                foreach (var k in new[] { Kind.Admin, Kind.Camera, Kind.Vital })
                {
                    if (Limit(k) <= 0f) continue;
                    lines.Add($"{KindName(k)}: {(IsExhausted(k) ? "×" : $"{Mathf.CeilToInt(Remaining(k))}s")}");
                }
                if (lines.Count > 0)
                    Utils.SendMessageAutoSplit(string.Join("\n", lines), true, GetString("DeviceTimeLimit.Title"));
            }
            // 会議ごとに戻す時は、会議の後の分として数え直す
            if (resetMode.GetValue() == (int)ResetMode.PerMeeting) ResetAll();
        }
    }
}
