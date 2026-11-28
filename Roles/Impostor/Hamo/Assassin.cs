using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHostL.Core.Win;
using TownOfHostL.Modules;
using TownOfHostL.Patches;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Crewmate;
using UnityEngine;
using static TownOfHostL.Translator;

namespace TownOfHostL.Roles.Impostor;

public sealed class Assassin : RoleBase, IImpostor, IUsePhantomButton, IDoubleTrigger, IKillFlashSeeable, IUsePetButton, IDissolvingKiller, ITeammateKiller
{
    // 役職の元: ExtremeRoles (TownOfHost-hamo 経由)
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Assassin),
            player => new Assassin(player),
            CustomRoles.Assassin,
            () => OptionHasOtherRole.GetBool() ? OptionHaveRole.GetRole().GetRoleInfo()?.BaseRoleType?.Invoke() ?? RoleTypes.Impostor : RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            (int)Options.offsetId.Hamo + 76100,
            SetupOptionItem,
            "as",
            introSound: () => GetIntroSound(RoleTypes.Detective),
            assignInfo: new RoleAssignInfo(CustomRoles.Assassin, CustomRoleTypes.Impostor)
            {
                AssignUnitRoles = [CustomRoles.Assassin, CustomRoles.Merlin]
            }
        );
    public Assassin(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        NowState = AssassinMeeting.WaitMeeting;
        MarlinIds = new();
        isDeadCache = new();
        GuessId = byte.MaxValue;
        cancallmeetingkill = OptionCanCallMeetingKill.GetBool();
        Holdings.Subscribe(CustomRoleManager.GameEndCountAdjustersOthers, CountWaitingAssassins);
        Holdings.Subscribe(CustomRoleManager.GameEndByCountHoldersOthers, IsMerlinCollected);
    }
    /// <summary>マーリンを当てている間は人数で終わらない</summary>
    private static bool IsMerlinCollected() => assassin?.NowState is AssassinMeeting.Collected;
    /// <summary>死んだアサシンの会議を待っている間は、インポスターが残っているものとして数える</summary>
    private static void CountWaitingAssassins(GameEndCounts counts)
    {
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.GetRoleClass() is Assassin a && !pc.IsAlive()
                && a.NowState is AssassinMeeting.WaitMeeting or AssassinMeeting.CallMetting or AssassinMeeting.Guessing)
                counts[CountTypes.Impostor]++;
        }
    }
    byte GuessId;
    // アサシン会議の間。全員の自分投票の能力を止めるので、試合の始めに必ず戻す (アサシンがいない試合にも残さない)
    public static bool NowUse;
    [Attributes.GameModuleInitializer]
    public static void InitStatic() => NowUse = false;
    public static List<byte> MarlinIds = new();
    public AssassinMeeting NowState;
    static Dictionary<byte, (bool isDead, bool Disconnected)> isDeadCache = new();

    static OptionItem OptionCanCallMeetingKill; static bool cancallmeetingkill;
    static OptionItem OptionHasOtherRole;
    static FilterOptionItem OptionHaveRole; static CustomRoles haverole;
    public static OptionItem OptionAssassinMeetingTime;

    public static OptionItem OptionMerlinHasTask;
    static Options.OverrideTasksData OptionMerlinNomalTask;
    public static OptionItem OptionMerlinWorkTask;
    public static OptionItem OptionMerlinCanSeeNeutral;
    public static OptionItem OptionMerlinOnlyNeutralKiller;
    public static OptionItem OptionMerlinCantSeeNeutralColor;
    public static OptionItem OptionMerlinCanSeeMadmate;
    RoleBase AddRole;
    public static Assassin assassin;
    public enum AssassinMeeting
    {
        WaitMeeting,
        CallMetting,
        Guessing,
        Collected,
        EndMeeting,
        DieWait,
        Win
    }
    static CustomRoles[] InvalidRoles()
    {
        List<CustomRoles> InvalidRoles = new();
        foreach (var data in CustomRoleManager.AllRolesInfo)
        {
            if (data.Key.IsImpostor() is false && data.Key is not CustomRoles.NotAssigned) continue;
            if (data.Key is CustomRoles.AlienHijack or CustomRoles.EvilSatellite or CustomRoles.ConnectSaver
            or CustomRoles.Limiter or CustomRoles.Assassin)
            {
                InvalidRoles.Add(data.Key);
                continue;
            }
        }
        return InvalidRoles.ToArray();
    }
    enum OptionName
    {
        AssasinCanCallMeetingKill,
        AssasinHasOtherRole,
        AssassinHaveRole,
        AssassinMeetingTime,
        WalkerWalkTaskCount,
        MerlinCanSeeNeutral,
        MerlinOnlyNeutralKiller,
        MerlinCantSeeNeutralColor,
        MerlinCanSeeMadmate,
        MerlinHasTask
    }
    public static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo);
        OptionCanCallMeetingKill = BooleanOptionItem.Create(RoleInfo, OptionName.AssasinCanCallMeetingKill, false, false);
        OptionHasOtherRole = BooleanOptionItem.Create(RoleInfo, OptionName.AssasinHasOtherRole, false, false);
        OptionHaveRole = FilterOptionItem.Create(RoleInfo, OptionName.AssassinHaveRole, 0, false, OptionHasOtherRole, true, false, false, false, false, () => InvalidRoles());
        OptionAssassinMeetingTime = IntegerOptionItem.Create(RoleInfo, OptionName.AssassinMeetingTime, new(10, 180, 1), 35, false).SetValueFormat(OptionFormat.Seconds);

        ObjectOptionitem.Create(RoleInfo, "AssassinMerlin", true, null).SetColor(Merlin.RoleInfo.RoleColor);
        OptionMerlinHasTask = BooleanOptionItem.Create(RoleInfo, OptionName.MerlinHasTask, false, false);
        OptionMerlinNomalTask = Options.OverrideTasksData.Create(RoleInfo, rolename: CustomRoles.Merlin, tasks: (true, 2, 0, 0), OptionMerlinHasTask);
        OptionMerlinWorkTask = IntegerOptionItem.Create(RoleInfo, OptionName.WalkerWalkTaskCount, (0, 99, 1), 6, false, OptionMerlinHasTask);
        OptionMerlinCanSeeNeutral = BooleanOptionItem.Create(RoleInfo, OptionName.MerlinCanSeeNeutral, false, false);
        OptionMerlinCanSeeMadmate = BooleanOptionItem.Create(RoleInfo, OptionName.MerlinCanSeeMadmate, true, false, OptionMerlinCanSeeNeutral);
        OptionMerlinOnlyNeutralKiller = BooleanOptionItem.Create(RoleInfo, OptionName.MerlinOnlyNeutralKiller, false, false, OptionMerlinCanSeeNeutral);
        OptionMerlinCantSeeNeutralColor = BooleanOptionItem.Create(RoleInfo, OptionName.MerlinCantSeeNeutralColor, true, false, OptionMerlinCanSeeNeutral);
    }
    public override void Add()
    {
        if (OptionHasOtherRole.GetBool())
        {
            haverole = OptionHaveRole.GetRole();
            if (CustomRoleManager.AllRolesInfo.TryGetValue(haverole, out var roleinfo))
            {
                AddRole = CreateInnerRole(roleinfo);
                Logger.Info($"Assasin + {haverole}", "Assasin");
            }
        }
        else
        {
            AddRole = null;
            haverole = CustomRoles.NotAssigned;
        }
        AddRole?.Add();
        assassin = this;
        Player.AddDoubleTrigger();
    }
    public override void OnDestroy()
    {
        if (AddRole is not null)
        {
            DestroyInnerRole(AddRole);
        }
        AddRole = null;
        haverole = CustomRoles.NotAssigned;
        NowUse = false;
        NowState = AssassinMeeting.EndMeeting;
        MarlinIds = new();
        isDeadCache = new();
        GuessId = byte.MaxValue;
        assassin = null;
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        // 会議中に推測などで死んだ時は、追放画面が終わってタスクに戻るまで待つ。
        // (会議が終わった瞬間に開くと、前の会議の後片付けとアサシン会議の準備が重なって、試合が終わらず暗転していた)
        if (NowState is not AssassinMeeting.WaitMeeting || player.IsAlive() || player == null || GameStates.IsMeeting
            || GameStates.CalledMeeting || GameStates.ExiledAnimate || !GameStates.IsInTask
            || CustomWinnerHolder.WinnerTeam is not CustomWinner.Default)
        {
            AddRole?.OnFixedUpdate(player);
            return;
        }
        if (!cancallmeetingkill)
        {
            NowState = AssassinMeeting.EndMeeting;
            return;
        }
        if (NowState is AssassinMeeting.WaitMeeting)
        {
            NowState = AssassinMeeting.CallMetting;
            Logger.Info("死んじゃった。", "Assassin");

            //この先ホストのみ実行
            if (!AmongUsClient.Instance.AmHost)
            {
                NowState = AssassinMeeting.Guessing;
                return;
            }

            // アサシン会議の段取りなので役職が外れても走らせる
            _ = new LateTask(() =>
            {
                // 待つ 1 秒の間に別の会議が始まった時は、全員を生きているとして送らず、その会議が明けてから開き直す
                // (会議中に送ると、その会議の死者が生きて見え、控えの生死が次の会議まで残る)
                if (GameStates.IsMeeting || GameStates.CalledMeeting || GameStates.ExiledAnimate || !GameStates.IsInTask
                    || CustomWinnerHolder.WinnerTeam is not CustomWinner.Default)
                {
                    if (NowState is AssassinMeeting.CallMetting) NowState = AssassinMeeting.WaitMeeting;
                    return;
                }
                // 待つ間にアサシンが抜けた時は開かない (全員を生きているとして送った後、開く人がいない)
                if (HasLeft())
                {
                    NowState = AssassinMeeting.EndMeeting;
                    return;
                }
                MyState.IsDead = false;
                foreach (var info in GameData.Instance.AllPlayers)
                {
                    if (info.PlayerId == Player.PlayerId)
                    {
                        isDeadCache[info.PlayerId] = (false, false);

                        info.IsDead = false;
                        info.Disconnected = false;
                        continue;
                    }
                    isDeadCache[info.PlayerId] = (PlayerState.GetByPlayerId(info.PlayerId).IsDead, info.Disconnected);

                    info.IsDead = false;
                    info.Disconnected = false;
                }
                NowUse = true;
                NowState = AssassinMeeting.Guessing;

                GameDataSerializePatch.SerializeMessageCount++;
                AntiBlackout.SendGameData();
                GameDataSerializePatch.SerializeMessageCount--;
                MyState.IsDead = false;
                _ = new LateTask(() =>
                {
                    GameDataSerializePatch.DontTouch = true;
                }, 1, "DontTuch", true);
                // アサシン会議の段取りなので役職が外れても走らせる
                _ = new LateTask(() =>
                {
                    if (AbortIfLeft()) return;
                    MyState.IsDead = false;
                    ReportDeadBodyPatch.ExReportDeadBody(Player, null, false, "AssassinMeeting", "#ff1919");
                    Utils.AllPlayerKillFlash();

                    GameDataSerializePatch.DontTouch = false;
                }, 3, "", true);
            }, 1, "");
        }
    }
    public override void OnSpawn(bool initialState = false)
    {
        AddRole?.OnSpawn(initialState);
        NowUse = false;
        if (NowState is AssassinMeeting.DieWait)
        {
            NowState = AssassinMeeting.EndMeeting;
            // 客の追放の演出の締めより前に死を送ると、暗転対策で合わせた頭数が崩れて暗転しうる
            AntiBlackout.RunAfterRevert(() =>
            {
                if (Player == null) return;
                Player.RpcExileV3(CustomDeathReason.etc, keepExisting: true);
            });
            return;
        }
        if (NowState is AssassinMeeting.Collected or AssassinMeeting.CallMetting)
        {
            //_ = new LateTask(() =>
            {
                if (NowState is AssassinMeeting.Collected)
                {
                    MyState.IsDead = false;
                    CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Impostor, Player.PlayerId, hantrole: CustomRoles.Assassin);
                    Logger.Info("まーりんぱりーん", "Assassin");
                    NowState = AssassinMeeting.Win;
                }
                else if (NowState is AssassinMeeting.CallMetting)
                {
                    MyState.IsDead = false;
                    GameDataSerializePatch.SerializeMessageCount++;
                    foreach (var info in GameData.Instance.AllPlayers)
                    {
                        if (info.PlayerId == Player.PlayerId)
                        {
                            isDeadCache[info.PlayerId] = (false, false);

                            info.IsDead = false;
                            info.Disconnected = false;
                            continue;
                        }
                        isDeadCache[info.PlayerId] = (PlayerState.GetByPlayerId(info.PlayerId).IsDead, info.Disconnected);

                        info.IsDead = false;
                        info.Disconnected = false;
                    }
                    NowUse = true;
                    NowState = AssassinMeeting.Guessing;

                    AntiBlackout.SendGameData();
                    GameDataSerializePatch.SerializeMessageCount--;
                    // アサシン会議の段取りなので役職が外れても走らせる (外れると Player は空になるので番号を控える)
                    var selfId = Player.PlayerId;
                    _ = new LateTask(() =>
                    {
                        Main.AllPlayerControls.DoIf(pc => !pc.AmOwner && pc.PlayerId != selfId && pc.GetClientId() >= 0,
                        pc => Utils.GetPlayerById(0).RpcSetRoleDesync(RoleTypes.Crewmate, pc.GetClientId()));
                    }, 2.5f, "");
                    // アサシン会議の段取りなので役職が外れても走らせる
                    _ = new LateTask(() =>
                    {//ホストだけSetRoleして復活させる。
                        if (AbortIfLeft()) return;
                        Main.AllPlayerControls.DoIf(pc => !pc.AmOwner && pc.PlayerId != Player.PlayerId && pc.GetClientId() >= 0,
                        pc => Utils.GetPlayerById(0).RpcSetRoleDesync(RoleTypes.Crewmate, pc.GetClientId()));
                        ReportDeadBodyPatch.ExReportDeadBody(Player, null, false, "AssassinMeeting", "#ff1919");
                    }, 3, "");
                }
            }//, 0.5f, "AssassinShori");
        }
    }
    bool HasLeft() => Player == null || Player.Data == null || Player.Data.Disconnected;
    /// <summary>
    /// 全員を生きているとして送った後、会議を開く前にアサシンが抜けた時は、控えた生死を戻してアサシン会議を取りやめる
    /// (抜けた人を通報者にした会議は捨てられ、全員が生き返って見えたまま残る)
    /// </summary>
    bool AbortIfLeft()
    {
        if (!HasLeft()) return false;
        Logger.Info("アサシンが抜けたのでアサシン会議を取りやめる", "Assassin");
        foreach (var info in GameData.Instance.AllPlayers)
        {
            if (info == null || !isDeadCache.TryGetValue(info.PlayerId, out var val)) continue;
            info.IsDead = val.isDead || PlayerState.GetByPlayerId(info.PlayerId)?.IsDead == true;
            info.Disconnected = val.Disconnected || PlayerState.GetByPlayerId(info.PlayerId)?.DeathReason == CustomDeathReason.Disconnected;
        }
        isDeadCache.Clear();
        NowUse = false;
        NowState = AssassinMeeting.EndMeeting;
        GameDataSerializePatch.DontTouch = false;
        GameDataSerializePatch.SerializeMessageCount++;
        AntiBlackout.SendGameData();
        GameDataSerializePatch.SerializeMessageCount--;
        // 役職名の見え方は NowState で変わるので出し直す
        Utils.NotifyRoles();
        return true;
    }
    public override void OnStartMeeting()
    {
        if (NowState is AssassinMeeting.Guessing)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            // アサシン会議の段取り (生死の控えを戻す) なので役職が外れても走らせる (外れると Player は空になるので番号を控える)
            var selfId = Player.PlayerId;
            _ = new LateTask(() =>
            {
                foreach (var info in GameData.Instance.AllPlayers)
                {
                    if (info == null) continue;
                    if (isDeadCache.TryGetValue(info.PlayerId, out var val))
                    {
                        // 控えを取った後に死んだ人 (待ちの間の遅延のキルなど) は、控えの「生きている」へ戻さない
                        info.IsDead = val.isDead
                            || (info.PlayerId != selfId && PlayerState.GetByPlayerId(info.PlayerId)?.IsDead == true);
                        info.Disconnected = val.Disconnected;
                    }
                }
                isDeadCache.Clear();

                GameDataSerializePatch.SerializeMessageCount++;
                AntiBlackout.SendGameData();
                GameDataSerializePatch.SerializeMessageCount--;

            }, 3, "Assassin-SetDie");
        }
        else AddRole?.OnStartMeeting();
    }
    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie, Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)
    {
        if (NowState is AssassinMeeting.EndMeeting or AssassinMeeting.CallMetting) return false;

        if (NowState is AssassinMeeting.Guessing)
        {
            var name = Camouflage.PlayerSkins.TryGetValue(Player.PlayerId, out var cos) ? cos.PlayerName : "^a^";
            var tage = Camouflage.PlayerSkins.TryGetValue(GuessId, out var tcos) ? tcos.PlayerName : GetString("AssassinGuessFallbackName");

            if (GuessId is not byte.MaxValue)
            {
                NowState = AssassinMeeting.DieWait;
                Player.RpcSetName(string.Format(GetString("AssassinGuessNonCollect"), tage) + "<size=0>");
                MeetingVoteManager.Voteresult = string.Format(GetString("AssassinGuessNonCollect"), tage);
                Logger.Info($"{GuessId}マーリンじゃない!", "Assassin");
            }
            if (PlayerState.GetByPlayerId(GuessId)?.GetNowMainRole() is CustomRoles.Merlin)
            {
                NowState = AssassinMeeting.Collected;
                Player.RpcSetName(string.Format(GetString("AssassinGuessCollect"), tage) + "<size=0>");
                MeetingVoteManager.Voteresult = string.Format(GetString("AssassinGuessCollect"), tage);
                Logger.Info($"{GuessId}マーリンだ!", "Assassin");
            }
            else
            {
                NowState = AssassinMeeting.DieWait;
                Player.RpcSetName(string.Format(GetString("AssassinGuessNonCollect"), tage) + "<size=0>");
                MeetingVoteManager.Voteresult = string.Format(GetString("AssassinGuessNonCollect"), tage);
            }
            Exiled = Player.Data;
            // アサシン会議の段取り (名前を戻す) なので役職が外れても走らせる
            var assassinPc = Player;
            _ = new LateTask(() => { if (assassinPc) assassinPc.RpcSetName(name); }, 6f, "AssassinSetName");
            return true;
        }
        else
            if (NowState is AssassinMeeting.WaitMeeting)
            {
                if (Exiled?.PlayerId == Player.PlayerId)
                {
                    NowState = AssassinMeeting.CallMetting;
                    Logger.Info("追放されちゃった！", "Assassin");
                    Exiled = null;
                    IsTie = false;
                    ClearAndExile = true;
                    return true;
                }
            }


        return AddRole?.VotingResults(ref Exiled, ref IsTie, vote, mostVotedPlayers, ClearAndExile) ?? false;
    }
    public override bool CheckAnyVote(byte votedForId, PlayerControl voter)
    {
        if (NowState is not AssassinMeeting.Guessing) return AddRole?.CheckAnyVote(votedForId, voter) ?? true;

        if (!Is(voter)) return false;
        if (votedForId is MeetingVoteManager.Skip
        || votedForId == Player.PlayerId
        || (votedForId == PlayerControl.LocalPlayer.PlayerId && Options.EnableGM.GetBool())) return false;

        GuessId = votedForId;
        Logger.Info($"{Utils.GetPlayerById(votedForId)?.Data?.PlayerName ?? "???"} はマーリンかな?", "Assassin");
        MeetingVoteManager.Instance.ClearAndExile(Player.PlayerId, Player.PlayerId);
        return true;
    }
    public override void OverrideShowMainRoleText(ref Color roleColor, ref string roleText)
    {
        if (AddRole is null) return;

        roleText += GetString($"{haverole}");
    }

    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => AddRole?.OnEnterVent(physics, ventId) ?? true;
    public override bool CanVentMoving(PlayerPhysics physics, int ventId) => AddRole?.CanVentMoving(physics, ventId) ?? true;
    public override bool CanUseAbilityButton() => AddRole?.CanUseAbilityButton() ?? false;
    public override void ApplyGameOptions(IGameOptions opt) => AddRole?.ApplyGameOptions(opt);
    public override bool OnCheckMurderAsTarget(MurderInfo info) => AddRole?.OnCheckMurderAsTarget(info) ?? true;
    public override void OnMurderPlayerAsTarget(MurderInfo info) => AddRole?.OnMurderPlayerAsTarget(info);
    public override void OnRevived() => AddRole?.OnRevived();
    public override void OnShapeshift(PlayerControl target) => AddRole?.OnShapeshift(target);
    public override bool OnCheckShapeshift(PlayerControl target, ref bool shouldAnimate) => AddRole?.OnCheckShapeshift(target, ref shouldAnimate) ?? true;
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => AddRole?.OnReportDeadBody(reporter, target);
    public override bool CanClickUseVentButton => AddRole?.CanClickUseVentButton ?? true;
    public override string MeetingAddMessage() => AddRole?.MeetingAddMessage() ?? "";
    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional) => AddRole?.ModifyVote(voterId, sourceVotedForId, isIntentional) ?? (null, null, true);
    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner) => AddRole?.OnExileWrapUp(exiled, ref DecidedWinner);
    public override void AfterMeetingTasks() => AddRole?.AfterMeetingTasks();
    public override void OnIntroEnd() => AddRole?.OnIntroEnd();
    public override bool OnInvokeSabotage(SystemTypes systemType) => AddRole?.OnInvokeSabotage(systemType) ?? true;
    public override bool OnSabotage(PlayerControl player, SystemTypes systemType) => AddRole?.OnSabotage(player, systemType) ?? true;
    public override void AfterSabotage(SystemTypes systemType) => AddRole?.AfterSabotage(systemType);
    public override bool NeedsSeenNames => AddRole?.NeedsSeenNames ?? false;
    public override bool ShowsRoleTo(PlayerControl seer, bool isMeeting)
        => AddRole?.ShowsRoleTo(seer, isMeeting) ?? false;
    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)
        => AddRole?.OverrideDisplayRoleNameAsSeen(seer, isMeeting, ref view);
    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen, bool isMeeting, ref TownOfHostL.Core.RoleNameView view)
    {
        if (NowState is AssassinMeeting.WaitMeeting or AssassinMeeting.CallMetting or AssassinMeeting.Guessing && !Is(seen))
            view.Enabled &= seen.Is(CustomRoles.UltraStar);

    }
    public override void OverrideProgressTextAsSeer(PlayerControl seen, ref bool enabled, ref string text)
    => AddRole?.OverrideProgressTextAsSeer(seen, ref enabled, ref text);
    public override void OverrideProgressTextAsSeen(PlayerControl seer, ref bool enabled, ref string text)
    => AddRole?.OverrideProgressTextAsSeen(seer, ref enabled, ref text);
    public override string GetProgressText(bool comms = false) => (AddRole?.GetProgressText(comms) ?? AddRole?.GetProgressText(comms));
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false) => AddRole?.GetMark(seer, seen, isForMeeting);
    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false) => AddRole?.GetLowerText(seer, seen, isForMeeting, isForHud);
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false) => AddRole?.GetSuffix(seer, seen, isForMeeting);
    public override string GetAbilityButtonText() => AddRole?.GetAbilityButtonText();
    public override bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportReason reason)
    {
        if (NowState is AssassinMeeting.CallMetting)
        {
            reason = DontReportReason.wait;
            return true;
        }
        return AddRole?.CancelReportDeadBody(reporter, target, ref reason) ?? false;
    }
    public override CustomRoles TellResults(PlayerControl player) => AddRole?.TellResults(player) ?? CustomRoles.NotAssigned;
    public override RoleTypes? AfterMeetingRole => AddRole?.AfterMeetingRole ?? null;
    public override void CheckWinner(GameOverReason reason) => AddRole?.CheckWinner(reason);
    public override void OnPlayerDead(PlayerControl player) => AddRole?.OnPlayerDead(player);
    public override void OnPlayerLeft(byte playerId) => AddRole?.OnPlayerLeft(playerId);
    public override void OnAddedMidGame() => AddRole?.OnAddedMidGame();
    public override void OnFixSabotage(PlayerControl player, SystemTypes systemType, byte amount) => AddRole?.OnFixSabotage(player, systemType, amount);
    public override void OnVentilationSystemUpdate(PlayerControl user, VentilationSystem.Operation operation, int ventId) => AddRole?.OnVentilationSystemUpdate(user, operation, ventId);
    public override bool CanTask() => AddRole?.CanTask() ?? base.CanTask();


    bool IImpostor.CanBeLastImpostor => AddRole is IImpostor impostor ? impostor.CanBeLastImpostor : true;
    public bool CanKill => AddRole is IImpostor impostor ? impostor.CanKill : true;
    public bool IsKiller => AddRole is IImpostor impostor ? impostor.IsKiller : true;
    public bool CanUseKillButton() => AddRole is IImpostor impostor ? impostor.CanUseKillButton() : CanKill;
    public float CalculateKillCooldown() => AddRole is IImpostor impostor ? impostor.CalculateKillCooldown() : Options.DefaultKillCooldown;
    public bool CanUseSabotageButton() => AddRole is IImpostor impostor ? impostor.CanUseSabotageButton() : true;
    public bool CanUseImpostorVentButton() => AddRole is IImpostor impostor ? impostor.CanUseImpostorVentButton() : true;
    // 中の役職のキル以外の押しも、守りより前に振り分ける。2 回押しが押しを取る時は中の役職へ回さない (キルの判定がそちらを先に振り分ける)
    public bool OnCheckMurderBeforeGuards(MurderInfo info)
        => AddRole is not IImpostor impostor || (Player.CanDoubleTrigger() && CheckAction) || impostor.OnCheckMurderBeforeGuards(info);
    public void OnCheckMurderAsKiller(MurderInfo info) { if (AddRole is IImpostor impostor) impostor.OnCheckMurderAsKiller(info); }
    public void OnCheckMurderDontKill(MurderInfo info) { if (AddRole is IImpostor impostor) impostor.OnCheckMurderDontKill(info); }
    public void OnMurderPlayerAsKiller(MurderInfo info) { if (AddRole is IImpostor impostor) impostor.OnMurderPlayerAsKiller(info); }
    public bool OverrideKillButtonText(out string text)
    {
        if ((AddRole as IImpostor)?.OverrideKillButtonText(out var killbuttontext) is true)
        {
            text = killbuttontext;
            return true;
        }
        text = default;
        return false;
    }

    // 中の役職の能力・表示の窓口 (渡さないと、中の役職の能力が働かない・残り回数が出ない)
    public override bool RevengesOnExile => AddRole?.RevengesOnExile ?? false;
    public override bool CanRevengeOn(PlayerControl candidate) => AddRole?.CanRevengeOn(candidate) ?? true;
    public override bool ShowsRoleColorToAll(bool isMeeting) => AddRole?.ShowsRoleColorToAll(isMeeting) ?? false;
    public override int? UsesRemaining => AddRole?.UsesRemaining;
    public override bool UsesOnKillButton => AddRole?.UsesOnKillButton ?? base.UsesOnKillButton;
    bool IUsePetButton.UsesPet => AddRole is IUsePetButton pet && pet.UsesPet;
    void IUsePetButton.OnPet() { if (AddRole is IUsePetButton pet) pet.OnPet(); }
    bool IDissolvingKiller.DissolvesBody(PlayerControl target) => AddRole is IDissolvingKiller dissolver && dissolver.DissolvesBody(target);
    bool ITeammateKiller.CanKillTeammates => AddRole is ITeammateKiller teammateKiller && teammateKiller.CanKillTeammates;
    bool IUsePhantomButton.SyncAbilityCooldownWithKillCooldown => AddRole is IUsePhantomButton synced && synced.SyncAbilityCooldownWithKillCooldown;
    void IUsePhantomButton.SetSyncedAbilityCooldown(float cooldown) { if (AddRole is IUsePhantomButton synced) synced.SetSyncedAbilityCooldown(cooldown); }
    public override bool OnCheckVanish(ref float killCooldown, ref bool canResetAbilityCooldown) => AddRole?.OnCheckVanish(ref killCooldown, ref canResetAbilityCooldown) ?? true;
    public override bool KnowsRoleOf(PlayerControl seen, bool isMeeting) => AddRole?.KnowsRoleOf(seen, isMeeting) ?? false;
    public override bool OnCheckPressAsTarget(MurderInfo info) => AddRole?.OnCheckPressAsTarget(info) ?? true;
    bool IUsePhantomButton.IsPhantomRole => AddRole is IUsePhantomButton iusephantom && iusephantom?.IsPhantomRole is true;
    bool IUsePhantomButton.UseOneclickButton => AddRole is IUsePhantomButton iusephantom && iusephantom?.UseOneclickButton is true;
    bool IUsePhantomButton.IsresetAfterKill => AddRole is IUsePhantomButton iusephantom && iusephantom.IsresetAfterKill;
    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        if (AddRole is IUsePhantomButton iusephantom)
        {
            iusephantom.OnClick(ref AdjustKillCooldown, ref ResetCooldown);
        }
    }
    public bool CheckAction => (AddRole as IDoubleTrigger)?.CheckAction ?? false;
    public bool SingleAction(PlayerControl killer, PlayerControl target)
    {
        return (AddRole as IDoubleTrigger)?.SingleAction(killer, target) ?? true;
    }

    public bool DoubleAction(PlayerControl killer, PlayerControl target)
    {
        return (AddRole as IDoubleTrigger)?.DoubleAction(killer, target) ?? true;
    }
    public bool? CheckKillFlash(MurderInfo info) => (AddRole as IKillFlashSeeable)?.CheckKillFlash(info) ?? false;
}
