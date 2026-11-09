using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHostL.Modules;
using TownOfHostL.Patches;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Neutral;

public sealed class Fox : RoleBase, ISystemTypeUpdateHook, IRoomTasker
{
    static bool AnyShown() => CustomRoleManager.AllActiveRoles.Values.Any(r => r is Fox f && f.IsShowMyRole);
    // 役職の元: TownOfHost_K (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Fox),
            player => new Fox(player),
            CustomRoles.Fox,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Neutral,
            (int)Options.offsetId.Hamo + 51500,
            SetupOptionItem,
            "Fox",
            "#d288ee",
            isDesyncImpostor: false,
            countType: CountTypes.Fox,
            assignInfo: new RoleAssignInfo(CustomRoles.Fox, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1)
            }
        );
    public Fox(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.ForRecompute
    )
    {
        CustomRoleManager.EveryoneNeedsSeenNamesOthers.Add(AnyShown);
        Maxmeter = OptionMeterCount.GetFloat();
        MeterDistance = OptionDistance.GetFloat();
        OnlySeekiller = OptionOnlySeeKiller.GetBool();
        IsActiveNotice = OptionNoticeRoomTask.GetBool();
        GiveGuardTaskCount = OptionGiveGuardTaskCount.GetInt();
        GiveGuardMax = OptionGiveGuardMax.GetInt();
        DieAliveCount = OptionDieAliveCount.GetInt();
        CrewTaskFinishFlug = (FoxCrewTaskFin)OptionCrewTaskFinish.GetValue();
        IsTellDie = OptionTellDie.GetBool();
        Engventcool = OptionEngVentCoolDown.GetFloat();
        Engventinmax = OptionEngVentInmaxtime.GetFloat();
        CanSeeImpostor = OptionCanSeeImpostor.GetBool();
        CanSeeNeutralKiller = OptionCanSeeNeutralKiller.GetBool();
        CanSeeOther = OptionCanSeeOther.GetBool();

        CompleteRoomTask = false;
        Taskcount = 0;
        Guard = 0;
        NowMeter = Maxmeter;
        IsShowMyRole = false;
        isnotice = false;
        meatermark = "";
    }
    bool CompleteRoomTask;

    float NowMeter; float timer; bool IsShowMyRole; string meatermark;
    int Taskcount; int Guard;
    static void SetupOptionItem()
    {
        string[] values = EnumHelper.GetAllNames<FoxCrewTaskFin>();
        SoloWinOption.Create(RoleInfo, defo: 15);
        OptionCrewTaskFinish = StringOptionItem.Create(RoleInfo, OptionName.FoxCrewTaskFin, values, 0, false);
        OptionDieAliveCount = IntegerOptionItem.Create(RoleInfo, OptionName.FoxDieAliveCount, new(0, 15, 1), 3, false);
        ObjectOptionitem.Create(RoleInfo, "FoxAbilitySetting", true, null).SetOptionName(() => "Ability Setting");
        OptionEngVentCoolDown = FloatOptionItem.Create(RoleInfo, StringNames.EngineerCooldown, new(0, 180, 0.5f), 10, false).SetValueFormat(OptionFormat.Seconds);
        OptionEngVentInmaxtime = FloatOptionItem.Create(RoleInfo, StringNames.EngineerInVentCooldown, new(0.5f, 30, 0.5f), 3, false).SetValueFormat(OptionFormat.Seconds);
        OptionTellDie = BooleanOptionItem.Create(RoleInfo, OptionName.FoxTellDie, false, false);
        OptionNoticeRoomTask = BooleanOptionItem.Create(RoleInfo, OptionName.FoxNoticeRoomTask, true, false);
        ObjectOptionitem.Create(RoleInfo, "FoxMeterSetting", true, null).SetOptionName(() => "Meter Setting");
        OptionMeterCount = FloatOptionItem.Create(RoleInfo, OptionName.FoxMeterCount, new(5, 1200, 5), 60, false).SetValueFormat(OptionFormat.Seconds);
        OptionDistance = FloatOptionItem.Create(RoleInfo, OptionName.FoxMeterDistance, new(0.5f, 5f, 0.25f), 1.25f, false).SetValueFormat(OptionFormat.Multiplier);
        OptionOnlySeeKiller = BooleanOptionItem.Create(RoleInfo, OptionName.FoxShowRoleOnlyKiller, false, false);
        OptionCanSeeImpostor = BooleanOptionItem.Create(RoleInfo, OptionName.FoxCanSeeImposotr, true, false, OptionOnlySeeKiller);
        OptionCanSeeNeutralKiller = BooleanOptionItem.Create(RoleInfo, OptionName.FoxCanSeeNeutralKiller, true, false, OptionOnlySeeKiller);
        OptionCanSeeOther = BooleanOptionItem.Create(RoleInfo, OptionName.FoxCanSeeOtherKiller, true, false, OptionOnlySeeKiller);
        ObjectOptionitem.Create(RoleInfo, "FoxGuardSetting", true, null).SetOptionName(() => "Guard Setting");
        OptionGiveGuardTaskCount = IntegerOptionItem.Create(RoleInfo, OptionName.FoxGiveGuardTaskcount, new(1, 99, 1), 3, false);
        OptionGiveGuardMax = IntegerOptionItem.Create(RoleInfo, OptionName.FoxGiveGuardMax, new(0, 99, 1), 2, false);
        Options.OverrideTasksData.Create(RoleInfo);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = Engventcool;
        AURoleOptions.EngineerInVentMaxTime = Engventinmax;
    }
    #region Meter / Die
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (player.IsAlive() is false) return;

        timer += Time.fixedDeltaTime;
        if (5 < timer)
        {
            var mypos = player.GetTruePosition();
            foreach (var otherpc in Main.AllAlivePlayerControls)
            {
                if (otherpc.PlayerId == player.PlayerId) continue;

                if (Vector2.Distance(mypos, otherpc.GetTruePosition()) < MeterDistance)
                {
                    NowMeter -= Time.fixedDeltaTime;
                    break;
                }
            }
            if (GetNow() != meatermark)
            {
                meatermark = GetNow();
                if (AmongUsClient.Instance.AmHost)
                    Utils.NotifyRoles();
            }
            if (IsShowMyRole is false && NowMeter < 0)
            {
                IsShowMyRole = true;
                isnotice = true;
                SendRPC_ShowMyRole();

                foreach (var seer in Main.AllAlivePlayerControls)
                {
                    if (CanSeeFox(seer))
                    {
                        NameColorManager.Add(seer.PlayerId, Player.PlayerId, RoleInfo.RoleColorCode);
                    }
                }
                if (AmongUsClient.Instance.AmHost)
                    Utils.NotifyRoles();
            }
        }

        //非ホスト導入者はタイマー処理のみ行う。
        if (AmongUsClient.Instance.AmHost is false)
        {
            if (Utils.AllAlivePlayersCount <= DieAliveCount)
            {
                MyState.DeathReason = CustomDeathReason.Spell;
                Player.RpcExileV3();
                Utils.AddGameLog($"Fox", $"{Player.ColoredName()}(<b>{RoleText.GetRoleNameText(Player.PlayerId, showSubRole: false)}</b>) [{Utils.GetVitalText(Player.PlayerId, true)}]");
            }
        }
    }
    public override void AfterMeetingTasks()
    {
        timer = 0;
        Logger.Info($"現在のメーター{NowMeter}", "Fox");
    }
    public override bool ShowsRoleTo(PlayerControl seer, bool isMeeting) => IsShowMyRole && seer != Player && CanSeeFox(seer);
    bool CanSeeFox(PlayerControl seer)
    {
        if (seer.IsAlive())
        {
            // キル役のみ制限無し
            if (OnlySeekiller is false) return true;

            if (seer.Is(CustomRoleTypes.Impostor)) return CanSeeImpostor;
            if (seer.IsNeutralKiller()) return CanSeeNeutralKiller;

            /* インポスターでも第三キラーでもない場合 */
            return (seer.GetRoleClass() as IKiller)?.IsKiller ?? false;
        }
        return false;
    }
    void SendRPC_ShowMyRole()
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.WritePacked((int)RPC_Types.IsShowMyRole);
    }
    #endregion
    #region RoomTask
    int? IRoomTasker.GetMaxTaskCount() => null;
    bool IRoomTasker.IsAssignRoomTask() => IsActiveNotice && !CompleteRoomTask && Player.IsAlive();
    void IRoomTasker.OnComplete(int completeroom) => SendRPC_CompleteRoom();
    void IRoomTasker.ChangeRoom(PlainShipRoom TaskRoom) => SendRPC_ChengeRoom(TaskRoom);
    public void SendRPC_CompleteRoom()
    {
        CompleteRoomTask = true;
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.WritePacked((int)RPC_Types.CompleteRoom);
    }
    public void SendRPC_ChengeRoom(PlainShipRoom TaskPSR)
    {
        using var sender = CreateSender(CustomRPC.CustomRoleSync);
        sender.Writer.WritePacked((int)RPC_Types.ChengeRoom);
        sender.Writer.Write((byte)TaskPSR.RoomId);
    }
    public override string MeetingAddMessage()
    {
        if (SelfVoteManager.Canuseability() is false) return "";
        var oldcomptask = CompleteRoomTask;
        CompleteRoomTask = false;
        if (!Player.IsAlive() || oldcomptask) return "";

        isnotice = true;
        var chance = IRandom.Instance.Next(100);
        if (chance > 95) return $"<color=#d288ee>{GetString("FoxAliveMeg1")}</color>";
        if (chance > 90) return $"<color=#d288ee>{GetString("FoxAliveMeg2")}</color>";
        if (chance > 85) return $"<color=#d288ee>{GetString("FoxAliveMeg3")}</color>";
        return $"<color=#d288ee>{GetString("FoxAliveMeg")}</color>";
    }
    #endregion
    #region Guard
    public override bool OnCompleteTask()
    {
        //もう上限に達しているなら処理終わり
        if (GiveGuardMax <= Guard) return true;
        //タスクカウントを増やす
        Taskcount++;
        //ガードを追加するタスク数に達したら
        if (GiveGuardTaskCount <= Taskcount)
        {
            Taskcount = 0;
            Guard++;
            Logger.Info($"ガード追加:{Guard}", "Fox");
        }
        return true;
    }
    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        if (info.IsAccident || info.IsSuicide || info.IsFakeSuicide) return true;
        if (Guard > 0)
        {
            var (killer, target) = info.AttemptTuple;

            killer.SetKillCooldown(ForceProtect: true, target: target);
            Guard--;
            Logger.Info($"ガード残り:{Guard}", "Fox");
            info.GuardPower = 2;
            return true;
        }
        return true;
    }
    #endregion
    #region Sabotage
    bool ISystemTypeUpdateHook.UpdateReactorSystem(ReactorSystemType reactorSystem, byte amount) => false;
    bool ISystemTypeUpdateHook.UpdateHeliSabotageSystem(HeliSabotageSystem heliSabotageSystem, byte amount) => false;
    bool ISystemTypeUpdateHook.UpdateLifeSuppSystem(LifeSuppSystemType lifeSuppSystem, byte amount) => false;
    bool ISystemTypeUpdateHook.UpdateHqHudSystem(HqHudSystemType hqHudSystemType, byte amount) => false;
    bool ISystemTypeUpdateHook.UpdateSwitchSystem(SwitchSystem switchSystem, byte amount) => false;
    bool ISystemTypeUpdateHook.UpdateHudOverrideSystem(HudOverrideSystemType hudOverrideSystem, byte amount) => false;
    #endregion
    #region win
    public static bool SFoxCheckWin(ref GameOverReason reason)
    {
        foreach (var pc in Main.AllPlayerControls.Where(pc => pc.Is(CustomRoles.Fox)))
        {
            if (pc.GetRoleClass() is Fox fox)
            {
                if (fox.FoxCheckWin(ref reason)) return true;
            }
        }
        return false;
    }
    public bool FoxCheckWin(ref GameOverReason reason)
    {
        if (!Player.IsAlive()) return false;

        if (reason is GameOverReason.CrewmatesByTask && CustomWinnerHolder.WinnerTeam is CustomWinner.Crewmate)
        {
            switch (CrewTaskFinishFlug)
            {
                case FoxCrewTaskFin.FoxCrewTaskFin_MyWin:
                    if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Fox, Player.PlayerId))
                    {
                        CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
                        reason = GameOverReason.ImpostorsByKill;
                        return true;
                    }
                    break;
                case FoxCrewTaskFin.FoxCrewTaskFin_Addwin:
                    CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Fox);
                    CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
                    return false;
            }
        }
        else
        {
            if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Fox, Player.PlayerId))
            {
                CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
                reason = GameOverReason.ImpostorsByKill;
                return true;
            }
        }
        return false;
    }
    public static bool BlockTaskWin()
    {
        foreach (var pc in Main.AllPlayerControls.Where(pc => pc.Is(CustomRoles.Fox)))
        {
            if (pc.GetRoleClass() is Fox fox)
            {
                if (pc.IsAlive() && CrewTaskFinishFlug is FoxCrewTaskFin.FoxCrewTaskFin_NoGameEnd)
                {
                    return true;
                }
            }
        }
        return false;
    }
    #endregion
    #region name
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seen != seer || !seer.IsAlive()) return "";

        return $"<size=40%><u>{GetNow()}</u></size>";
    }
    public override void OverrideProgressTextAsSeen(PlayerControl seer, ref bool enabled, ref string text)
    {
        if (seer == null) return;
        if (Is(seer) || seer.Is(CustomRoles.GM) || !seer.IsAlive()) return;

        text = $"<#cccccc>(?/{MyTaskState.AllTasksCount})";
    }
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer != seen) return "";
        return (seer.GetRoleClass() as IRoomTasker)?.GetLowerText(seer, RoleInfo.RoleColorCode) ?? "";
    }
    string GetNow()//バッテリー量の表示
    {
        var battery = (NowMeter / Maxmeter) * 100;
        if (battery <= 0) return "　";
        if (battery <= 5) return "<mark=#d95327><color=#000000>||</mark>           </size></color>";
        if (battery <= 10) return "<mark=#d96e27><color=#000000>|||</mark>          </size></color>";
        if (battery <= 20) return "<mark=#d9b827><color=#000000>||||</mark>         </size></color>";
        if (battery <= 30) return "<mark=#d6d927><color=#000000>|||||</mark>        </size></color>";
        if (battery <= 40) return "<mark=#b8d13b><color=#000000>||||||</mark>       </size></color>";
        if (battery <= 50) return "<mark=#a7ba47><color=#000000>|||||||</mark>      </size></color>";
        if (battery <= 60) return "<mark=#96ba47><color=#000000>||||||||</mark>     </size></color>";
        if (battery <= 70) return "<mark=#84ba47><color=#000000>|||||||||</mark>    </size></color>";
        if (battery <= 80) return "<mark=#75ba47><color=#000000>||||||||||</mark>   </size></color>";
        if (battery <= 90) return "<mark=#3fb81d><color=#000000>|||||||||||</mark>  </size></color>";
        else return "<mark=#03ff4a><color=#000000>||||||||||</mark> </size></color>";
    }
    #endregion
    #region RPC
    public override void ReceiveRPC(MessageReader reader, CustomRPC rpcType)
    {
        if (rpcType != CustomRPC.CustomRoleSync) return;
        var iroomtasker = Player.GetRoleClass() is IRoomTasker roomTasker ? roomTasker : null;
        switch ((RPC_Types)reader.ReadPackedInt32())
        {
            case RPC_Types.ChengeRoom:
                iroomtasker?.ReceiveRoom(Player.PlayerId, reader);
                break;
            case RPC_Types.CompleteRoom:
                var a = MessageReader.Get(reader);
                iroomtasker?.ReceiveCompleteRoom(Player.PlayerId, reader);
                CompleteRoomTask = true;
                break;
            case RPC_Types.IsShowMyRole:
                IsShowMyRole = true;
                break;
        }
    }

    enum RPC_Types
    {
        ChengeRoom,
        CompleteRoom,
        IsShowMyRole
    }
    #endregion
    public override CustomRoles TellResults(PlayerControl player)
    {
        //ぽんこつ占い師のぽんこつ占い師で死ぬのはかわいそう('ω')
        if (AmongUsClient.Instance.AmHost && IsTellDie && player != null && player.IsAlive() && Player.IsAlive())
        {
            Player.RpcExileV3();
            MyState.DeathReason = CustomDeathReason.Spell;
            MyState.SetDead();

            if (Main.AllPlayerControls.Any(pc => pc.Is(CustomRoles.Guesser)))
                ChatPushOut.PushOut();

            Utils.AddGameLog($"Fox", $"{Player.ColoredName()}(<b>{RoleText.GetRoleNameText(Player.PlayerId, showSubRole: false)}</b>) [{Utils.GetVitalText(Player.PlayerId, true)}]");
            Utils.AddGameLog("", $"\n\t┗ {GetString("Skillplayer")}{player.ColoredName()}(<b>{RoleText.GetRoleNameText(player.PlayerId, showSubRole: false)}</b>)");

            /*何この処理。
            var meetingHud = MeetingHud.Instance;
            var hudManager = DestroyableSingleton<HudManager>.Instance.KillOverlay;
            {
                GameDataSerializePatch.SerializeMessageCount++;
                foreach (var pc in Main.AllAlivePlayerControls)
                {
                    if (pc == Player) continue;
                    pc.Data.IsDead = false;
                }
                ExtendedPlayerControl.RpcSyncAllNetworkedPlayer(Player.GetClientId());
                GameDataSerializePatch.SerializeMessageCount--;
            }*/

            Utils.SendMessage(Player.ColoredName() + GetString("Meetingkill"), title: GetString("MSKillTitle"));
            foreach (var go in Main.AllPlayerControls.Where(pc => pc != null && !pc.IsAlive()))
            {
                Utils.SendMessage(string.Format(GetString("FoxTelldie"), Player.ColoredName()), go.PlayerId, GetString("RMSKillTitle"));
            }
            MeetingVoteManager.ResetVoteManager(Player.PlayerId);
            Player.RpcMeetingKill(Player);
        }
        return CustomRoles.NotAssigned;
    }
    /* 自身が妖狐であると暴露されるまでのカウント */
    static OptionItem OptionMeterCount; static float Maxmeter;
    static OptionItem OptionDistance; static float MeterDistance;
    static OptionItem OptionOnlySeeKiller; static bool OnlySeekiller;
    static OptionItem OptionCanSeeImpostor; static bool CanSeeImpostor;
    static OptionItem OptionCanSeeNeutralKiller; static bool CanSeeNeutralKiller;
    static OptionItem OptionCanSeeOther; static bool CanSeeOther;
    /* 毎ターン指定した部屋に行かないと通知される */
    static OptionItem OptionNoticeRoomTask; static bool IsActiveNotice;
    /* キルガード */
    static OptionItem OptionGiveGuardTaskCount; static int GiveGuardTaskCount;
    static OptionItem OptionGiveGuardMax; static int GiveGuardMax;
    /* 設定人数以下になったら成仏 */
    static OptionItem OptionDieAliveCount; static int DieAliveCount;
    /* タスク勝利時の挙動 */
    static OptionItem OptionCrewTaskFinish; static FoxCrewTaskFin CrewTaskFinishFlug;
    enum FoxCrewTaskFin { FoxCrewTaskFin_MyWin, FoxCrewTaskFin_Lose, FoxCrewTaskFin_NoGameEnd, FoxCrewTaskFin_Addwin }
    /* その他*/
    static OptionItem OptionTellDie; static bool IsTellDie;
    static OptionItem OptionEngVentCoolDown; static float Engventcool;
    static OptionItem OptionEngVentInmaxtime; static float Engventinmax;
    enum OptionName
    {
        FoxMeterCount, FoxMeterDistance, FoxShowRoleOnlyKiller,
        FoxCanSeeImposotr, FoxCanSeeNeutralKiller, FoxCanSeeOtherKiller,
        FoxNoticeRoomTask,
        FoxDieAliveCount,
        FoxCrewTaskFin,
        FoxGiveGuardTaskcount, FoxGiveGuardMax,
        FoxTellDie
    }
    bool isnotice;
}
