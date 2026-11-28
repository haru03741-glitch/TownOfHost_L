using AmongUs.GameOptions;

using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Roles.Madmate;

public sealed class MadDictator : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(MadDictator),
            player => new MadDictator(player),
            CustomRoles.MadDictator,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            (int)Options.offsetId.MadY + 0,
            SetupOptionItem,
            "マッドディクテーター",
            introSound: () => GetIntroSound(RoleTypes.Impostor)
        );
    public MadDictator(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        canVent = OptionCanVent.GetBool();
    }

    private static OptionItem OptionCanVent;
    private static bool canVent;

    private static void SetupOptionItem()
    {
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, GeneralOption.CanVent, false, false);
        Options.SetUpAddOnOptions(RoleInfo.RoleName, RoleInfo.Tab);
    }
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
        // 既定値
        var (votedForId, numVotes, doVote) = base.ModifyVote(voterId, sourceVotedForId, isIntentional);
        var baseVote = (votedForId, numVotes, doVote);
        if (voterId == Player.PlayerId)
        {
            var armed = abilityArmed;
            abilityArmed = false;
            if (!armed) return baseVote;
        }
        //死んでいないディクテーターが投票済み
        // 本人が選んでいない票 (天秤会議の強制の票など) では能力を使わない
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