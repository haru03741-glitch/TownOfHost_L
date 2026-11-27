using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using UnityEngine;

using TownOfHostL.Attributes;
using TownOfHostL.Roles.AddOns.Common;

namespace TownOfHostL.Modules;

/// <summary>
/// 通常モードで、本編のかくれんぼの懐中電灯 (向いた先だけが見える円錐と、体の周りの小さな円) を持たせる。
///
/// 本編の客の PlayerControl.IsFlashlightEnabled は、手元の設定の GameMode が HideNSeek か (と UseFlashlight) だけを見て、
/// 試合の種類も役職も見ない。懐中電灯を点ける・消すのは AdjustLighting で、これは視界の半径
/// (ShipStatus.CalculateLightRadius) が変わった時・死んだ時・イントロの終わりにしか呼ばれない。
/// そこで客ごとに次の順で送る (2026-11-24 実機・本編の客):
///   1. かくれんぼ型の設定 (UseFlashlight と大きさ・視界の半径を少しずらす)。半径が変わるので客の手元で懐中電灯が点く
///   2. 少し後に、1 と同じ半径の通常の設定。半径が変わらないので懐中電灯はそのまま残る
/// 1 のままにすると客の手元の LogicOptionsNormal.GameOptions が空になり、毎フレームの例外で歩けなくなるので、すぐ 2 で戻す。
/// 戻した後も半径を動かすと消えるので、ずらした半径のまま送り続け、点け直すたびにずらす向きを替える。
/// 会議明け・停電・暗転などで半径が変わると消えるので、その後に点け直す。点け直しの間 (HoldSeconds) は客が止まる。
/// ホスト自身は手元の判定を書き換えて点ける (電文は要らない)。
/// </summary>
public static class Flashlight
{
    // 点け直すたびに足す・外す視界の倍率。半径が本編の Approximately で別の値になれば足りる
    private const float Shift = 0.02f;
    // かくれんぼ型の設定のままにしておく秒数。この間に客の FixedUpdate が少なくとも 1 回回ればよい
    private const float HoldSeconds = 0.4f;
    // イントロ・会議明けの後、視界の送り直し (暗転対策など) が落ち着くまで待つ秒数
    private const float AfterPhaseDelay = 2f;
    // 停電が始まった・直った後、視界が絞られ終わるまで待つ秒数
    private const float LightsDelay = 4f;
    // 送る視界の値が変わった後に点け直すまでの秒数 (続けて変わる時にまとめる)
    private const float AfterChangeDelay = 1f;
    // 点け直しを人ごとにずらす間隔と、同じ人を続けて点け直さない最短の間隔。
    // 1 回の点け直しは同じ客へ設定の全文を 2 本送るので、全員を同じ瞬間に送らず、同じ客へも詰めて送らない
    private const float ArmStagger = 0.15f;
    private const float MinArmInterval = 1.5f;

    private static readonly HashSet<byte> shifted = new();
    private static readonly Dictionary<byte, float> armAt = new();
    private static readonly Dictionary<byte, float> holdUntil = new();
    private static readonly Dictionary<byte, (float Crew, float Imp)> lastBase = new();
    private static readonly List<byte> work = new();
    private static readonly Dictionary<byte, float> lastArmAt = new();
    private static float nextArmSlot;
    private static bool wasInTask;
    private static float inTaskSince;
    private static bool lightsWere;
    private static float nextLightsCheck;
    // 試合が始まって最初の Tick で決める (設定は試合中に変わらない)。毎フレームの判定を軽くする。
    // 試合の始めの初期化で決めると、属性 (AddLight) の初期化より先に走った時に前の試合の値を読む
    private static bool activeThisGame;
    private static bool activeKnown;
    private static bool officialWarned;

    private static bool GlobalOn => Options.FlashlightMode.GetBool();

    /// <summary>この試合で懐中電灯を使うか (通常モードで、全員か属性のどちらかが有効)。</summary>
    public static bool Active
        => Options.CurrentGameMode == CustomGameMode.Standard
           && GameOptionsManager.Instance != null && GameOptionsManager.Instance.currentGameMode == GameModes.Normal
           && (GlobalOn || AddLight.FlashlightOn);

