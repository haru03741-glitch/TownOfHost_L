using System;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;
using TownOfHostL.Attributes;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 試合中に、GameManager をほかのモードの物へ一時的に差し替える窓 (ホストのみ)。
///
/// 本編の客は、SetRole を受けた時などに <c>GameManager.Instance</c> へ問い合わせて決める物がある (体の種類など)。
/// 窓の中で差し替えた GameManager を出してから送ると、客はその GameManager の答えで決める。
/// 順番: 暗くする → 今の GameManager を Despawn → 間 → 差し替えの物を Spawn → 送る (使い手の処理) → 間 → Despawn → 間 → 通常の物を Spawn。
///
/// - Despawn と Spawn の間は空ける。客の手元で同じフレームに重なると、新しい GameManager は Awake で自分を消す。
/// - 窓の間 (約 3 秒) は客の画面で設定が引けず、全員が歩けない。待つ側は <see cref="InWindow"/> を見て、
///   通報を預かり、キル・勝敗判定・設定の送信を止める。止まっている所を見せないよう、窓の少し前に全員の視界を
///   自分の周りだけまで狭め、窓が閉じるまで暗くする (0 にすると自分の姿と名前の下の文字も見えなくなる)。
/// - 通常の GameManager の部品の OnGameStart は空なので、出し直した物に StartGame が無くても困らない。
/// - 途中で失敗したら、通常の GameManager を必ず戻して窓を閉じる。閉じた時の処理は失敗の時も呼ぶ。
/// - 客がかくれんぼの物の Despawn と通常の物の Spawn を同じフレームで処理すると (窓の最中に 1.6 秒以上固まった時など)、
///   通常の物は Awake で消え、Instance は消えたかくれんぼの物を指したまま残る。その客は試合の終わりまで設定が引けず、
///   ボタンも押せず、SetRole も受けない (CoSetRole が GameManager を待ち続ける)。窓の後で間を空けてもう一度、
///   通常の物だけを出し直す (直し)。消えた物は Awake の判定で無いことになるので、固まった客にも新しい物が入る。
/// </summary>
public static class GameManagerWindow
{
    // Despawn から Spawn までの間 (客の手元で同じフレームに重ならないように)。
    // 客が一瞬固まる (役職の作り直し・原子炉の点滅) と 0.5 秒では重なり、通常の GameManager が戻らないことがあった
    private const float SpawnGap = 0.8f;
    // 差し替えの GameManager を出してから送るまで
    private const float SendDelay = 0.2f;
    // 送ってから、差し替えの GameManager を消すまで (客が送った物を処理し終えるまで)
    private const float Hold = 0.8f;
    // 窓の後、次の窓を開けるまでの最短の間
    private const float BetweenWindows = 1f;
    // 暗転させてから GameManager を消すまで (視界が暗くなり終えるまで)
    private const float CoverLead = 0.4f;
    // 窓を閉じてから直しを始めるまで。客の固まりが明けてから出し直す。
    // 閉じた時の Revive.BeginSettling (SettleTime) より短くして、直しが終わるまで通報を預からせる
    private const float RepairDelay = 2f;

    private enum Phase { Idle, Cover, SpawnTemp, Send, DespawnTemp, SpawnNormal }

    private static Phase phase;
    private static float nextAt;
    private static float idleSince;
    // 失敗から戻そうとした回数 (窓ごと)
    private static int recoverTries;
    private static GameModes tempMode;
    private static Action onSend;
    private static Action onClosed;
    // 直しの最中か (差し替えの物を出さず、通常の物を出し直すだけ)
    private static bool repairing;
    // 直しを始める時刻 (Time.realtimeSinceStartup)。無ければ正の無限大
    private static float repairAt = float.PositiveInfinity;

    /// <summary>窓の間の視界の倍率 (自分の周りだけが見える)</summary>
    public const float DimVision = 0.15f;
    // 窓の暗さを掛けている人
    private static readonly System.Collections.Generic.HashSet<byte> dimmed = new();

