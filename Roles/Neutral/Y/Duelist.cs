using System.Collections.Generic;
using AmongUs.GameOptions;

using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Neutral;
public sealed class Duelist : RoleBase, IAdditionalWinner, IAdditionalWinGranter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Duelist),
            player => new Duelist(player),
            CustomRoles.Duelist,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.NeuY + 700,
            null,
            "決闘者",
            "#ff6347"
        );
    public Duelist(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Holdings.Subscribe(CustomRoleManager.MarkOthers, GetMarkOthers);
        Duelists.Add(this);
        Archenemy = null;
    }
    public override void OnDestroy()
    {
        Duelists.Remove(this);
    }

    private static HashSet<Duelist> Duelists = new(15);
    PlayerControl Archenemy;
    /// <summary>宿敵を選ぶ会議か (まだ選んでいない・能力を使える会議。初手強制会議で能力を使えない時は次の会議)</summary>
    bool ChoosingNow => Archenemy == null && !FirstTurnMeeting.CantAbilityNow;
    // この会議が宿敵を選ぶ会議だった
    bool hadChance;
    public override void OnStartMeeting() => hadChance = Player.IsAlive() && ChoosingNow;
    public override void AfterMeetingTasks()
    {
        // 選ぶ会議で誰も選ばなかった (投票しなかった) ら自滅する
        if (hadChance && Archenemy == null && Player.IsAlive())
        {
            MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Suicide, Player.PlayerId);
            Logger.Info($"宿敵を選ばなかったので {Player.GetNameWithRole()} は自滅", "Duelist");
        }
        hadChance = false;
    }

    public override void Add()
    {

    }
    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        // 既定値
        var (votedForId, numVotes, doVote) = base.ModifyVote(voterId, sourceVotedForId, isIntentional);
        if (ChoosingNow && voterId == Player.PlayerId && Player.IsAlive())
        {
            if (sourceVotedForId != Player.PlayerId && sourceVotedForId < 253)
            {
                numVotes = 0;//投票を見えなくする
                var VotedForPC = Utils.GetPlayerById(sourceVotedForId);
                VotedForPC.RpcSetCustomRole(CustomRoles.Archenemy);
                Archenemy = VotedForPC;
                Utils.NotifyRoles();
            }
            else
            {
                MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Suicide, Player.PlayerId);
            }
        }
        return (votedForId, numVotes, doVote);
    }

    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        if (!ChoosingNow || !isForMeeting || !Player.IsAlive()) return string.Empty;

        //seenが省略の場合seer
        seen ??= seer;
        //seeおよびseenが自分である場合以外は関係なし
        if (!Is(seer) || !Is(seen)) return "";

        return Translator.GetString("DuelistVote").Color(RoleInfo.RoleColor);
    }
    public override string GetMark(PlayerControl seer, PlayerControl seen, bool _ = false)
    {
        //seenが省略の場合seer
        seen ??= seer;

        if (seer == Player && seen == Archenemy)
            return Utils.ColorString(RoleInfo.RoleColor, "χ");
        return string.Empty;
    }
    public string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;

        if (seer == Archenemy && seen == Player)
            return Utils.ColorString(RoleInfo.RoleColor, "χ");
        return string.Empty;
    }

    public bool CheckWin(ref CustomRoles winnerRole)
    {
        // 宿敵を決める前 (最初の会議の前) に終わった時は、宿敵が「死んでいる」扱いにならないよう勝たない
        return Archenemy != null && Player.IsAlive() && !Archenemy.IsAlive();
    }

    /// <summary>自分が死んで宿敵が生きていれば、宿敵が追加勝利する</summary>
    public void GrantAdditionalWins()
    {
        if (Player.IsAlive() || !Archenemy.IsAlive()) return;
        CustomWinnerHolder.WinnerIds.Add(Archenemy.PlayerId);
        CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Archenemy);
    }
    public static bool CheckNotify(PlayerControl pc)
    {
        foreach (var duelist in Duelists)
        {
            if (pc == duelist.Archenemy || pc == duelist.Player) return true;
        }
        return false;
    }
}