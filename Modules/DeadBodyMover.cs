using System.Collections.Generic;
using System.Linq;
using Hazel;
using InnerNet;
using UnityEngine;
using TownOfHostL.Attributes;
using TownOfHostL.Core;
using TownOfHostL.Modules.Net;

namespace TownOfHostL.Modules;

/// <summary>
/// 床に残っている死体を別の場所へ動かす (ホストだけが呼ぶ)。
/// <para>
/// 本編には死体を動かす電文も消す電文も無いので、本編の客の画面では「元の所を床の色で覆い、新しい所に作り直す」。
/// 作り直しは、客ごとに 1 つの包みで「死んでいる持ち主の幽霊を新しい所へ SnapTo → 殺し手の CNO に持ち主を殺させる」を送る。
/// 客の手元で本編のキルの演出が走り、持ち主の色の本物の死体が新しい所にできる。客はその死体をボタンで通報できる
/// (2026-11-25 公式鯖・本編の客で確認)。持ち主本人の客には送らない (その客では本人が殺される側になる)。
/// </para>
/// <para>
/// 本編のキルの演出は、殺し手の体のキルの動きが終わるのを待ってから、殺し手を死体の位置へ寄せ、最後に死体を有効にする
/// (有効になるまで、客の通報ボタンはその死体で光らない)。殺し手は、動きを持つ生きた体でないと演出が最後まで進まない。
/// そこで殺し手に CNO を 1 体出し、誰にも見えない遠くで動かす。演出の最後に CNO は死体の位置へ寄せられるので、
/// CNO の体が見えないよう絵を床の色の覆いにし、死体が有効になったらすぐ消す。
/// </para>
/// <para>
/// ホストの手元の死体はそのまま動かす。同じ版の TOHL の客には動かす電文を送り、覆いと作り直しは出さない。
/// 覆った元の所でも、本編の客の通報ボタンは光る (下に死体が残っている)。その通報は <see cref="AllowsReport"/> で捨てる。
/// 会議が始まると客の手元の死体は全部消えるので、覆いも外して記録を捨てる。
/// </para>
/// </summary>
public static class DeadBodyMover
{
    private const string Tag = nameof(DeadBodyMover);
    // 殺し手の CNO を置く所 (地図の外)
    private static readonly Vector2 KillerPark = new(40f, 40f);
    // 殺し手の CNO を消すまでの間。CNO のキルの動きはすぐ終わり、殺し手は死体の位置へ寄せられて床色の四角に見えるので、
    // 演出が終わって死体が有効になってから、なるべく早く消す (公式鯖・本編の客で 0.3 秒以内に寄せられた)
    private const float KillerHold = 0.6f;
    // 殺し手の CNO が出るまでの待ちの上限 (イントロ・会議明けの待ちを含む)
    private const float SpawnTimeout = 30f;
    // 本編の通報の届く距離に足す余り (客とホストの位置のずれ)
    private const float ReportSlack = 1f;
    // 持ち主の NetTransform に送る SnapTo の通し番号の進め幅。幽霊の歩きの電文がホストに届く前でも客の番号より新しくなるよう、
    // ExtendedPlayerControl.SendSnapTo と同じだけ進め、ホストの手元の番号もそろえる
    private const uint SnapSequenceStep = 32;
    // 殺し手と覆いの CNO を出す時の SnapTo (1 体 2 本)
    private const int CnoSnapCost = 4;

    private sealed class Move
    {
        public byte ParentId;
        public Vector2 StandPos;
        public CustomNetObject Killer;
        public HashSet<int> ModClients;
        public float Deadline;
    }

    // 動かしたことのある死体の持ち主 (会議まで)
    private static readonly HashSet<byte> movedParents = new();
    // 元の所の覆い (会議まで)
    private static readonly List<CustomNetObject> covers = new();
    // 作り直しの途中の物 (持ち主ごとに 1 つ)
    private static readonly Dictionary<byte, Move> inFlight = new();

    [GameModuleInitializer]
    public static void Init()
    {
        movedParents.Clear();
        covers.Clear();
        inFlight.Clear();
    }

    /// <summary>この人の死体を作り直している途中か (終わるまで次は動かせない)</summary>
    public static bool IsMoving(byte parentId) => inFlight.ContainsKey(parentId);

    /// <summary>作り直しの途中の死体があるか</summary>
    public static bool IsBusy => inFlight.Count > 0;

    /// <summary>画面の死体を合わせ直せた客から、元の所の覆いを外す (その客の画面には、元の所の死体がもう無い)</summary>
    public static void HideCoversFrom(int clientId)
    {
        foreach (var c in covers) c?.HideFromClient(clientId);
    }

