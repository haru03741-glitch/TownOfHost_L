using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;
using TownOfHostL.Core;
using TownOfHostL.Core.Modes;
using TownOfHostL.Core.Win;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using static TownOfHostL.Translator;

namespace TownOfHostL;

public static partial class Options
{
    public static OptionItem KedoroKillCooldown;
    public static OptionItem KedoroRescueCooldown;
    public static OptionItem KedoroJailCount;
    public static OptionItem KedoroVentCooldown;
    public static OptionItem KedoroVentMaxTime;
    public static OptionItem KedoroSabotageDelay;
    public static OptionItem KedoroSabotageTime;
    public static OptionItem KedoroKillCdReduceTaskPercent;
    public static OptionItem KedoroMinKillCooldown;
    public static OptionItem KedoroSpeedUpTaskPercent;
    public static OptionItem KedoroOniMaxSpeed;
    public static OptionItem KedoroNotifyOnRescue;
    public static OptionItem KedoroRescueCooldownAfterTasks;
    public static OptionItem KedoroRescuerSpeed;
    public static OptionItem KedoroRescuerShield;
    public static OptionItem KedoroRescuerSpeedTime;
    public static OptionItem KedoroRescuerShieldTime;
    public static OptionItem KedoroRescuedSpeed;
    public static OptionItem KedoroRescuedShield;
    public static OptionItem KedoroRescuedSpeedTime;
    public static OptionItem KedoroRescuedShieldTime;
    public static OptionItem KedoroVentUses;

    static void SetupKedoroOptions()
    {
        const CustomGameMode mode = CustomGameMode.Kedoro;
        const TabGroup tab = TabGroup.ModMainSettings;
        OptionItem Sec(OptionItem o) => o.SetValueFormat(OptionFormat.Seconds).SetParent(KedoroKillCooldown).SetGameMode(mode);
        OptionItem Mul(OptionItem o) => o.SetValueFormat(OptionFormat.Multiplier).SetParent(KedoroKillCooldown).SetGameMode(mode);
        OptionItem Pcs(OptionItem o) => o.SetValueFormat(OptionFormat.Pieces).SetParent(KedoroKillCooldown).SetGameMode(mode);
        OptionItem Pct(OptionItem o) => o.SetValueFormat(OptionFormat.Percent).SetParent(KedoroKillCooldown).SetGameMode(mode);

        KedoroKillCooldown = FloatOptionItem.Create("KedoroKillCooldown", new(0f, 180f, 1f), 20f, tab, false).SetValueFormat(OptionFormat.Seconds)
            .SetHeader(true).SetColorcode("#ff6b6b").SetGameMode(mode);
        KedoroRescueCooldown = Sec(FloatOptionItem.Create("KedoroRescueCooldown", new(0f, 180f, 1f), 30f, tab, false));
        KedoroJailCount = Pcs(IntegerOptionItem.Create("KedoroJailCount", new(1, 30, 1), 3, tab, false));
        Kedoro.SetupJailOptions(KedoroJailCount);
        KedoroVentCooldown = Sec(FloatOptionItem.Create("KedoroVentCooldown", new(0f, 180f, 1f), 15f, tab, false));
        KedoroVentMaxTime = Sec(FloatOptionItem.Create("KedoroVentMaxTime", new(0f, 180f, 1f), 5f, tab, false)).SetZeroNotation(OptionZeroNotation.Infinity);
        KedoroSabotageDelay = Sec(FloatOptionItem.Create("KedoroSabotageDelay", new(30f, 1000f, 1f), 30f, tab, false));
        KedoroSabotageTime = Sec(FloatOptionItem.Create("KedoroSabotageTime", new(0f, 1000f, 1f), 1f, tab, false));
        KedoroKillCdReduceTaskPercent = Pct(FloatOptionItem.Create("KedoroKillCdReduceTaskPercent", new(0f, 100f, 5f), 20f, tab, false));
        KedoroMinKillCooldown = Sec(FloatOptionItem.Create("KedoroMinKillCooldown", new(0f, 180f, 1f), 5f, tab, false));
        KedoroSpeedUpTaskPercent = Pct(FloatOptionItem.Create("KedoroSpeedUpTaskPercent", new(0f, 100f, 5f), 50f, tab, false));
        KedoroOniMaxSpeed = Mul(FloatOptionItem.Create("KedoroOniMaxSpeed", new(1f, 5f, 0.25f), 3f, tab, false));
        KedoroNotifyOnRescue = BooleanOptionItem.Create("KedoroNotifyOnRescue", false, tab, false).SetParent(KedoroKillCooldown).SetGameMode(mode);
        KedoroRescueCooldownAfterTasks = Sec(FloatOptionItem.Create("KedoroRescueCooldownAfterTasks", new(0f, 180f, 1f), 15f, tab, false));
        KedoroRescuerSpeed = Mul(FloatOptionItem.Create("KedoroRescuerSpeed", new(1f, 5f, 0.25f), 1.5f, tab, false));
        KedoroRescuerShield = Pcs(IntegerOptionItem.Create("KedoroRescuerShield", new(0, 99, 1), 1, tab, false));
        KedoroRescuerSpeedTime = Sec(FloatOptionItem.Create("KedoroRescuerSpeedTime", new(0f, 180f, 1f), 10f, tab, false));
        KedoroRescuerShieldTime = Sec(FloatOptionItem.Create("KedoroRescuerShieldTime", new(0f, 180f, 1f), 5f, tab, false));
        KedoroRescuedSpeed = Mul(FloatOptionItem.Create("KedoroRescuedSpeed", new(1f, 5f, 0.25f), 1.25f, tab, false));
        KedoroRescuedShield = Pcs(IntegerOptionItem.Create("KedoroRescuedShield", new(0, 99, 1), 2, tab, false));
        KedoroRescuedSpeedTime = Sec(FloatOptionItem.Create("KedoroRescuedSpeedTime", new(0f, 180f, 1f), 15f, tab, false));
        KedoroRescuedShieldTime = Sec(FloatOptionItem.Create("KedoroRescuedShieldTime", new(0f, 180f, 1f), 10f, tab, false));
        KedoroVentUses = IntegerOptionItem.Create("KedoroVentUses", new(0, 99, 1), 0, tab, false).SetValueFormat(OptionFormat.Times)
            .SetParent(KedoroKillCooldown).SetZeroNotation(OptionZeroNotation.Infinity).SetGameMode(mode);
    }
}

