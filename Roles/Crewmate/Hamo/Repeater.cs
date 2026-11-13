using AmongUs.GameOptions;

using TownOfHostL.Patches;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Modules.SelfVoteManager;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Crewmate;

// ===== リピーター (Repeater) =====
// イントロ：ただしい結果にしよう
// 陣営：クルーメイト / 置き換え：クルーメイト
//
// 会議中に自投票で能力を使用する。
// 能力を使用したターンの投票結果は強制スキップになる(誰も追放されない)。
// そのターンの会議が終わったあと、すぐもう一度会議を始める。
public sealed class Repeater : RoleBase, ISelfVoter
{
    // 役職の元: TownOfHost-hamo
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Repeater),
            player => new Repeater(player),
            CustomRoles.Repeater,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 96600,
            SetupOptionItem,
            "Rpt",
            "#8CFF19"
        );

    public Repeater(PlayerControl player) : base(RoleInfo, player)
    {
        taskCountToUnlock = OptionTaskCountToUnlock.GetInt();
        maxUseCount = OptionUseCount.GetInt();
        extendMeetingSeconds = OptionExtendSeconds.GetInt();
        additionalVotes = OptionAdditionalVotes.GetInt();
        revealJudgement = OptionRevealJudgement.GetBool();

        remainingUses = maxUseCount;
        isArmedThisMeeting = false;
    }

    static OptionItem OptionTaskCountToUnlock;
    static OptionItem OptionUseCount;
    static OptionItem OptionExtendSeconds;
    static OptionItem OptionAdditionalVotes;
    static OptionItem OptionRevealJudgement;

    enum OptionName
    {
        RepeaterTaskCountToUnlock,
        RepeaterUseCount,
        RepeaterExtendSeconds,
        RepeaterAdditionalVotes,
        RepeaterRevealJudgement,
    }

    private readonly int taskCountToUnlock;
    private readonly int maxUseCount;
    private readonly int extendMeetingSeconds;
    private readonly int additionalVotes;
    private readonly bool revealJudgement;

    private int remainingUses;
    private bool isArmedThisMeeting;

    static void SetupOptionItem()
    {
        OptionTaskCountToUnlock = IntegerOptionItem.Create(RoleInfo, OptionName.RepeaterTaskCountToUnlock,
            new(0, 255, 1), 5, false)
            .SetValueFormat(OptionFormat.Pieces);
        OptionUseCount = IntegerOptionItem.Create(RoleInfo, OptionName.RepeaterUseCount,
            new(1, 100, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionExtendSeconds = IntegerOptionItem.Create(RoleInfo, OptionName.RepeaterExtendSeconds,
            new(10, 300, 5), 30, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAdditionalVotes = IntegerOptionItem.Create(RoleInfo, OptionName.RepeaterAdditionalVotes,
            new(0, 999, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionRevealJudgement = BooleanOptionItem.Create(RoleInfo, OptionName.RepeaterRevealJudgement, true, false);
    }

    private bool CanUseAbility()
        => Player.IsAlive() && remainingUses > 0
        && (taskCountToUnlock <= 0 || MyTaskState.CompletedTasksCount >= taskCountToUnlock);

    public override bool CheckAnyVote(byte votedForId, PlayerControl voter)
    {
        if (!Is(voter) || !Canuseability() || !CanUseAbility()) return true;

        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            // 同じ会議で 2 回自投票しても、回数は 1 回分だけ使う (2 回目は票を取り消すだけ)
            if (status is VoteStatus.Self && isArmedThisMeeting)
            {
                SetMode(Player, false);
                return false;
            }
            if (status is VoteStatus.Self)
            {
                isArmedThisMeeting = true;
                remainingUses--;
                Utils.SendMessage(GetString("Repeater.Armed"), Player.PlayerId);

                if (revealJudgement)
                {
                    Utils.SendMessage(string.Format(GetString("Repeater.JudgementRevealed"), Player.ColoredName()));
                }

                Utils.AddGameLog("Repeater",
                    $"{Player.ColoredName()}が能力を発動した");

                SetMode(Player, false);
                return false;
            }
        }

        return true;
    }

    public override void OnStartMeeting()
    {
        isArmedThisMeeting = false;
    }

    /// <summary>能力を発動したターンのみ、自分の投票の重みを追加投票数分だけ増やす。</summary>
    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        if (voterId != Player.PlayerId || !isArmedThisMeeting || additionalVotes <= 0)
            return (null, null, true);

        return (null, 1 + additionalVotes, true);
    }

    /// <summary>
    /// 投票集計直後(WrapUpより前)のフック。
    /// 能力発動時は投票結果を強制的にスキップ扱い(誰も追放しない)に上書きし、
    /// このタイミングで「もう一度会議を始める」処理を予約する。
    /// 【重要】もう一度会議を開く処理は、以前はOnExileWrapUp(実際に誰かが
    /// 追放された場合のみ呼ばれるフック)に置いていたが、強制スキップ化により
    /// 誰も追放されなくなったため、OnExileWrapUpだと一切呼ばれなくなってしまう。
    /// 投票結果が確定するVotingResults側に処理を移し、追放の有無によらず
    /// 必ず「もう一度会議を始める」動作が起きるようにする。
    /// </summary>
    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie, System.Collections.Generic.Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)
    {
        if (!isArmedThisMeeting) return false;

        // 初手会議と同様の「強制スキップ」扱いにする。誰も追放されない。
        Exiled = null;
        IsTie = false;

        Utils.SendMessage(GetString("Repeater.ForceSkip"));

        Utils.AddGameLog("Repeater",
            $"{Player.ColoredName()}の能力により投票結果が強制スキップになった");

        // 会議が終わった直後、設定秒数を挟んでもう一度緊急会議を開く(リピーターが押したものとして扱う)。
        // 自然にもう一度会議が始まる演出のため、自身の残りボタン数は消費しない。
        var self = Player;
        _ = new LateTask(() =>
        {
            if (self == null || !self.IsAlive()) return;
            var state = PlayerState.GetByPlayerId(self.PlayerId);
            var buttonsBefore = state?.NumberOfRemainingButtons ?? -1;

            ReportDeadBodyPatch.ExReportDeadBody(self, null, false);

            if (state != null && buttonsBefore >= 0)
            {
                state.NumberOfRemainingButtons = buttonsBefore;
            }
        }, (float)extendMeetingSeconds, "Repeater.ReOpenMeeting");

        return true;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!isForMeeting || !Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        var prefix = isForHud ? "" : "<size=60%>";
        var c = RoleInfo.RoleColorCode;

        if (!CanUseAbility() && remainingUses <= 0)
            return $"{prefix}<color={c}>能力: 使用回数なし</color>";

        if (taskCountToUnlock > 0 && MyTaskState.CompletedTasksCount < taskCountToUnlock)
            return $"{prefix}<color={c}>タスクを進めると能力が解放されます</color>";

        var stateText = isArmedThisMeeting ? "発動済み(この会議)" : "会議自投票→発動";
        return $"{prefix}<color={c}>{stateText}(残り{remainingUses}回)</color>";
    }

}
