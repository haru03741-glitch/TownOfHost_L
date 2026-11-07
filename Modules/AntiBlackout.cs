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

namespace TownOfHostL;

public static partial class AntiBlackout
{
    ///<summary>
    ///追放処理を上書きするかどうか
    ///</summary>
    public static bool OverrideExiledPlayer
        => Options.NoGameEnd.GetBool()
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
            _ => false,
        };
    }

    // 会議の後、本編のクライアントは追放の演出の締めで「生存インポスター ≥ 生存クルー」なら試合が終わるとみなし、
    // ホストの試合終了を待って暗転する。fillCrew のときは各クライアントの見え方で生存クルーが足りなければ、
    // 足りない数だけ死者を生きたクルーに見せ、演出の後で幽霊に戻す (RevertFilledCrew)。
    // ホストの演出はクライアントより先に終わるので、その死者の IsDead を戻して送るのも RevertFilledCrew まで待つ。
    // 謝辞: 死者を見た目だけ生き返らせて頭数をそろえる方法は Endless Host Roles (Gurge44, GPL-3.0) と End K not に倣った。
    private static readonly Dictionary<int, List<PlayerControl>> filledCrew = new();
    private static readonly Dictionary<byte, (bool isDead, bool Disconnected)> deferredDead = new();

    public static void SetRoleChange(PlayerControl killer = null, bool fillCrew = false)
    {
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;

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
            if (pc.IsAlive() && pc.GetCustomRole().GetRoleInfo()?.IsDesyncImpostor == true) continue;

            var clientId = pc.GetClientId();
            if (clientId == -1) continue;
            var isKiller = killer != null && pc.PlayerId == killer.PlayerId;
            var sender = CustomRpcSender.Create("AntiBlackout.SetRoleChange", Hazel.SendOption.Reliable);
            sender.StartMessage(clientId);
            var count = 0;

            foreach (var dummy in list)
            {
                if (isKiller && dummy.PlayerId == pc.PlayerId) continue;
                sender.StartRpc(dummy.NetId, RpcCalls.SetRole)
                    .Write((ushort)dummy.GetCustomRole().GetRoleTypes())
                    .Write(true)
                    .EndRpc();
                count++;
            }
            int lacking = 0;
            if (fillCrew)
            {
                filledCrew.Remove(clientId);
                int aliveCrew = Main.AllAlivePlayerControls.Count(x => x.PlayerId != ExiledPlayerId && !list.Contains(x));
                lacking = list.Count + 1 - aliveCrew;
            }
            foreach (var dead in deadPlayers)
            {
                bool fill = lacking > 0;
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
            Logger.Info($"SetDummyImpostor player: {pc?.name} ({count} rpc{(filledCrew.TryGetValue(clientId, out var f) ? $", 生きたクルーに見せた死者 {f.Count}" : "")})", "AntiBlackout");
        }
        ExiledPlayerId = -1;
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
    public static bool DeferDeathIfFilled(byte playerId)
    {
        if (!deferredDead.TryGetValue(playerId, out var val)) return false;
        deferredDead[playerId] = (true, val.Disconnected);
        return true;
    }

    // 追放の演出の締めで各クライアントが判定を終えた後に呼ぶ
    public static void RevertFilledCrew()
    {
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

    ///<summary>
    ///一時的にIsDeadを本来のものに戻した状態でコードを実行します
    ///<param name="action">実行内容</param>
    ///</summary>
    public static void TempRestore(Action action)
    {
        logger.Info("==Temp Restore==");
        //IsDeadが上書きされた状態でTempRestoreが実行されたかどうか
        bool before_IsCached = IsCached;
        try
        {
            if (before_IsCached) RestoreIsDead(doSend: false);
            action();
        }
        catch (Exception ex)
        {
            logger.Warn("AntiBlackout.TempRestore内で例外が発生しました");
            logger.Exception(ex);
        }
        finally
        {
            if (before_IsCached) SetIsDead(doSend: false);
            logger.Info("==/Temp Restore==");
        }
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
    }
}