/// <summary>ケイドロ。鬼 (インポスター) が逃走者を捕まえて牢屋へ送り、逃走者はペットで仲間を助ける</summary>
public sealed class KedoroMode : GameMode
{
    public override CustomGameMode Id => CustomGameMode.Kedoro;
    public override bool UsesStandardRoles => false;
    public override bool AllowsDoorSabotage => false;
    public override bool AllowsSabotage => false;
    public override bool AllowsReport(PlayerControl reporter, NetworkedPlayerInfo target) => false;
    public override void SelectRoles() => Kedoro.Assign();
    public override (string Key, Color Color)? Label => ("Kedoro", new Color32(255, 107, 107, 255));
}

public static class Kedoro
{
    const float TaskCompleteSpeedMultiplier = 1.25f;
    const float JailLeashDistance = 0.9f;
    const float RescueRange = 2.5f;
    const float TickInterval = 0.5f;
    const string SpeedKey = "Kedoro";
    const string JailSprite = "<size=70%><line-height=97%><cspace=0.16em><mark=#555555>WWWW</mark>\n<mark=#555555>W</mark><mark=#00000000>WW</mark><mark=#555555>W</mark>\n<mark=#555555>WWWW</mark>\n<mark=#555555>W</mark><mark=#00000000>WW</mark><mark=#555555>W</mark>\n<mark=#555555>WWWW";

