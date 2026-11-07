using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Hazel;
using Il2CppSystem.Text;

using AmongUs.GameOptions;
using TownOfHostL.Attributes;
using TownOfHostL.Roles.Core.Interfaces;
using TownOfHostL.Roles.Crewmate;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Neutral;
using TownOfHostL.Roles.Unit;
using TownOfHostL.Roles.AddOns.Common;
using TownOfHostL.Roles.AddOns;

namespace TownOfHostL.Roles.Core;

public static partial class CustomRoleManager
{
    public static Type[] AllRolesClassType;
    // CustomRolesHelper の static 初期化は RoleCatalog 経由でここを引くので、この初期化から CustomRolesHelper を読まない
    public static Dictionary<CustomRoles, SimpleRoleInfo> AllRolesInfo = new();
    public static Dictionary<byte, RoleBase> AllActiveRoles = new(15);

    public static SimpleRoleInfo GetRoleInfo(this CustomRoles role) => AllRolesInfo.TryGetValue(role, out var info) ? info : null;
    public static RoleBase GetRoleClass(this PlayerControl player) => GetByPlayerId(player.PlayerId);
    public static RoleBase GetByPlayerId(byte playerId) => AllActiveRoles.TryGetValue(playerId, out var roleBase) ? roleBase : null;
    public static void Do<T>(this List<T> list, Action<T> action) => list.ToArray().Do(action);
    // == CheckMurder関連処理 ==
    public static Dictionary<byte, MurderInfo> CheckMurderInfos = new();
    /// <summary>
    ///
    /// </summary>
    /// <param name="attemptKiller">実際にキルを行ったプレイヤー 不変</param>
    /// <param name="attemptTarget">>Killerが実際にキルを行おうとしたプレイヤー 不変</param>
    public static bool OnCheckMurder(PlayerControl attemptKiller, PlayerControl attemptTarget, bool killerMyselfInvoke = false)
        => OnCheckMurder(attemptKiller, attemptTarget, attemptKiller, attemptTarget, killerMyselfInvoke);
    /// <summary>
    ///
    /// </summary>
    /// <param name="attemptKiller">実際にキルを行ったプレイヤー 不変</param>
    /// <param name="attemptTarget">>Killerが実際にキルを行おうとしたプレイヤー 不変</param>
    /// <param name="appearanceKiller">見た目上でキルを行うプレイヤー 可変</param>
    /// <param name="appearanceTarget">見た目上でキルされるプレイヤー 可変</param>
    /// <param name="killerMyselfInvoke">自身で呼び出すときはxxいくつかの処理をスキップ</param>
    public static bool OnCheckMurder(
        PlayerControl attemptKiller, PlayerControl attemptTarget,
        PlayerControl appearanceKiller, PlayerControl appearanceTarget,
        bool killerMyselfInvoke = false)
        => OnCheckMurder(attemptKiller, attemptTarget, appearanceKiller, appearanceTarget, killerMyselfInvoke, false, 1, CustomDeathReason.Kill);
    /// <summary>hamo の形のキルチェック。force はキラーのキルチェックを飛ばす、DontRoleAbility は相手の能力を使わせない</summary>
    public static bool OnCheckMurder(
        PlayerControl attemptKiller, PlayerControl attemptTarget,
        PlayerControl appearanceKiller, PlayerControl appearanceTarget,
        bool? force, bool? DontRoleAbility = false, int Killpower = 1,
        CustomDeathReason deathReason = CustomDeathReason.Kill)
    {
        var killerMyselfInvoke = force == true;
        Logger.Info($"Attempt  :{attemptKiller.GetNameWithRole()} => {attemptTarget.GetNameWithRole()}", "CheckMurder");
        if (appearanceKiller != attemptKiller || appearanceTarget != attemptTarget)
            Logger.Info($"Apperance:{appearanceKiller.GetNameWithRole()} => {appearanceTarget.GetNameWithRole()}", "CheckMurder");

        var info = new MurderInfo(attemptKiller, attemptTarget, appearanceKiller, appearanceTarget, DontRoleAbility, Killpower, 0, deathReason);
        var hamoGuardian = false;

        appearanceKiller.ResetKillCooldown();

        // 無効なキルをブロックする処理 必ず最初に実行する
        if (!CheckMurderPatch.CheckForInvalidMurdering(info, killerMyselfInvoke)) return false;

        // インポスター同士のキル無効
        if (attemptKiller.GetCustomRole().IsImpostor() && attemptTarget.GetCustomRole().IsImpostor() && !attemptKiller.Is(CustomRoles.StrayWolf)) return false;
        // ゴッドファーザーのロックキル
        if (CustomRoles.Godfather.IsPresent() && GodfatherAndJanitor.JanitorTarget.Contains(attemptKiller.PlayerId)) return false;

        var killerRole = attemptKiller.GetRoleClass();
        var targetRole = attemptTarget.GetRoleClass();

        // キラーがキル能力持ちなら
        if (killerRole is IKiller killer)
        {
            if (killer.IsKiller)
            {
                // イビルディバイナーのみ占いのためここで先に処理
                if (killerRole is EvilDiviner && !EvilDiviner.OnCheckMurder(info)) return false;
                // ガーディング属性によるガード
                if (Guarding.OnCheckMurder(info)) { }
                // メディックの対象プレイヤー
                else if (Medic.GuardPlayerCheckMurder(info)) { }
                // 背徳者の対象プレイヤー
                else if (Immoralist.GuardPlayerCheckMurder(info)) { }
                // ターゲットのキルチェック処理実行
                else if (targetRole != null && info.DontRoleAbility is not true)
                {
                    if (!targetRole.OnCheckMurderAsTarget(info))
                    {
                        killer.OnCheckMurderDontKill(info);
                        return false;
                    }
                }
                // 守護者 (hamo) のガード
                if (info.KillPower > info.GuardPower && TownOfHostL.Roles.Hamo.Crewmate.Guardian.FindGuardianFor(attemptTarget.PlayerId) != null)
                {
                    info.GuardPower = 1;
                    hamoGuardian = true;
                }
            }
            // キラーのキルチェック処理実行 (hamo のダブルトリガーを先に見る)
            if (!HamoDoubleTrigger.OnCheckMurderAsKiller(info) && !killerMyselfInvoke)
            {
                killer.OnCheckMurderAsKiller(info);
            }
            // hamo のガードの力によるガード
            if (info.KillPower <= info.GuardPower && killer.IsKiller)
            {
                info.IsGuard = true;
                info.CanKill = false;
                if (hamoGuardian)
                {
                    var guardian = TownOfHostL.Roles.Hamo.Crewmate.Guardian.FindGuardianFor(attemptTarget.PlayerId);
                    guardian?.Sacrifice(attemptTarget);
                    UtilsGameLog.AddGameLog("Guardian", UtilsName.GetPlayerColor(attemptTarget) + ":  " + string.Format(Translator.GetString("GuardMaster.Guard"), UtilsName.GetPlayerColor(attemptKiller, true)));
                }
                attemptKiller.RpcProtectedMurderPlayer(attemptTarget);
                Utils.NotifyRoles();
                killer.OnCheckMurderDontKill(info);
                return false;
            }
        }
        // CC
        if (Options.IsCCMode)
        {
            CatchCat.LeaderPlayer.OnCheckMurder(info);
            CatchCat.CatPlayer.OnCheckMurder(info);
        }
        // ON
        //if (Options.IsONMode && attemptTarget.Is(CustomRoles.ONPhantomThief))
        //{
        //    ONPhantomThief.OnCheckMurderTarget(info);
        //}

        //キル可能だった場合のみMurderPlayerに進む
        if (info.CanKill && info.DoKill)
        {
            //MurderPlayer用にinfoを保存
            CheckMurderInfos[appearanceKiller.PlayerId] = info;
            bool killerIsViper = appearanceKiller.Is(CustomRoles.Viper) || appearanceKiller.Is(CustomRoles.NormalViper);
            if (killerIsViper)
            {
                var capKiller = appearanceKiller;
                var capTarget = appearanceTarget;
                _ = new LateTask(() =>
                {
                    capKiller.RpcMurderPlayer(capTarget);
                }, 0.2f, "ViperMurderDelay");
            }
            else if (TownOfHostL.Modules.DeadBodyEraser.TakeNoBody(appearanceTarget))
            {
                TownOfHostL.Modules.DeadBodyEraser.ExileInsteadOfMurder(appearanceKiller, appearanceTarget);
            }
            else
            {
                appearanceKiller.RpcMurderPlayer(appearanceTarget);
            }
            return true;
        }
        else
        {
            if (!info.CanKill) Logger.Info($"{appearanceTarget.GetNameWithRole()}をキル出来ない。", "CheckMurder");
            if (!info.DoKill) Logger.Info($"{appearanceKiller.GetNameWithRole()}はキルしない。", "CheckMurder");
            return false;
        }
    }
    /// <summary>
    /// MurderPlayer実行後の各役職処理
    /// </summary>
    /// <param name="appearanceKiller">見た目上でキルを行うプレイヤー 可変</param>
    /// <param name="appearanceTarget">見た目上でキルされるプレイヤー 可変</param>
    public static void OnMurderPlayer(PlayerControl appearanceKiller, PlayerControl appearanceTarget)
    {
        //MurderInfoの取得
        if (CheckMurderInfos.TryGetValue(appearanceKiller.PlayerId, out var info))
        {
            //参照出来たら削除
            CheckMurderInfos.Remove(appearanceKiller.PlayerId);
        }
        else
        {
            //CheckMurderを経由していない場合はappearanceで処理
            info = new MurderInfo(appearanceKiller, appearanceTarget, appearanceKiller, appearanceTarget);
        }

        (var attemptKiller, var attemptTarget) = info.AttemptTuple;

        Logger.Info($"Real Killer={attemptKiller.GetNameWithRole()}", "MurderPlayer");

        //キラーの処理
        (attemptKiller.GetRoleClass() as IKiller)?.OnMurderPlayerAsKiller(info);

        //ターゲットの処理
        var targetRole = attemptTarget.GetRoleClass();
        if (targetRole != null)
            targetRole.OnMurderPlayerAsTarget(info);

        //その他視点の処理があれば実行
        foreach (var onMurderPlayer in OnMurderPlayerOthers.ToArray())
        {
            onMurderPlayer(info);
        }

        if (info.IsMeeting)
        {
            Lovers.VoteSuicide(attemptTarget.PlayerId);
            Janitor.VoteSuicide(attemptTarget.PlayerId);
        }
        else
        {
            Lovers.KillSuicide(attemptTarget.PlayerId);
            Janitor.KillSuicide(attemptTarget.PlayerId);
        }

        //TargetDeadArrow
        if (!info.IsMeeting) TargetDeadArrow.UpdateDeadBody();
        //WinTask
        VentEnterTask.TaskWinCountAllComplete(attemptTarget.PlayerId);

        //以降共通処理
        var targetState = PlayerState.GetByPlayerId(attemptTarget.PlayerId);
        if (targetState.DeathReason == CustomDeathReason.etc)
        {
            //死因が設定されていない場合は死亡判定
            targetState.DeathReason = CustomDeathReason.Kill;
        }

        targetState.SetDead();
        attemptTarget.SetRealKiller(attemptKiller, true);

        //ホストの死後タスク免除
        if (attemptTarget == PlayerControl.LocalPlayer && Options.HostGhostIgnoreTasks.GetBool()
            && !attemptTarget.Is(CustomRoles.Gang) && Utils.HasTasks(attemptTarget.Data))
        {
            var taskState = PlayerControl.LocalPlayer.GetPlayerTaskState();
            taskState.CompletedTasksCount = taskState.AllTasksCount;
            Logger.Info($"{PlayerControl.LocalPlayer.GetNameWithRole()}: TaskCounts = {taskState.CompletedTasksCount}/{taskState.AllTasksCount}", "OnMurderPlayer/HostGhostIgnoreTasks");

            foreach (var task in PlayerControl.LocalPlayer.Data.Tasks)
            {
                if (!task.Complete)
                {
                    task.Complete = true;
                    GameData.Instance.CompletedTasks++;
                }
            }
            Logger.Info($"{PlayerControl.LocalPlayer.GetNameWithRole()}.Dead TotalTaskCounts = {GameData.Instance.CompletedTasks}/{GameData.Instance.TotalTasks}", "OnMurderPlayer/HostGhostIgnoreTasks");
        }

        Utils.CountAlivePlayers(true);

        Utils.TargetDies(info);
                
        if (!AmongUsClient.Instance.IsGameOver)
        {
            Utils.SyncAllSettings();
            Utils.NotifyRoles();
        }
    }
    /// <summary>
    /// その他視点からのMurderPlayer処理
    /// 初期化時にOnMurderPlayerOthers+=で登録
    /// </summary>
    public static HashSet<Action<MurderInfo>> OnMurderPlayerOthers = new();

