using AmongUs.GameOptions;

using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Impostor
{
    public sealed class TimeThief : RoleBase, IMeetingTimeAlterable, IImpostor, IMeetingTimeActive
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
                typeof(TimeThief),
                player => new TimeThief(player),
                CustomRoles.TimeThief,
                () => RoleTypes.Impostor,
                CustomRoleTypes.Impostor,
                (int)Options.offsetId.ImpTOH + 1200,
                SetupOptionItem,
                "タイムシーフ"
            );
        public TimeThief(PlayerControl player)
        : base(
            RoleInfo,
            player
        )
        {
            KillCooldown = OptionKillCooldown.GetFloat();
            DecreaseMeetingTime = OptionDecreaseMeetingTime.GetInt();
            LowerLimitVotingTime = OptionLowerLimitVotingTime.GetInt();
            ReturnStolenTimeUponDeath = OptionReturnStolenTimeUponDeath.GetBool();
            meetingUses = OptionMeetingUses.GetInt();
            meetingStealTime = OptionMeetingStealTime.GetInt();
        }
        private static OptionItem OptionKillCooldown;
        private static OptionItem OptionDecreaseMeetingTime;
        private static OptionItem OptionLowerLimitVotingTime;
        private static OptionItem OptionReturnStolenTimeUponDeath;
        private static OptionItem OptionMeetingUses;
        private static OptionItem OptionMeetingStealTime;
        enum OptionName
        {
            TimeThiefDecreaseMeetingTime,
            TimeThiefLowerLimitVotingTime,
            TimeThiefReturnStolenTimeUponDeath,
            TimeThiefMeetingUses,
            TimeThiefMeetingStealTime,
        }
        /// <summary>会議中に縮めても残す投票の時間 (秒)。縮めた瞬間に締まって、誰も投票できずに終わるのを防ぐ</summary>
        private const int MeetingMinRemaining = 10;
        /// <summary>会議中に縮められる残りの回数 (試合を通して)</summary>
        private int meetingUses;
        private int meetingStealTime;
        public static float KillCooldown;
        public static int DecreaseMeetingTime;
        public static int LowerLimitVotingTime;
        public static bool ReturnStolenTimeUponDeath;

        public bool RevertOnDie => ReturnStolenTimeUponDeath;

        private static void SetupOptionItem()
        {
            OptionKillCooldown = FloatOptionItem.Create(RoleInfo, GeneralOption.KillCooldown, new(2.5f, 180f, 2.5f), 30f, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionDecreaseMeetingTime = IntegerOptionItem.Create(RoleInfo, OptionName.TimeThiefDecreaseMeetingTime, new(0, 100, 1), 20, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionLowerLimitVotingTime = IntegerOptionItem.Create(RoleInfo, OptionName.TimeThiefLowerLimitVotingTime, new(1, 300, 1), 10, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionReturnStolenTimeUponDeath = BooleanOptionItem.Create(RoleInfo, OptionName.TimeThiefReturnStolenTimeUponDeath, true, false);
            OptionMeetingUses = IntegerOptionItem.Create(RoleInfo, OptionName.TimeThiefMeetingUses, new(0, 10, 1), 1, false)
                .SetValueFormat(OptionFormat.Times);
            OptionMeetingStealTime = IntegerOptionItem.Create(RoleInfo, OptionName.TimeThiefMeetingStealTime, new(5, 120, 5), 30, false, OptionMeetingUses)
                .SetValueFormat(OptionFormat.Seconds);
        }

        // 会議中の短縮: 自分への投票 (票は取り消して、改めて投票できる) か /time
        public void UseMeetingTimeAbility()
            => MeetingTimeManager.TryRoleAdjust(Player, ref meetingUses, -meetingStealTime, minTotal: LowerLimitVotingTime, minRemaining: MeetingMinRemaining);

        public override bool CheckAnyVote(byte votedForId, PlayerControl voter)
        {
            if (!Is(voter) || votedForId != Player.PlayerId || meetingUses <= 0 || !Player.IsAlive()) return true;
            UseMeetingTimeAbility();
            return false;
        }
        public float CalculateKillCooldown() => KillCooldown;
        public int CalculateMeetingTimeDelta()
        {
            var sec = -(DecreaseMeetingTime * MyState.GetKillCount(true));
            return sec;
        }
        public override string GetProgressText(bool comms = false)
        {
            var time = CalculateMeetingTimeDelta();
            var text = time < 0 ? $"{time}s" : "";
            if (OptionMeetingUses.GetInt() > 0) text += $"({meetingUses})";
            return text.Length > 0 ? Utils.ColorString(Palette.ImpostorRed.ShadeColor(0.5f), text) : "";
        }
    }
}