    static readonly HashSet<byte> Oni = new();
    static readonly HashSet<byte> Runners = new();
    static readonly Dictionary<byte, Vector2> Captured = new();
    static readonly Dictionary<byte, float> LastRescue = new();
    static readonly List<Vector2> JailPositions = new();
    static readonly List<CustomNetObject> JailObjects = new();
    static bool jailObjectsSpawned;
    static readonly Dictionary<byte, List<(float Mult, float Until)>> SpeedBuffs = new();
    static readonly Dictionary<byte, List<float>> Shields = new();
    static readonly HashSet<byte> TaskCompleted = new();
    static readonly Dictionary<byte, float> SentSpeed = new();
    static readonly Dictionary<byte, int> VentUsed = new();
    static readonly Dictionary<byte, float> LastVentCount = new();
    static float sentOniKillCooldown = -1f;
    static float tickTimer;
    static bool sabotageStarted;
    static float sabotageElapsed;

    public static bool IsActive => Options.CurrentGameMode == CustomGameMode.Kedoro;
    public static bool IsOni(PlayerControl pc) => pc != null && Oni.Contains(pc.PlayerId);
    public static bool IsRunner(PlayerControl pc) => pc != null && Runners.Contains(pc.PlayerId);
    public static bool IsCaptured(PlayerControl pc) => pc != null && Captured.ContainsKey(pc.PlayerId);

    [Attributes.GameModuleInitializer]
    public static void Reset()
    {
        Oni.Clear();
        Runners.Clear();
        Captured.Clear();
        LastRescue.Clear();
        JailPositions.Clear();
        VentUsed.Clear();
        LastVentCount.Clear();
        JailObjects.Clear();
        jailObjectsSpawned = false;
        SpeedBuffs.Clear();
        Shields.Clear();
        TaskCompleted.Clear();
        SentSpeed.Clear();
        sentOniKillCooldown = -1f;
        tickTimer = 0f;
        sabotageStarted = false;
        sabotageElapsed = 0f;
    }

    public static void Assign()
    {
        Reset();
        RpcSetRoleReplacer.StartReplace();
        Main.NormalOptions.roleOptions.SetRoleRate(RoleTypes.Engineer, 15, 100);
        var players = Main.AllPlayerControls.ToList();
        if (Options.EnableGM.GetBool())
        {
            players.RemoveAll(x => x == PlayerControl.LocalPlayer);
            PlayerControl.LocalPlayer.RpcSetRole(RoleTypes.Crewmate);
            PlayerState.GetByPlayerId(PlayerControl.LocalPlayer.PlayerId).SetMainRole(CustomRoles.GM);
        }
        var numOni = Math.Clamp(Main.NormalOptions.NumImpostors, 1, Math.Max(1, players.Count - 1));
        foreach (var pc in players.OrderBy(_ => IRandom.Instance.Next(1000)))
        {
            var state = PlayerState.GetByPlayerId(pc.PlayerId);
            if (Oni.Count < numOni)
            {
                Oni.Add(pc.PlayerId);
                pc.RpcSetRole(RoleTypes.Impostor);
                state.SetMainRole(CustomRoles.Impostor);
            }
            else
            {
                Runners.Add(pc.PlayerId);
                pc.RpcSetRole(RoleTypes.Engineer);
                state.SetMainRole(CustomRoles.Engineer);
            }
        }
        Main.NormalOptions.roleOptions.SetRoleRate(RoleTypes.Engineer, 0, 0);
        RpcSetRoleReplacer.Release();

        BuildJailPositions();
        CustomRoleManager.MarkOthers.Add(GetVentMark);
        HamoModes.FinishAssign(Rules);
        foreach (var id in Oni) Main.AllPlayerKillCooldown[id] = Options.KedoroKillCooldown.GetFloat();
    }

    public static void OnIntroEnd() { }

