using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;
using TownOfHostL.Attributes;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 通常モードの試合で、人の体を本編のかくれんぼの鬼の体 (マスク) にする (ホストのみ)。
///
/// 本編の客は SetRole を受けた時、役職を付けた直後に <c>GameManager.Instance.GetBodyType(自分)</c> で体を決める。
/// かくれんぼの GameManager は「その客の画面で対象がインポスター系なら鬼の体」を返し、通常の GameManager は普通の体しか返さない。
/// そこで GameManager を Despawn → かくれんぼの物を Spawn → 対象の SetRole を送る → Despawn → 通常の物を Spawn し直す (窓)。
/// TOHL の通常モードは生きている人の SetRole を上書き可で送るので、客の画面の役職は固まらず、試合中いつでも通る。
/// 通常の GameManager の部品の OnGameStart は空なので、出し直した物に StartGame が無くても困らない。
///
/// 限り:
/// - 鬼の体はその客の画面で対象がインポスター系の時しか出ない。クルーの客には対象をインポスターとして送る (RoleView に記録するので
///   暗転対策の数え方にも入る)。自分の画面でキルを持つ客 (デシンクのキル役) には送らない。仲間に見えてキルできなくなるため。
/// - 後で対象の SetRole がその客へ届くと、通常の GameManager の下で体が決め直されて外れる (暗転対策のキル後の送り直しなど)。
///   キルの後と、関所 (RoleView) を通った送り直しの後に、窓を開け直して付け直す。
/// - 窓の間 (約 2.5 秒) は客の画面で設定が引けず、全員が歩けない。窓の間は通報を預かり、キル・勝敗判定・設定の送信を止める。
/// - Despawn と Spawn の間は空ける。客の手元で同じフレームに重なると、新しい GameManager は Awake で自分を消す。
/// </summary>
public static class SeekerBody
{
    // Despawn から Spawn までの間 (客の手元で同じフレームに重ならないように)。
    // 客が一瞬固まる (役職の作り直しなど) と 0.5 秒では重なり、通常の GameManager が戻らないことがあった
    private const float SpawnGap = 0.8f;
    // かくれんぼの GameManager を出してから SetRole を送るまで
    private const float SendDelay = 0.2f;
    // SetRole を送ってから、かくれんぼの GameManager を消すまで (客が SetRole を処理し終えるまで)
    private const float Hold = 0.8f;
    // 窓の後、次の窓を開けるまでの最短の間
    private const float BetweenWindows = 1f;

    private enum Phase { Idle, SpawnHns, Send, DespawnHns, SpawnNormal }

    private sealed class Entry
    {
        public Action OnApplied;
        public Action OnReleased;
        // マスクを送った見る人と、送る前の見え方
        public readonly Dictionary<byte, RoleTypes> Original = new();
    }

    private static Phase phase;
    private static float nextAt;
    private static float idleSince;
    private static readonly Dictionary<byte, Entry> masked = new();
    private static readonly Dictionary<byte, float> applyAt = new();
    private static readonly HashSet<byte> releaseAfterWindow = new();
    private static readonly List<byte> sending = new();
    private static readonly List<byte> work = new();
    // 自分で送っている間は、関所の記録で付け直しを予約しない
    private static bool selfSending;

    /// <summary>窓の最中か (GameManager を差し替えている間)。この間は GameManager.Instance が無いか、かくれんぼの物</summary>
    public static bool InWindow => phase != Phase.Idle;
    public static bool IsMasked(byte playerId) => masked.ContainsKey(playerId);

    [GameModuleInitializer]
    public static void Init()
    {
        phase = Phase.Idle;
        nextAt = 0f;
        idleSince = 0f;
        masked.Clear();
        applyAt.Clear();
        releaseAfterWindow.Clear();
        sending.Clear();
        selfSending = false;
    }

