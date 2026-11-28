using AmongUs.GameOptions;

using System;

using System.Linq;

using System.Collections.Generic;



using TownOfHostL.Roles.Core;



using static TownOfHostL.Modules.SelfVoteManager;

using static TownOfHostL.Modules.MeetingVoteManager;

using static TownOfHostL.Modules.MeetingTimeManager;


using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;



namespace TownOfHostL.Roles.Crewmate;



public sealed class Balancer : RoleBase, ISelfVoter

{

    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(Balancer),
            player => new Balancer(player),
            CustomRoles.Balancer,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 30700,
            SetupOptionItem,
            "bal",
            "#cff100",
            introSound: () => GetIntroSound(RoleTypes.Crewmate)
        );

    public Balancer(PlayerControl player)

    : base(

        RoleInfo,

        player

    )

    {

        meetingtime = OptionMeetingTime.GetInt();

        CanUseAllAlive = OptionCanUseAllAlive.GetBool();

        target1 = 255;

        target2 = 255;

        Target1 = 255;

        Target2 = 255;

        used = false;

        Id = 255;

        nickname = null;

        Holdings.Subscribe(CustomRoleManager.MarkOthers, OtherMark);

    }



    static OptionItem OptionMeetingTime;

    static OptionItem OptionCanUseAllAlive;

    public static OptionItem OptionCanMeetingAbility;

    //共有用

    public static byte target1 = 255, target2 = 255;

    public static byte Id = 255;

    public static int meetingtime;

    static string nickname;

    static bool CanUseAllAlive; //誰かが死亡するまで、能力を使えない

    //プレイヤーによって操作できる

    byte Target1, Target2;
    // 天秤会議の演出で札を光らせる 2 人
    public static byte[] MeetingTargets = System.Array.Empty<byte>();

    bool used;



    enum Option

    {

        BalancerMeetingTime,

        BalancerCanUseAllAlive,

        BalancerCanUseMeetingAbility

    }



    private static void SetupOptionItem()

    {

        OptionMeetingTime = IntegerOptionItem.Create(RoleInfo, Option.BalancerMeetingTime, new(15, 120, 1), 30, false)

            .SetValueFormat(OptionFormat.Seconds);

        OptionCanUseAllAlive = BooleanOptionItem.Create(RoleInfo, Option.BalancerCanUseAllAlive, false, false);

        OptionCanMeetingAbility = BooleanOptionItem.Create(RoleInfo, Option.BalancerCanUseMeetingAbility, false, false);

        SaruWarningOption.AddTo(RoleInfo);

    }



    /// <summary>
    /// 天秤の名前を戻す。素の本名でなく、他人の名前として送る形 (変身先の名前・通信妨害のカモフラ中は隠す) で戻す。
    /// 抜けた人は戻さない
    /// </summary>
    private static void RestoreName(byte id)
    {
        var pc = Utils.GetPlayerById(id);
        if (pc == null || pc.Data == null) return;
        // 試合の外では手元の名前が天秤の見出しのままなので、控えの本名で戻す
        var name = GameStates.IsInGame ? TownOfHostL.Core.ChatSenderName.RestoreFor(pc)
            : Main.AllPlayerNames.TryGetValue(id, out var realName) ? realName : null;
        if (name != null) pc.RpcSetName(name);
    }
    public override void OnDestroy()

    {

        Id = byte.MaxValue;

        target1 = byte.MaxValue;

        target2 = byte.MaxValue;



        if (AmongUsClient.Instance.AmHost)

        {

            if (Target1 != byte.MaxValue && Target2 != byte.MaxValue)

            {

                // 抜けた人は戻さない (null のまま呼ぶと、ここで後始末が止まる)
                RestoreName(Target1);

                RestoreName(Target2);

            }

            if (nickname != null)

                Main.nickName = nickname;

            nickname = null;

        }

    }

    bool ISelfVoter.CanUseVoted() => Canuseability() && !used && Id is not 255 && (CanUseAllAlive || GameStates.AlreadyDied);

    public override bool CheckAnyVote(byte votedForId, PlayerControl voter)

    {

        if (!Canuseability()) return true;

        //誰かが天秤を発動していて、自分ではないなら実行しない

        if (Id is not 255 && Id != Player.PlayerId) return true;

        //発動してるなら～

        if (Id is not 255)

        {

            //投票先が天秤のターゲットではないなら投票しない

            if (votedForId == Target1 || votedForId == Target2)

                return true;

            return false;

        }



        //通常会議の処理 投票した人が自分ではない or 能力使用済みならここから先は実行しない

        if (voter.PlayerId != Player.PlayerId || used || (CanUseAllAlive && !GameStates.AlreadyDied))

            return true;



        //天秤モードかチェック

        if (CheckSelfVoteMode(Player, votedForId, out var status))

        {

            if (status is VoteStatus.Self)

            {

                //ターゲットの情報をリセット

                Target1 = 255;

                Target2 = 255;

                Utils.SendMessage(string.Format(GetString("SkillMode"), GetString("Mode.Balancer"), GetString("Vote.Balancer")) + GetString("VoteSkillMode"), Player.PlayerId);

                //正直前のメッセージの方が好きby ky

                //↑ﾏｴﾉｯﾃﾅﾆ..._(:3 」∠)_ byʕ⓿ᴥ⓿ʔ

            }

            if (status is VoteStatus.Skip)

            {

                SetMode(Player, false);

                Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);

            }

            //選ぶ処理

            if (status is VoteStatus.Vote)

            {

                Vote();

            }

            return false;

        }

        else

        {

            if (votedForId == Player.PlayerId && ((Target1 != 255 && Target2 == 255) || (Target1 == 255 && Target2 != 255)))

            {

                Vote();

                return false;

            }

        }

        return true;



        void Vote()

        {

            //1一目が決まってないなら一人目を決める

            if (Target1 == 255)

                Target1 = votedForId;

            //二人目が決まってないなら二人目を決める

            else if (Target2 == 255)

                Target2 = votedForId;



            //同じ人なら二人目をリセット

            if (Target1 == Target2)

                Target2 = 255;



            //プレイヤーの状態を取得

            var p1 = Utils.GetPlayerById(Target1);

            var p2 = Utils.GetPlayerById(Target2);



            //切断or死んでいるならリセット

            if (!p1.IsAlive())

                Target1 = 255;

            if (!p2.IsAlive())

                Target2 = 255;



            //どちらかの情報があるならチャットで伝える

            if (Target1 != 255 || Target2 != 255)

            {

                //どちらかが決まっていなかったら一人目

                var Nowtargetcount = (Target1 != 255 && Target2 != 255) ? GetString("TowPlayer") : GetString("OnePlayer");

                var sendtext = string.Format(GetString("Skill.Balancer"), Nowtargetcount, (Utils.GetPlayerById(votedForId)).ColoredName());

                Utils.SendMessage(sendtext, Player.PlayerId);

            }



            //二人決まったなら会議を終了

            if (Target1 != 255 && Target2 != 255)

            {

                byte[] random_target = [Target1, Target2];

                random_target = [.. random_target.OrderBy(x => Guid.NewGuid())];

                Voteresult = "<color=#cff100>☆" + GetString("BalancerMeeting") + "☆</color>\n" + string.Format(GetString("BalancerMeetingInfo"), (Utils.GetPlayerById(random_target[0])).ColoredName(), (Utils.GetPlayerById(random_target[1])).ColoredName());

                used = true;

                target1 = Target1;

                target2 = Target2;



                ExileControllerWrapUpPatch.AntiBlackout_LastExiled = null;

                _ = new LateTask(() => Instance.ClearAndExiles([Target1, Target2], Skip), 0.3f, "");

            }

        }

    }



    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie, Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)

    {

        //天秤会議開始時は処理スキップ

        if (Id == 255 && Target1 != 255 && Target2 != 255) return true;



        //天秤モードじゃないor自分の天秤じゃないなら実行しない

        if (Id != Player.PlayerId) return false;



        //ディクテーターなどの強制的に会議を終わらせるものなら生存確認の処理スキップ

        if (!ClearAndExile)

        {

            var d1 = Utils.GetPlayerById(Target1);

            var d2 = Utils.GetPlayerById(Target2);



            //二人とも切断or死んでいるなら同数

            if (!d1.IsAlive() && !d2.IsAlive())

            {

                IsTie = true;

                Exiled = null;

                return true;

            }

            IsTie = false;

            //チェック

            if (!d1.IsAlive())

            {

                Exiled = d2.Data;

                vote[d2.PlayerId] = Utils.AllAlivePlayersCount;

                return true;

            }

            if (!d2.IsAlive())

            {

                Exiled = d1.Data;

                vote[d1.PlayerId] = Utils.AllAlivePlayersCount;

                return true;

            }

        }



        var rand = new Random();

        Dictionary<byte, int> data = new(2)

        {

            //セット

            { Target1, 0 },

            { Target2, 0 }

        };



        //投票をカウント、投票してない場合はどちらかに投票させる

        foreach (var voteData in Instance.AllVotes)

        {

            var voted = voteData.Value;



            //死んでたらスキップ

            if (!Utils.GetPlayerById(voted.Voter).IsAlive()) continue;



            //ディクテーターなどの強制的に会議を終わらせるものではないならランダム投票

            if (!voted.HasVoted && !ClearAndExile)

            {

                var id = rand.Next(0, 2) is 0 ? Target1 : Target2;

                Instance.SetVote(voted.Voter, id, isIntentional: false);

                data[id] += 1;

            }

            else if (voted.VotedFor is not NoVote) //投票なし(死亡時、会議強制終了時など)の人はスキップ

            {

                data[voted.VotedFor] += voted.NumVotes;

            }

        }

        //暗転対策の追放リセット

        ExileControllerWrapUpPatch.AntiBlackout_LastExiled = null;



        //ランダムで追放者を決める 同数ならどちらも追放

        var exileId = data.Where(kv => kv.Value == data.Values.Max())

                        .Select(kv => kv.Key)

                        .OrderBy(x => Guid.NewGuid())

                        .FirstOrDefault();

        Exiled = GameData.Instance.GetPlayerById(exileId);

        var ex = Utils.GetPlayerById(exileId);

        //どちらも追放の時?

        if (data.Values.Distinct().Count() == 1)

        {

            Exiled = null;

            //追放画面が出てくるちょい前に名前を変える

            _ = new LateTask(() =>

            {

                //ホストなら別の処理

                if (exileId is 0)

                {

                    nickname = Main.nickName;

                    Main.nickName = GetString("Balancer.Executad") + "<size=0>";

                }

                else

                    ex.RpcSetName(GetString("Balancer.Executad") + "<size=0>");

            }, 4f, "dotiramotuihou☆");

            var toExile = data.Keys.ToArray();

            bool Iscrew = false;

            bool winnoyatu = false;

            foreach (var playerId in toExile)

            {

                ex?.SetRealKiller(null);

                Iscrew |= Utils.GetPlayerById(playerId).GetCustomRole().IsCrewmate();

            }

            MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Vote, toExile);

            Voteresult = GetString("Balancer.Executad");

            Utils.AddGameLog($"Vote", GetString("Balancer.Executad"));





            foreach (var playerId in toExile)

            {

                CustomRoleManager.GetByPlayerId(playerId)?.OnExileWrapUp(Utils.GetPlayerInfoById(playerId), ref winnoyatu);

            }

        }

        else

        {


        }

        return true;

    }



    public void BalancerAfterMeetingTasks()

    {

        if (!AmongUsClient.Instance.AmHost) return;

        //天秤会議になってない状態なら

        if (Id == 255 && Target1 != 255 && Target2 != 255)

        {

            //天秤会議にする

            Id = Player.PlayerId;
            Scheduled = true;

            var ran = IRandom.Instance.Next(0, 2);

            var oniku = Utils.GetPlayerById(ran == 0 ? Target1 : Target2) ?? PlayerControl.LocalPlayer;

            var reporter = Utils.GetPlayerById(ran == 0 ? Target2 : Target1) ?? PlayerControl.LocalPlayer;



            foreach (var pc in Main.AllPlayerControls.Where(pc => pc.PlayerId == Target1 || pc.PlayerId == Target2))

                pc.RpcSetName("<#ff1919>Ω<i><u>" + Utils.ColorString(RoleInfo.RoleColor, Main.AllPlayerNames[pc.PlayerId]) + "</u>");

            _ = new LateTask(() =>

            {

                _ = new LateTask(() => Utils.AllPlayerKillFlash(), 1f, "BMkillf");

                // 名前は会議を開く前に戻す (開いた後に戻すと、会議用に送った名前を素の名前で上書きする)
                RestoreName(Target1);
                RestoreName(Target2);
                MeetingTargets = new[] { Target1, Target2 };
                ReportDeadBodyPatch.ExReportDeadBody(reporter, oniku.Data, false, "Balancer.meeting", Utils.GetRoleColorCode(CustomRoles.Balancer));
                // 会議が捨てられた (通報者が抜けた・勝ちが決まった・追放の画面が終わらない) 時に、予約を残さない
                _ = new LateTask(() => CancelIfNotOpened(0), 1f, "Balancer.CheckOpened");
            }, 2f, "BalanerMeeting");



            //対象の名前を天秤の色に

            TownOfHostL.Modules.MeetingTimeManager.VotingOnly(meetingtime);



            _ = new LateTask(() =>

            {

                Utils.AddGameLog("Meeting", string.Format(GetString("BalancerMeetingInfo"), (Utils.GetPlayerById(Target1)).ColoredName(), (Utils.GetPlayerById(Target2)).ColoredName()).Split("\n")[0] + "</color>");

            }, 2.8f);



            return;

        }

    }

    public static string OtherMark(PlayerControl seer, PlayerControl seen, bool isForMeeting = false)

    {

        seen ??= seer;

        if (!isForMeeting) return "";

        if (Id == byte.MaxValue) return "";

        if (seen.PlayerId == target1 || seen.PlayerId == target2)

        {

            return "<#ff1919>Ω</color>";

        }

        return "";

    }

    public override void AfterMeetingTasks()

    {

        //ホストのみ実行

        if (AmongUsClient.Instance.AmHost)

        {

            //自分の天秤会議じゃないなら実行しない

            if (Id != Player.PlayerId)

                return;



            //名前を戻す

            RestoreName(Target1);

            RestoreName(Target2);



            if (nickname != null)

                Main.nickName = nickname;

            nickname = null;



            //名前にロールとかのを適用

            _ = new LateTask(() => Utils.NotifyRoles(ForceLoop: true, NoCache: true), Main.LagTime);

        }



        //リセット

        Id = 255;

        Target1 = 255;

        Target2 = 255;

        target1 = 255;

        target2 = 255;

        return;

    }

    // 天秤会議の間に掛けた 2 人のどちらかが死ぬか抜けたら、その場で会議を閉じる (会議の毎フレームの処理から呼ぶ)
    static bool closedByTargetLoss;
    public static void CheckTargetLoss(MeetingHud meetingHud)
    {
        if (Id == byte.MaxValue) { closedByTargetLoss = false; return; }
        if (closedByTargetLoss || TownOfHostL.Modules.MeetingVoteManager.Instance == null) return;
        if (meetingHud.state is not (MeetingHud.MeetingStates.NotVoted or MeetingHud.MeetingStates.Voted)) return;
        if (Utils.GetPlayerById(target1).IsAlive() && Utils.GetPlayerById(target2).IsAlive()) return;
        closedByTargetLoss = true;
        TownOfHostL.Modules.MeetingVoteManager.Instance.EndMeeting(false);
    }
    /// <summary>
    /// 天秤会議を開きに行った後、開いたかを見る。開いていなければ予約を取り下げ、止めていた会議明けの処理を流す
    /// (残すと会議明けの処理が止まったまま日が進まず、次の普通の会議が天秤会議として扱われる)
    /// </summary>
    static void CancelIfNotOpened(int tries, bool lastLooked = false)
    {
        if (!Scheduled || !GameStates.IsInGame) return;
        // 会議が始まった後は開いた会議の側が予約を外す
        if (GameStates.IsMeeting || MeetingStates.MeetingCalled) return;
        // 追放の画面を待っている・通報を預かっている間は、もう少し待つ (追放の画面は最大 20 秒待つ)
        if ((ExileController.Instance != null || TownOfHostL.Modules.Revive.HasHeldReport) && tries < 25)
        {
            _ = new LateTask(() => CancelIfNotOpened(tries + 1), 1f, "Balancer.CheckOpened");
            return;
        }
        // 追放の画面が消えた直後は、会議を開く側の待ち (0.5 秒ごと) がまだ開いていないことがあるので、もう 1 回だけ見る
        if (!lastLooked)
        {
            _ = new LateTask(() => CancelIfNotOpened(tries + 1, true), 1f, "Balancer.CheckOpened");
            return;
        }
        Logger.Info("天秤会議が開かなかったので取りやめる", "Balancer");
        Scheduled = false;
        foreach (var role in CustomRoleManager.AllActiveRoles.Values)
        {
            if (role is not Balancer balancer) continue;
            // 天秤会議は開かなかったので、2 人を選んだ天秤の能力は使っていない扱いに戻す
            if (balancer.Target1 != 255 && balancer.Target2 != 255) balancer.used = false;
            balancer.Target1 = 255;
            balancer.Target2 = 255;
        }
        AfterMeetingReset();
        Utils.AfterMeetingTasks();
        Utils.NotifyRoles(ForceLoop: true);
    }
    // 天秤会議でない会議の始まりに、前の会議で 1 人だけ選んだ途中の状態と天秤モードを下ろす
    // (残すと次の会議の普通の票が 2 人目として数えられ、天秤会議が始まる)
    public override void OnStartMeeting()
    {
        if (Id != 255 || Scheduled) return;
        Target1 = 255;
        Target2 = 255;
        SetMode(Player, false);
    }
    /// <summary>天秤会議を開く予約をしてから、実際に開くまでの間</summary>
    public static bool Scheduled;
    [TownOfHostL.Attributes.GameModuleInitializer]
    public static void InitStatic()
    {
        Scheduled = false;
        AfterMeetingReset();
    }
    public static void AfterMeetingReset()

    {

        Id = 255;

        target1 = 255;

        target2 = 255;

    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (isForMeeting && Player.IsAlive() && (!GameStates.AlreadyDied || CanUseAllAlive) && seer.PlayerId == seen.PlayerId && Canuseability() && !used)

        {

            var mes = $"<color={RoleInfo.RoleColorCode}>{GetString("SelfVoteRoleInfoMeg")}</color>";

            return isForHud ? mes : $"<size=40%>{mes}</size>";

        }

        return "";

    }


}