    public static void OnFixedUpdate()
    {
        if (!IsActive || !HamoModes.IntroDone || GameStates.IsMeeting) return;
        if (!sabotageStarted)
        {
            sabotageElapsed += Time.fixedDeltaTime;
            if (sabotageElapsed >= Options.KedoroSabotageDelay.GetFloat())
            {
                sabotageStarted = true;
                HamoSabotageTrigger.Critical(Math.Max(1f, Options.KedoroSabotageTime.GetFloat()));
                Utils.NotifyRoles(NoCache: true);
            }
        }
        if (!jailObjectsSpawned)
        {
            jailObjectsSpawned = true;
            foreach (var pos in JailPositions)
            {
                try { JailObjects.Add(CustomNetObject.Create(JailSprite, pos)); }
                catch (Exception e) { Logger.Warn($"Kedoro jail object: {e.Message}", "Kedoro"); }
            }
        }
        tickTimer += Time.fixedDeltaTime;
        if (tickTimer < TickInterval) return;
        tickTimer = 0f;
        KeepCapturedInJail();
        CheckTaskCompletion();
        UpdateOni();
        UpdateRunnerSpeeds();
    }

    static readonly Dictionary<MapNames, OptionItem> JailMapOptions = new();
    static readonly Dictionary<MapNames, List<(OptionItem Option, Vector2 Pos)>> JailRoomOptions = new();

    static RandomSpawn.SpawnMap SpawnMapOf(MapNames map) => map switch
    {
        MapNames.Skeld => new RandomSpawn.SkeldSpawnMap(),
        MapNames.MiraHQ => new RandomSpawn.MiraHQSpawnMap(),
        MapNames.Polus => new RandomSpawn.PolusSpawnMap(),
        MapNames.Airship => new RandomSpawn.AirshipSpawnMap(),
        MapNames.Fungle => new RandomSpawn.FungleSpawnMap(),
        _ => null,
    };

    public static void SetupJailOptions(OptionItem parent)
    {
        foreach (var map in new[] { MapNames.Skeld, MapNames.MiraHQ, MapNames.Polus, MapNames.Airship, MapNames.Fungle })
        {
            var spawnMap = SpawnMapOf(map);
            if (spawnMap == null) continue;
            var label = map switch { MapNames.Skeld => "The Skeld", MapNames.MiraHQ => "MIRA HQ", MapNames.Polus => "Polus", MapNames.Airship => "The Airship", _ => "The Fungle" };
            var mapOpt = BooleanOptionItem.Create($"KedoroJail{map}", false, TabGroup.ModMainSettings, false).SetParent(parent).SetGameMode(CustomGameMode.Kedoro)
                .SetOptionName(() => string.Format(GetString("KedoroJailMap"), label));
            JailMapOptions[map] = mapOpt;
            var list = new List<(OptionItem, Vector2)>();
            foreach (var (room, pos) in spawnMap.Positions)
            {
                var opt = BooleanOptionItem.Create($"KedoroJail{map}_{room}", false, TabGroup.ModMainSettings, false).SetParent(mapOpt).SetGameMode(CustomGameMode.Kedoro);
                var roomKey = room;
                opt.SetOptionName(() => GetString(roomKey));
                list.Add((opt, pos));
            }
            JailRoomOptions[map] = list;
        }
    }

    static void BuildJailPositions()
    {
        var map = (MapNames)Main.NormalOptions.MapId;
        var candidates = new List<Vector2>();
        if (JailMapOptions.TryGetValue(map, out var mapOpt) && mapOpt.GetBool() && JailRoomOptions.TryGetValue(map, out var rooms))
            candidates.AddRange(rooms.Where(r => r.Option.GetBool()).Select(r => r.Pos));
        if (candidates.Count == 0 && JailRoomOptions.TryGetValue(map, out var all))
            candidates.AddRange(all.Select(r => r.Pos));
        if (candidates.Count == 0 && PlayerControl.LocalPlayer != null)
            candidates.Add(PlayerControl.LocalPlayer.transform.position);
        var rnd = IRandom.Instance;
        for (var k = candidates.Count - 1; k > 0; k--)
        {
            var j = rnd.Next(k + 1);
            (candidates[k], candidates[j]) = (candidates[j], candidates[k]);
        }
        JailPositions.AddRange(candidates.Take(Mathf.Clamp(Options.KedoroJailCount.GetInt(), 1, 30)));
        Logger.Info($"牢屋: {JailPositions.Count}個 / 候補{candidates.Count}か所", "Kedoro");
    }

