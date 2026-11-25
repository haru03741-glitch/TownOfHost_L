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
        /// <summary>今の会議に配っている話し合いの時間 (秒)。会議中に時間を変えた直後も、設定を送り直す前の古い値を読まないよう、締めの判定や残り時間の表示はこちらを見る</summary>
        public static int CurrentDiscussionTime => DiscussionTime;
        /// <summary>今の会議に配っている投票の時間 (秒・0 は無制限)</summary>
        public static int CurrentVotingTime => VotingTime;

        /// <summary>
        /// 会議の途中で残り時間を増減する。客の会議の時計は毎フレーム受け取った設定の時間を読むので、
        /// 設定を送り直せば Mod の無い客の画面の残りも変わる。締めるのはホストの時計 (ホストの設定も同じ値に替える)。
        /// 投票の時間だけを動かす (話し合いの時間を動かすと、話し合いから投票へ移る時刻がずれる)。
        /// 縮めて残りが無くなった時は、ホストの時計が次のフレームで締める。
        /// </summary>
        /// <returns>実際に動いた秒数</returns>
        public static int AdjustDuringMeeting(int deltaSeconds)
        {
            if (!AmongUsClient.Instance.AmHost || deltaSeconds == 0) return 0;
            var meetingHud = MeetingHud.Instance;
            if (meetingHud == null || GameStates.ExiledAnimate) return 0;
            // 無制限の会議は延ばす意味が無く、縮めると時間切れが生まれて意味が変わるので触らない
            if (VotingTime <= 0) return 0;

            float elapsedVoting = meetingHud.discussionTimer - DiscussionTime;
            // 0 は無制限の意味になるので 1 秒より下げない。経過より短くすれば、ホストの時計がすぐ締める
            int floor = Math.Max(1, (int)Math.Ceiling(Math.Max(0f, elapsedVoting)));
            int next = Math.Clamp(VotingTime + deltaSeconds, Math.Min(floor, VotingTime), 3600);
            int moved = next - VotingTime;
            if (moved == 0) return 0;
            VotingTime = next;
            Logger.Info($"会議中に時間を {moved:+#;-#;0} 秒: DiscussionTime:{DiscussionTime}, VotingTime:{VotingTime}, 経過:{meetingHud.discussionTimer:0.0}", "MeetingTimeManager.AdjustDuringMeeting");
            // 1 人 1 本 (0.2 秒ごとのまとめ送りに乗せる)。ホスト自身の分は手元の LogicOptions に入る
            PlayerGameOptionsSender.SetDirtyToAll();
            return moved;
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