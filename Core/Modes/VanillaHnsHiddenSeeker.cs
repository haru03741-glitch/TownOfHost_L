using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼで、鬼の正体を隠れる側から隠す。
/// 開始の配役で、鬼を「鬼本人とホスト」には鬼の役職、他の客にはエンジニアとして配る。
/// 隠れる側の手元では鬼がクルーなので、鬼のマスク (HideAndSeekManager.GetBodyType は手元の IsImpostor で決まる) も赤い名前も出ない。
/// 鬼が変身すると、隠れる側からは変身先と見分けがつかない (2026-11-24 実機)。
/// キル・時間・勝敗はホストが本当の役職で決めるので、本編のまま動く。
/// 試合中の SetRole は客に効かないので、開始の配役の時にしか決められない。
/// 隠れる側の結果画面では鬼も勝者の列に並ぶので、結果の知らせに鬼の名前を足す (VanillaHideAndSeekRoles)。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsHiddenSeeker
{
    private static readonly List<byte> seekers = new();
    // 配役の間に選ばれた鬼。客への役職の電文は、鬼が全員決まってから送る (鬼同士には本当の役職を見せるため)
    private static readonly List<(PlayerControl seeker, RoleTypes role, bool canOverride)> pending = new();

    public static bool Enabled => Options.VanillaHnsHiddenSeeker.GetBool();

    /// <summary>この試合で正体を隠した鬼の名前 (結果の知らせ用)。隠していない試合は空。</summary>
    public static IEnumerable<string> SeekerNames
        => seekers.Select(id => Players.ById(id)?.Data?.PlayerName).Where(n => n != null);

    /// <summary>役職を配る前に呼ぶ。</summary>
    public static void Reset()
    {
        seekers.Clear();
        pending.Clear();
        ventOf.Clear();
        sentSid.Clear();
    }

    /// <summary>
    /// 配役の終わりに呼ぶ。鬼の役職を、鬼本人と他の鬼には本当の役職、隠れる側にはエンジニアとして送る。
    /// 鬼ごとにその場で送ると、先に配った鬼の画面で後の鬼がクルーに見え、鬼が鬼を殺せてしまう (2026-11-24 実機・鬼 2 人)。
    /// </summary>
    public static void Flush()
    {
        try
        {
            foreach (var (seeker, role, canOverride) in pending)
            {
                if (seeker == null || seeker.Data == null) continue;
                foreach (var pc in Players.All)
                {
                    if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
                    // ホストは本当の役職を持つ (キル・勝敗の判定に使う)。ホストが隠れる側でもホストの画面には鬼が見える
                    if (pc.AmOwner) continue;
                    var shown = seekers.Contains(pc.PlayerId) ? role : RoleTypes.Engineer;
                    int clientId = pc.GetClientId();
                    if (clientId < 0) continue;
                    var writer = AmongUsClient.Instance.StartRpcImmediately(seeker.NetId, (byte)RpcCalls.SetRole, Hazel.SendOption.Reliable, clientId);
                    writer.Write((ushort)shown);
                    writer.Write(canOverride);
                    AmongUsClient.Instance.FinishRpcImmediately(writer);
                    // 本編のかくれんぼは生きている時の見え方の表を使わないので、画面の表にだけ書く
                    TownOfHostL.Modules.RoleView.Note(pc, seeker, shown, transient: true);
                }
                Logger.Info($"{seeker.Data.PlayerName}: 鬼 ({role}) を隠れる側にはエンジニアとして配った", nameof(VanillaHnsHiddenSeeker));
            }
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(VanillaHnsHiddenSeeker)); }
        finally { pending.Clear(); }
    }

    // 鬼の役職の差し替え (VanillaHnsRules) の後に動かす
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
    private static class RpcSetRolePatch
    {
        [HarmonyPriority(Priority.Low)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] RoleTypes roleType, [HarmonyArgument(1)] bool canOverrideRole)
        {
            try
            {
                if (!VanillaHnsRules.Assigning || !Enabled || !RoleManager.IsImpostorRole(roleType)) return true;
                var seeker = __instance;
                // ホストの手元は本編の RpcSetRole と同じく、配役の canOverrideRole のまま CoSetRole で付ける。
                // 上書き可 (true) で付けると、ホストのかくれんぼの時計 (LogicGameFlowHnS) が始まらず時間切れで終わらなくなる (2026-11-24 実機)
                seeker.StartCoroutine(seeker.CoSetRole(roleType, canOverrideRole));
                if (!seekers.Contains(seeker.PlayerId)) seekers.Add(seeker.PlayerId);
                pending.Add((seeker, roleType, canOverrideRole));
                return false;
            }
            catch (Exception ex)
            {
                // 失敗したら本編の配り方に任せる (鬼が見えるだけで、試合は進む)
                Logger.Exception(ex, nameof(VanillaHnsHiddenSeeker));
                return true;
            }
        }
    }

    // 隠れる側の手元では鬼がエンジニアなので、ファントムの消える・現れるの電文が効かず、消えた鬼が見えたままになる。
    // 隠れる側にだけ、鬼がいちばん近いベントに入った・出たと送って姿を消す (エンジニアのベントは客の手元で通る)
    // 本編の CheckVanish / CheckAppear は RpcVanish / RpcAppear を通らず中で電文を送るので、こちらに付ける。
    // 待ち時間などで本体を止めた時 (__runOriginal が false) は送らない
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckVanish))]
    private static class VanishPatch
    {
        public static void Postfix(PlayerControl __instance, bool __runOriginal)
        {
            if (__runOriginal) SendVent(__instance, RpcCalls.EnterVent);
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckAppear))]
    private static class AppearPatch
    {
        public static void Postfix(PlayerControl __instance, bool __runOriginal)
        {
            if (__runOriginal) SendVent(__instance, RpcCalls.ExitVent);
        }
    }

    private static readonly Dictionary<byte, int> ventOf = new();

    private static void SendVent(PlayerControl seeker, RpcCalls call)
    {
        try
        {
            if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !seeker || !seekers.Contains(seeker.PlayerId)) return;
            int ventId;
            if (call == RpcCalls.EnterVent)
            {
                var vent = NearestVent(seeker.GetTruePosition());
                if (vent == null) return;
                ventId = vent.Id;
                ventOf[seeker.PlayerId] = ventId;
                // 本編の客はベントに入る人をベントまで歩かせるので、先にベントの上へ置いてその場で入らせる
                SendSnap(seeker, (UnityEngine.Vector2)vent.transform.position - seeker.Collider.offset);
            }
            else if (!ventOf.Remove(seeker.PlayerId, out ventId)) return;
            foreach (var pc in Players.All)
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected || pc.AmOwner || seekers.Contains(pc.PlayerId)) continue;
                int clientId = pc.GetClientId();
                if (clientId < 0) continue;
                var writer = AmongUsClient.Instance.StartRpcImmediately(seeker.MyPhysics.NetId, (byte)call, Hazel.SendOption.Reliable, clientId);
                writer.WritePacked(ventId);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
            // 本編の客はベントから出す時に人をそのベントの上へ置く。止まっている鬼は位置を送らないので、出し終えた頃に本当の位置へ戻す。
            // 出す電文より先に着くと上書きされるので、間を空けて信頼性のある送り方で送る
            if (call == RpcCalls.ExitVent)
            {
                var target = seeker;
                _ = new LateTask(() =>
                {
                    if (target && target.Data != null && !target.Data.Disconnected) SendSnap(target, (UnityEngine.Vector2)target.transform.position);
                }, 0.25f, "VanillaHnsHiddenSeeker.SnapBack");
            }
            Logger.Info($"{seeker.Data?.PlayerName}: 隠れる側へ {call} (ベント {ventId})", nameof(VanillaHnsHiddenSeeker));
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(VanillaHnsHiddenSeeker)); }
    }

    // 隠れる側に送った位置の番号。客はベントの出入りのたびに手元の番号を 1 進めるので、前に送った番号より先へ進めて送る
    private static readonly Dictionary<byte, ushort> sentSid = new();

    // 隠れる側にだけ鬼の位置を送る
    private static void SendSnap(PlayerControl seeker, UnityEngine.Vector2 pos)
    {
        try
        {
            if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost) return;
            var net = seeker.NetTransform;
            ushort sid = (ushort)(net.lastSequenceId + 3);
            // 鬼が止まっているとホストの番号は進まないので、前に送った番号 + 入る・出るの 2 つ分より先にする
            if (sentSid.TryGetValue(seeker.PlayerId, out var prev) && NetHelpers.SidGreaterThan((ushort)(prev + 3), sid)) sid = (ushort)(prev + 3);
            sentSid[seeker.PlayerId] = sid;
            foreach (var pc in Players.All)
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected || pc.AmOwner || seekers.Contains(pc.PlayerId)) continue;
                int clientId = pc.GetClientId();
                if (clientId < 0) continue;
                var writer = AmongUsClient.Instance.StartRpcImmediately(net.NetId, (byte)RpcCalls.SnapTo, Hazel.SendOption.Reliable, clientId);
                NetHelpers.WriteVector2(pos, writer);
                writer.Write(sid);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
        }
        catch (Exception ex) { Logger.Exception(ex, nameof(VanillaHnsHiddenSeeker)); }
    }

    private static Vent NearestVent(UnityEngine.Vector2 pos)
    {
        Vent best = null;
        float bestDist = float.MaxValue;
        if (!ShipStatus.Instance) return null;
        foreach (var v in ShipStatus.Instance.AllVents)
        {
            if (v == null) continue;
            float d = UnityEngine.Vector2.Distance(pos, v.transform.position);
            if (d < bestDist) { bestDist = d; best = v; }
        }
        return best;
    }

    // 鬼が鬼を狙うキルは断る。本編のホストは相手の陣営を見ないので、客の画面で鬼がクルーに見えていると通ってしまう
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
    private static class MurderPatch
    {
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(PlayerControl __instance, [HarmonyArgument(0)] PlayerControl target)
        {
            if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || !__instance || !target) return true;
            if (GameOptionsManager.Instance == null || GameOptionsManager.Instance.currentGameMode != GameModes.HideNSeek) return true;
            if (__instance.Data?.Role == null || target.Data?.Role == null) return true;
            if (!__instance.Data.Role.IsImpostor || !target.Data.Role.IsImpostor || __instance == target) return true;
            Logger.Info($"{__instance.Data.PlayerName} が鬼の {target.Data.PlayerName} を狙ったキルを断りました", nameof(VanillaHnsHiddenSeeker));
            return false;
        }
    }
}