    public static bool TryUseVent(PlayerControl pc)
    {
        if (!IsActive || pc == null || !IsRunner(pc)) return true;
        if (IsCaptured(pc)) return false;
        var max = Options.KedoroVentUses.GetInt();
        if (max <= 0) return true;
        VentUsed.TryGetValue(pc.PlayerId, out var used);
        if (used >= max)
        {
            Utils.SendMessage(GetString("KedoroVentEmpty"), pc.PlayerId);
            return false;
        }
        if (!LastVentCount.TryGetValue(pc.PlayerId, out var last) || Time.time - last > 1f)
        {
            LastVentCount[pc.PlayerId] = Time.time;
            VentUsed[pc.PlayerId] = used + 1;
            Utils.NotifyRoles(SpecifySeer: pc);
        }
        return true;
    }

    static string GetVentMark(PlayerControl seer, PlayerControl seen, bool isForMeeting)
    {
        seen ??= seer;
        if (seer != seen || !IsRunner(seer) || !seer.IsAlive()) return "";
        var sb = new System.Text.StringBuilder();
        var max = Options.KedoroVentUses.GetInt();
        if (max > 0)
        {
            VentUsed.TryGetValue(seer.PlayerId, out var used);
            var left = Math.Max(0, max - used);
            sb.Append($"<{(left == 0 ? "#ff6b6b" : "#7fd8ff")}>({left})</color>");
        }
        var shields = ShieldCount(seer.PlayerId);
        if (shields > 0) sb.Append($"<#ffd24a>[{shields}]</color>");
        if (IsCaptured(seer)) sb.Append($"<#ff6b6b>{GetString("KedoroCaptured")}</color>");
        return sb.ToString();
    }

    static void KeepCapturedInJail()
    {
        foreach (var (id, jail) in Captured.ToArray())
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || !pc.IsAlive()) continue;
            if (Vector2.Distance(pc.GetTruePosition(), jail) > JailLeashDistance)
                pc.RpcSnapTo(jail, true);
        }
    }

    static bool AllTasksDone(PlayerControl pc) => pc.GetPlayerTaskState().IsTaskFinished;

    static float RunnerTaskProgress()
    {
        int done = 0, all = 0;
        foreach (var id in Runners)
        {
            var ts = PlayerState.GetByPlayerId(id)?.GetTaskState();
            if (ts == null || !ts.hasTasks) continue;
            all += ts.AllTasksCount;
            done += Math.Min(ts.CompletedTasksCount, ts.AllTasksCount);
        }
        return all <= 0 ? 0f : done * 100f / all;
    }

    static float Ramp(float progress, float threshold)
    {
        if (progress < threshold) return 0f;
        if (threshold >= 100f) return 1f;
        return Mathf.Clamp01((progress - threshold) / (100f - threshold));
    }

    public static float CurrentOniKillCooldown()
    {
        var baseCd = Options.KedoroKillCooldown.GetFloat();
        var minCd = Mathf.Min(baseCd, Options.KedoroMinKillCooldown.GetFloat());
        var t = Ramp(RunnerTaskProgress(), Options.KedoroKillCdReduceTaskPercent.GetFloat());
        return Mathf.Round(Mathf.Lerp(baseCd, minCd, t) * 10f) / 10f;
    }

    static float CurrentOniSpeed()
        => Mathf.Lerp(1f, Options.KedoroOniMaxSpeed.GetFloat(), Ramp(RunnerTaskProgress(), Options.KedoroSpeedUpTaskPercent.GetFloat()));

    static void SetSpeed(PlayerControl pc, float mult)
    {
        if (SentSpeed.TryGetValue(pc.PlayerId, out var sent) && Mathf.Approximately(sent, mult)) return;
        SentSpeed[pc.PlayerId] = mult;
        PlayerSpeed.Multiply(pc.PlayerId, SpeedKey, mult);
        pc.MarkDirtySettings();
    }

    static void UpdateOni()
    {
        var cd = CurrentOniKillCooldown();
        var speed = CurrentOniSpeed();
        foreach (var id in Oni)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null) continue;
            if (!Mathf.Approximately(sentOniKillCooldown, cd))
            {
                Main.AllPlayerKillCooldown[id] = cd;
                pc.MarkDirtySettings();
            }
            SetSpeed(pc, speed);
        }
        sentOniKillCooldown = cd;
    }

    static void CheckTaskCompletion()
    {
        foreach (var id in Runners)
        {
            if (TaskCompleted.Contains(id)) continue;
            var pc = Utils.GetPlayerById(id);
            if (pc == null || !pc.IsAlive() || !AllTasksDone(pc)) continue;
            TaskCompleted.Add(id);
            AddShields(id, 1, 0f);
            Utils.SendMessage(GetString("KedoroTaskComplete"), id);
        }
    }

    public static void OnCompleteTask(PlayerControl pc) { }

    static void AddSpeedBuff(byte id, float mult, float seconds)
    {
        if (mult <= 1f || seconds <= 0f) return;
        if (!SpeedBuffs.TryGetValue(id, out var list)) SpeedBuffs[id] = list = new();
        list.Add((mult, Time.time + seconds));
    }

    static void AddShields(byte id, int count, float seconds)
    {
        if (count <= 0) return;
        if (!Shields.TryGetValue(id, out var list)) Shields[id] = list = new();
        var until = seconds > 0f ? Time.time + seconds : float.MaxValue;
        for (int i = 0; i < count; i++) list.Add(until);
    }

    static bool TryConsumeShield(byte id)
    {
        if (!Shields.TryGetValue(id, out var list)) return false;
        list.RemoveAll(until => until < Time.time);
        if (list.Count == 0) return false;
        list.Sort();
        list.RemoveAt(0);
        return true;
    }

    static int ShieldCount(byte id) => Shields.TryGetValue(id, out var list) ? list.Count(until => until >= Time.time) : 0;

    static void UpdateRunnerSpeeds()
    {
        foreach (var id in Runners)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null) continue;
            float mult = TaskCompleted.Contains(id) ? TaskCompleteSpeedMultiplier : 1f;
            if (SpeedBuffs.TryGetValue(id, out var list))
            {
                list.RemoveAll(b => b.Until < Time.time);
                foreach (var b in list) mult = Mathf.Max(mult, b.Mult);
            }
            SetSpeed(pc, mult);
        }
    }

    static void Capture(PlayerControl oni, PlayerControl runner)
    {
        if (IsCaptured(runner) || !runner.IsAlive()) return;
        if (TryConsumeShield(runner.PlayerId))
        {
            oni.RpcProtectedMurderPlayer(runner);
            oni.SetKillCooldown();
            Utils.SendMessage(string.Format(GetString("KedoroShieldBlocked"), ShieldCount(runner.PlayerId)), runner.PlayerId);
            return;
        }
        var location = JailPositions.Count == 0 ? (Vector2)oni.transform.position : JailPositions[IRandom.Instance.Next(JailPositions.Count)];
        Captured[runner.PlayerId] = location;
        runner.RpcSnapTo(location, true);
        oni.SetKillCooldown();
        Utils.NotifyRoles(NoCache: true);
    }

    static void RescueNearby(PlayerControl rescuer)
    {
        if (!IsRunner(rescuer) || IsCaptured(rescuer) || !rescuer.IsAlive()) return;
        var cooldown = TaskCompleted.Contains(rescuer.PlayerId) ? Options.KedoroRescueCooldownAfterTasks.GetFloat() : Options.KedoroRescueCooldown.GetFloat();
        if (LastRescue.TryGetValue(rescuer.PlayerId, out var last) && cooldown > 0f && Time.time - last < cooldown) return;
        var target = Main.AllPlayerControls
            .Where(pc => IsCaptured(pc) && pc.IsAlive())
            .Select(pc => (pc, d: Mathf.Min(Vector2.Distance(rescuer.GetTruePosition(), pc.GetTruePosition()), Vector2.Distance(rescuer.GetTruePosition(), Captured[pc.PlayerId]))))
            .Where(x => x.d <= RescueRange)
            .OrderBy(x => x.d)
            .Select(x => x.pc)
            .FirstOrDefault();
        if (target == null) return;
        Captured.Remove(target.PlayerId);
        target.RpcSnapTo(rescuer.GetTruePosition() + new Vector2(0.35f, 0f), true);
        LastRescue[rescuer.PlayerId] = Time.time;
        AddSpeedBuff(rescuer.PlayerId, Options.KedoroRescuerSpeed.GetFloat(), Options.KedoroRescuerSpeedTime.GetFloat());
        AddShields(rescuer.PlayerId, Options.KedoroRescuerShield.GetInt(), Options.KedoroRescuerShieldTime.GetFloat());
        AddSpeedBuff(target.PlayerId, Options.KedoroRescuedSpeed.GetFloat(), Options.KedoroRescuedSpeedTime.GetFloat());
        AddShields(target.PlayerId, Options.KedoroRescuedShield.GetInt(), Options.KedoroRescuedShieldTime.GetFloat());
        UpdateRunnerSpeeds();
        if (Options.KedoroNotifyOnRescue.GetBool())
            Utils.SendMessage(string.Format(GetString("KedoroRescued"), rescuer.Data?.PlayerName, target.Data?.PlayerName));
        Utils.NotifyRoles(NoCache: true);
    }

    public static void OnPet(PlayerControl pc)
    {
        if (!IsActive || !AmongUsClient.Instance.AmHost) return;
        RescueNearby(pc);
    }

    public static void ApplyGameOptions(PlayerControl pc, IGameOptions opt)
    {
        if (!IsRunner(pc)) return;
        opt.SetFloat(FloatOptionNames.EngineerCooldown, Options.KedoroVentCooldown.GetFloat());
        var maxTime = Options.KedoroVentMaxTime.GetFloat();
        opt.SetFloat(FloatOptionNames.EngineerInVentMaxTime, maxTime >= 180f ? 0f : maxTime);
    }

    public static readonly GameEndRuleSet Rules = new(CheckGameEnd, GameEndRules.BySabotage);

    static bool CheckGameEnd(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        var aliveRunners = Runners.Count(id => Utils.GetPlayerById(id)?.IsAlive() == true);
        var capturedRunners = Captured.Keys.Count(id => Utils.GetPlayerById(id)?.IsAlive() == true);
        if (aliveRunners > 0 && capturedRunners >= aliveRunners)
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
            foreach (var id in Oni) CustomWinnerHolder.WinnerIds.Add(id);
            return true;
        }
        var active = Runners.Select(Utils.GetPlayerById).Where(pc => pc != null && pc.IsAlive()).ToList();
        if (active.Count > 0 && active.All(AllTasksDone))
        {
            reason = GameOverReason.CrewmatesByTask;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Crewmate);
            foreach (var pc in active) CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            return true;
        }
        if (aliveRunners == 0)
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
            foreach (var id in Oni) CustomWinnerHolder.WinnerIds.Add(id);
            return true;
        }
        return false;
    }

    [HarmonyPatch(typeof(CustomRoleManager), nameof(CustomRoleManager.OnCheckMurder),
        typeof(PlayerControl), typeof(PlayerControl), typeof(PlayerControl), typeof(PlayerControl), typeof(bool?), typeof(bool?), typeof(int), typeof(CustomDeathReason))]
    static class CapturePatch
    {
        static bool Prefix(PlayerControl attemptKiller, PlayerControl attemptTarget, ref bool __result)
        {
            if (!IsActive || !IsOni(attemptKiller) || !IsRunner(attemptTarget)) return true;
            Capture(attemptKiller, attemptTarget);
            __result = false;
            return false;
        }
    }
}
