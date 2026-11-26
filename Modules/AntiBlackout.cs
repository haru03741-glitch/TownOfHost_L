using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using AmongUs.GameOptions;
using TownOfHostL.Attributes;
using TownOfHostL.Modules;
using TownOfHostL.Roles.Impostor;
using TownOfHostL.Roles.Neutral;
using TownOfHostL.Roles.Core;
using TownOfHostL.Roles.Unit;

using TownOfHostL.Core.Modes;
using TownOfHostL.Core.Win;

namespace TownOfHostL;

public static partial class AntiBlackout
{
    ///<summary>
    ///追放処理を上書きするかどうか
    ///</summary>
    public static bool OverrideExiledPlayer
        => Options.NoGameEndByCount.GetBool()
        || Jackal.RoleInfo.IsEnable || StrayWolf.RoleInfo.IsEnable
        || Pirate.RoleInfo.IsEnable || JackOLantern.RoleInfo.IsEnable
        || ModeCatalog.IsCatchCat;

    public static bool IsCached { get; private set; } = false;
    private static Dictionary<byte, (bool isDead, bool Disconnected)> isDeadCache = new();
    private readonly static LogHandler logger = Logger.Handler("AntiBlackout");

    private static RecognizeType recognizeType = RecognizeType.Impostor;
    public static int ExiledPlayerId = -1;

    public static void SetIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        SetRoleChange(fillCrew: true);