    public static bool Has(PlayerControl pc)
        => pc != null && pc.Data != null && !pc.Data.IsDead && !pc.Data.Disconnected
           && (GlobalOn || (AddLight.FlashlightOn && AddLight.IsThisRole(pc.PlayerId)));

    private static (float Crew, float Imp) Sizes(PlayerControl pc)
    {
        if (AddLight.FlashlightOn && AddLight.IsThisRole(pc.PlayerId)) return (AddLight.FlashlightSize, AddLight.FlashlightSize);
        return (Options.FlashlightCrewSize.GetFloat(), Options.FlashlightImpostorSize.GetFloat());
    }

    [GameModuleInitializer]
    public static void Init()
    {
        shifted.Clear();
        armAt.Clear();
        holdUntil.Clear();
        lastBase.Clear();
        lastArmAt.Clear();
        nextArmSlot = 0f;
        wasInTask = false;
        inTaskSince = 0f;
        lightsWere = false;
        nextLightsCheck = 0f;
        activeKnown = false;
    }

    /// <summary>試合が終わった時に呼ぶ。点け直しの途中の印を残すと、ロビーでその人の設定が送られなくなる。</summary>
    public static void Clear()
    {
        holdUntil.Clear();
        armAt.Clear();
        activeThisGame = false;
        activeKnown = true;
    }

    /// <summary>点け直しの最中か。最中は TOHL のふだんの設定の送信を止める (かくれんぼ型を上書きしない)。</summary>
    public static bool IsHolding(byte playerId) => holdUntil.ContainsKey(playerId);
    /// <summary>誰かの点け直しの最中か</summary>
    public static bool AnyHolding => holdUntil.Count > 0;

    /// <summary>懐中電灯を持つ全員の点け直しを予約する (GameManager を出し直して視界が配り直された時など)</summary>
    public static void RequestAll()
    {
        if (!activeThisGame) return;
        foreach (var pc in Core.Players.All)
            if (Has(pc)) RequestArm(pc.PlayerId, AfterChangeDelay);
    }

    private static void RequestArm(byte playerId, float delay)
    {
        float at = Time.realtimeSinceStartup + delay;
        if (!armAt.TryGetValue(playerId, out var cur) || cur < at) armAt[playerId] = at;
    }

    /// <summary>
    /// 1 人分の設定を組み終えた所で呼ぶ (PlayerGameOptionsSender.BuildGameOptions の最後)。
    /// 視界の値が変わったら点け直しを予約し、ずらしている人には半径のずれを足す。
    /// </summary>
    public static void ApplyGameOptions(PlayerControl player, IGameOptions opt)
    {
        if (!AmongUsClient.Instance.AmHost || !Active || !Has(player)) return;
        byte id = player.PlayerId;
        float crew = opt.GetFloat(FloatOptionNames.CrewLightMod);
        float imp = opt.GetFloat(FloatOptionNames.ImpostorLightMod);
        if (!lastBase.TryGetValue(id, out var b) || !Mathf.Approximately(b.Crew, crew) || !Mathf.Approximately(b.Imp, imp))
        {
            lastBase[id] = (crew, imp);
            RequestArm(id, AfterChangeDelay);
        }
        // 暗転中 (視界 0) は真っ暗のままにする
        if (crew <= 0f && imp <= 0f) return;
        if (shifted.Contains(id))
        {
            opt.SetFloat(FloatOptionNames.CrewLightMod, crew + Shift);
            opt.SetFloat(FloatOptionNames.ImpostorLightMod, imp + Shift);
        }
    }

    private static bool InTaskNow()
        => GameStates.IsInTask && Main.introDestroyed && ShipStatus.Instance && !ExileController.Instance;