    /// <summary>
    /// 客の手元で <paramref name="parent"/> の死体を <paramref name="standPos"/> に作る 2 つの電文を書く:
    /// 持ち主の幽霊を SnapTo で寄せ、殺し手の CNO (<paramref name="killerNetId"/>) に持ち主を殺させる。
    /// 殺し手 1 体に、同じ包みで 2 人を殺させない (キルの演出が終わらず、死体が通報できないまま残る)。
    /// </summary>
    public static void WriteRecreate(MessageWriter m, PlayerControl parent, Vector2 standPos, ushort seq, uint killerNetId)
    {
        m.StartMessage(2);
        m.WritePacked(parent.NetTransform.NetId);
        m.Write((byte)RpcCalls.SnapTo);
        NetHelpers.WriteVector2(standPos, m);
        m.Write(seq);
        m.EndMessage();
        m.StartMessage(2);
        m.WritePacked(killerNetId);
        m.Write((byte)RpcCalls.MurderPlayer);
        m.WriteNetObject(parent);
        m.Write((int)MurderResultFlags.Succeeded);
        m.EndMessage();
    }

    /// <summary>
    /// <paramref name="parentId"/> の死体を、<paramref name="standPos"/> に立っていた人が殺された時の場所へ動かす。
    /// 持ち主が死んでいない・死体が無い・消された・作り直しの途中・CNO が出せない (試合の外) 時は false
    /// </summary>
    public static bool MoveBody(byte parentId, Vector2 standPos)
    {
        if (!AmongUsClient.Instance || !AmongUsClient.Instance.AmHost || GameStates.IsMeeting) return false;
        if (inFlight.ContainsKey(parentId) || DeadBodyEraser.IsErased(parentId)) return false;
        var parent = Utils.GetPlayerById(parentId);
        var body = FindBody(parentId);
        // 作り直しは客の手元で持ち主を殺すので、生きている人 (生き返った人など) の死体は動かさない
        if (parent == null || parent.Data == null || !parent.Data.IsDead || parent.Data.Disconnected || body == null) return false;

        // 客の人数ぶんの SnapTo を払えない時は、ホストの手元も動かさずにやめる (動かすと客と位置がずれる)
        if (!SnapToBudget.HasHeadroom(VanillaTargetCount(parentId) + CnoSnapCost)) return false;

        var oldCenter = DeadBodyEraser.CenterOf(body);
        var killer = CustomNetObject.Create(DeadBodyEraser.CoverSpriteAt(standPos), KillerPark);
        if (killer == null) return false;

        // ホストの手元
        PlaceLocal(body, standPos);
        movedParents.Add(parentId);

        // 同じ版の TOHL の客は手元で動かす。その客には覆いも作り直しも出さない
        var modClients = new HashSet<int>();
        Net.ModClientSync.SendBodyMove(parentId, standPos, modClients);
        killer.HideFrom(p => p.AmOwner || modClients.Contains(p.OwnerId));

        var cover = DeadBodyEraser.CoverAt(oldCenter, modClients);
        if (cover != null) covers.Add(cover);

        var move = new Move { ParentId = parentId, StandPos = standPos, Killer = killer, ModClients = modClients, Deadline = Time.realtimeSinceStartup + SpawnTimeout };
        inFlight[parentId] = move;
        WaitLive(move);
        Logger.Info($"{parentId} の死体を ({standPos.x:0.00},{standPos.y:0.00}) へ動かします", Tag);
        return true;
    }

    /// <summary>
    /// 通報を通してよいか。動かした死体は、ホストの手元の死体 (今の場所) から通報の届く距離にいる人の通報だけ通す
    /// (本編の客は覆った元の所でも通報ボタンが光る)
    /// </summary>
    public static bool AllowsReport(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (reporter == null || target == null || !movedParents.Contains(target.PlayerId)) return true;
        var body = FindBody(target.PlayerId);
        if (body == null) return true;
        var range = reporter.MaxReportDistance + ReportSlack;
        var ok = Vector2.Distance(reporter.GetTruePosition(), body.TruePosition) <= range;
        if (!ok) Logger.Info($"{reporter.PlayerId} の通報は動かす前の {target.PlayerId} の死体なので捨てます", Tag);
        return ok;
    }

    public static void OnReportDeadBody()
    {
        ClientBodySync.OnReportDeadBody();
        foreach (var c in covers) c?.Despawn();
        covers.Clear();
        foreach (var m in inFlight.Values) m.Killer?.Despawn();
        inFlight.Clear();
        movedParents.Clear();
    }

    /// <summary>同じ版の TOHL の客の手元で、ホストが動かした死体を動かす</summary>
    public static void MoveOnClient(byte parentId, Vector2 standPos)
    {
        var body = FindBody(parentId);
        if (body == null)
        {
            Logger.Warn($"{parentId} の死体を動かすよう届きましたが、手元に死体がありません", Tag);
            return;
        }
        PlaceLocal(body, standPos);
    }