        logger.Info($"SetIsDead is called from {callerMethodName}");
        if (IsCached)
        {
            logger.Info("再度SetIsDeadを実行する前に、RestoreIsDeadを実行してください。");
            return;
        }
        isDeadCache.Clear();
        foreach (var info in GameData.Instance.AllPlayers)
        {
            if (info == null) continue;
            isDeadCache[info.PlayerId] = (info.IsDead, info.Disconnected);
            info.IsDead = false;
            info.Disconnected = false;
        }
        IsCached = true;
        if (doSend) SendGameData();
    }
    private enum RecognizeType
    {
        Impostor,
        StrayWolf,
        Jackal,
        Pirate,
        // 上のどれも生きていない時の残りのキル陣営 (パブロフ・スタンド・イーターなど)。
        // 本編の客の画面で生存インポスターが 0 人になると、追放の演出の締めでクルーの勝ちとみなして暗転する
        OtherKiller,
    }
    private static bool IsRecognizeType(PlayerControl pc, RecognizeType type)
    {
        if (pc == null) return false;
        var customRole = pc.GetCustomRole();
        var countType = PlayerState.GetByPlayerId(pc.PlayerId).CountType;
        //var countType = customRole.GetRoleInfo().CountType;
        Logger.Info($"RecognizeType {pc?.name}, {countType}, {customRole}({customRole.GetRoleInfo()?.CountType})", "AntiBlackout");
        return type switch
        {
            RecognizeType.Impostor => countType == CountTypes.Impostor && !customRole.IsLoneImpostor(),
            RecognizeType.StrayWolf => countType == CountTypes.Impostor && customRole.IsLoneImpostor(),
            RecognizeType.Jackal => countType == CountTypes.Jackal,
            RecognizeType.Pirate => countType == CountTypes.Pirate,
            RecognizeType.OtherKiller => GameEndCounts.IsKillerSide(countType),
            _ => false,
        };
    }

    /// <summary>seer の画面に、dummies の外の生きたキル役 (自分も含む) が見えているか (見え方は RoleView)</summary>
    private static bool SeesOtherKiller(PlayerControl seer, List<PlayerControl> dummies)
    {
        foreach (var target in Main.AllAlivePlayerControls)
        {
            if (target.PlayerId == ExiledPlayerId || dummies.Contains(target)) continue;
            if (RoleView.IsKillerRole(RoleView.Living(target, seer))) return true;
        }
        return false;
    }

    // 会議の後、本編のクライアントは追放の演出の締めで「生存インポスター ≥ 生存クルー」なら試合が終わるとみなし、
    // ホストの試合終了を待って暗転する。fillCrew のときは各クライアントの見え方で生存クルーが足りなければ、
    // 足りない数だけ死者を生きたクルーに見せ、演出の後で幽霊に戻す (RevertFilledCrew)。
    // ホストの演出はクライアントより先に終わるので、その死者の IsDead を戻して送るのも RevertFilledCrew まで待つ。
    // 謝辞: 死者を見た目だけ生き返らせて頭数をそろえる方法は Endless Host Roles (Gurge44, GPL-3.0) に倣った。
    private static readonly Dictionary<int, List<PlayerControl>> filledCrew = new();
    private static readonly Dictionary<byte, (bool isDead, bool Disconnected)> deferredDead = new();
    // デシンクの人の画面に送った身代わり (演出の後で RoleView の見え方へ戻す)
    private static readonly Dictionary<int, (PlayerControl seer, List<PlayerControl> dummies)> desyncDummies = new();

    // 会議の締めで身代わりなどを送ってから、追放の演出の後でそれを戻すまでの間 (この間に客の画面の役職を送り直すと暗転しうる)
    private static bool revertPending;
    public static bool IsRevertPending => revertPending;

    public static void SetRoleChange(PlayerControl killer = null, bool fillCrew = false)
    {
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;
        if (fillCrew) revertPending = true;

        RecognizeType remaining = RecognizeType.Impostor;
        List<PlayerControl> list = new();
        foreach (RecognizeType type in Enum.GetValues(typeof(RecognizeType)))
        {
            remaining = type;
            list = Main.AllAlivePlayerControls.Where(pc => pc.PlayerId != ExiledPlayerId && IsRecognizeType(pc, type)).ToList();
            Logger.Info($"CheckRoleCount type: {remaining}, count: {list.Count}, exiled: {ExiledPlayerId}", "AntiBlackout");
            if (list.Count > 0) break;
        }

        recognizeType = remaining;

        Logger.Info($"SetDummyImpostor type: {recognizeType}, count:{list.Count}", "AntiBlackout");
        var deadPlayers = Main.AllDeadPlayerControls.Where(x => !x.Data.Disconnected).ToList();
        var exiledForFill = fillCrew ? GetExiledForFill(list) : null;

        foreach (var pc in Main.AllPlayerControls.Where(x => !x.Data.Disconnected))
        {
            if (pc.PlayerId == PlayerControl.LocalPlayer.PlayerId) continue;
            // デシンクの人は、自分の画面で自分がキル役なら生存インポスターが自分で足りるので送らない。
            // キルを持つ前の役職 (ワルキューレなど) は自分をクルーに見せ、他人はキルの対象に見せるので、
            // その画面には生存インポスターがいない。追放の演出の締めでクルーの勝ちとみなして暗転する (2026-11-10 実機で確認)
            bool desync = RoleView.IsDesync(pc);
            // 身代わりも死者の幽霊化も送らない (自分のキル役で生存インポスターが足りる)。頭数が並ぶ時の手当て (下の BalanceView) だけ送る
            bool selfKiller = pc.IsAlive() && desync && RoleView.IsKillerRole(RoleView.Living(pc, pc));
            // 身代わりのインポスターの外に、生きたキル役が既に見えている画面 (役職の能力でキル役になった本人・その仲間) には身代わりを足さない。
            // 足すとその画面の生存インポスターが増え、追放の演出の締めで試合が終わったとみなして暗転する (2026-11-09 実機で確認)。
            // 死者を幽霊のクルーにする分は送る (会議中は全員を生きているとして送るので、死んだキル役の ImpostorGhost も生存インポスターに数えられる)
            bool noDummies = selfKiller || SeesOtherKiller(pc, list);

            var clientId = pc.GetClientId();
            if (clientId == -1) continue;
            var isKiller = killer != null && pc.PlayerId == killer.PlayerId;
            var sender = CustomRpcSender.Create("AntiBlackout.SetRoleChange", Hazel.SendOption.Reliable);
            sender.StartMessage(clientId);
            var count = 0;

            // 自分の画面で自分がキル役でない身代わり (犬のいるパブロフのオーナーなど) には、他に身代わりがいれば自分を送らない (キルのボタンが出る)
            bool skipSelf = list.Count > 1 && list.Contains(pc) && !RoleView.IsKillerRole(RoleView.Living(pc, pc));
            foreach (var dummy in list)
            {
                if (noDummies) break;
                if (isKiller && dummy.PlayerId == pc.PlayerId) continue;
                if (skipSelf && dummy.PlayerId == pc.PlayerId) continue;
                // 本編の役職がキル役でない身代わりは、インポスターとして送る
                var dummyRole = dummy.GetCustomRole().GetRoleTypes();
                bool replaced = !RoleView.IsKillerRole(dummyRole);
                if (replaced) dummyRole = RoleTypes.Impostor;
                sender.StartRpc(dummy.NetId, RpcCalls.SetRole)
                    .Write((ushort)dummyRole)
                    .Write(true)
                    .EndRpc();
                count++;
                // 本人へ届く役職は、客の手元で役職を作り直し、能力のクールを 0 にする
                if (dummy.PlayerId == pc.PlayerId) AbilityCooldownClock.Clear(pc.PlayerId);
                // デシンクの人の画面と、デシンクの役職・インポスターに差し替えた身代わり (会議明けの配り直しが見え方へ戻してしまう) は、演出の後で戻す。
                // 本物のインポスターの身代わりは戻さない (戻すと、演出の締めがホストより遅れた客の画面で生存インポスターが 0 人になる)
                if ((desync && pc.IsAlive()) || RoleView.IsDesync(dummy) || replaced)
                {
                    if (!desyncDummies.TryGetValue(clientId, out var sent)) desyncDummies[clientId] = sent = (pc, new());
                    if (!sent.dummies.Contains(dummy)) sent.dummies.Add(dummy);
                }
            }
            int lacking = 0;
            if (fillCrew)
            {
                filledCrew.Remove(clientId);
            }
            if (fillCrew && !noDummies)
            {
                int aliveCrew = Main.AllAlivePlayerControls.Count(x => x.PlayerId != ExiledPlayerId && !list.Contains(x));
                lacking = list.Count + 1 - aliveCrew;
            }
            else if (fillCrew)
            {
                // 身代わりを送らない画面でも、見えているキル役 (自分・仲間) が生きたクルー以上なら、追放の演出の締めで試合が終わったとみなして暗転する
                // (ジャッカルとサイドキックの 2 人・インポスター・クルーが残った会議の後、ジャッカルの画面が 2 対 2 で暗転した。2026-11-21 実機で確認)
                lacking = BalanceView(pc, clientId, sender, ref count);
            }
            foreach (var dead in deadPlayers)
            {
                bool fill = lacking > 0;
                if (!fill && selfKiller) continue;
                // バイタルで生きているように見せている死者の幽霊の役職は、嘘を受けている人へは送り直さない
                // (その画面では死んだ時に幽霊の役職になっている。送り直すと手元で死亡に戻り、開いているバイタルが死亡のまま戻らない)
                if (!fill && Modules.Net.VitalsMask.SkipsGhostRoleFor(pc.PlayerId, dead.PlayerId)) continue;
                if (fill)
                {
                    lacking--;
                    if (!filledCrew.TryGetValue(clientId, out var filled)) filledCrew[clientId] = filled = new();
                    filled.Add(dead);
                }
                sender.StartRpc(dead.NetId, RpcCalls.SetRole)
                    .Write((ushort)(fill ? RoleTypes.Crewmate : RoleTypes.CrewmateGhost))
                    .Write(true)
                    .EndRpc();
                count++;
            }
            // 補える死者が足りない時 (追放前に死者がいない 1 対 1 など) は、追放者を数に入れる。
            // 追放者を伏せて票を締めた時は、客の画面では追放者がまだ生きたクルーなので、その死を送るのを演出の後まで遅らせればよい
            if (lacking > 0 && exiledForFill != null)
            {
                if (!filledCrew.TryGetValue(clientId, out var filled)) filledCrew[clientId] = filled = new();
                filled.Add(exiledForFill);
                lacking--;
            }
            // 死者も追放者も足りず頭数をそろえられない (総人数 2 人の卓など)。本編の客はこの会議の後で暗転し、操作が戻らない
            if (lacking > 0 && !pc.IsModClient())
                logger.Warn($"{pc.name} の画面で生きたクルーが {lacking} 人足りない (会議の後で暗転する)");

            if (count > 0)
            {
                sender.EndMessage();
                sender.SendMessage();
            }
            else
            {
                sender.stream.Recycle();
            }
            Logger.Info($"SetDummyImpostor player: {pc?.name} ({count} rpc{(filledCrew.TryGetValue(clientId, out var f) ? $", 生きたクルーに見せた死者 {f.Count}" : "")})", "AntiBlackout");
        }
        ExiledPlayerId = -1;
    }

    /// <summary>
    /// 身代わりを送らない画面 (自分や仲間のキル役が見えている) の頭数を、追放の演出の締めで試合が終わらない形にそろえる。
    /// 生きたキル役が生きたクルー以上に見えるなら、本人以外のキル役を演出の間だけクルーに見せ (RevertDesyncDummies で戻す)、
    /// それでも足りない数を返す (死者を生きたクルーに見せて補う)
    /// </summary>
    private static int BalanceView(PlayerControl pc, int clientId, CustomRpcSender sender, ref int count)
    {
        var killers = new List<PlayerControl>();
        int alive = 0;
        foreach (var target in Main.AllAlivePlayerControls)
        {
            if (target.PlayerId == ExiledPlayerId) continue;
            alive++;
            if (RoleView.IsKillerRole(RoleView.Living(target, pc))) killers.Add(target);
        }
        int visible = killers.Count;
        if (visible == 0) return 0;
        foreach (var target in killers)
        {
            if (visible <= 1 || alive - visible > visible) break;
            if (target.PlayerId == pc.PlayerId) continue;
            sender.StartRpc(target.NetId, RpcCalls.SetRole)
                .Write((ushort)RoleTypes.Crewmate)
                .Write(true)
                .EndRpc();
            count++;
            visible--;
            if (!desyncDummies.TryGetValue(clientId, out var sent)) desyncDummies[clientId] = sent = (pc, new());
            if (!sent.dummies.Contains(target)) sent.dummies.Add(target);
            logger.Info($"{pc.name} の画面で {target.name} を演出の間だけクルーに見せる");
        }
        return visible + 1 - (alive - visible);
    }

    /// <summary>
    /// viewer の画面に dummy を身代わりのインポスターとして見せていて、追放の演出の後まで戻さない時は true。
    /// 会議明けの役職の配り直しは、この組を飛ばす (演出の締めより前に戻すと、客の画面の生存インポスターが 0 人になって暗転する)
    /// </summary>
    public static bool IsPendingDummy(PlayerControl dummy, int viewerClientId)
        => desyncDummies.TryGetValue(viewerClientId, out var sent) && sent.dummies.Contains(dummy);

    /// <summary>身代わりのインポスターとして送った人を、見え方の表 (RoleView) どおりに戻す</summary>
    private static void RevertDesyncDummies()
    {
        if (desyncDummies.Count == 0) return;
        if (CustomWinnerHolder.WinnerTeam == CustomWinner.Default && GameStates.IsInGame)
        {
            foreach (var (clientId, (seer, dummies)) in desyncDummies)
            {
                if (seer == null || seer.Data == null || seer.Data.Disconnected) continue;
                var sender = CustomRpcSender.Create("AntiBlackout.RevertDesyncDummies", Hazel.SendOption.Reliable);
                sender.StartMessage(clientId);
                int count = 0;
                foreach (var dummy in dummies)
                {
                    if (dummy == null || dummy.Data == null || dummy.Data.Disconnected || !dummy.IsAlive()) continue;
                    sender.StartRpc(dummy.NetId, RpcCalls.SetRole)
                        .Write((ushort)RoleView.Living(dummy, seer))
                        .Write(true)
                        .EndRpc();
                    count++;
                    if (dummy.PlayerId == seer.PlayerId) AbilityCooldownClock.Clear(seer.PlayerId);
                }
                if (count > 0) { sender.EndMessage(); sender.SendMessage(); }
                else sender.stream.Recycle();
                logger.Info($"身代わりを見え方へ戻した: {seer.name} の画面 ({count} 人)");
            }
        }
        desyncDummies.Clear();
    }

    // 追放者を伏せて票を締めた時だけ、客の画面で生きたクルーに見えている追放者を返す
    private static PlayerControl GetExiledForFill(List<PlayerControl> impostorLike)
    {
        if (!OverrideExiledPlayer || ExiledPlayerId < 0) return null;
        var exiled = Utils.GetPlayerById((byte)ExiledPlayerId);
        if (exiled == null || !exiled.IsAlive() || exiled.Data.Disconnected || impostorLike.Contains(exiled)) return null;
        var role = exiled.GetCustomRole();
        bool seenAsImpostor = role.GetRoleInfo()?.IsDesyncImpostor != true
            && role.GetRoleTypes() is RoleTypes.Impostor or RoleTypes.Shapeshifter or RoleTypes.Phantom;
        return seenAsImpostor ? null : exiled;
    }

    /// <summary>
    /// 生きたクルーに見せた人なら、死を送るのを RevertFilledCrew まで遅らせて true を返す。
    /// 追放の締めで追放者の IsDead を立てる前に呼ぶ
    /// </summary>
    public static bool IsDeathDeferred(byte playerId) => deferredDead.ContainsKey(playerId);

    public static bool DeferDeathIfFilled(byte playerId)
    {
        if (!deferredDead.TryGetValue(playerId, out var val)) return false;
        deferredDead[playerId] = (true, val.Disconnected);
        return true;
    }

    // 戻しの後まで待たせた処理 (会議明けの道連れなどの死)
    private static readonly List<Action> afterRevert = new();

    /// <summary>
    /// 身代わりを戻すまでの間なら、action を戻しの後まで待たせる。そうでなければすぐ走らせる。
    /// 客の追放の演出の締め (生存インポスターと生存クルーの数での判定) より前に死を送ると、客の画面で頭数が減って暗転しうる
    /// </summary>
    public static void RunAfterRevert(Action action)
    {
        if (revertPending) afterRevert.Add(action);
        else action();
    }

    // 追放の演出の締めで各クライアントが判定を終えた後に呼ぶ
    public static void RevertFilledCrew()
    {
        try { RevertFilledCrewCore(); }
        finally
        {
            if (afterRevert.Count > 0)
            {
                var actions = afterRevert.ToArray();
                afterRevert.Clear();
                foreach (var action in actions)
                {
                    try { action(); }
                    catch (Exception ex) { logger.Exception(ex); }
                }
            }
        }
    }

    private static void RevertFilledCrewCore()
    {
        revertPending = false;
        RevertDesyncDummies();
        if (deferredDead.Count > 0)
        {
            foreach (var info in GameData.Instance.AllPlayers)
            {
                if (info == null || !deferredDead.TryGetValue(info.PlayerId, out var val)) continue;
                info.IsDead = val.isDead;
                info.Disconnected = val.Disconnected;
            }
            deferredDead.Clear();
            SendGameData();
            // 追放の締めの NotifyRoles はこの人をまだ生きているものとして組んだので、死者として見える名前 (他人の役職など) を送り直す。
            // 試合が終わっていれば、結果画面向けの名前を上書きしないよう送らない
            if (CustomWinnerHolder.WinnerTeam == CustomWinner.Default && GameStates.IsInGame) Utils.NotifyRoles();
        }
        if (filledCrew.Count == 0) return;
        // 試合が終わっていれば、結果画面へ向かうクライアントに役職を送り直さない
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default || !GameStates.IsInGame)
        {
            filledCrew.Clear();
            return;
        }
        foreach (var (clientId, players) in filledCrew)
        {
            var sender = CustomRpcSender.Create("AntiBlackout.RevertFilledCrew", Hazel.SendOption.Reliable);
            sender.StartMessage(clientId);
            int count = 0;
            foreach (var dead in players)
            {
                if (dead == null || dead.Data == null || dead.Data.Disconnected) continue;
                sender.StartRpc(dead.NetId, RpcCalls.SetRole)
                    .Write((ushort)RoleTypes.CrewmateGhost)
                    .Write(true)
                    .EndRpc();
                count++;
            }
            if (count > 0)
            {
                sender.EndMessage();
                sender.SendMessage();
            }
            else
            {
                sender.stream.Recycle();
            }
        }
        logger.Info($"生きたクルーに見せた死者を幽霊に戻した ({filledCrew.Count} 人の画面)");
        filledCrew.Clear();
    }
    public static void RestoreIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        logger.Info($"RestoreIsDead is called from {callerMethodName}");
        var filledIds = filledCrew.Values.SelectMany(x => x).Where(x => x != null).Select(x => x.PlayerId).ToHashSet();
        foreach (var info in GameData.Instance.AllPlayers)
        {
            if (info == null) continue;
            if (isDeadCache.TryGetValue(info.PlayerId, out var val))
            {
                if (filledIds.Contains(info.PlayerId))
                {
                    deferredDead[info.PlayerId] = val;
                    continue;
                }
                // 退避の間に死んだ人 (退避中は死を送らないので、控えのまま戻すと客の画面で生きたままになる)。
                // 客の追放の演出の締めより前に死を送ると暗転しうるので、生きたクルーに見せた死者と同じく戻しの後で送る
                if (!val.isDead && PlayerState.GetByPlayerId(info.PlayerId)?.IsDead == true)
                {
                    logger.Info($"{info.PlayerName}: 退避の間に死んだので、死を戻しの後で送る");
                    deferredDead[info.PlayerId] = (true, val.Disconnected);
                    info.IsDead = false;
                    continue;
                }
                info.IsDead = val.isDead;
                info.Disconnected = val.Disconnected;
            }
        }
        isDeadCache.Clear();
        IsCached = false;
        if (doSend) SendGameData();
    }

    public static void SendGameData([CallerMemberName] string callerMethodName = "")
    {
        logger.Info($"SendGameData is called from {callerMethodName}");
        foreach (var innerNetObject in GameData.Instance.AllPlayers)
        {
            innerNetObject.SetDirtyBit(uint.MaxValue);
        }
    }
    public static void OnDisconnect(NetworkedPlayerInfo player)
    {
        // 実行条件: クライアントがホストである, IsDeadが上書きされている, playerが切断済み
        if (!AmongUsClient.Instance.AmHost || !IsCached || !player.Disconnected) return;
        isDeadCache[player.PlayerId] = (true, true);
        player.IsDead = player.Disconnected = false;
        SendGameData();
    }


    [GameModuleInitializer]
    public static void Reset()
    {
        logger.Info("==Reset==");
        if (isDeadCache == null) isDeadCache = new();
        isDeadCache.Clear();
        IsCached = false;
        recognizeType = RecognizeType.Impostor;
        filledCrew.Clear();
        deferredDead.Clear();
        desyncDummies.Clear();
        afterRevert.Clear();
        revertPending = false;
    }
}