    private static void Tick()
    {
        if (!activeKnown)
        {
            activeThisGame = Active;
            activeKnown = true;
        }
        if (!activeThisGame || !AmongUsClient.Instance.AmHost) return;
        // 鬼の体の窓の間は設定を送れない
        if (SeekerBody.InWindow) return;
        float now = Time.realtimeSinceStartup;

        bool inTask = InTaskNow();
        if (inTask && !wasInTask)
        {
            // イントロの後・会議明け。視界が配り直されるので、落ち着いてから点け直す
            inTaskSince = now;
            foreach (var pc in Core.Players.All)
                if (Has(pc)) RequestArm(pc.PlayerId, AfterPhaseDelay);
        }
        wasInTask = inTask;

        if (now >= nextLightsCheck)
        {
            nextLightsCheck = now + 0.25f;
            bool lights = inTask && Utils.IsActive(SystemTypes.Electrical);
            if (lights != lightsWere)
            {
                lightsWere = lights;
                foreach (var pc in Core.Players.All)
                    if (Has(pc)) RequestArm(pc.PlayerId, LightsDelay);
            }
        }

        if (holdUntil.Count > 0)
        {
            work.Clear();
            foreach (var (id, until) in holdUntil) if (now >= until) work.Add(id);
            foreach (var id in work) FinishHold(id);
        }

        if (armAt.Count == 0 || !inTask || now - inTaskSince < AfterPhaseDelay) return;
        work.Clear();
        foreach (var (id, at) in armAt) if (now >= at) work.Add(id);
        foreach (var id in work)
        {
            // 1 回の Tick で点け直すのは 1 人まで。次の人は ArmStagger 後
            if (now < nextArmSlot) break;
            var pc = Core.Players.ById(id);
            if (pc == null || !Has(pc)) { armAt.Remove(id); continue; }
            if (IsHolding(id)) continue;
            if (PlayerState.GetByPlayerId(id)?.IsBlackOut == true) continue;
            if (lastArmAt.TryGetValue(id, out var last) && now - last < MinArmInterval) continue;
            armAt.Remove(id);
            lastArmAt[id] = now;
            nextArmSlot = now + ArmStagger;
            Arm(pc);
        }
    }

    private static PlayerGameOptionsSender SenderOf(byte playerId)
    {
        foreach (var s in GameOptionsSender.AllSenders)
            if (s is PlayerGameOptionsSender ps && ps.player != null && ps.player.PlayerId == playerId) return ps;
        return null;
    }

    private static void Arm(PlayerControl pc)
    {
        if (pc.AmOwner)
        {
            // ホストは手元の判定 (下のパッチ) で点くので、点け直しを促すだけ
            pc.AdjustLighting();
            return;
        }
        // 公式サーバーは、通常の試合でかくれんぼ型の設定を送るとホストを Hacking で切断する
        // (2026-11-27 公式 Asia: 会議明けに 1 回目を送った直後に 2 試合とも切られた)。公式では客へ送らない
        if (Net.OfficialServer.IsActive)
        {
            if (!officialWarned)
            {
                officialWarned = true;
                Logger.Warn("公式サーバーではかくれんぼ型の設定を送ると切断されるので、客の懐中電灯は点けない", nameof(Flashlight));
            }
            return;
        }
        var sender = SenderOf(pc.PlayerId);
        if (sender == null || pc.LacksClient("Flashlight")) return;
        var normal = sender.BuildGameOptions();
        if (normal == null) return;
        byte id = pc.PlayerId;
        bool nowShifted = shifted.Contains(id);
        float crew = normal.GetFloat(FloatOptionNames.CrewLightMod) - (nowShifted ? Shift : 0f);
        float imp = normal.GetFloat(FloatOptionNames.ImpostorLightMod) - (nowShifted ? Shift : 0f);
        if (crew <= 0f && imp <= 0f) return;
        float next = nowShifted ? 0f : Shift;
        var (crewSize, impSize) = Sizes(pc);

        var hns = new HideNSeekGameOptionsV12(new InnerNet.UnityLogger().Cast<Hazel.ILogger>());
        int count = GameData.Instance ? GameData.Instance.PlayerCount : 4;
        hns.SetRecommendations(Mathf.Max(count, 4), true);
        hns.MapId = normal.MapId;
        // 部屋そのものの値 (最大人数・言語) は部屋の設定と同じにする。公式鯖は、部屋と違う言語の入った設定を客へ送ったホストを
        // その場で切断する (かくれんぼ型の既定の言語のままだと日本語の部屋で切られる・2026-11-27 公式アジアで実測)
        hns.MaxPlayers = normal.MaxPlayers;
        hns.Keywords = normal.Keywords;
        hns.NumImpostors = normal.NumImpostors;
        hns.PlayerSpeedMod = normal.GetFloat(FloatOptionNames.PlayerSpeedMod);
        hns.KillCooldown = normal.GetFloat(FloatOptionNames.KillCooldown);
        hns.KillDistance = normal.GetInt(Int32OptionNames.KillDistance);
        hns.CrewLightMod = crew + next;
        hns.ImpostorLightMod = imp + next;
        hns.useFlashlight = true;
        hns.CrewmateFlashlightSize = crewSize;
        hns.ImpostorFlashlightSize = impSize;

        holdUntil[id] = Time.realtimeSinceStartup + HoldSeconds;
        sender.SendRaw(hns.Cast<IGameOptions>());
        Logger.Info($"{pc.Data?.PlayerName}: 懐中電灯を点ける (視界 {crew:0.00}/{imp:0.00} + {next}・"
            + $"map {hns.MapId}・imp {hns.NumImpostors}・max {hns.MaxPlayers}・kw {(uint)hns.Keywords}・speed {hns.PlayerSpeedMod:0.###}・kcd {hns.KillCooldown:0.###}・kdist {hns.KillDistance}・"
            + $"size {crewSize:0.##}/{impSize:0.##}・人数 {count})", nameof(Flashlight));
    }