    public static void OnFixedUpdate(PlayerControl player)
    {
        if (GameStates.IsInTask)
        {
            player.GetRoleClass()?.OnFixedUpdate(player);
            //その他視点処理があれば実行
            foreach (var onFixedUpdate in OnFixedUpdateOthers)
            {
                onFixedUpdate(player);
            }
        }
    }
    /// <summary>
    /// タスクターンに常時呼ばれる関数
    /// 他役職への干渉用
    /// Host以外も呼ばれるので注意
    /// 初期化時にOnFixedUpdateOthers+=で登録
    /// </summary>
    public static HashSet<Action<PlayerControl>> OnFixedUpdateOthers = new();

    public static bool OnSabotage(PlayerControl player, SystemTypes systemType)
    {
        bool cancel = false;
        foreach (var roleClass in AllActiveRoles.Values)
        {
            if (!roleClass.OnSabotage(player, systemType))
            {
                cancel = true;
            }
        }
        return !cancel;
    }
    // ==初期化関連処理 ==
    [GameModuleInitializer]
    public static void Initialize()
    {
        AllRolesInfo.Do(kvp => kvp.Value.IsEnable = kvp.Key.IsEnable());
        AllActiveRoles.Clear();
        MarkOthers.Clear();
        LowerOthers.Clear();
        SuffixOthers.Clear();
        CheckMurderInfos.Clear();
        OnMurderPlayerOthers.Clear();
        OnFixedUpdateOthers.Clear();
        OnEnterVentOthers.Clear();
    }
    public static void CreateInstance()
    {
        foreach (var pc in Main.AllPlayerControls)
        {
            CreateInstance(pc.GetCustomRole(), pc);
        }
    }
    public static void CreateInstance(CustomRoles role, PlayerControl player)
    {
        if (AllRolesInfo.TryGetValue(role, out var roleInfo))
        {
            var roleClass = roleInfo.CreateInstance(player);
            roleClass.Add();
            // hamo: 途中で役職が変わった時・自投票・部屋タスク
            if (Main.introDestroyed && GameStates.IsInGame && roleClass is HamoRoleBase hamoRole) hamoRole.ChengeRoleAdd();
            if (roleClass is TownOfHostL.Roles.Core.Interfaces.ISelfVoter selfVoter) selfVoter.AddSelfVoter(player);
            if (roleClass is TownOfHostL.Roles.Core.Interfaces.IRoomTasker roomTasker) roomTasker.AddRoomTaker(player.PlayerId);
        }
        //AddonAdd
        {
            foreach (var subRole in player.GetCustomSubRoles())
            {
                SubRoleAdd(player.PlayerId, subRole);
            }
        }
        if (player.Data.Role.Role == RoleTypes.Shapeshifter)
        {
            Main.CheckShapeshift.TryAdd(player.PlayerId, false);
        }
    }
    public static void SubRoleAdd(byte playerId, CustomRoles subRole)
        => AddOnList.Get(subRole)?.OnAdd?.Invoke(playerId);
    /// <summary>
    /// 受信したRPCから送信先を読み取ってRoleClassに配信する
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="rpcType"></param>
    public static void DispatchRpc(MessageReader reader, CustomRPC rpcType)
    {
        var playerId = reader.ReadByte();
        GetByPlayerId(playerId)?.ReceiveRPC(reader, rpcType);
    }
    //NameSystem
    public static HashSet<Func<PlayerControl, PlayerControl, bool, string>> MarkOthers = new();
    public static HashSet<Func<PlayerControl, PlayerControl, bool, bool, string>> LowerOthers = new();
    public static HashSet<Func<PlayerControl, PlayerControl, bool, string>> SuffixOthers = new();
    /// <summary>
    /// seer,seenが役職であるかに関わらず発動するMark
    /// 登録されたすべてを結合する。
    /// </summary>
    /// <param name="seer">見る側</param>
    /// <param name="seen">見られる側</param>
    /// <param name="isForMeeting">会議中フラグ</param>
    /// <returns>結合したMark</returns>
    public static string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        var sb = new StringBuilder(100);
        foreach (var marker in MarkOthers)
        {
            sb.Append(marker(seer, seen, isForMeeting));
        }
        return sb.ToString();
    }
    /// <summary>
    /// seer,seenが役職であるかに関わらず発動するLowerText
    /// 登録されたすべてを結合する。
    /// </summary>
    /// <param name="seer">見る側</param>
    /// <param name="seen">見られる側</param>
    /// <param name="isForMeeting">会議中フラグ</param>
    /// <param name="isForHud">ModでHudとして表示する場合</param>
    /// <returns>結合したLowerText</returns>
    public static string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        var sb = new StringBuilder(100);
        foreach (var lower in LowerOthers)
        {
            sb.Append(lower(seer, seen, isForMeeting, isForHud));
        }
        return sb.ToString();
    }
    /// <summary>
    /// seer,seenが役職であるかに関わらず発動するSuffix
    /// 登録されたすべてを結合する。
    /// </summary>
    /// <param name="seer">見る側</param>
    /// <param name="seen">見られる側</param>
    /// <param name="isForMeeting">会議中フラグ</param>
    /// <returns>結合したSuffix</returns>
    public static string GetSuffixOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        var sb = new StringBuilder(100);
        foreach (var suffix in SuffixOthers)
        {
            sb.Append(suffix(seer, seen, isForMeeting));
        }
        return sb.ToString();
    }
    /// <summary>
    /// オブジェクトの破棄
    /// </summary>
    public static void Dispose()
    {
        Logger.Info($"Dispose ActiveRoles", "CustomRoleManager");
        MarkOthers.Clear();
        LowerOthers.Clear();
        SuffixOthers.Clear();
        CheckMurderInfos.Clear();
        OnMurderPlayerOthers.Clear();
        OnFixedUpdateOthers.Clear();
        OnEnterVentOthers.Clear();

        AllActiveRoles.Values.ToArray().Do(roleClass => roleClass.Dispose());
    }
}
public class MurderInfo
{
    /// <summary>実際にキルを行ったプレイヤー 不変</summary>
    public PlayerControl AttemptKiller { get; }
    /// <summary>Killerが実際にキルを行おうとしたプレイヤー 不変</summary>
    public PlayerControl AttemptTarget { get; }
    /// <summary>見た目上でキルを行うプレイヤー 可変</summary>
    public PlayerControl AppearanceKiller { get; set; }
    /// <summary>見た目上でキルされるプレイヤー 可変</summary>
    public PlayerControl AppearanceTarget { get; set; }

