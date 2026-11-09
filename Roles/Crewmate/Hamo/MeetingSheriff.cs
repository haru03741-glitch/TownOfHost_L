using AmongUs.GameOptions;

using UnityEngine;

using Hazel;

using System.Linq;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Impostor;

using TownOfHostL.Roles.Neutral;


using static TownOfHostL.Modules.SelfVoteManager;

using TownOfHostL.Modules;

using TownOfHostL.Roles.Core.Interfaces;
using static TownOfHostL.Translator;



namespace TownOfHostL.Roles.Crewmate;



public sealed class MeetingSheriff : RoleBase, ISelfVoter

{

    // 役職の元: SuperNewRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.Create(
            typeof(MeetingSheriff),
            player => new MeetingSheriff(player),
            CustomRoles.MeetingSheriff,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            (int)Options.offsetId.Hamo + 32700,
            SetupOptionItem,
            "mts",
            "#f8cd46"
        );

    public MeetingSheriff(PlayerControl player)

    : base(

        RoleInfo,

        player

    )

    {

        Max = OptionSheriffShotLimit.GetFloat();

        Usedcount = 0;

        MeetingUsedcount = 0;

        MeetingSheriffCanKillMadMate = OptionMeetingSheriffCanKillMadMate.GetBool();

        MeetingSheriffCanKillNeutrals = OptionMeetingSheriffCanKillNeutrals.GetBool();

        cantaskcount = Optioncantaskcount.GetFloat();

        OneMeetingMaximum = Option1MeetingMaximum.GetFloat();



        onemeetingkill = 0;

    }



    private static OptionItem OptionSheriffShotLimit;

    private static OptionItem OptionMeetingSheriffCanKillMadMate;

    private static OptionItem OptionMeetingSheriffCanKillNeutrals;

    private static OptionItem OptionMeetingSheriffCanKillLovers;

    private static OptionItem Optioncantaskcount;

    private static OptionItem Option1MeetingMaximum;

    static float Max;

    static float cantaskcount;

    bool MeetingSheriffCanKillMadMate;

    bool MeetingSheriffCanKillNeutrals;

    int Usedcount;

    static float OneMeetingMaximum;

    int MeetingUsedcount;





    int onemeetingkill;

    enum Option

    {

        SheriffShotLimit,

        cantaskcount,//効果を発揮タスク数

        MeetingSheriffCanKillMadMate,

        MeetingSheriffCanKillNeutrals,

        SheriffCanKillLovers

    }

    private static void SetupOptionItem()

    {

        OptionSheriffShotLimit = IntegerOptionItem.Create(RoleInfo, Option.SheriffShotLimit, new(1, 15, 1), 1, false)

            .SetValueFormat(OptionFormat.Times);

        Optioncantaskcount = IntegerOptionItem.Create(RoleInfo, Option.cantaskcount, new(0, 99, 1), 5, false);

        OptionMeetingSheriffCanKillMadMate = BooleanOptionItem.Create(RoleInfo, Option.MeetingSheriffCanKillMadMate, true, false);

        OptionMeetingSheriffCanKillNeutrals = BooleanOptionItem.Create(RoleInfo, Option.MeetingSheriffCanKillNeutrals, true, false);

        OptionMeetingSheriffCanKillLovers = BooleanOptionItem.Create(RoleInfo, Option.SheriffCanKillLovers, true, false);

        SaruWarningOption.AddTo(RoleInfo);

        Option1MeetingMaximum = IntegerOptionItem.Create(RoleInfo, GeneralOption.MeetingMaxTime, new(0, 99, 1), 0, false)

            .SetValueFormat(OptionFormat.Times).SetZeroNotation(OptionZeroNotation.Infinity);

    }

    private void SendRPC()

    {

        using var sender = CreateSender(CustomRPC.CustomRoleSync);

        sender.Writer.Write(Usedcount);

    }

    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)

    {
        if (rpcType != CustomRPC.CustomRoleSync) return;

        Usedcount = reader.ReadInt32();

    }

    public override void OnStartMeeting()

    {

        onemeetingkill = 0;

        MeetingUsedcount = 0;

    }

    public override string GetProgressText(bool comms = false) => Utils.ColorString(MyTaskState.CompletedTasksCount < cantaskcount ? Color.gray : Max <= Usedcount ? Color.gray : Color.cyan, $"({Max - Usedcount})");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (isForMeeting && Player.IsAlive() && seer.PlayerId == seen.PlayerId && Canuseability() && Max > Usedcount && MyTaskState.HasCompletedEnoughCountOfTasks((int)cantaskcount))

        {

            var mes = $"<color={RoleInfo.RoleColorCode}>{GetString("SelfVoteRoleInfoMeg")}</color>";

            return isForHud ? mes : $"<size=40%>{mes}</size>";

        }

        return "";

    }

    bool ISelfVoter.CanUseVoted() => Canuseability() && Max > Usedcount && MyTaskState.HasCompletedEnoughCountOfTasks((int)cantaskcount) && (MeetingUsedcount < OneMeetingMaximum || OneMeetingMaximum == 0);



    public override bool CheckAnyVote(byte votedForId, PlayerControl voter)

    {

        if (!Canuseability()) return true;

        if (Max > Usedcount && Is(voter) && MyTaskState.HasCompletedEnoughCountOfTasks((int)cantaskcount) && (MeetingUsedcount < OneMeetingMaximum || OneMeetingMaximum == 0))

        {

            if (CheckSelfVoteMode(Player, votedForId, out var status))

            {

                if (status is VoteStatus.Self)

                    Utils.SendMessage(string.Format(GetString("SkillMode"), GetString("Mode.MeetingSheriff"), GetString("Vote.MeetingSheriff")) + GetString("VoteSkillMode"), Player.PlayerId);

                if (status is VoteStatus.Skip)

                    Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);

                if (status is VoteStatus.Vote)

                    Sheriff(votedForId);

                SetMode(Player, status is VoteStatus.Self);

                return false;

            }

        }

        return true;

    }

    public void Sheriff(byte votedForId)

    {

        PlayerState state;

        var target = Utils.GetPlayerById(votedForId);

        if (!target.IsAlive()) return;

        if (!AmongUsClient.Instance.AmHost) return;

        if (target.Is(CustomRoles.Stand))

        {

            var sm = target.GetRoleClass() as TownOfHostL.Roles.Neutral.Stand;

            var owner = sm?.GetOwner();

            if (owner != null && owner.Player.IsAlive())

            {

                Utils.SendMessage(

                    "<color=#8B4513>残念だったな！スタンドは撃ち抜けないんだぜ！</color>",

                    Player.PlayerId);

                return;

            }

        }

        var meetingHud = MeetingHud.Instance;

        var hudManager = DestroyableSingleton<HudManager>.Instance.KillOverlay;

        Usedcount++;

        MeetingUsedcount++;//1会議のカウント

        SendRPC();



        //ゲッサーがいるなら～

        if (Main.AllPlayerControls.Any(pc => pc.Is(CustomRoles.Guesser)))

            ChatPushOut.PushOut();



        var AlienTairo = false;

        var targetroleclass = target.GetRoleClass();






        if ((CanBeKilledBy(target.GetCustomRole()) && !AlienTairo) || (target.Is(CustomRoles.Lovers) && OptionMeetingSheriffCanKillLovers.GetBool()) || (target.Is(CustomRoles.Amanojaku) && OptionMeetingSheriffCanKillNeutrals.GetBool()))

        {

            state = PlayerState.GetByPlayerId(target.PlayerId);

            target.RpcExileV3();

            state.DeathReason = CustomDeathReason.Kill;

            state.SetDead();



            Utils.AddGameLog($"MeetingSheriff", $"{target.ColoredName()}(<b>{RoleText.GetRoleNameText(target.PlayerId, showSubRole: false)}</b>) [{Utils.GetVitalText(target.PlayerId, true)}]");

            Utils.AddGameLog("", $"\n\t⇐ {Player.ColoredName()}(<b>{RoleText.GetRoleNameText(Player.PlayerId, showSubRole: false)}</b>)");




            Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}がシェリフ成功({target.GetNameWithRole().RemoveHtmlTags()}) 残り{Max - Usedcount}", "MeetingSheriff");

            Utils.SendMessage(target.ColoredName() + GetString("Meetingkill"), title: GetString("MSKillTitle"));

            foreach (var go in Main.AllPlayerControls.Where(pc => pc != null && !pc.IsAlive()))

            {

                Utils.SendMessage(string.Format(GetString("MMeetingKill"), Player.ColoredName(), target.ColoredName()), go.PlayerId, GetString("RMSKillTitle"));

            }



            MeetingVoteManager.ResetVoteManager(target.PlayerId);

            if (target != PlayerControl.LocalPlayer) Player.RpcMeetingKill(target);


            onemeetingkill++;

            return;

        }

        Player.RpcExileV3();

        MyState.DeathReason = target.Is(CustomRoles.Tairou) && Tairou.TairoDeathReason ? CustomDeathReason.Counter : CustomDeathReason.Misfire;

        MyState.SetDead();



        Utils.AddGameLog($"MeetingSheriff", $"{Player.ColoredName()}(<b>{RoleText.GetRoleNameText(Player.PlayerId, showSubRole: false)}</b>) [{Utils.GetVitalText(Player.PlayerId, true)}]");

        Utils.AddGameLog("", $"\n\t┗ {GetString("Skillplayer")}{target.ColoredName()}(<b>{RoleText.GetRoleNameText(target.PlayerId, showSubRole: false)}</b>)");




        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}がシェリフ失敗({target.GetNameWithRole().RemoveHtmlTags()}) 残り{Max - Usedcount}", "MeetingSheriff");

        Utils.SendMessage(Player.ColoredName() + GetString("Meetingkill"), title: GetString("MSKillTitle"));

        foreach (var go in Main.AllPlayerControls.Where(pc => pc != null && !pc.IsAlive()))

        {

            Utils.SendMessage(string.Format(GetString("MMeetingKillfall"), Player.ColoredName(), target.ColoredName()), go.PlayerId, GetString("RMSKillTitle"));

        }



        MeetingVoteManager.ResetVoteManager(Player.PlayerId);

        if (Player != PlayerControl.LocalPlayer) Player.RpcMeetingKill(Player);

    }

    bool CanBeKilledBy(CustomRoles role)

    {

        if (role == CustomRoles.SKMadmate) return MeetingSheriffCanKillMadMate;

        if (role == CustomRoles.Jackaldoll) return MeetingSheriffCanKillNeutrals;



        return role.GetCustomRoleTypes() switch

        {

            CustomRoleTypes.Impostor => role is not CustomRoles.Tairou,

            CustomRoleTypes.Madmate => MeetingSheriffCanKillMadMate,

            CustomRoleTypes.Neutral => MeetingSheriffCanKillNeutrals,

            CustomRoleTypes.Crewmate => role is CustomRoles.WolfBoy,

            _ => false

        };

    }

    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)

    {

        if (exiled == null || exiled?.Object == null)

        {


            return;

        }

        if (exiled.Object.GetCustomRole().IsCrewmate() is false) onemeetingkill++;




    }


}

//コード多分改良できるけど動いてるからヨシ。(´・ω・｀)