    /// <summary>窓の最中か (GameManager を差し替えている間)。この間は GameManager.Instance が無いか、差し替えの物</summary>
    // 窓の最中に試合が終わると窓は閉じずに残るので、試合の外では窓の最中と見ない (次の試合の始めに Init で戻る)
    public static bool InWindow => phase != Phase.Idle && GameStates.IsInGame;
    /// <summary>窓の暗さを掛けている人か (人ごとの設定を組む時に視界を狭める)</summary>
    // 窓の最中に試合が終わると暗さの印も残るので、試合の外では見ない
    public static bool IsDimmed(byte playerId) => dimmed.Count > 0 && GameStates.IsInGame && dimmed.Contains(playerId);

    /// <summary>
    /// クルーの視界の倍率に掛けると、停電中でもインポスターの視界 (停電の影響を受けない) と同じ半径になる値。
    /// 本編の客の視界の半径は、クルーが Lerp(MinLightRadius, MaxLightRadius, 明るさ) × CrewLightMod、
    /// インポスターが MaxLightRadius × ImpostorLightMod
    /// </summary>
    public static float CrewLightCompensation()
    {
        var ship = ShipStatus.Instance;
        if (!ship || !ship.Systems.TryGetValue(SystemTypes.Electrical, out var system)) return 1f;
        var lights = system.TryCast<SwitchSystem>();
        if (lights == null) return 1f;
        float radius = Mathf.Lerp(ship.MinLightRadius, ship.MaxLightRadius, lights.Level);
        if (radius <= 0.01f) return 1f;
        return Mathf.Clamp(ship.MaxLightRadius / radius, 1f, 10f);
    }

    [GameModuleInitializer]
    public static void Init()
    {
        phase = Phase.Idle;
        nextAt = 0f;
        idleSince = 0f;
        recoverTries = 0;
        onSend = null;
        onClosed = null;
        repairing = false;
        repairAt = float.PositiveInfinity;
        dimmed.Clear();
    }