    /// <summary>
    /// targetがキル出来るか
    /// </summary>
    public bool CanKill = true;
    /// <summary>
    /// Killerが実際にキルするか
    /// </summary>
    public bool DoKill = true;
    /// <summary>
    ///転落死など事故の場合(キラー不在)
    /// </summary>
    public bool IsAccident = false;

    // 分解用 (killer, target) = info.AttemptTuple; のような記述でkillerとtargetをまとめて取り出せる
    public (PlayerControl killer, PlayerControl target) AttemptTuple => (AttemptKiller, AttemptTarget);
    public (PlayerControl killer, PlayerControl target) AppearanceTuple => (AppearanceKiller, AppearanceTarget);
    /// <summary>
    /// 本来の自殺
    /// </summary>
    public bool IsSuicide => AttemptKiller.PlayerId == AttemptTarget.PlayerId;
    /// <summary>
    /// 遠距離キル代わりの疑似自殺
    /// </summary>
    public bool IsFakeSuicide => AppearanceKiller.PlayerId == AppearanceTarget.PlayerId;
    /// <summary>
    ///会議中キルの場合
    /// </summary>
    public bool IsMeeting => GameStates.IsMeeting;
    // hamo のキルの力・ガードの力
    public bool IsGuard = false;
    public int KillPower = 1;
    public int GuardPower = 0;
    /// <summary>true なら相手の役職の能力を使わせない</summary>
    public bool? DontRoleAbility = false;
    public UnityEngine.Vector2 killerpos;
    public CustomDeathReason DeathReason;
    public bool CheckHasGuard() => KillPower <= GuardPower;
    public bool IsCanKilling => !CheckHasGuard() && !IsSuicide && !IsFakeSuicide && DoKill && CanKill && !IsAccident;
    public MurderInfo(PlayerControl attemptKiller, PlayerControl attemptTarget, PlayerControl appearanceKiller, PlayerControl appearancetarget, bool? DontRoleAbility = false, int Killpower = 1, int guardpower = 0, CustomDeathReason deathReason = CustomDeathReason.Kill)
    {
        AttemptKiller = attemptKiller;
        AttemptTarget = attemptTarget;
        AppearanceKiller = appearanceKiller;
        AppearanceTarget = appearancetarget;
        this.DontRoleAbility = DontRoleAbility;
        killerpos = appearanceKiller.transform.position;
        KillPower = Killpower;
        GuardPower = guardpower;
        DeathReason = deathReason;
    }
}

