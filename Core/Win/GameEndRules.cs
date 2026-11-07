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

/// <summary>キルで人数を減らして勝つ陣営。生き残りがこの陣営だけになり、クルーの数が並んだら勝つ。</summary>
public sealed class KillingFaction
{
    public readonly CountTypes CountType;
    public readonly CustomWinner Winner;
    /// <summary>勝った時に、役職ごと勝者にする役職</summary>
    public readonly CustomRoles[] WinnerRoles;
    /// <summary>結果画面で追加勝利として並べる役職</summary>
    public readonly CustomRoles[] AdditionalWinnerRoles;

    public KillingFaction(CountTypes countType, CustomWinner winner, CustomRoles[] winnerRoles = null, CustomRoles[] additionalWinnerRoles = null)
    {
        CountType = countType;
        Winner = winner;
        WinnerRoles = winnerRoles ?? [];
        AdditionalWinnerRoles = additionalWinnerRoles ?? [];
    }
}

public static class GameEndRules
{
    /// <summary>並びは判定の順。人数が同じでも前の陣営が先に勝つ。</summary>
    public static readonly KillingFaction[] KillingFactions =
    [
        new(CountTypes.Impostor, CustomWinner.Impostor),
        new(CountTypes.Jackal, CustomWinner.Jackal,
            [CustomRoles.JackalHadouHo, CustomRoles.JackalWolf, CustomRoles.Tama, CustomRoles.Jackal, CustomRoles.JClient, CustomRoles.JSidekick]),
        new(CountTypes.Pirate, CustomWinner.Pirate,
            [CustomRoles.Pirate, CustomRoles.Gang], [CustomRoles.Gang]),
    ];

    private static readonly int[] factionCounts = new int[KillingFactions.Length];

    public static bool ByLivingPlayers(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;

        // hamo: アサシンがマーリンを当てている間は終わらない
        if (TownOfHostL.Roles.Hamo.Impostor.Assassin.assassin?.NowState is TownOfHostL.Roles.Hamo.Impostor.Assassin.AssassinMeeting.Collected) return false;

        var factions = KillingFactions;
        // 毎フレーム呼ばれるので、数を入れる配列は使い回す
        var counts = factionCounts;
        var killers = 0;
        for (var i = 0; i < factions.Length; i++)
        {
            counts[i] = Utils.AlivePlayersCount(factions[i].CountType);
            killers += counts[i];
        }
        // hamo: 妖狐はクルーとして数える
        var crew = Utils.AlivePlayersCount(CountTypes.Crew) + Utils.AlivePlayersCount(CountTypes.Fox);
        // hamo: インポスター・ジャッカル・クルーとして数える役職と、独自に数える陣営 (パブロフ・スタンド・イーター)
        int imp = counts[0], jackal = counts[1];
        var hamo = HamoGameEnd.Count(ref imp, ref jackal, ref crew);
        killers += imp - counts[0] + jackal - counts[1];
        counts[0] = imp; counts[1] = jackal;
        int other = hamo.Pavlov + hamo.StandMaster + hamo.Eater;

        if (killers == 0 && crew == 0 && other == 0) //全滅
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.None);
            return true;
        }
        if (Main.AllAlivePlayerControls.All(p => p.Is(CustomRoles.Lovers))) //ラバーズ勝利
        {
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Lovers);
            return true;
        }
        for (var i = 0; i < factions.Length; i++)
        {
            // 他のキル陣営が全滅していて、クルーの数がこの陣営以下
            if (killers - counts[i] != 0 || other != 0 || crew > counts[i]) continue;
            var faction = factions[i];
            CustomWinnerHolder.ResetAndSetWinner(faction.Winner);
            foreach (var role in faction.WinnerRoles) CustomWinnerHolder.WinnerRoles.Add(role);
            foreach (var role in faction.AdditionalWinnerRoles) CustomWinnerHolder.AdditionalWinnerRoles.Add(role);
            return true;
        }
        if (HamoGameEnd.CheckTeamWin(counts[0], counts[1], counts[2], crew, hamo, ref reason)) return true;
        if (killers == 0 && other == 0) //クルー勝利
        {
            reason = GameOverReason.CrewmatesByVote;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Crewmate);
            return true;
        }
        return false;
    }

    public static bool ByLivingPlayersHideAndSeek(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;

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
        if (Options.DisableTaskWin.GetBool() || TaskState.InitialTotalTasks == 0 || TownOfHostL.Roles.Hamo.Neutral.Fox.BlockTaskWin()) return false;

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

    /// <summary>ShipStatus.Systems から、致命的な妨害の時間切れを調べる。</summary>
    public static bool BySabotage(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        if (ShipStatus.Instance.Systems == null) return false;

        // TryGetValueは使用不可
        var systems = ShipStatus.Instance.Systems;
        LifeSuppSystemType lifeSupp;
        if (systems.ContainsKey(SystemTypes.LifeSupp) &&
            (lifeSupp = systems[SystemTypes.LifeSupp].TryCast<LifeSuppSystemType>()) != null &&
            lifeSupp.Countdown < 0f)
        {
            // 酸素
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
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Impostor);
            reason = GameOverReason.ImpostorsBySabotage;
            critical.ClearSabotage();
            return true;
        }
        return false;
    }
}
