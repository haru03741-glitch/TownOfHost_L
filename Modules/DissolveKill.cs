using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TownOfHostL.Attributes;
using TownOfHostL.Core;
using TownOfHostL.Modules.Net;

namespace TownOfHostL.Modules;

/// <summary>
/// キルと同時に死体を消す役職のキルを、本編の客の画面で「溶ける死体」にする (ホストのみ)。
/// <para>
/// 本編の死体の型は、キルの瞬間に客の手元で殺し手の役職が決める。殺し手がヴァイパーならヴァイパーの死体になり、
/// 溶ける秒数はその瞬間の客の設定 (ViperDissolveTime) で決まる。溶け切った死体は絵も当たり判定も消えて通報できない。
/// そこでキルの直前に、見る人ごとに 1 つの包みで { 溶ける秒数を短くした設定 / 殺し手をヴァイパーにする SetRole } を送り、
/// キルの後で元の役職と設定へ戻す。偽の会議を使わないので、役職の作り直しが要らず、どの役職・ベントの中・死者の客にも効く。
/// </para>
/// <para>
/// 送らない客: ホスト・同じ版の TOHL の客 (死体を消す電文で消す)・殺し手本人 (本人の役職を作り直すことになる)・
/// 自分の画面でキルを持つデシンクの役職の客 (殺し手をキル役に見せると仲間になる)。この客たちは今までどおり
/// 死体の合わせ直し (<see cref="ClientBodySync"/>) か覆いで消す。
/// </para>
/// </summary>
public static class DissolveKill
{
    // 溶ける秒数。溶けるのはキルの動きが終わって死体が出てからで、0 だと溶けない。
    // 本編は秒数の 2 割・7 割の所で溶ける絵の動きを流し、終わりで絵を隠す。短すぎると動きの途中で隠して、溶けかけの死体が残る
    // (2026-11-27 実機: 0.1 秒は残り、1 秒は約 1 秒で消えた)
    public static float DissolveSeconds = 1.2f;
    // 役職と設定を元へ戻すまでの間 (キルの電文が届いて死体の型が決まった後)
    private const float RevertDelay = 2f;
    // 1 回のキルで送る宛先の上限。見る人ごとの包みを同じフレームに出すので、多い部屋ではこの手当てを使わず今までの消し方に任せる
    private const int MaxTargets = 8;
    // 死体を消す時に、溶かした印を信じる上限 (キルから死体を消すまで)。過ぎた印は、キルが通らなかった時の残りとみなす
    private const float HandledLifetime = 5f;
    // 本編は溶け切る瞬間に全員の PlayerControl の Data を引き、出したばかりで Data の無い CNO が 1 体でもいると例外で止まり、
    // 溶けかけの死体が通報できるまま残る。溶け切る見込みの時刻の前後は、CNO を出し始めない (CNO の Data が無いのは、
    // spawn から可視化までの 0.1〜0.7 秒と、客に届くまでの遅れ)。
    // キルから死体が溶け始めるまで (キルの動きの間) の見込み
    public const float KillSettle = 0.3f;
    // 溶け切る見込みの時刻の、これだけ前から CNO を出し始めない
    private const float BusyLead = 1.5f;
    // 溶け切る見込みの時刻の、これだけ後まで (客ごとの届く遅れとキルの動きの長さの違い)
    private const float BusyTail = 1.2f;
    // 溶け切る見込みの時刻 (古い物は IsBusy で捨てる)
    private static readonly List<float> dissolveEnds = new();

    /// <summary>
    /// 溶けている死体が客の画面で溶け切る頃か。この間は Data の無い CNO を出さない
    /// (<see cref="Core.CustomNetObject"/> の spawn と、死体の合わせ直しが見る)
    /// </summary>
    public static bool IsBusy
    {
        get
        {
            if (dissolveEnds.Count == 0) return false;
            float now = UnityEngine.Time.realtimeSinceStartup;
            bool busy = false;
            for (int i = dissolveEnds.Count - 1; i >= 0; i--)
            {
                float end = dissolveEnds[i];
                if (now > end + BusyTail) { dissolveEnds.RemoveAt(i); continue; }
                if (now >= end - BusyLead) busy = true;
            }
            return busy;
        }
    }