public enum CustomRoles
{
    NotAssigned = -1,

    //Default
    Crewmate = 0,
    //Impostor(Vanilla)
    Impostor,
    Shapeshifter,
    Phantom,
    Viper,
    //Impostor
    NormalImpostor,
    NormalShapeshifter,
    NormalPhantom,
    NormalViper,
    CustomImpostor,
    EvilWatcher,
    BountyHunter,
    FireWorks,
    Mafia,
    SerialKiller,
    ShapeMaster,
    Sniper,
    Vampire,
    Witch,
    Warlock,
    Mare,
    Penguin,
    Puppeteer,
    TimeThief,
    EvilTracker,
    Stealth,
    NekoKabocha,
    Insider,
    EvilNekomata,
    AntiAdminer,
    CursedWolf,
    Greedier,
    Ambitioner,
    Scavenger,
    EvilDiviner,
    Telepathisters,
    ShapeKiller,
    StrayWolf,
    EvilIgnition,
    Escalationer,
    EvilDyer,
    BestieWolf,
    EvilGuesser,
    SelfBomber,
    GrudgeCharger,
    Charger,
    Chaser,
    CharismaStar,
    EvilHacker,
    Telekinetic,
    Administer,

    Godfather,
    Janitor,
    Jack,
    //Madmate
    Madmate,
    MadGuardian,
    MadSnitch,
    MadSheriff,
    MadDictator,
    MadNatureCalls,
    MadBrackOuter,
    MadNimrod,
    MadScientist,
    MadJester,
    MadGuesser,
    MadConnecter,