    /// <summary>今、窓を開けられるか (窓の最中でなく、前の窓から間が空き、試合が普通に進んでいる)</summary>
    public static bool CanOpen()
    {
        if (phase != Phase.Idle) return false;
        if (Time.realtimeSinceStartup - idleSince < BetweenWindows) return false;
        if (!GameStates.IsInTask || GameStates.IsMeeting || MeetingHud.Instance || ExileController.Instance) return false;
        if (!Main.introDestroyed || !GameManager.Instance || !ShipStatus.Instance) return false;
        if (AntiBlackout.IsRevertPending || AntiBlackout.IsCached || Revive.HasHeldReport) return false;
        if (Flashlight.AnyHolding) return false;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default || AmongUsClient.Instance.IsGameOver) return false;
        return GameOptionsManager.Instance.currentGameMode == GameModes.Normal;
    }

    /// <summary>
    /// 窓を開ける (<see cref="CanOpen"/> が true の時だけ呼ぶ)。mode の GameManager を出した後で send を呼び、
    /// 通常の GameManager を出し直した後で closed を呼ぶ (途中で失敗した時も呼ぶ)
    /// </summary>
    public static void Open(GameModes mode, Action send, Action closed)
    {
        var client = AmongUsClient.Instance;
        if (!client || !client.AmHost || phase != Phase.Idle) return;
        tempMode = mode;
        onSend = send;
        onClosed = closed;
        // この窓でも通常の物を出し直すので、待っている直しはこの窓の後へ回す
        repairing = false;
        repairAt = float.PositiveInfinity;
        try { StartWindow(); }
        catch (Exception ex) { Fail(client, ex); }
    }

    // 試合中は毎フレーム呼ばれるので、窓も直しの予約も無い間は何も作らずに返す
    private static void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (phase == Phase.Idle)
        {
            if (now >= repairAt) TryStartRepair();
            return;
        }
        if (now < nextAt) return;
        var client = AmongUsClient.Instance;
        if (!client || !client.AmHost || !GameStates.IsInGame) return;
        try { Advance(client); }
        catch (Exception ex) { Fail(client, ex); }
    }

    // 直しを始める。会議や試合の終わりが来ていたらやめ、懐中電灯の一時の設定を送っている間は待つ
    private static void TryStartRepair()
    {
        var client = AmongUsClient.Instance;
        if (!client || !client.AmHost || !GameStates.IsInGame || !GameStates.IsInTask || GameStates.IsMeeting
            || MeetingHud.Instance || ExileController.Instance || !GameManager.Instance || !ShipStatus.Instance
            || CustomWinnerHolder.WinnerTeam != CustomWinner.Default || client.IsGameOver
            || GameOptionsManager.Instance.currentGameMode != GameModes.Normal)
        {
            repairAt = float.PositiveInfinity;
            return;
        }
        if (Flashlight.AnyHolding) return;
        repairAt = float.PositiveInfinity;
        repairing = true;
        onSend = null;
        onClosed = null;
        Logger.Info("通常の GameManager を出し直す", nameof(GameManagerWindow));
        try { StartWindow(); }
        catch (Exception ex) { Fail(client, ex); }
    }

    // 窓の 1 段が失敗した。通常の GameManager を必ず戻す
    private static void Fail(InnerNetClient client, Exception ex)
    {
        Logger.Exception(ex, nameof(GameManagerWindow));
        Recover(client);
    }

    private static void StartWindow()
    {
        Revive.BeginSettling();
        // 暗転の設定は窓に入る前に送る (窓の間は送れない)
        Cover();
        phase = Phase.Cover;
        nextAt = Time.realtimeSinceStartup + CoverLead;
    }

    private static void Cover()
    {
        foreach (var pc in Main.AllPlayerControls)
            if (pc.Data != null && !pc.Data.Disconnected) dimmed.Add(pc.PlayerId);
        Utils.MarkEveryoneDirtySettings();
        Utils.SyncAllSettings();
    }

    private static void Advance(InnerNetClient client)
    {
        float now = Time.realtimeSinceStartup;
        Revive.BeginSettling();
        switch (phase)
        {
            case Phase.Cover:
                client.Despawn(GameManager.Instance);
                GameManager.Instance = null;
                phase = repairing ? Phase.SpawnNormal : Phase.SpawnTemp;
                nextAt = now + SpawnGap;
                break;
            case Phase.SpawnTemp:
                client.Spawn(GameManagerCreator.CreateGameManager(tempMode), -2, SpawnFlags.None);
                phase = Phase.Send;
                nextAt = now + SendDelay;
                break;
            case Phase.Send:
                onSend?.Invoke();
                phase = Phase.DespawnTemp;
                nextAt = now + Hold;
                break;
            case Phase.DespawnTemp:
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
        recoverTries = 0;
        idleSince = Time.realtimeSinceStartup;
        // 差し替えの窓の後にだけ直しを予約する (直しの後には予約しない)
        repairAt = repairing ? float.PositiveInfinity : idleSince + RepairDelay;
        Logger.Info(repairing ? "直しを終えた" : "窓を閉じた", nameof(GameManagerWindow));
        repairing = false;
        // 暗転を明ける。出し直した GameManager は全員に同じ設定を配ったので、人ごとの設定を送り直す。懐中電灯も消えるので点け直す
        dimmed.Clear();
        // ここで例外が出ても、閉じた時の処理 (使い手の後始末) は必ず呼ぶ
        try
        {
            Utils.MarkEveryoneDirtySettings();
            Flashlight.RequestAll();
            Revive.BeginSettling();
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(GameManagerWindow)); }
        var closed = onClosed;
        onSend = null;
        onClosed = null;
        try { closed?.Invoke(); }
        catch (Exception ex) { Logger.Exception(ex, nameof(GameManagerWindow)); }
    }

    // 失敗した時: 通常の GameManager を戻して窓を閉じる。差し替えの物が残っていれば消し、間を空けてから出す
    // (同じフレームに Despawn と Spawn を送ると、客の手元で新しい GameManager が Awake で自分を消す)
    private static void Recover(InnerNetClient client)
    {
        try
        {
            if (GameManager.Instance && GameManager.Instance.TryCast<NormalGameManager>() != null)
            {
                FinishWindow();
                return;
            }
            if (recoverTries++ < 2)
            {
                if (GameManager.Instance) client.Despawn(GameManager.Instance);
                GameManager.Instance = null;
                phase = Phase.SpawnNormal;
                nextAt = Time.realtimeSinceStartup + SpawnGap;
                return;
            }
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(GameManagerWindow)); }
        FinishWindow();
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