    /// <summary>今のキルの死体が、客の画面で <paramref name="seconds"/> 秒かけて溶けることを覚える (ホストのみ)</summary>
    public static void NoteDissolve(float seconds)
    {
        if (seconds <= 0f || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        dissolveEnds.Add(UnityEngine.Time.realtimeSinceStartup + KillSettle + seconds);
    }

    /// <summary>本物のヴァイパーのキルの死体が、客の画面で溶ける秒数 (客へ送っている設定の値)</summary>
    public static float ViperSeconds
        => Roles.Impostor.NormalViper.RoleInfo.IsEnable ? Roles.Impostor.NormalViper.DissolveTime : Utils.ViperDissolveTime;

    // 死体の持ち主 → 溶ける死体にした客 (clientId) と、送った時刻
    private static readonly Dictionary<byte, (HashSet<int> Clients, float At)> handled = new();
    // 溶ける秒数を短くしている見る人 (PlayerId) と、重なっている手当ての数。
    // キルの後の処理が設定をすぐ送り直すことがあるので、手当ての間は設定を組み立てるたびに短い秒数を入れる
    private static readonly Dictionary<byte, int> optionHolds = new();
    // 殺し手をヴァイパーに見せている (見る人, 殺し手) と、重なっている手当ての数・手当ての前の見え方。
    // 2 秒の間に続けてキルしても、最後の戻しで手当ての前の見え方へ戻す
    private static readonly Dictionary<(byte Seer, byte Killer), (int Count, RoleTypes Original)> roleHolds = new();

    [GameModuleInitializer]
    public static void Init()
    {
        handled.Clear();
        optionHolds.Clear();
        roleHolds.Clear();
        dissolveEnds.Clear();
    }

    /// <summary>この人へ送る設定の、ヴァイパーの溶ける秒数を短くする間か (<see cref="PlayerGameOptionsSender"/> が見る)</summary>
    public static bool Overrides(byte seerId) => optionHolds.ContainsKey(seerId);

    /// <summary>
    /// これから <paramref name="killer"/> が <paramref name="target"/> を殺す時に、本編の客の画面で死体が溶けるように送る。
    /// キルの電文より前に呼ぶ。送った客は <see cref="TakeHandled"/> で引ける。失敗してもキルは止めない
    /// </summary>
    public static void Prepare(PlayerControl killer, PlayerControl target)
    {
        try
        {
            PrepareCore(killer, target);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "DissolveKill");
        }
    }

