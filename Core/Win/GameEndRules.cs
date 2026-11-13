using System;
using System.Collections.Generic;
using System.Linq;

using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Core.Win;

/// <summary>試合を終わらせる条件を 1 つ調べる。満たしたら勝者を CustomWinnerHolder に入れて true を返す。</summary>
public delegate bool GameEndRule(out GameOverReason reason);

/// <summary>ゲームモードごとの終了条件の並び。前から順に調べ、最初に満たした物で終わる。</summary>
public sealed class GameEndRuleSet
{
    private readonly GameEndRule[] rules;
    public GameEndRuleSet(params GameEndRule[] rules) => this.rules = rules;

    public bool Check(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        // 役職などが先に勝者を決めていれば、ここでは何もしない
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return false;
        foreach (var rule in rules)
            if (rule(out reason)) return true;
        return false;
    }

    public static readonly GameEndRuleSet Normal = new(GameEndRules.ByLivingPlayers, GameEndRules.ByTask, GameEndRules.BySabotage);
    public static readonly GameEndRuleSet HideAndSeek = new(GameEndRules.ByLivingPlayersHideAndSeek, GameEndRules.ByTask);
}

/// <summary>
/// キルで人数を減らして勝つ陣営。生き残りのキルする側がこの陣営だけになり、クルー側の数が並んだら勝つ。
/// インポスターはここで持ち、他の陣営は役職が SimpleRoleInfo で宣言する (同じ CountTypes は 1 つだけ)。
/// </summary>
public sealed class KillingFaction
{
    public readonly CountTypes CountType;
    public readonly CustomWinner Winner;
    /// <summary>判定の順。人数が同じでも小さい陣営が先に勝つ。今の順: インポスター 0・ジャッカル 100・海賊 200・パブロフ 300・スタンド 400</summary>
    public readonly int Order;
    /// <summary>勝った時に、役職ごと勝者にする役職</summary>
    public readonly CustomRoles[] WinnerRoles;
    /// <summary>結果画面で追加勝利として並べる役職</summary>
    public readonly CustomRoles[] AdditionalWinnerRoles;
    /// <summary>人数の条件に加えて勝てるか (null なら人数だけ)</summary>
    public readonly Func<bool> CanWin;
    /// <summary>勝った時に、役職ごとでなく人を勝者に入れる (null なら無し)</summary>
    public readonly Action AddWinners;

    public KillingFaction(CountTypes countType, CustomWinner winner, int order,
        CustomRoles[] winnerRoles = null, CustomRoles[] additionalWinnerRoles = null,
        Func<bool> canWin = null, Action addWinners = null)
    {
        CountType = countType;
        Winner = winner;
        Order = order;
        WinnerRoles = winnerRoles ?? [];
        AdditionalWinnerRoles = additionalWinnerRoles ?? [];
        CanWin = canWin;
        AddWinners = addWinners;
    }
}

public static class GameEndRules
{
    private static readonly KillingFaction ImpostorFaction = new(CountTypes.Impostor, CustomWinner.Impostor, 0);

    private static KillingFaction[] killingFactions;
    /// <summary>判定の順に並べた陣営。役職の登録は起動時に揃うので、最初に使う時に 1 回だけ組む (static 初期化から役職を引かない)</summary>
    private static KillingFaction[] KillingFactions => killingFactions ??= BuildKillingFactions();

    private static KillingFaction[] BuildKillingFactions()
    {
        var list = new List<KillingFaction> { ImpostorFaction };
        foreach (var info in CustomRoleManager.AllRolesInfo.Values)
        {
            if (info.KillingFaction is not { } faction) continue;
            if (list.Exists(f => f.CountType == faction.CountType))
            {
                Logger.Error($"{faction.CountType} の陣営が 2 つ宣言されています ({info.RoleName})", "GameEndRules");
                continue;
            }
            list.Add(faction);
        }
        list.Sort((a, b) => a.Order.CompareTo(b.Order));
        return list.ToArray();
    }

    /// <summary>毎フレーム呼ばれるので、数を入れる箱は使い回す</summary>
    private static readonly GameEndCounts counts = new();

    /// <summary>この試合でキルする側 (キル陣営・独自の陣営) が 1 人でも生きていたことがあるか</summary>
    private static bool sawKillers;

    /// <summary>試合の始めに呼ぶ (終了判定を入れ替える時)。</summary>
    public static void ResetForNewGame() => sawKillers = false;