    private static DeadBody FindBody(byte parentId)
        => Object.FindObjectsOfType<DeadBody>().FirstOrDefault(b => b != null && b.gameObject.activeSelf && b.ParentId == parentId);

    // 本編のキルの演出と同じく、立っていた位置に体の置き場のずれを足して置く
    private static void PlaceLocal(DeadBody body, Vector2 standPos)
    {
        var offset = PlayerControl.LocalPlayer ? PlayerControl.LocalPlayer.KillAnimations[0].BodyOffset : Vector3.zero;
        Vector3 v = (Vector3)standPos + offset;
        v.z = v.y / 1000f;
        body.transform.position = v;
    }

    private static void WaitLive(Move move)
    {
        new LateTask(() =>
        {
            if (!inFlight.TryGetValue(move.ParentId, out var cur) || cur != move) return;
            if (move.Killer.IsGone)
            {
                inFlight.Remove(move.ParentId);
                return;
            }
            if (!move.Killer.IsLive)
            {
                if (Time.realtimeSinceStartup > move.Deadline)
                {
                    Logger.Warn($"{move.ParentId} の死体の作り直しの CNO が出ないので止めます", Tag);
                    Finish(move);
                    return;
                }
                WaitLive(move);
                return;
            }
            Recreate(move);
        }, 0.1f, "");
    }

    private static void Finish(Move move)
    {
        move.Killer.Despawn();
        if (inFlight.TryGetValue(move.ParentId, out var cur) && cur == move) inFlight.Remove(move.ParentId);
    }

    // 客ごとに 1 つの包みで、持ち主の幽霊を新しい所へ寄せてから、殺し手の CNO に持ち主を殺させる。
    // 公式鯖は 1 パケットにルートのメッセージを 2 個以上入れると切るので、宛先ごとに別のパケットで送る
    private static void Recreate(Move move)
    {
        var client = AmongUsClient.Instance;
        var parent = Utils.GetPlayerById(move.ParentId);
        if (parent == null || parent.Data == null || !parent.Data.IsDead || parent.Data.Disconnected || move.Killer.NetId == 0)
        {
            Finish(move);
            return;
        }
        var targets = VanillaTargets(move.ParentId, move.ModClients);
        // CNO を出している間に SnapTo を使われて足りなくなったら、払えるまで待つ
        if (!SnapToBudget.HasHeadroom(targets.Count))
        {
            if (Time.realtimeSinceStartup > move.Deadline)
            {
                Logger.Warn($"{move.ParentId} の死体の作り直しは SnapTo が足りないので止めます", Tag);
                Finish(move);
                return;
            }
            WaitLive(move);
            return;
        }
        var nt = parent.NetTransform;
        var seq = (ushort)(nt.lastSequenceId + SnapSequenceStep);
        // ホストの手元の番号もそろえる (そろえないと、次にこの人へ送る SnapTo の番号が客の番号より古くなり捨てられる)
        nt.lastSequenceId = seq;
        int sent = 0;
        foreach (var target in targets)
        {
            var m = MessageWriter.Get(SendOption.Reliable);
            m.StartMessage(6);
            m.Write(client.GameId);
            m.WritePacked(target.OwnerId);
            WriteRecreate(m, parent, move.StandPos, seq, move.Killer.NetId);
            m.EndMessage();
            client.SendOrDisconnect(m);
            m.Recycle();
            SnapToBudget.Record();
            sent++;
        }
        Logger.Info($"{move.ParentId} の死体を作り直しました (客 {sent})", Tag);
        new LateTask(() =>
        {
            Finish(move);
            // 本編の客の画面には元の所の死体が覆いの下に残る。送れる客から、画面の死体をホストの手元に合わせ直す
            ClientBodySync.Request();
        }, KillerHold, "");
    }

    // 作り直しを送る本編の客 (ホスト・CNO・切断した人・持ち主本人・同じ版の TOHL の客を除く)
    private static List<PlayerControl> VanillaTargets(byte parentId, HashSet<int> modClients)
    {
        var list = new List<PlayerControl>();
        foreach (var target in PlayerControl.AllPlayerControls.ToArray())
        {
            if (!target || target.AmOwner || CustomNetObject.Is(target) || target.Data == null || target.Data.Disconnected) continue;
            if (target.PlayerId == parentId || (modClients != null && modClients.Contains(target.OwnerId))) continue;
            list.Add(target);
        }
        return list;
    }

    private static int VanillaTargetCount(byte parentId) => VanillaTargets(parentId, null).Count;
}