    private static void PrepareCore(PlayerControl killer, PlayerControl target)
    {
        var client = AmongUsClient.Instance;
        if (killer == null || target == null || client == null || !client.AmHost) return;
        if (!GameStates.IsInTask || GameStates.IsMeeting) return;
        // 本物のヴァイパーは、見る人ごとのヴァイパー化を別の所で送る
        if (killer.Is(RoleTypes.Viper)) return;

        var seers = new List<(PlayerControl Seer, int ClientId, RoleTypes Was)>();
        foreach (var seer in Main.AllPlayerControls)
        {
            if (!seer || seer.AmOwner || CustomNetObject.Is(seer) || seer.Data == null || seer.Data.Disconnected) continue;
            if (seer.PlayerId == killer.PlayerId) continue;
            int clientId = seer.GetClientId();
            if (clientId < 0 || ModClientSync.IsPeer(clientId)) continue;
            var was = RoleView.Living(killer, seer);
            if (RoleView.IsDesync(seer) && !RoleView.IsKillerRole(was)) continue;
            seers.Add((seer, clientId, was));
        }
        if (seers.Count == 0) return;
        if (seers.Count > MaxTargets)
        {
            Logger.Info($"見る人が {seers.Count} 人いるので、溶ける死体にせず今までの消し方に任せます", "DissolveKill");
            return;
        }

        var sentTo = new HashSet<int>();
        var held = new List<byte>();
        foreach (var (seer, clientId, was) in seers)
        {
            var options = PlayerGameOptionsSender.Of(seer.PlayerId);
            if (options == null) continue;
            Hold(seer.PlayerId, killer.PlayerId, was);
            try
            {
                var bytes = options.BuildBytes();
                if (bytes == null) throw new InvalidOperationException("設定を組み立てられませんでした");
                Send(clientId, killer, RoleTypes.Viper, bytes);
                RoleView.NoteByClientId(clientId, killer, RoleTypes.Viper, transient: true);
                // 送った物として覚える (戻す前に会議になっても、会議明けの送り直しが元の秒数を送る)
                options.NoteSent(bytes);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "DissolveKill");
                Release(seer.PlayerId, killer.PlayerId);
                continue;
            }
            held.Add(seer.PlayerId);
            sentTo.Add(clientId);
        }
        if (held.Count == 0) return;
        float now = UnityEngine.Time.realtimeSinceStartup;
        handled[target.PlayerId] = (sentTo, now);
        NoteDissolve(DissolveSeconds);
        Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} の死体を、{sentTo.Count} 人の画面で溶ける死体にします", "DissolveKill");

        var capKiller = killer;
        byte killerId = killer.PlayerId;
        _ = new LateTask(() => Revert(capKiller, killerId, held), RevertDelay, "DissolveKillRevert");
    }

    // 殺し手の見え方と、溶ける秒数を元へ戻す
    private static void Revert(PlayerControl killer, byte killerId, List<byte> held)
    {
        var client = AmongUsClient.Instance;
        // 戻す前に死んでいたら役職は送らない (幽霊の役職を生きた役職で上書きしない)
        bool alive = killer != null && killer.IsAlive();
        foreach (var seerId in held)
        {
            var original = Release(seerId, killerId);
            if (client == null || !client.AmHost || !GameStates.IsInGame) continue;
            try
            {
                var seer = Utils.GetPlayerById(seerId);
                if (seer == null || seer.Data == null || seer.Data.Disconnected) continue;
                int clientId = seer.GetClientId();
                if (clientId < 0) continue;
                var options = PlayerGameOptionsSender.Of(seerId);
                // まだ同じ殺し手の手当てが重なっているなら、見え方はその戻しに任せる
                RoleTypes? role = alive ? original : null;
                // 会議中は役職を送らない (見せは生きている時の見え方の表に書いていないので、表は戻さなくてよい)。設定は送り直しを頼む
                if (GameStates.IsMeeting)
                {
                    options?.SetDirty();
                    continue;
                }
                var bytes = options?.BuildBytes();
                if (!role.HasValue && bytes == null) continue;
                Send(clientId, killer, role, bytes);
                if (role.HasValue) RoleView.RecordByClientId(clientId, killer, role.Value);
                if (bytes != null) options.NoteSent(bytes);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "DissolveKill");
            }
        }
    }

    // 見る人 1 人へ、{ 設定 / 殺し手への SetRole } を 1 つの包みで送る (どちらかは必ずある)
    private static void Send(int clientId, PlayerControl killer, RoleTypes? role, Il2CppStructArray<byte> options)
    {
        var client = AmongUsClient.Instance;
        var m = MessageWriter.Get(SendOption.Reliable);
        try
        {
            m.StartMessage(6);
            m.Write(client.GameId);
            m.WritePacked(clientId);
            if (options != null) PlayerGameOptionsSender.WriteOptions(m, options);
            if (role.HasValue)
            {
                m.StartMessage(2);
                m.WritePacked(killer.NetId);
                m.Write((byte)RpcCalls.SetRole); // 画面の表は呼び元が送った後で覚える
                m.Write((ushort)role.Value);
                m.Write(true);
                m.EndMessage();
            }
            m.EndMessage();
            client.SendOrDisconnect(m);
        }
        finally
        {
            m.Recycle();
        }
    }

    private static void Hold(byte seerId, byte killerId, RoleTypes was)
    {
        optionHolds[seerId] = optionHolds.GetValueOrDefault(seerId) + 1;
        var key = (seerId, killerId);
        if (roleHolds.TryGetValue(key, out var h)) roleHolds[key] = (h.Count + 1, h.Original);
        else roleHolds[key] = (1, was);
    }

    // 手当てを 1 つ外す。同じ (見る人, 殺し手) の手当てが無くなったら、手当ての前の見え方を返す
    private static RoleTypes? Release(byte seerId, byte killerId)
    {
        if (optionHolds.TryGetValue(seerId, out int n))
        {
            if (n <= 1) optionHolds.Remove(seerId);
            else optionHolds[seerId] = n - 1;
        }
        var key = (seerId, killerId);
        if (!roleHolds.TryGetValue(key, out var h)) return null;
        if (h.Count > 1)
        {
            roleHolds[key] = (h.Count - 1, h.Original);
            return null;
        }
        roleHolds.Remove(key);
        return h.Original;
    }

    /// <summary>この人の死体を溶ける死体にした客を引き取る (無ければ null)</summary>
    public static HashSet<int> TakeHandled(byte playerId)
        => handled.Remove(playerId, out var h) && UnityEngine.Time.realtimeSinceStartup - h.At <= HandledLifetime ? h.Clients : null;
}
