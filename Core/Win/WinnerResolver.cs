using System;

using TownOfHostL.Roles.AddOns;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Neutral;

namespace TownOfHostL.Core.Win;

/// <summary>
/// 試合の終わりが決まった後に、誰が勝つかを決める。
/// 段の順: 基本の勝者 → 単独勝利の上書き → 勝者が空なら詰め直し → 追加勝利 → 後から決める勝ち方 → 確定敗北。
/// 全滅 (None) と廃村 (Draw) では基本の勝者より後の段を飛ばす。
/// 役職の勝ち方は役職のクラス (ISoloWinner・IAdditionalWinner・IAdditionalWinGranter・ILateWinner・WinsWithBaseTeam)、
/// 属性の勝ち方は AddOnInfo.Win と印 (NoTeamWin・NeverWins) で宣言し、ここでは役職の名前を並べない。
/// </summary>
public static class WinnerResolver
{
    public sealed class Context
    {
        /// <summary>終了判定で決まった、上書き前の勝者</summary>
        public readonly CustomWinner Original;
        /// <summary>hamo の後から決める勝ち方 (妖狐など) が書き換えることがある</summary>
        public GameOverReason Reason;
        /// <summary>単独勝利の上書きが 1 つでも起きたか</summary>
        public bool Overridden;
        public Context(CustomWinner original, GameOverReason reason)
        {
            Original = original;
            Reason = reason;
        }
    }

    private static readonly SoloWinTier[] SoloWinTiers = (SoloWinTier[])Enum.GetValues(typeof(SoloWinTier));

    /// <returns>終わり方 (hamo の役職が書き換えた時はその値)</returns>
    public static GameOverReason Resolve(GameOverReason reason)
    {
        var ctx = new Context(CustomWinnerHolder.WinnerTeam, reason);

        AddBaseTeamWinners(ctx.Original);
        if (ctx.Original is CustomWinner.Draw or CustomWinner.None) return ctx.Reason;

        foreach (var tier in SoloWinTiers) ApplySoloWins(ctx, tier);

        // 上書きが無く、勝者のチームはそのままで、誰も入っていない時だけ詰め直す
        if (!ctx.Overridden && CustomWinnerHolder.WinnerTeam == ctx.Original && CustomWinnerHolder.WinnerIds.Count == 0)
            AddBaseTeamWinners(ctx.Original);

        // hamo: 単独勝利・乗っ取り (妖狐・マグロ・スペランカー・ゾンビ)
        HamoGameEnd.LateWin(ref ctx.Reason, ctx.Original);

        foreach (var pc in Main.AllPlayerControls) AddAdditionalWinners(pc);
        // 抜けた人の役職も拾うので、部屋にいる人ではなく役職の一覧から回す
        foreach (var role in CustomRoleManager.AllActiveRoles.Values)
            if (role is IAdditionalWinGranter granter) granter.GrantAdditionalWins();

        foreach (var pc in Main.AllPlayerControls)
            if (pc.GetRoleClass() is ILateWinner lateWinner) lateWinner.CheckLateWin();

        foreach (var pc in Main.AllPlayerControls)
        {
            if (!PlayerTraits.NeverWins(pc)) continue;
            CustomWinnerHolder.WinnerIds.Remove(pc.PlayerId);
            CustomWinnerHolder.CantWinPlayerIds.Add(pc.PlayerId);
        }

        // hamo: 勝利処理がほぼ終わった後の役職ごとの処理
        foreach (var pc in Main.AllPlayerControls)
            (pc.GetRoleClass() as HamoRoleBase)?.CheckWinner(ctx.Reason);
        return ctx.Reason;
    }

    /// <summary>クルー勝利・インポスター勝利の時に、その陣営の人を勝者に入れる。</summary>
    private static void AddBaseTeamWinners(CustomWinner team)
    {
        switch (team)
        {
            case CustomWinner.Crewmate:
                // 切断した人も入れる
                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    if (pc.Is(CustomRoleTypes.Crewmate) && PlayerTraits.WinsWithBaseTeam(pc))
                        CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
                }
                break;
            case CustomWinner.Impostor:
                // エゴイストが勝つ時は、CheckWin の中で勝者がエゴイストに置き換わる
                if (Egoist.CheckWin()) break;
                foreach (var pc in Main.AllPlayerControls)
                {
                    if ((pc.Is(CustomRoleTypes.Impostor) || pc.Is(CustomRoleTypes.Madmate)) && PlayerTraits.WinsWithBaseTeam(pc))
                        CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
                }
                break;
        }
    }

    /// <summary>1 つの段の単独勝利。最初に当たった人が勝者をリセットし、2 人目からは勝者に足す (チームは後の人の物になる)。</summary>
    private static void ApplySoloWins(Context ctx, SoloWinTier tier)
    {
        var triggered = false;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.GetRoleClass() is ISoloWinner soloWinner && soloWinner.SoloWinTier == tier
                && soloWinner.CheckSoloWin(ctx, out var winner))
                AddSoloWinner(ctx, pc, winner, ref triggered);

            foreach (var subRole in pc.GetCustomSubRoles())
            {
                var win = AddOnList.Get(subRole)?.Win;
                if (win?.CheckSoloWin != null && win.SoloWinTier == tier && win.CheckSoloWin(ctx, pc))
                    AddSoloWinner(ctx, pc, win.SoloWinner, ref triggered);
            }
        }
    }

    private static void AddSoloWinner(Context ctx, PlayerControl pc, CustomWinner winner, ref bool triggered)
    {
        if (!triggered)
        {
            CustomWinnerHolder.ResetAndSetWinner(winner);
            ctx.Overridden = true;
            triggered = true;
        }
        else
        {
            CustomWinnerHolder.WinnerTeam = winner;
        }
        CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
    }

    private static void AddAdditionalWinners(PlayerControl pc)
    {
        foreach (var subRole in pc.GetCustomSubRoles())
        {
            var win = AddOnList.Get(subRole)?.Win;
            if (win?.CheckAdditionalWin == null || !win.CheckAdditionalWin(pc)) continue;
            CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
            CustomWinnerHolder.AdditionalWinnerRoles.Add(subRole);
        }

        if (pc.GetRoleClass() is IAdditionalWinner additionalWinner)
        {
            var winnerRole = pc.GetCustomRole();
            if (additionalWinner.CheckWin(ref winnerRole))
            {
                CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
                CustomWinnerHolder.AdditionalWinnerRoles.Add(winnerRole);
            }
        }
    }
}