    MadDilemma,
    jO,
    SKMadmate,
    //Crewmate(Vanilla)
    Engineer,
    GuardianAngel,
    Scientist,
    Tracker,
    Noisemaker,
    Detective,
    Judge,
    //Crewmate
    NormalEngineer,
    NormalScientist,
    NormalTracker,
    NormalNoisemaker,
    NormalDetective,
    NormalJudge,
    CustomCrewmate,
    NiceWatcher,
    Bait,
    Lighter,
    Mayor,
    SabotageMaster,
    Sheriff,
    Snitch,
    SpeedBooster,
    Trapper,
    Dictator,
    Doctor,
    Seer,
    TimeManager,
    Bakery,
    TaskManager,
    SillySheriff,
    GrudgeSheriff,
    Hunter,
    Nekomata,
    Chairman,
    Express,
    SeeingOff,
    Rainbow,
    Sympathizer,
    Blinder,
    Medic,
    CandleLighter,
    FortuneTeller,
    Psychic,
    Nimrod,
    Detector,
    LoyalDoggy,
    Rabbit,
    VentManager,
    NiceGuesser,
    Elder,

    Counselor,
    Lantern,
    Potentialist,
    //Neutral
    Arsonist,
    Egoist,
    Jester,
    Opportunist,
    SchrodingerCat,
    SchrodingerCatKiller,
    Terrorist,
    Executioner,
    Jackal,
    JSidekick,
    JClient,
    AntiComplete,
    Workaholic,
    DarkHide,
    LoveCutter,
    PlatonicLover,
    Lawyer,
    Pursuer,
    Totocalcio,
    Duelist,
    God,
    FoxSpirit,
    Immoralist,
    Ogre,
    Pirate,
    Gang,
    ChainShifter,
    Vendetta,

