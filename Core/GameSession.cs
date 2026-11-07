using System;
using HarmonyLib;
using InnerNet;

using TownOfHostL;

namespace TownOfHostL.Core;

/// <summary>
/// 1 試合分の寿命。試合開始 (CoStartGame) で始まり、試合終了 (OnGameEnd) で「終了済み」になり、
/// ロビーに戻るか部屋から抜けた時に「閉じた」になる。
/// 試合中に予約した遅延処理は、その試合が閉じたら走らせずに捨てる。
/// 終了から閉じるまでの間 (結果画面) は Current が null になり、その間に予約した処理はどの試合にも属さない。
/// </summary>
[Modes.PatchBundle(Modes.PatchBundles.Core)]
public sealed class GameSession
{
    private static int nextId = 1;
    private static GameSession latest;

    /// <summary>試合中だけ非 null。</summary>
    public static GameSession Current { get; private set; }

    public int Id { get; }
    public DateTime StartedAt { get; }
    public bool IsFinished { get; private set; }
    public bool IsClosed { get; private set; }

    /// <summary>試合が閉じた時に呼ばれる。試合をまたいで残してはいけない状態を捨てる所。</summary>
    public static event Action<GameSession> Closed;

    private GameSession(int id)
    {
        Id = id;
        StartedAt = DateTime.Now;
    }

    internal static void Begin()
    {
        // 試合中は設定が変わらないので、予約中の保存はここで書き切る
        TownOfHostL.Modules.OptionSaver.Flush();
        CloseLatest("次の試合の開始");
        latest = Current = new GameSession(nextId++);
        Log.Info("GameSession", $"#{Current.Id} 開始");
    }

    internal static void Finish()
    {
        if (Current == null) return;
        Current.IsFinished = true;
        Log.Info("GameSession", $"#{Current.Id} 終了");
        Current = null;
    }

    internal static void CloseLatest(string reason)
    {
        var session = latest;
        if (session == null || session.IsClosed) return;
        session.IsFinished = true;
        session.IsClosed = true;
        if (Current == session) Current = null;
        Log.Info("GameSession", $"#{session.Id} を閉じる ({reason})");
        NotifyRolesQueue.Clear();
        NameRpcBatcher.Discard();
        try
        {
            Closed?.Invoke(session);
        }
        catch (Exception ex)
        {
            TownOfHostL.Logger.Exception(ex, "GameSession");
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGame))]
    private static class StartPatch
    {
        // 既存の CoStartGame の後置処理 (役職の準備) より先に始めておく
        [HarmonyPriority(Priority.First)]
        public static void Postfix() => Begin();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class EndPatch
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix() => Finish();
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    private static class LobbyPatch
    {
        public static void Postfix() => CloseLatest("ロビーに戻った");
    }

    // AmongUsClient.OnDisconnected は自分から抜けた時 (ExitGame) には呼ばれない。どの切断でも通る DisconnectInternal で見る
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.DisconnectInternal))]
    private static class DisconnectPatch
    {
        public static void Postfix()
        {
            CloseLatest("切断");
            // ロビーで抜けた時は閉じる試合が無いので、前の部屋宛ての溜まりはここで必ず捨てる
            NotifyRolesQueue.Clear();
            NameRpcBatcher.Discard();
        }
    }
}