    /// <summary>
    /// pc に鬼の体を付ける。次に開ける窓で送る。onApplied は送った窓の後に、onReleased は会議などでこちらから外した時に呼ぶ
    /// (<see cref="Release"/> を自分で呼んだ時は呼ばない)
    /// </summary>
    public static void Apply(PlayerControl pc, Action onApplied = null, Action onReleased = null)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return;
        if (!masked.TryGetValue(pc.PlayerId, out var entry)) masked[pc.PlayerId] = entry = new();
        entry.OnApplied = onApplied;
        entry.OnReleased = onReleased;
        releaseAfterWindow.Remove(pc.PlayerId);
        RequestApply(pc.PlayerId, 0f);
        Logger.Info($"鬼の体を付ける: {pc.GetNameWithRole()}", nameof(SeekerBody));
    }

    /// <summary>pc の鬼の体を外す (見え方を元に戻す)。窓の最中なら窓の後で外す</summary>
    public static void Release(PlayerControl pc)
    {
        if (pc == null || !masked.ContainsKey(pc.PlayerId)) return;
        applyAt.Remove(pc.PlayerId);
        // 窓の間は SetRole を送れない (GameManager が無いか、かくれんぼの物) ので、窓の後で外す
        if (InWindow)
        {
            releaseAfterWindow.Add(pc.PlayerId);
            return;
        }
        Restore(pc.PlayerId, notify: false);
    }

    /// <summary>
    /// 会議を開く前に呼ぶ (ReportDeadBodyPatch)。全員の鬼の体を外し、外した SetRole が落ち着くまで通報を預からせる。
    /// 本編の客は SetRole の直後に会議が来ると会議の画面が壊れる。
    /// </summary>
    public static void ReleaseAllForMeeting()
    {
        if (masked.Count == 0) return;
        applyAt.Clear();
        work.Clear();
        work.AddRange(masked.Keys);
        foreach (var id in work)
        {
            if (InWindow) releaseAfterWindow.Add(id);
            else Restore(id, notify: true);
        }
        Revive.BeginSettling();
    }

    /// <summary>キルの後に呼ぶ。暗転対策の送り直し (キルの 0.5 秒後) で外れた分を付け直す</summary>
    public static void OnAnyMurder()
    {
        if (masked.Count == 0) return;
        foreach (var id in masked.Keys) RequestApply(id, 1f);
    }

    /// <summary>関所 (RoleView) で target の見え方が送られた時。窓の外なら付け直しを予約する</summary>
    internal static void OnRoleSent(PlayerControl target)
    {
        if (selfSending || target == null || !masked.ContainsKey(target.PlayerId)) return;
        RequestApply(target.PlayerId, 0.5f);
    }

    private static void RequestApply(byte id, float delay)
    {
        float at = Time.realtimeSinceStartup + delay;
        if (!applyAt.TryGetValue(id, out var cur) || cur < at) applyAt[id] = at;
    }

    private static bool CanStart(float now)
    {
        if (now - idleSince < BetweenWindows) return false;
        if (!GameStates.IsInTask || GameStates.IsMeeting || MeetingHud.Instance || ExileController.Instance) return false;
        if (!Main.introDestroyed || !GameManager.Instance || !ShipStatus.Instance) return false;
        if (AntiBlackout.IsRevertPending || AntiBlackout.IsCached || Revive.HasHeldReport) return false;
        if (Flashlight.AnyHolding) return false;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default || AmongUsClient.Instance.IsGameOver) return false;
        return GameOptionsManager.Instance.currentGameMode == GameModes.Normal;
    }

    private static void Update()
    {
        var client = AmongUsClient.Instance;
        if (!client || !client.AmHost || !GameStates.IsInGame) return;
        float now = Time.realtimeSinceStartup;

        if (phase == Phase.Idle)
        {
            DropDead();
            if (applyAt.Count == 0 || !CanStart(now)) return;
            sending.Clear();
            foreach (var (id, at) in applyAt) if (now >= at) sending.Add(id);
            if (sending.Count == 0) return;
            foreach (var id in sending) applyAt.Remove(id);
            Step(client, () => StartWindow(client));
            return;
        }
        if (now < nextAt) return;
        Step(client, () => Advance(client));
    }

    // 窓の 1 段を進める。失敗したら通常の GameManager を必ず戻す
    private static void Step(InnerNetClient client, Action act)
    {
        try { act(); }
        catch (Exception ex)
        {
            Logger.Exception(ex, nameof(SeekerBody));
            Recover(client);
        }
    }

    private static void StartWindow(InnerNetClient client)
    {
        Logger.Info($"窓を開ける: [{string.Join(",", sending)}]", nameof(SeekerBody));
        Revive.BeginSettling();
        client.Despawn(GameManager.Instance);
        GameManager.Instance = null;
        phase = Phase.SpawnHns;
        nextAt = Time.realtimeSinceStartup + SpawnGap;
    }

    private static void Advance(InnerNetClient client)
    {
        float now = Time.realtimeSinceStartup;
        Revive.BeginSettling();
        switch (phase)
        {
            case Phase.SpawnHns:
                client.Spawn(GameManagerCreator.CreateGameManager(GameModes.HideNSeek), -2, SpawnFlags.None);
                phase = Phase.Send;
                nextAt = now + SendDelay;
                break;
            case Phase.Send:
                foreach (var id in sending) SendMask(id);
                phase = Phase.DespawnHns;
                nextAt = now + Hold;
                break;
            case Phase.DespawnHns:
                client.Despawn(GameManager.Instance);
                GameManager.Instance = null;
                phase = Phase.SpawnNormal;
                nextAt = now + SpawnGap;
                break;
            case Phase.SpawnNormal:
                SpawnNormal(client);
                FinishWindow();
                break;
        }
    }

    private static void SpawnNormal(InnerNetClient client)
    {
        var gm = GameManagerCreator.CreateGameManager(GameModes.Normal);
        client.Spawn(gm, -2, SpawnFlags.None);
        gm.LogicOptions?.SyncOptions();
    }

    private static void FinishWindow()
    {
        phase = Phase.Idle;
        idleSince = Time.realtimeSinceStartup;
        Logger.Info("窓を閉じた", nameof(SeekerBody));
        // 出し直した GameManager は全員に同じ設定を配ったので、人ごとの設定を送り直す。懐中電灯も消えるので点け直す
        Utils.MarkEveryoneDirtySettings();
        Flashlight.RequestAll();
        Revive.BeginSettling();
        foreach (var id in sending)
        {
            if (releaseAfterWindow.Contains(id)) continue;
            if (masked.TryGetValue(id, out var entry))
            {
                try { entry.OnApplied?.Invoke(); }
                catch (Exception ex) { Logger.Exception(ex, nameof(SeekerBody)); }
            }
        }
        sending.Clear();
        if (releaseAfterWindow.Count > 0)
        {
            work.Clear();
            work.AddRange(releaseAfterWindow);
            releaseAfterWindow.Clear();
            foreach (var id in work) Restore(id, notify: true);
        }
    }

    // 失敗した時: GameManager が無ければ通常の物を出し、窓を閉じる
    private static void Recover(InnerNetClient client)
    {
        try
        {
            if (!GameManager.Instance || GameManager.Instance.TryCast<NormalGameManager>() == null)
            {
                if (GameManager.Instance) client.Despawn(GameManager.Instance);
                GameManager.Instance = null;
                SpawnNormal(client);
            }
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(SeekerBody)); }
        FinishWindow();
    }

    // 死んだ人・抜けた人の印を消す (死ぬ時の幽霊の SetRole で体は戻っている)
    private static void DropDead()
    {
        if (masked.Count == 0) return;
        work.Clear();
        foreach (var id in masked.Keys)
        {
            var pc = Utils.GetPlayerById(id);
            if (pc == null || pc.Data == null || pc.Data.Disconnected || !pc.IsAlive()) work.Add(id);
        }
        foreach (var id in work)
        {
            var entry = masked[id];
            masked.Remove(id);
            applyAt.Remove(id);
            try { entry.OnReleased?.Invoke(); }
            catch (Exception ex) { Logger.Exception(ex, nameof(SeekerBody)); }
        }
    }

    private static void SendMask(byte id)
    {
        var target = Utils.GetPlayerById(id);
        if (target == null || !target.IsAlive() || !masked.TryGetValue(id, out var entry)) return;
        selfSending = true;
        try
        {
            foreach (var seer in Main.AllPlayerControls)
            {
                if (seer.Data == null || seer.Data.Disconnected) continue;
                if (seer.AmOwner)
                {
                    // ホストの画面は手元で体だけ替える (ホストの手元の役職は試合の始めに固まっていて、SetRole は通らない)
                    target.MyPhysics.SetBodyType(GameManager.Instance.GetBodyType(target));
                    continue;
                }
                if (seer.GetClientId() == -1) continue;
                var current = RoleView.Living(target, seer);
                RoleTypes send;
                if (seer.PlayerId == target.PlayerId || RoleView.IsKillerRole(current)) send = current;
                // 自分の画面でキルを持つ人には送らない (インポスターに見せると仲間になってキルできなくなる)
                else if (seer.IsAlive() && RoleView.IsKillerRole(RoleView.Living(seer, seer))) continue;
                else send = RoleTypes.Impostor;
                entry.Original.TryAdd(seer.PlayerId, current);
                RoleView.Send(target, seer, send);
            }
        }
        finally { selfSending = false; }
    }

    // 見え方を送る前の物に戻す。通常の GameManager の下で SetRole が走るので体も普通に戻る
    private static void Restore(byte id, bool notify)
    {
        if (!masked.Remove(id, out var entry)) return;
        applyAt.Remove(id);
        var target = Utils.GetPlayerById(id);
        if (target != null && target.IsAlive() && target.Data != null && !target.Data.Disconnected)
        {
            selfSending = true;
            try
            {
                if (GameManager.Instance) target.MyPhysics.SetBodyType(GameManager.Instance.GetBodyType(target));
                foreach (var (seerId, role) in entry.Original)
                {
                    var seer = Utils.GetPlayerById(seerId);
                    if (seer == null) continue;
                    RoleView.Send(target, seer, role);
                }
            }
            finally { selfSending = false; }
            Revive.BeginSettling();
        }
        Logger.Info($"鬼の体を外す: {target?.GetNameWithRole() ?? id.ToString()}", nameof(SeekerBody));
        if (!notify) return;
        try { entry.OnReleased?.Invoke(); }
        catch (Exception ex) { Logger.Exception(ex, nameof(SeekerBody)); }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.FixedUpdate))]
    private static class TickPatch
    {
        public static void Postfix() => Update();
    }

    // 窓の間はホストの手元でも本編がキルの距離を引けない。キルの対象は無いことにする
    [HarmonyPatch(typeof(ImpostorRole), nameof(ImpostorRole.FindClosestTarget))]
    private static class FindClosestTargetPatch
    {
        public static bool Prefix(ref PlayerControl __result)
        {
            if (!InWindow) return true;
            __result = null;
            return false;
        }
    }

    // 窓の間だけ置くかくれんぼの GameManager に、勝敗を判定させない
    [HarmonyPatch(typeof(LogicGameFlowHnS), nameof(LogicGameFlowHnS.CheckEndCriteria))]
    private static class HnsEndPatch
    {
        public static bool Prefix() => !InWindow;
    }
}