    // hamo から移植
    Evilgambler, Samurai, HadouHo, Pusher, HateKiller, Framer, Surrogate, Maestro, TimeSleeper, Bomber,
    EvilLinker, Tairou, EvilBlender, Ballooner, AntiReporter, EvilAddoer, Driver, Assassin, EvilCommander,
    Magician, Evolver, Destroyer, Nova, Reloader,
    MadMagic, MadKaiser, MadAvenger, Braid,
    Sage, MeetingSheriff, Balancer, VillageChief, NiceHacker, Guardian, Walkure, Repeater, NiceWorkaholic,
    SheriffHadouHo, Analyzer, UltraStar, NiceAddoer, Merlin,
    Spelunker, Tuna, Vulture, PavlovDog, PavlovOwner, PavlovDogImprint, JackalHadouHo, Tama, Onmyoji, Shikigami,
    Freeter, AntiHero, StandMaster, Stand, Zombie, JackalWolf, Chameleon, Eater, Fox, Obolus, PlagueDoctor,

    GM,
    CounselorAndMadDilemma,
    GodfatherAndJanitor,
    JackOLantern,
    MaxMain,
    /************/

    //ON
    ONStart = 400,

    MaxON,
    /************/

    //CC
    CCStart = 480,
    CCRedLeader,
    CCBlueLeader,
    CCYellowLeader,
    CCNoCat,
    CCRedCat,
    CCBlueCat,
    CCYellowCat,

