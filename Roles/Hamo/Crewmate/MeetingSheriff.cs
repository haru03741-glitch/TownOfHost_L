using AmongUs.GameOptions;

using UnityEngine;

using Hazel;

using System.Linq;



using TownOfHostL.Roles.Core;

using TownOfHostL.Roles.Hamo.Impostor;

using TownOfHostL.Roles.Hamo.Neutral;

using TownOfHostL.Modules.ChatManager;

using static TownOfHostL.Modules.SelfVoteManager;

using TownOfHostL.Modules;

using TownOfHostL.Roles.Core.Interfaces;



namespace TownOfHostL.Roles.Hamo.Crewmate;



public sealed class MeetingSheriff : HamoRoleBase, ISelfVoter

{

    public static readonly SimpleRoleInfo RoleInfo =

        SimpleRoleInfo.CreateHamo(

            typeof(MeetingSheriff),

            player => new MeetingSheriff(player),

            CustomRoles.MeetingSheriff,

            () => RoleTypes.Crewmate,

            CustomRoleTypes.Crewmate,

            32700,

            SetupOptionItem,

            "mts",

            "#f8cd46",

            (2, 2),

            from: From.SuperNewRoles

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

        using var sender = CreateSender();

        sender.Writer.Write(Usedcount);

    }

    public override void ReceiveRPC(MessageReader reader)

    {

        Usedcount = reader.ReadInt32();

    }

    public override void OnStartMeeting()

    {

        onemeetingkill = 0;

        MeetingUsedcount = 0;

    }

