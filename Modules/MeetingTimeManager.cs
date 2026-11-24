using System;
using System.Linq;
using AmongUs.GameOptions;

using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Crewmate;

namespace TownOfHostL.Modules
{
    public partial class MeetingTimeManager
    {
        private static int DiscussionTime;
        private static int VotingTime;
        private static int DefaultDiscussionTime;
        private static int DefaultVotingTime;

        /// <summary>話し合いを飛ばして、投票の時間だけにする (天秤の会議)</summary>
        public static void VotingOnly(int time)
        {
            DiscussionTime = 0;
            VotingTime = time;
        }

        [GameModuleInitializer]
        public static void Init()
        {
            DefaultDiscussionTime = Main.RealOptionsData.GetInt(Int32OptionNames.DiscussionTime);
            DefaultVotingTime = Main.RealOptionsData.GetInt(Int32OptionNames.VotingTime);
            Logger.Info($"DefaultDiscussionTime:{DefaultDiscussionTime}, DefaultVotingTime{DefaultVotingTime}", "MeetingTimeManager.Init");
            ResetMeetingTime();
        }
        public static void ApplyGameOptions(IGameOptions opt)
        {
            opt.SetInt(Int32OptionNames.DiscussionTime, DiscussionTime);
            opt.SetInt(Int32OptionNames.VotingTime, VotingTime);
        }
        private static void ResetMeetingTime()
        {
            DiscussionTime = DefaultDiscussionTime;
            VotingTime = DefaultVotingTime;
        }
        public static void OnReportDeadBody()
        {
            // 天秤会議とアサシン会議は、それぞれの役職の設定の投票時間だけにする
            if (Roles.Crewmate.Balancer.Id != byte.MaxValue
                && Roles.Crewmate.Balancer.target1 != byte.MaxValue
                && Roles.Crewmate.Balancer.target2 != byte.MaxValue)
            {
                // 天秤会議が開いたので、会議明けの処理を止めておく予約を外す
                Roles.Crewmate.Balancer.Scheduled = false;
                VotingOnly(Roles.Crewmate.Balancer.meetingtime);
                return;
            }
            if (Roles.Impostor.Assassin.assassin?.NowState is Roles.Impostor.Assassin.AssassinMeeting.Guessing
                or Roles.Impostor.Assassin.AssassinMeeting.CallMetting)
            {
                VotingOnly(Roles.Impostor.Assassin.OptionAssassinMeetingTime.GetInt());
                return;
            }
            if (Options.AllAliveMeeting.GetBool() && Utils.IsAllAlive)
            {
                DiscussionTime = 0;
                VotingTime = Options.AllAliveMeetingTime.GetInt();
                Logger.Info($"DiscussionTime:{DiscussionTime}, VotingTime{VotingTime}", "MeetingTimeManager.OnReportDeadBody");
                return;
            }

            ResetMeetingTime();
            int BonusMeetingTime = 0;
            int MeetingTimeMin = 0;
            int MeetingTimeMax = 300;
            HamoMeeting.ApplyLimits(ref MeetingTimeMin, ref MeetingTimeMax);

            foreach (var role in CustomRoleManager.AllActiveRoles.Values.ToArray())
            {
                if (role is IMeetingTimeAlterable meetingTimeAlterable)
                {
                    if (role is TimeThief)
                    {
                        // Hyz-sui: 会議時間をいじる役職が増えたら上限&下限設定の置き場所要検討
                        MeetingTimeMin = TimeThief.LowerLimitVotingTime;
                    }

                    if (role is TimeManager)
                    {
                        MeetingTimeMax = TimeManager.MeetingTimeLimit;
                    }

                    if (!role.Player.IsAlive() && meetingTimeAlterable.RevertOnDie)
                    {
                        continue;
                    }

                    var time = meetingTimeAlterable.CalculateMeetingTimeDelta();
                    Logger.Info($"会議時間-{role.Player.GetNameWithRole()}: {time} s", "MeetingTimeManager.OnReportDeadBody");
                    BonusMeetingTime += time;
                }
            }

            int TotalMeetingTime = DiscussionTime + VotingTime;
            //時間の下限、上限で刈り込み (下限の役職と上限の役職の設定が逆転していたら下限に合わせる。Clamp は min > max で例外を出す)
            MeetingTimeMax = Math.Max(MeetingTimeMax, MeetingTimeMin);
            BonusMeetingTime = Math.Clamp(TotalMeetingTime + BonusMeetingTime, MeetingTimeMin, MeetingTimeMax) - TotalMeetingTime;
            if (BonusMeetingTime >= 0)
                VotingTime += BonusMeetingTime; //投票時間を延長
            else
            {
                DiscussionTime += BonusMeetingTime; //会議時間を優先的に短縮
                if (DiscussionTime < 0) //会議時間だけでは賄えない場合
                {
                    VotingTime += DiscussionTime; //足りない分投票時間を短縮
                    DiscussionTime = 0;
                }
            }
            Logger.Info($"DiscussionTime:{DiscussionTime}, VotingTime{VotingTime}", "MeetingTimeManager.OnReportDeadBody");
        }
    }
}