    MaxCC,
    /************/

    //HideAndSeek
    HASStart = 490,
    HASFox,
    HASTroll,

    MaxHAS,
    /************/

    // Addon
    StartAddon = 500,
    LastImpostor,
    CompleteCrew,
    Lovers,
    Workhorse,

    AddWatch,
    AddLight,
    AddSeer,
    Autopsy,
    VIP,
    Revenger,
    Management,
    Sending,
    TieBreaker,
    Loyalty,
    PlusVote,
    Guarding,
    AddBait,
    Refusing,
    Revealer,

    Sunglasses,
    Clumsy,
    InfoPoor,
    NonReport,

    Archenemy,
    ChainShifterAddon,

    MaxAddon,

    //dummy
    DummyNext = 10000,

    // hamo の役職から名前だけ参照される、TOH_L に無い役職 (配られない)
    HamoOnlyStart = 20000,
    Alien, AlienHijack, Amanojaku, Amnesia, Amnesiac, Autocrat, ConnectSaver, CountKiller, DoppelGanger, Elector,
    EvilSatellite, GrimReaper, Guesser, JackalAlien, JackalMafia, Jackaldoll, Jumbo, King, LastNeutral, Limiter,
    MadProxy, MadWorker, MagicHand, Moon, Opener, Powerful, Remotekiller, Serial, Speeding, Stack, Stamina,
    Strawdoll, SwitchSheriff, Water, WolfBoy,
}
public enum CustomRoleTypes
{
    Impostor,
    Madmate,
    Crewmate,
    Neutral,
    Unit
}
public enum HasTask
{
    True,
    False,
    ForRecompute
}