    // かくれんぼ型を送った後、同じ半径の通常の設定へ戻す
    private static void FinishHold(byte id)
    {
        holdUntil.Remove(id);
        if (!shifted.Remove(id)) shifted.Add(id);
        var sender = SenderOf(id);
        if (sender == null) return;
        // 会議の電文より前に届くよう、組み直してすぐ送る (SendAllGameOptions を待たない)。
        // 止めていた間に溜まった変更もこれで送られる。dirty は残してよい (同じ中身は送り直さない)
        sender.SetDirty();
        try { sender.SendGameOptions(); }
        catch (System.Exception ex) { Logger.Exception(ex, nameof(Flashlight)); }
    }

    /// <summary>会議を開く直前に呼ぶ。かくれんぼ型のまま会議に入ると客の会議の画面が壊れるので、先に通常へ戻す。</summary>
    public static void BeforeMeeting()
    {
        if (holdUntil.Count == 0) return;
        work.Clear();
        work.AddRange(holdUntil.Keys);
        foreach (var id in work) FinishHold(id);
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.FixedUpdate))]
    private static class TickPatch
    {
        public static void Postfix() => Tick();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class GameEndPatch
    {
        public static void Postfix() => Clear();
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcStartMeeting))]
    private static class MeetingPatch
    {
        public static void Prefix()
        {
            try { if (AmongUsClient.Instance.AmHost) BeforeMeeting(); }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(Flashlight)); }
        }
    }

    // ホスト自身: 本編は設定の GameMode で判定するので、ホストの分はここで点ける
    private static bool HostHas
        => AmongUsClient.Instance && AmongUsClient.Instance.AmHost && !LobbyBehaviour.Instance && Active && Has(PlayerControl.LocalPlayer);

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.IsFlashlightEnabled))]
    private static class HostEnabledPatch
    {
        public static void Postfix(ref bool __result)
        {
            if (!__result && HostHas) __result = true;
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.AdjustLighting))]
    private static class HostLightingPatch
    {
        public static bool Prefix(PlayerControl __instance)
        {
            if (__instance != PlayerControl.LocalPlayer || !HostHas || !__instance.lightSource) return true;
            var (crewSize, impSize) = Sizes(__instance);
            float size = __instance.Data.Role != null && __instance.Data.Role.IsImpostor ? impSize : crewSize;
            __instance.SetFlashlightInputMethod();
            __instance.lightSource.SetupLightingForGameplay(true, size, __instance.TargetFlashlight ? __instance.TargetFlashlight.transform : null);
            return false;
        }
    }
}