    /// <summary>生きている人を陣営ごとに数え、役職の補正を通す</summary>
    private static void CountAlive(GameEndCounts c)
    {
        c.Clear();
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (pc.GetRoleClass() is { CountsForGameEnd: false }) continue;
            c[pc.GetCountTypes()]++;
        }
        foreach (var adjust in CustomRoleManager.GameEndCountAdjustersOthers) adjust(c);
    }

    public static bool ByLivingPlayers(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (Options.NoGameEndByCount.GetBool()) return false;

        // アサシンがマーリンを当てている間などは終わらない
        foreach (var hold in CustomRoleManager.GameEndByCountHoldersOthers)
            if (hold()) return false;

        var c = counts;
        CountAlive(c);
        var killers = c.KillerSide;
        var crew = c.CrewSide;

        if (killers == 0 && crew == 0) //全滅
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.None);
            return true;
        }
        if (Main.AllAlivePlayerControls.All(p => p.Is(CustomRoles.Lovers))) //ラバーズ勝利
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Lovers);
            return true;
        }
        foreach (var faction in KillingFactions)
        {
            // 他のキルする側が全滅していて、クルー側の数がこの陣営以下
            var mine = c[faction.CountType];
            if (killers != mine || crew > mine) continue;
            if (faction.CanWin != null && !faction.CanWin()) continue;
            CustomWinnerHolder.ResetAndSetWinner(faction.Winner);
            foreach (var role in faction.WinnerRoles) CustomWinnerHolder.WinnerRoles.Add(role);
            foreach (var role in faction.AdditionalWinnerRoles) CustomWinnerHolder.AdditionalWinnerRoles.Add(role);
            faction.AddWinners?.Invoke();
            return true;
        }
        if (killers > 0) sawKillers = true;
        // キルする側が最初からいない試合 (1 人で試す時など) は、クルーの勝ちで即座に終わらせない。
        // 判定はイントロ中に走らないので、イントロ中に追放・切断で消えた分は配られた役職から見る
        else if (sawKillers || (sawKillers = WereKillersAssigned())) //クルー勝利
        {
            reason = GameOverReason.CrewmatesByVote;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Crewmate);
            return true;
        }
        return false;
    }

    /// <summary>死んだ人・抜けた人も含めて、キルする側か独自の陣営の役職が配られていたか</summary>
    private static bool WereKillersAssigned()
    {
        foreach (var state in PlayerState.AllPlayerStates.Values)
            if (GameEndCounts.IsKillerSide(state.CountType)) return true;
        return false;
    }

    public static bool ByLivingPlayersHideAndSeek(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (Options.NoGameEndByCount.GetBool()) return false;

        int imp = Utils.AlivePlayersCount(CountTypes.Impostor);
        int crew = Utils.AlivePlayersCount(CountTypes.Crew);

        if (imp == 0 && crew == 0) //全滅
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.None);
        else if (crew <= 0) //インポスター勝利
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
        else if (imp == 0) //クルー勝利(インポスター切断など)
        {
            reason = GameOverReason.CrewmatesByVote;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Crewmate);
        }
        else return false;
        return true;
    }

    /// <summary>GameData の TotalTasks と CompletedTasks から、タスク勝利を調べる。</summary>
    public static bool ByTask(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (Options.NoGameEndByTask.GetBool() || TaskState.InitialTotalTasks == 0) return false;
        // 妖狐の「クルーのタスク完了で終わらない」など
        foreach (var block in CustomRoleManager.TaskWinBlockersOthers)
            if (block()) return false;
        // タスク勝利の無効化: 1 = 常に、2 = 生きているクルーがいない時だけ (幽霊のタスクだけで勝たせない)
        switch (Options.DisableTaskWin.GetValue())
        {
            case 1: return false;
            case 2 when Utils.AlivePlayersCount(CountTypes.Crew) == 0: return false;
        }

        (int vtComp, int vtTotal) = VentEnterTask.TaskWinCountData();
        if (GameData.Instance.TotalTasks + vtTotal <= GameData.Instance.CompletedTasks + vtComp)
        {
            reason = GameOverReason.CrewmatesByTask;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Crewmate);
            Logger.Info($"GemeEndByTask task: {GameData.Instance.CompletedTasks}/{GameData.Instance.TotalTasks}", "CheckGameEndByTask");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 片付けた妨害を客へ送る印を立てる。ClearSabotage は印を立てないので、試合を続ける時はこれが無いと
    /// 客の画面で警報とカウントダウンが残る (2026-11-08 実機: Skeld の原子炉で客が「あと -24」のまま)。
    /// </summary>
    private static void MarkDirty(ISystemType sys)
    {
        if (sys.TryCast<ReactorSystemType>() is { } reactor) reactor.IsDirty = true;
        else if (sys.TryCast<HeliSabotageSystem>() is { } heli) heli.IsDirty = true;
    }

    /// <summary>ShipStatus.Systems から、致命的な妨害の時間切れを調べる。</summary>
    public static bool BySabotage(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (ShipStatus.Instance.Systems == null) return false;
        // 終わらせない時も、時間切れになった妨害は片付ける (警報が鳴り続けないように)
        var blocked = Options.NoGameEndBySabotage.GetBool();

        // TryGetValueは使用不可
        var systems = ShipStatus.Instance.Systems;
        LifeSuppSystemType lifeSupp;
        if (systems.ContainsKey(SystemTypes.LifeSupp) &&
            (lifeSupp = systems[SystemTypes.LifeSupp].TryCast<LifeSuppSystemType>()) != null &&
            lifeSupp.Countdown < 0f)
        {
            // 酸素
            if (blocked)
            {
                if (lifeSupp.TryCast<ICriticalSabotage>() is { } o2) o2.ClearSabotage();
                else lifeSupp.Countdown = 10000f;
                lifeSupp.IsDirty = true;
                return false;
            }
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
            reason = GameOverReason.ImpostorsBySabotage;
            lifeSupp.Countdown = 10000f;
            return true;
        }

        ISystemType sys = null;
        if (systems.ContainsKey(SystemTypes.Reactor)) sys = systems[SystemTypes.Reactor];
        else if (systems.ContainsKey(SystemTypes.Laboratory)) sys = systems[SystemTypes.Laboratory];
        else if (systems.ContainsKey(SystemTypes.HeliSabotage)) sys = systems[SystemTypes.HeliSabotage];

        ICriticalSabotage critical;
        if (sys != null &&
            (critical = sys.TryCast<ICriticalSabotage>()) != null &&
            critical.Countdown < 0f)
        {
            // リアクター
            if (blocked)
            {
                critical.ClearSabotage();
                MarkDirty(sys);
                return false;
            }
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
            reason = GameOverReason.ImpostorsBySabotage;
            critical.ClearSabotage();
            return true;
        }
        return false;
    }
}
