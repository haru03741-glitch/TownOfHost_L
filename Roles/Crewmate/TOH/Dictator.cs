using AmongUs.GameOptions;

using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Crewmate;
public sealed class Dictator : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Dictator),
            player => new Dictator(player),
            CustomRoles.Dictator,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.CrewTOH + 900,
            null,
            "ディクテーター",
            "#df9b00"
        );
    public Dictator(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    { }
    // 自投票で能力のモードに入り、次に投票した相手に能力を使う (モードでない時の投票はふつうの投票)
    bool abilityArmed;
    public override bool CheckAnyVote(byte votedForId, PlayerControl voter)
    {
        if (!Is(voter)) return true;
        abilityArmed = false;
        if (!(Player.IsAlive()) || !SelfVoteManager.Canuseability()) return true;
        var pick = SelfVoteManager.SelfVotePick(Player, votedForId, "Mode.Dictator", "Vote.Dictator");
        if (pick == SelfVoteManager.PickResult.Cancel) return false;
        abilityArmed = pick == SelfVoteManager.PickResult.Pick;
        return true;
    }
    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        var (votedForId, numVotes, doVote) = base.ModifyVote(voterId, sourceVotedForId, isIntentional);
        var baseVote = (votedForId, numVotes, doVote);
        if (voterId == Player.PlayerId)
        {
            var armed = abilityArmed;
            abilityArmed = false;
            if (!armed) return baseVote;
        }
        if (!isIntentional || voterId != Player.PlayerId || sourceVotedForId == Player.PlayerId || sourceVotedForId >= 253 || !Player.IsAlive())
        {
            return baseVote;
        }
        MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Suicide, Player.PlayerId);
        Utils.GetPlayerById(sourceVotedForId).SetRealKiller(Player);
        MeetingVoteManager.Instance.ClearAndExile(Player.PlayerId, sourceVotedForId);
        return (votedForId, numVotes, false);
    }

    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        if (!isForMeeting || !Player.IsAlive()) return string.Empty;

        //seenが省略の場合seer
        seen ??= seer;
        //seeおよびseenが自分である場合以外は関係なし
        if (!Is(seer) || !Is(seen)) return "";

        return Translator.GetString("DictatorVote").Color(RoleInfo.RoleColor);
    }
}
