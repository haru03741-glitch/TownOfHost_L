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

using TownOfHostL.Core.Modes;

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
    /// <summary>キルの力・ガードの力を見るキルチェック。force はキラーのキルチェックを飛ばす、DontRoleAbility は相手の能力を使わせない</summary>
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
        var guardianGuarded = false;

        appearanceKiller.ResetKillCooldown();

        // 無効なキルをブロックする処理 必ず最初に実行する
        if (!CheckMurderPatch.CheckForInvalidMurdering(info, killerMyselfInvoke)) return false;

        // インポスター同士のキル無効
        if (attemptKiller.GetCustomRole().IsImpostor() && attemptTarget.GetCustomRole().IsImpostor() && !attemptKiller.GetCustomRole().IsLoneImpostor()) return false;
        // 役職によって今キルできない状態 (ゴッドファーザーのロック・ペンギンの拉致など)
        foreach (var isBlocked in KillBlockersOthers)
            if (isBlocked(attemptKiller)) return false;

        var killerRole = attemptKiller.GetRoleClass();
        var targetRole = attemptTarget.GetRoleClass();

        // キラーがキル能力持ちなら
        if (killerRole is IKiller killer)
        {
            if (killer.IsKiller)
            {
                // イビルディバイナーのみ占いのためここで先に処理
                if (killerRole is EvilDiviner diviner && !diviner.OnCheckMurder(info)) return false;
                // 役職に関わらないガード (ガーディング・メディック・背徳者など)
                if (TryKillGuards(info)) { }
                // ターゲットのキルチェック処理実行
                else if (targetRole != null && info.DontRoleAbility is not true)
                {
                    if (!targetRole.OnCheckMurderAsTarget(info))
                    {
                        killer.OnCheckMurderDontKill(info);
                        return false;
                    }
                }
                // 守護者 のガード
                if (info.KillPower > info.GuardPower && TownOfHostL.Roles.Crewmate.Guardian.FindGuardianFor(attemptTarget.PlayerId) != null)
                {
                    info.GuardPower = 1;
                    guardianGuarded = true;
                }
            }
            // キラーのキルチェック処理実行 (IDoubleTrigger の役職は 1 回押しと 2 回押しを先に振り分ける)
            if (!DoubleTrigger.OnCheckMurderAsKiller(info) && !killerMyselfInvoke)
            {
                killer.OnCheckMurderAsKiller(info);
            }
            // ガードの力によるガード
            if (info.KillPower <= info.GuardPower && killer.IsKiller)
            {
                info.IsGuard = true;
                info.CanKill = false;
                if (guardianGuarded)
                {
                    var guardian = TownOfHostL.Roles.Crewmate.Guardian.FindGuardianFor(attemptTarget.PlayerId);
                    guardian?.Sacrifice(attemptTarget);
                    Utils.AddGameLog("Guardian", attemptTarget.ColoredName() + ":  " + string.Format(Translator.GetString("GuardMaster.Guard"), attemptKiller.ColoredName()));
                }
                attemptKiller.RpcProtectedMurderPlayer(attemptTarget);
                Utils.NotifyRoles();
                killer.OnCheckMurderDontKill(info);
                return false;
            }
        }
        // CC
        if (ModeCatalog.IsCatchCat)
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
            bool killerIsViper = appearanceKiller.Is(RoleTypes.Viper);
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

        // 恋人・ジャニターの後追いは、下の SetDead の「誰かが死んだ」の知らせで動く

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

        // ホストの死後のタスク免除は SetDead の中で行う (キル以外の死に方でも効かせるため)
        targetState.SetDead();
        attemptTarget.SetRealKiller(attemptKiller, true);

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
        NameColorOthers.Clear();
        KillBlockersOthers.Clear();
        TaskExemptionsOthers.Clear();
        EveryoneNeedsSeenNamesOthers.Clear();
        KillGuardsOthers.Clear();
        OnPlayerDeadOthers.Clear();
        OnPlayerRevivedOthers.Clear();
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
            // 試合の途中で付いた役職・自投票・部屋タスク
            if (Main.introDestroyed && GameStates.IsInGame && roleClass != null) roleClass.OnAddedMidGame();
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
    /// seer,seenが役職であるかに関わらず、名前の色を上書きする (seer, seen, 今の色コード) → 新しい色コード。
    /// 色が無い時の色コードは空文字。登録の順にすべて通す。
    /// </summary>
    public static HashSet<Func<PlayerControl, PlayerControl, string, string>> NameColorOthers = new();
    /// <summary>
    /// 役職に関わらず、キルしようとした人が今キルできない状態か (killer → true でキルを取り消す)。
    /// ゴッドファーザーのロック・ペンギンの拉致など、その試合の役職が登録する
    /// </summary>
    public static HashSet<Func<PlayerControl, bool>> KillBlockersOthers = new();
    /// <summary>
    /// 役職に関わらず、その人のタスクを免除するか (true で免除)。死後のタスク免除の設定など、その試合の役職が登録する
    /// </summary>
    public static HashSet<Func<NetworkedPlayerInfo, bool>> TaskExemptionsOthers = new();
    /// <summary>
    /// 見る人に関わらず、全員の他人の名前を組み直す状況か (true で組み直す)。呪い・停電中の変装など、その試合の役職が登録する。
    /// 同じ役職が何人いても 1 つになるよう、捕まえる物の無いラムダか static メソッドで登録する
    /// </summary>
    public static HashSet<Func<bool>> EveryoneNeedsSeenNamesOthers = new();
    /// <summary>
    /// 狙われた人の役職に関わらずキルを止めるガード (ガーディング属性・メディックの守り・背徳者の守りなど)。
    /// order の小さい順に見て、最初に true を返したガードでキルを止め、残りのガードと狙われた人の OnCheckMurderAsTarget は見ない。
    /// 今の order: ガーディング 100・メディック 200・背徳者 300
    /// </summary>
    private static readonly List<(int Order, Func<MurderInfo, bool> TryGuard)> KillGuardsOthers = new();
    /// <summary>キルのガードを登録する。同じデリゲートは 1 つにまとめるので、static メソッドで登録する</summary>
    public static void AddKillGuard(int order, Func<MurderInfo, bool> tryGuard)
    {
        if (KillGuardsOthers.Exists(g => g.TryGuard == tryGuard)) return;
        var index = KillGuardsOthers.FindIndex(g => g.Order > order);
        KillGuardsOthers.Insert(index < 0 ? KillGuardsOthers.Count : index, (order, tryGuard));
    }
    /// <summary>
    /// 誰かが死んだ時 (キル・追放・会議後の死・切断・役職の能力による死など、死に方によらず 1 人 1 回)。ホストだけで呼ぶ。
    /// 引数は死んだ人の PlayerId と死因。PlayerState.SetDead から呼ぶ
    /// </summary>
    public static HashSet<Action<byte, CustomDeathReason>> OnPlayerDeadOthers = new();
    /// <summary>
    /// 誰かが生き返った時 (Modules.Revive)。ホストだけで呼ぶ。OnPlayerDeadOthers で「一度きり」の旗を立てた側が戻すのに使う
    /// </summary>
    public static HashSet<Action<byte>> OnPlayerRevivedOthers = new();
    /// <summary>誰かがベントに入った時 (ホスト)。戻り値は今は使わない (追い出す時は登録した側で RpcBootFromVent を送る)</summary>
    public static HashSet<Func<PlayerPhysics, int, bool>> OnEnterVentOthers = new();
    /// <summary>
    /// 投票で追放された人の後処理 (OnExileWrapUp の後の SetDead) の間だけ、その人の PlayerId。それ以外は byte.MaxValue。
    /// 追放を OnExileWrapUp で扱う役職が、知らせの側で同じ死を 2 度扱わないために見る (死因が Vote でも、全員追放や処刑は追放の後処理を通らない)
    /// </summary>
    public static byte ExileWrapUpPlayerId { get; private set; } = byte.MaxValue;
    public static void SetDeadByExile(byte playerId)
    {
        var state = PlayerState.GetByPlayerId(playerId);
        if (state == null) return;
        ExileWrapUpPlayerId = playerId;
        try { state.SetDead(); }
        finally { ExileWrapUpPlayerId = byte.MaxValue; }
    }
    public static void NotifyPlayerDead(byte playerId, CustomDeathReason reason)
    {
        // 中で役職が変わって登録が外れても回せるよう、写しを回す
        foreach (var onDead in OnPlayerDeadOthers.ToArray())
        {
            try { onDead(playerId, reason); }
            catch (Exception ex) { Logger.Exception(ex, "OnPlayerDeadOthers"); }
        }
        if (!GameStates.IsInGame || Utils.GetPlayerById(playerId) is not PlayerControl player) return;
        foreach (var role in AllActiveRoles.Values.ToArray())
        {
            try { role.OnPlayerDead(player); }
            catch (Exception ex) { Logger.Exception(ex, "OnPlayerDead"); }
        }
    }
    /// <summary>試合中に誰かが部屋から抜けた時 (ホストのみ)。全員の役職の OnPlayerLeft を呼ぶ</summary>
    public static void NotifyPlayerLeft(byte playerId)
    {
        foreach (var role in AllActiveRoles.Values.ToArray())
        {
            try { role.OnPlayerLeft(playerId); }
            catch (Exception ex) { Logger.Exception(ex, "OnPlayerLeft"); }
        }
    }
    public static void NotifyPlayerRevived(byte playerId)
    {
        if (OnPlayerRevivedOthers.Count == 0) return;
        foreach (var onRevived in OnPlayerRevivedOthers.ToArray())
        {
            try { onRevived(playerId); }
            catch (Exception ex) { Logger.Exception(ex, "OnPlayerRevivedOthers"); }
        }
    }
    private static bool TryKillGuards(MurderInfo info)
    {
        foreach (var (_, tryGuard) in KillGuardsOthers)
            if (tryGuard(info)) return true;
        return false;
    }
    public static string ApplyNameColorOthers(PlayerControl seer, PlayerControl seen, string colorCode)
    {
        foreach (var overrideColor in NameColorOthers)
            colorCode = overrideColor(seer, seen, colorCode);
        return colorCode;
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
        NameColorOthers.Clear();
        KillBlockersOthers.Clear();
        TaskExemptionsOthers.Clear();
        EveryoneNeedsSeenNamesOthers.Clear();
        KillGuardsOthers.Clear();
        OnPlayerDeadOthers.Clear();
        OnPlayerRevivedOthers.Clear();
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
    // キルの力・ガードの力
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

    // TownOfHost-hamo から移植
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
    HASPortalMaker,

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

    // 役職から名前だけ参照される、配られない役職
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