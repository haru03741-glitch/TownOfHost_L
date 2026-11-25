using AmongUs.GameOptions;

using TownOfHostL.Modules;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;

namespace TownOfHostL.Roles.Crewmate
{
    public sealed class TimeManager : RoleBase, IMeetingTimeAlterable, IMeetingTimeActive
    {
        public static readonly SimpleRoleInfo RoleInfo =
            SimpleRoleInfo.Create(
                typeof(TimeManager),
                player => new TimeManager(player),
                CustomRoles.TimeManager,
                () => RoleTypes.Crewmate,
                CustomRoleTypes.Crewmate,
                (int)Options.offsetId.CrewTOH + 1100,
                SetupOptionItem,
                "タイムマネージャー",
                "#6495ed"
            );
        public TimeManager(PlayerControl player)
        : base(
            RoleInfo,
            player
        )
        {
            IncreaseMeetingTime = OptionIncreaseMeetingTime.GetInt();
            MeetingTimeLimit = OptionMeetingTimeLimit.GetInt();
            meetingUses = OptionMeetingUses.GetInt();
            meetingAddTime = OptionMeetingAddTime.GetInt();
        }
        private static OptionItem OptionIncreaseMeetingTime;
        private static OptionItem OptionMeetingTimeLimit;
        private static OptionItem OptionMeetingUses;
        private static OptionItem OptionMeetingAddTime;
        enum OptionName
        {
            TimeManagerIncreaseMeetingTime,
            TimeManagerLimitMeetingTime,
            TimeManagerMeetingUses,
            TimeManagerMeetingAddTime,
        }
        /// <summary>会議中に延ばせる残りの回数 (試合を通して)</summary>
        private int meetingUses;
        private int meetingAddTime;
        public static int IncreaseMeetingTime;
        public static int MeetingTimeLimit;

        public bool RevertOnDie => true;

        private static void SetupOptionItem()
        {
            OptionIncreaseMeetingTime = IntegerOptionItem.Create(RoleInfo, OptionName.TimeManagerIncreaseMeetingTime, new(5, 30, 1), 15, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionMeetingTimeLimit = IntegerOptionItem.Create(RoleInfo, OptionName.TimeManagerLimitMeetingTime, new(200, 900, 10), 300, false)
                .SetValueFormat(OptionFormat.Seconds);
            OptionMeetingUses = IntegerOptionItem.Create(RoleInfo, OptionName.TimeManagerMeetingUses, new(0, 10, 1), 1, false)
                .SetValueFormat(OptionFormat.Times);
            OptionMeetingAddTime = IntegerOptionItem.Create(RoleInfo, OptionName.TimeManagerMeetingAddTime, new(5, 120, 5), 30, false, OptionMeetingUses)
                .SetValueFormat(OptionFormat.Seconds);
        }

        // 会議中の延長: 自分への投票 (票は取り消して、改めて投票できる) か /time
        public void UseMeetingTimeAbility()
            => MeetingTimeManager.TryRoleAdjust(Player, ref meetingUses, meetingAddTime, maxTotal: MeetingTimeLimit);

        public override bool CheckAnyVote(byte votedForId, PlayerControl voter)
        {
            if (!Is(voter) || votedForId != Player.PlayerId || meetingUses <= 0 || !Player.IsAlive()) return true;
            UseMeetingTimeAbility();
            return false;
        }

        public int CalculateMeetingTimeDelta()
        {
            var sec = IncreaseMeetingTime * MyTaskState.CompletedTasksCount;
            return sec;
        }
        public override string GetProgressText(bool comms = false)
        {
            var time = CalculateMeetingTimeDelta();
            var text = time > 0 ? $"+{time}s" : "";
            if (OptionMeetingUses.GetInt() > 0) text += $"({meetingUses})";
            return text.Length > 0 ? Utils.ColorString(Utils.GetRoleColor(CustomRoles.TimeManager).ShadeColor(0.5f), text) : "";
        }
    }
}