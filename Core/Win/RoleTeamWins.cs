using System.Linq;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL;

/// <summary>パブロフ・スタンドマスター・イーターなどの陣営の勝利判定と、勝者が決まった後の乗っ取り・単独勝利</summary>
public static class RoleTeamWins
{
    public struct Counts
    {
        public int Pavlov;
        public int StandMaster;
        public int Eater;
    }

    /// <summary>生きている人を陣営ごとに数える (GameEndRules の数え方に足す)</summary>
    public static Counts Count(ref int imp, ref int jackal, ref int crew)
    {
        var c = new Counts();
        int ownerAlive = 0, ownerRemaining = 0;
        foreach (var pc in Main.AllAlivePlayerControls)
        {
            if (pc.Is(CustomRoles.PavlovOwner))
            {
                ownerAlive++;
                if (pc.GetRoleClass() is PavlovOwner owner && owner.HasRemainingImprintCount()) ownerRemaining++;
                continue;
            }
            switch (pc.GetCountTypes())
            {
                case CountTypes.Pavlov: c.Pavlov++; break;
                case CountTypes.StandMaster:
                    // キル能力 ON の間はスタンドマスター本人もキル陣営として数える
                    if (!pc.Is(CustomRoles.StandMaster) || StandMaster.EnableKillAbility) c.StandMaster++;
                    break;
                case CountTypes.Eater: c.Eater++; break;
            }
        }
        // 犬がいない間は、刷り込み回数が残っているオーナーだけが続行を止める
        c.Pavlov += PavlovDog.HasAliveDog() ? ownerAlive : ownerRemaining;

        // ジャッカルが全滅していても、ジャッカルの仲間 (たま) が残っていればジャッカル側
        if (jackal == 0 && (CustomRoles.Jackal.IsPresent() || CustomRoles.JackalHadouHo.IsPresent() || CustomRoles.JackalWolf.IsPresent()))
        {
            if (Main.AllAlivePlayerControls.Any(p => p.Is(CustomRoles.Tama)))
            {
                jackal++;
                crew--;
            }
        }
        // 死んだアサシンの会議を待っている間はインポスターが残っているものとする
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.GetRoleClass() is Assassin assassin && !pc.IsAlive()
                && assassin.NowState is Assassin.AssassinMeeting.WaitMeeting or Assassin.AssassinMeeting.CallMetting or Assassin.AssassinMeeting.Guessing)
                imp++;
        }
        return c;
    }

    static bool IsPresent(this CustomRoles role) => Main.AllPlayerControls.Any(p => p.Is(role));

    /// <summary>パブロフ・スタンドマスターの人数勝利</summary>
    public static bool CheckTeamWin(int imp, int jackal, int pirate, int crew, Counts c, ref GameOverReason reason)
    {
        if (imp != 0 || jackal != 0 || pirate != 0 || c.Eater != 0) return false;
        if (c.StandMaster == 0 && c.Pavlov > 0 && crew <= c.Pavlov && PavlovDog.HasAliveDog())
        {
            reason = GameOverReason.ImpostorsByKill;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Pavlov);
            CustomWinnerHolder.WinnerRoles.Add(CustomRoles.PavlovDog);
            CustomWinnerHolder.WinnerRoles.Add(CustomRoles.PavlovOwner);
            CustomWinnerHolder.WinnerRoles.Add(CustomRoles.PavlovDogImprint);
            return true;
        }
        if (c.Pavlov == 0 && Main.AllAlivePlayerControls.Any(p => p.Is(CustomRoles.StandMaster)) && crew <= c.StandMaster)
        {
            reason = GameOverReason.ImpostorsByKill;
            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.StandMaster);
            foreach (var pc in Main.AllPlayerControls.Where(pc => pc.Is(CustomRoles.StandMaster) || pc.Is(CustomRoles.Stand)))
                CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            return true;
        }
        return false;
    }

    /// <summary>勝者が決まった直後の乗っ取り (ゾンビ・陰陽師)</summary>
    public static void TakeOver(ref GameOverReason reason)
    {
        var team = CustomWinnerHolder.WinnerTeam;
        if (team is CustomWinner.Default or CustomWinner.Draw) return;
        if (reason == GameOverReason.ImpostorsBySabotage && team is not CustomWinner.None) return;
        Zombie.TryTakeOverCrewWin(ref reason, team);
        Onmyoji.TryTakeOverCrewWin(ref reason);
    }

    /// <summary>追加勝利の後の単独勝利 (妖狐・マグロ・スペランカー・ゾンビ)</summary>
    public static void LateWin(ref GameOverReason reason, CustomWinner originalTeam)
    {
        if (CustomWinnerHolder.WinnerTeam is CustomWinner.Draw) return;
        if (reason == GameOverReason.ImpostorsBySabotage) return;
        Fox.SFoxCheckWin(ref reason);
        Tuna.CheckWin(ref reason);
        Spelunker.CheckWin(ref reason);
        Zombie.TryTakeOverCrewWin(ref reason, originalTeam);
    }
}