    public override string GetProgressText(bool comms = false, bool gamelog = false) => Utils.ColorString(MyTaskState.CompletedTasksCount < cantaskcount ? Color.gray : Max <= Usedcount ? Color.gray : Color.cyan, $"({Max - Usedcount})");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)

    {

        seen ??= seer;

        if (isForMeeting && Player.IsAlive() && seer.PlayerId == seen.PlayerId && Canuseability() && Max > Usedcount && MyTaskState.HasCompletedEnoughCountOfTasks(cantaskcount))

        {

            var mes = $"<color={RoleInfo.RoleColorCode}>{GetString("SelfVoteRoleInfoMeg")}</color>";

            return isForHud ? mes : $"<size=40%>{mes}</size>";

        }

        return "";

    }

    bool ISelfVoter.CanUseVoted() => Canuseability() && Max > Usedcount && MyTaskState.HasCompletedEnoughCountOfTasks(cantaskcount) && (MeetingUsedcount < OneMeetingMaximum || OneMeetingMaximum == 0);



    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)

    {

        if (!Canuseability()) return true;

        if (Max > Usedcount && Is(voter) && MyTaskState.HasCompletedEnoughCountOfTasks(cantaskcount) && (MeetingUsedcount < OneMeetingMaximum || OneMeetingMaximum == 0))

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

        var target = PlayerCatch.GetPlayerById(votedForId);

        if (!target.IsAlive()) return;

        if (!AmongUsClient.Instance.AmHost) return;

        if (target.Is(CustomRoles.Stand))

        {

            var sm = target.GetRoleClass() as TownOfHostL.Roles.Hamo.Neutral.Stand;

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

        if ((PlayerCatch.AllPlayerControls.Any(pc => pc.Is(CustomRoles.Guesser)) || CustomRolesHelper.CheckGuesser()) && !Options.ExHideChatCommand.GetBool())

            ChatManager.SendPreviousMessagesToAll();



        var AlienTairo = false;

        var targetroleclass = target.GetRoleClass();






        if ((CanBeKilledBy(target.GetCustomRole()) && !AlienTairo) || (target.IsLovers() && OptionMeetingSheriffCanKillLovers.GetBool()) || (target.Is(CustomRoles.Amanojaku) && OptionMeetingSheriffCanKillNeutrals.GetBool()))

        {

            state = PlayerState.GetByPlayerId(target.PlayerId);

            target.RpcExileV3();

            state.DeathReason = CustomDeathReason.Kill;

            state.SetDead();



            UtilsGameLog.AddGameLog($"MeetingSheriff", $"{UtilsName.GetPlayerColor(target, true)}(<b>{UtilsRoleText.GetTrueRoleName(target.PlayerId, false)}</b>) [{Utils.GetVitalTextHamo(target.PlayerId, true)}]");

            UtilsGameLog.AddGameLogsub($"\n\t⇐ {UtilsName.GetPlayerColor(Player, true)}(<b>{UtilsRoleText.GetTrueRoleName(Player.PlayerId, false)}</b>)");



            if (Options.ExHideChatCommand.GetBool())

            {

                ChatManager.OnDisconnectOrDeadPlayer(target.PlayerId);

            }

            Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}がシェリフ成功({target.GetNameWithRole().RemoveHtmlTags()}) 残り{Max - Usedcount}", "MeetingSheriff");

            Utils.SendMessage(UtilsName.GetPlayerColor(target, true) + GetString("Meetingkill"), title: GetString("MSKillTitle"));

            foreach (var go in PlayerCatch.AllPlayerControls.Where(pc => pc != null && !pc.IsAlive()))

            {

                Utils.SendMessage(string.Format(GetString("MMeetingKill"), UtilsName.GetPlayerColor(Player, true), UtilsName.GetPlayerColor(target, true)), go.PlayerId, GetString("RMSKillTitle"));

            }



            MeetingVoteManager.ResetVoteManager(target.PlayerId);

            if (target != PlayerControl.LocalPlayer) Player.RpcMeetingKill(target);

            Achievements.RpcCompleteAchievement(Player.PlayerId, 1, achievements[0]);

            onemeetingkill++;

            return;

        }

        Player.RpcExileV3();

        MyState.DeathReason = target.Is(CustomRoles.Tairou) && Tairou.TairoDeathReason ? CustomDeathReason.Counter : CustomDeathReason.Misfire;

        MyState.SetDead();



        UtilsGameLog.AddGameLog($"MeetingSheriff", $"{UtilsName.GetPlayerColor(Player, true)}(<b>{UtilsRoleText.GetTrueRoleName(Player.PlayerId, false)}</b>) [{Utils.GetVitalTextHamo(Player.PlayerId, true)}]");

        UtilsGameLog.AddGameLogsub($"\n\t┗ {GetString("Skillplayer")}{UtilsName.GetPlayerColor(target, true)}(<b>{UtilsRoleText.GetTrueRoleName(target.PlayerId, false)}</b>)");



        if (Options.ExHideChatCommand.GetBool())

        {

            ChatManager.OnDisconnectOrDeadPlayer(Player.PlayerId);

        }

        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}がシェリフ失敗({target.GetNameWithRole().RemoveHtmlTags()}) 残り{Max - Usedcount}", "MeetingSheriff");

        Utils.SendMessage(UtilsName.GetPlayerColor(Player, true) + GetString("Meetingkill"), title: GetString("MSKillTitle"));

        foreach (var go in PlayerCatch.AllPlayerControls.Where(pc => pc != null && !pc.IsAlive()))

        {

            Utils.SendMessage(string.Format(GetString("MMeetingKillfall"), UtilsName.GetPlayerColor(Player, true), UtilsName.GetPlayerColor(target, true)), go.PlayerId, GetString("RMSKillTitle"));

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

            CustomRoleTypes.Crewmate => role is CustomRoles.WolfBoy && WolfBoy.SeenAsImpostor,

            _ => false

        };

    }

    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)

    {

        if (exiled == null || exiled?.Object == null)

        {

            if (3 <= onemeetingkill) Achievements.RpcCompleteAchievement(Player.PlayerId, 0, achievements[1]);

            return;

        }

        if (exiled.Object.GetCustomRole().IsCrewmate() is false) onemeetingkill++;



        if (3 <= onemeetingkill) Achievements.RpcCompleteAchievement(Player.PlayerId, 0, achievements[1]);

    }

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();

    [Attributes.PluginModuleInitializer]

    public static void Load()

    {

        var n1 = new Achievement(RoleInfo, 0, 5, 0, 0);

        var l1 = new Achievement(RoleInfo, 1, 1, 0, 1);

        achievements.Add(0, n1);

        achievements.Add(1, l1);

    }

}

//コード多分改良できるけど動いてるからヨシ。(´・ω・｀)