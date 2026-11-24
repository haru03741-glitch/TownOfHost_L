using System;
using System.Collections.Generic;
using Hazel;
using UnityEngine;
using TownOfHostL.Roles.Core;

namespace TownOfHostL.Modules.Net;

/// <summary>
/// ホストと同じ版の TOHL を入れた客に、TOHL の画面を出すための情報を送る (ホスト) ・受ける (客)。
/// <para>
/// 握手: ホストは入ってきた人に版を送る。客は版・コミット・ForkId がすべて自分と同じ時だけ、ホストにだけ宛てて版を 1 回返す。
/// ホストは返ってきた客だけを送り先にする。版が違う・ほかの Mod のホスト・本編のホストの時は、客は何も返さず本編の客のまま。
/// </para>
/// <para>
/// 送る物: 設定の値 (初期値と違う物だけ・変わった時はその分)、全員の役職 (配役の後と、変わった時)、
/// 客本人のボタンの見え方 (<see cref="RoleHudState"/>・変わった時だけ)、試合の結果。
/// どれも送り先の客だけに宛てる (本編の客には届かない)。1 本は 800B 前後までに分け、同じ客へは間を空けて送る。
/// </para>
/// </summary>
public static class ModClientSync
{
    /// <summary>同じ版かを見る印。版の知らせに載せている物と同じ</summary>
    public static string BuildTag => $"{ThisAssembly.Git.Commit}({ThisAssembly.Git.Branch})";

    public static bool IsSameBuild(PlayerVersion v)
        => v != null && v.forkId == Main.ForkId && v.version.Equals(Main.version) && v.tag == BuildTag;

    // ───────────── ホスト ─────────────

    private sealed class Peer
    {
        public readonly Queue<(CustomRPC call, Action<MessageWriter> write)> Queue = new();
        public float NextAt;
        // ボタンの見え方: 最後に組んだ物と、待ちに入っているか (待ちの 1 本は送る時に最新の物を書く)
        public RoleHudState Hud = RoleHudState.None;
        public bool HudKnown;
        public bool HudQueued;
    }

    // 送り先の客 (clientId ごと)
    private static readonly Dictionary<int, Peer> peers = new();
    // 同じ客へ続けて送る時の間 (同じ宛先へ同種の Reliable を秒十数本出すと公式サーバーに切られる)
    private const float PeerInterval = 0.25f;
    private const int OptionPairsPerMessage = 100;
    private const int EndPlayersPerMessage = 4;
    private const int RolePlayersPerMessage = 8;
    // 1 人の進み具合の文字列の上限 (バイト)。役職の数や名前の上限と合わせ、1 本が下の上限に収まるようにする
    private const int MaxProgressBytes = 120;
    private const int MaxMainRoles = 6;
    private const int MaxSubRoles = 12;
    // 1 本の上限。公式サーバーは 1199B までを通し 1250B で切るので、それより小さく取り、超えたら送らない
    private const int MaxMessageBytes = 1100;

    // 予約した時刻 (LateTask は部屋を出る・休む・客になる時にまとめて捨てられるので、古い予約は無いものとして扱う)
    private static float drainScheduledAt = float.NegativeInfinity;
    private static float optionFlushScheduledAt = float.NegativeInfinity;
    private static float roleFlushScheduledAt = float.NegativeInfinity;
    private const float ScheduleStale = 2f;
    private static bool Pending(float at) => Time.realtimeSinceStartup - at < ScheduleStale;
    private static bool fullOptionsPending;
    private static readonly HashSet<int> dirtyOptions = new();
    private static bool rolesSent;

    private static bool AmHost => AmongUsClient.Instance && AmongUsClient.Instance.AmHost;

    public static bool HasPeers
    {
        get
        {
            if (peers.Count == 0) return false;
            Prune();
            return peers.Count > 0;
        }
    }

    private static void Prune()
    {
        if (!AmongUsClient.Instance) { peers.Clear(); return; }
        List<int> gone = null;
        foreach (var id in peers.Keys)
            if (AmongUsClient.Instance.GetClient(id) == null) (gone ??= new()).Add(id);
        if (gone != null) foreach (var id in gone) peers.Remove(id);
    }

    /// <summary>ホストが客の版を受けた時。同じ版なら送り先に足し、設定を全部送る</summary>
    public static void OnVersionFromClient(PlayerControl pc, PlayerVersion version)
    {
        if (!AmHost || pc == null || pc.AmOwner) return;
        int clientId = pc.OwnerId;
        if (clientId < 0) return;
        if (!IsSameBuild(version))
        {
            peers.Remove(clientId);
            return;
        }
        if (peers.ContainsKey(clientId)) return;
        peers[clientId] = new Peer();
        Logger.Info($"{pc.Data?.PlayerName} は同じ版の TOHL なので、TOHL の画面の情報を送ります", "ModClientSync");
        EnqueueFullOptions(clientId);
        if (GameStates.IsInGame && rolesSent) EnqueueRoles(clientId, initial: true);
    }

    // ── ボタンの見え方 ──

    private const float HudInterval = 0.2f;
    private static float nextHudAt;

    /// <summary>ホストの毎フレーム。試合中、送り先の客それぞれのボタンの見え方を組み、変わっていたら送る</summary>
    public static void TickHud()
    {
        if (peers.Count == 0 || !rolesSent || !AmHost) return;
        float now = Time.realtimeSinceStartup;
        if (now < nextHudAt) return;
        nextHudAt = now + HudInterval;
        Prune();
        foreach (var (clientId, peer) in peers)
        {
            var pc = AmongUsClient.Instance.GetClient(clientId)?.Character;
            if (pc == null) continue;
            RoleHudState hud;
            try { hud = RoleHud.Build(pc, remote: true); }
            catch (Exception ex) { Logger.Exception(ex, "ModClientSync.Hud"); continue; }
            if (peer.HudKnown && peer.Hud.Equals(hud)) continue;
            peer.Hud = hud;
            peer.HudKnown = true;
            if (peer.HudQueued) continue;
            peer.HudQueued = true;
            var p = peer;
            Enqueue(clientId, CustomRPC.ModClientHud, w => p.Hud.Write(w));
        }
    }

    private static void Enqueue(int clientId, CustomRPC call, Action<MessageWriter> write)
    {
        if (!peers.TryGetValue(clientId, out var peer)) return;
        if (call == CustomRPC.ModClientRoles && peer.Queue.Count > 0)
        {
            // 役職は設定の送りの後ろに並べない (イントロに間に合わせる)。役職どうしの順は保つ
            var rest = peer.Queue.ToArray();
            peer.Queue.Clear();
            int i = 0;
            for (; i < rest.Length && rest[i].call == CustomRPC.ModClientRoles; i++) peer.Queue.Enqueue(rest[i]);
            peer.Queue.Enqueue((call, write));
            for (; i < rest.Length; i++) peer.Queue.Enqueue(rest[i]);
        }
        else peer.Queue.Enqueue((call, write));
        if (!Pending(drainScheduledAt)) Drain();
    }

    private static void Drain()
    {
        drainScheduledAt = float.NegativeInfinity;
        if (!AmHost || PlayerControl.LocalPlayer == null) { peers.Clear(); return; }
        Prune();
        float now = Time.realtimeSinceStartup;
        bool more = false;
        foreach (var (clientId, peer) in peers)
        {
            if (peer.Queue.Count == 0) continue;
            if (now >= peer.NextAt)
            {
                var (call, write) = peer.Queue.Dequeue();
                // 送りが失敗しても次の見え方を待ちに入れられるよう、取り出した時に下ろす
                if (call == CustomRPC.ModClientHud) peer.HudQueued = false;
                Send(clientId, call, write);
                peer.NextAt = now + PeerInterval;
            }
            if (peer.Queue.Count > 0) more = true;
        }
        if (more && !Pending(drainScheduledAt))
        {
            drainScheduledAt = Time.realtimeSinceStartup;
            _ = new LateTask(Drain, 0.05f, "", true);
        }
    }

    private static void Send(int clientId, CustomRPC call, Action<MessageWriter> write)
    {
        try
        {
            if (clientId < 0) return;
            var writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)call, SendOption.Reliable, clientId);
            write(writer);
            if (writer.Length > MaxMessageBytes)
            {
                Logger.Error($"{call} が {writer.Length}B になったので送りません", "ModClientSync");
                writer.Recycle();
                return;
            }
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "ModClientSync.Send");
        }
    }

    // ── 設定 ──

    public static void OnOptionChanged(OptionItem item)
    {
        if (peers.Count == 0 || !AmHost) return;
        dirtyOptions.Add(item.Index);
        ScheduleOptionFlush();
    }

    public static void OnOptionsReloaded()
    {
        if (peers.Count == 0 || !AmHost) return;
        fullOptionsPending = true;
        ScheduleOptionFlush();
    }

    private static void ScheduleOptionFlush()
    {
        if (Pending(optionFlushScheduledAt)) return;
        optionFlushScheduledAt = Time.realtimeSinceStartup;
        // 続けて変わった値 (プリセットの切り替え・スライダーの連打) を 1 本にまとめる
        _ = new LateTask(FlushOptions, 0.3f, "", true);
    }

    private static void FlushOptions()
    {
        optionFlushScheduledAt = float.NegativeInfinity;
        if (!AmHost || !HasPeers) { dirtyOptions.Clear(); fullOptionsPending = false; return; }
        if (fullOptionsPending)
        {
            fullOptionsPending = false;
            dirtyOptions.Clear();
            foreach (var id in new List<int>(peers.Keys)) EnqueueFullOptions(id);
            return;
        }
        if (dirtyOptions.Count == 0) return;
        var pairs = new List<(int, int)>(dirtyOptions.Count);
        foreach (var index in dirtyOptions) pairs.Add((index, OptionItem.AllOptions[index].StoredValue));
        dirtyOptions.Clear();
        foreach (var id in new List<int>(peers.Keys)) EnqueueOptionPairs(id, pairs, reset: false);
    }

    private static void EnqueueFullOptions(int clientId)
    {
        var pairs = new List<(int, int)>();
        foreach (var o in OptionItem.AllOptions)
        {
            if (o is PresetOptionItem) continue;
            var v = o.StoredValue;
            if (v != o.DefaultValue) pairs.Add((o.Index, v));
        }
        EnqueueOptionPairs(clientId, pairs, reset: true);
    }

    private static void EnqueueOptionPairs(int clientId, List<(int index, int value)> pairs, bool reset)
    {
        int start = 0;
        do
        {
            int from = start, count = Math.Min(OptionPairsPerMessage, pairs.Count - start);
            bool first = reset && start == 0;
            Enqueue(clientId, CustomRPC.ModClientOptions, w =>
            {
                w.Write(first);
                // 設定の番号は作った順の位置なので、数が違えば並びも違う。客は数が合わなければ使わない
                if (first) w.WritePacked(OptionItem.AllOptions.Count);
                w.WritePacked(count);
                for (int i = from; i < from + count; i++)
                {
                    w.WritePacked(pairs[i].index);
                    w.WritePacked(pairs[i].value);
                }
            });
            start += count;
        } while (start < pairs.Count);
    }

    // ── 役職 ──

    /// <summary>配役が終わった時 (本編のイントロを送る前) に、全員の役職を送る</summary>
    public static void SendRoles() => SendRoles(initial: true);

    private static void SendRoles(bool initial)
    {
        if (!AmHost) return;
        rolesSent = true;
        if (!HasPeers) return;
        foreach (var id in new List<int>(peers.Keys)) EnqueueRoles(id, initial);
    }

    /// <summary>試合の途中で役職・属性が変わった時。少し待ってから全員の分を送り直す</summary>
    public static void MarkRolesDirty()
    {
        if (!rolesSent || peers.Count == 0 || !AmHost || Pending(roleFlushScheduledAt) || !GameStates.IsInGame) return;
        roleFlushScheduledAt = Time.realtimeSinceStartup;
        _ = new LateTask(() =>
        {
            roleFlushScheduledAt = float.NegativeInfinity;
            if (rolesSent && GameStates.IsInGame) SendRoles(initial: false);
        }, 0.3f, "", true);
    }

    private static void EnqueueRoles(int clientId, bool initial)
    {
        // 1 本に 8 人まで。始めの 1 本だけが「新しい配役」の印を持つ
        var ids = new List<byte>(PlayerState.AllPlayerStates.Keys);
        int start = 0;
        do
        {
            int from = start, count = Math.Min(RolePlayersPerMessage, ids.Count - start);
            bool first = initial && start == 0;
            // 新しい配役を受けた客はボタンの見え方を捨てるので、組み直して送り直す
            if (first && peers.TryGetValue(clientId, out var hudPeer)) hudPeer.HudKnown = false;
            Enqueue(clientId, CustomRPC.ModClientRoles, w =>
            {
                w.Write(first);
                var receiver = AmongUsClient.Instance.GetClient(clientId)?.Character;
                int n = 0;
                for (int i = from; i < from + count; i++) if (PlayerState.GetByPlayerId(ids[i]) != null) n++;
                w.WritePacked(n);
                for (int i = from; i < from + count; i++)
                {
                    var id = ids[i];
                    var state = PlayerState.GetByPlayerId(id);
                    if (state == null) continue;
                    w.Write(id);
                    WritePlayer(w, id);
                    // 本人には、本人に見せる役職 (別の役職として見せる役職) を最後の役職として送る
                    CustomRoles? shown = receiver != null && receiver.PlayerId == id ? CustomRoleManager.GetByPlayerId(id)?.DisplayedRole : null;
                    WriteRoles(w, state, shown);
                }
            });
            start += Math.Max(count, 1);
        } while (start < ids.Count);
    }

    private static void WritePlayer(MessageWriter w, byte id)
    {
        var name = Main.AllPlayerNames != null && Main.AllPlayerNames.TryGetValue(id, out var n) ? n ?? "" : "";
        w.Write(name.Length > 20 ? name.Substring(0, 20) : name);
        Color32 c = Main.PlayerColors.TryGetValue(id, out var color) ? color : Color.white;
        w.Write(c.r); w.Write(c.g); w.Write(c.b);
    }

    private static void WriteRoles(MessageWriter w, PlayerState state, CustomRoles? shownLast)
    {
        // 役職の移り変わりは新しい方から決まった数まで (1 本の大きさを抑える)
        var mains = state.MainRoleList;
        int skip = Math.Max(0, mains.Count - MaxMainRoles);
        w.WritePacked(mains.Count - skip);
        for (int i = skip; i < mains.Count; i++)
            w.WritePacked((int)(i == mains.Count - 1 && shownLast is CustomRoles s ? s : mains[i]));
        int subs = Math.Min(state.SubRoles.Count, MaxSubRoles);
        w.WritePacked(subs);
        for (int i = 0; i < subs; i++) w.WritePacked((int)state.SubRoles[i]);
    }

    // ── 結果 ──

    /// <summary>UTF-8 で <paramref name="maxBytes"/> に収める。収まらない時は色の札を外してから後ろを切る (札の途中で切らない)</summary>
    private static string FitBytes(string text, int maxBytes)
    {
        var enc = System.Text.Encoding.UTF8;
        if (enc.GetByteCount(text) <= maxBytes) return text;
        text = text.RemoveHtmlTags();
        while (text.Length > 0 && enc.GetByteCount(text) > maxBytes) text = text.Substring(0, text.Length - 1);
        return text;
    }

    /// <summary>試合を終える電文の直前 (どの終わり方でもここを通る)。勝者と、結果画面の一覧に要る物を送る</summary>
    public static void SendEnd()
    {
        if (!AmHost || !rolesSent) return;
        rolesSent = false;
        if (!HasPeers) return;
        var winners = EndGamePatch.ResolveWinners().ConvertAll(pc => pc.PlayerId);
        var ids = new List<byte>(PlayerState.AllPlayerStates.Keys);
        var progress = new Dictionary<byte, string>(ids.Count);
        foreach (var id in ids)
        {
            string text;
            try { text = Utils.GetProgressTextFor(id); }
            catch (Exception ex) { text = ""; Logger.Exception(ex, "ModClientSync.Progress"); }
            progress[id] = FitBytes(text, MaxProgressBytes);
        }
        int parts = Math.Max(1, (ids.Count + EndPlayersPerMessage - 1) / EndPlayersPerMessage);
        foreach (var clientId in new List<int>(peers.Keys))
        {
            // 待っている役職の送り直しは捨てる (結果に全員の役職が載る。結果より後に届くと、客が新しい配役と取り違える)。
            // 残りは先に流し、結果より前に届ける
            if (peers.TryGetValue(clientId, out var peer) && peer.Queue.Count > 0)
            {
                foreach (var (call, write) in peer.Queue)
                    if (call is not (CustomRPC.ModClientRoles or CustomRPC.ModClientHud)) Send(clientId, call, write);
                peer.Queue.Clear();
            }
            if (peer != null) { peer.HudQueued = false; peer.HudKnown = false; peer.Hud = RoleHudState.None; }
            for (int part = 0; part < parts; part++)
            {
                int p = part, from = part * EndPlayersPerMessage, count = Math.Min(EndPlayersPerMessage, ids.Count - from);
                // 終わりの電文より先に届くよう、間を空けずに送る (1 人の客へ数本だけ)
                Send(clientId, CustomRPC.ModClientEnd, w =>
                {
                    w.Write((byte)p);
                    w.Write((byte)parts);
                    if (p == 0)
                    {
                        w.WritePacked((int)CustomWinnerHolder.WinnerTeam);
                        w.WritePacked(CustomWinnerHolder.AdditionalWinnerRoles.Count);
                        foreach (var r in CustomWinnerHolder.AdditionalWinnerRoles) w.WritePacked((int)r);
                        w.WritePacked(winners.Count);
                        foreach (var id in winners) w.Write(id);
                    }
                    w.WritePacked(count);
                    for (int i = from; i < from + count; i++)
                    {
                        var id = ids[i];
                        var state = PlayerState.GetByPlayerId(id);
                        w.Write(id);
                        WritePlayer(w, id);
                        WriteRoles(w, state, null);
                        w.Write(state.IsDead);
                        w.WritePacked((int)state.DeathReason);
                        w.Write(state.GetRealKiller());
                        w.Write(progress[id]);
                    }
                });
            }
        }
    }

    // ───────────── 客 ─────────────

    private static int linkedHostId = -1;
    private static bool rolesReady;
    private static bool endReady;
    private static readonly Dictionary<byte, string> endProgress = new();

    /// <summary>同じ版の TOHL のホストの部屋に客として入っていて、握手が済んでいる</summary>
    public static bool IsLinked
        => linkedHostId >= 0 && TownOfHostL.Core.VanillaHideAndSeek.IsClientMode
           && AmongUsClient.Instance && AmongUsClient.Instance.HostId == linkedHostId;
    /// <summary>この試合の役職が届いている (客の画面に TOHL の役職を出してよい)</summary>
    public static bool ViewReady => rolesReady && IsLinked;
    /// <summary>ホストから届いた、自分のボタンの見え方</summary>
    public static RoleHudState RemoteHud { get; private set; } = RoleHudState.None;
    /// <summary>この試合の結果が届いている</summary>
    public static bool EndReady => endReady && IsLinked;

    /// <summary>客がホストの版を受けた時。同じ版なら、ホストにだけ版を返して握手する</summary>
    public static void OnHostVersion(PlayerVersion version)
    {
        var client = AmongUsClient.Instance;
        if (!client || PlayerControl.LocalPlayer == null) return;
        if (!IsSameBuild(version))
        {
            if (linkedHostId >= 0) Reset();
            return;
        }
        if (linkedHostId == client.HostId)
        {
            // 結果画面からロビーへ戻ると、ホストからは入り直しに見えて送り先から外れている。
            // ホストは入ってきた人に版を送り直すので、受けるたびに返し直す (ホストは送り先に居れば何もしない)
            if (Time.realtimeSinceStartup - lastReplyAt >= 3f) Reply(client);
            return;
        }
        Reset();
        linkedHostId = client.HostId;
        OptionItem.BeginRemoteValues();
        Reply(client);
        Logger.Info("ホストが同じ版の TOHL なので、TOHL の画面を使います", "ModClientSync");
    }

    private static float lastReplyAt = float.NegativeInfinity;
    private static void Reply(AmongUsClient client)
    {
        lastReplyAt = Time.realtimeSinceStartup;
        var writer = client.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.VersionCheck, SendOption.Reliable, client.HostId);
        writer.Write(Main.PluginVersion);
        writer.Write(BuildTag);
        writer.Write(Main.ForkId);
        client.FinishRpcImmediately(writer);
    }

    /// <summary>客の間、毎フレーム。ホストが替わったら (自分が引き継いだ時も) 届いた物を捨てて本編の客に戻る</summary>
    public static void Tick()
    {
        if (linkedHostId < 0) return;
        var client = AmongUsClient.Instance;
        if (!client || client.HostId != linkedHostId)
        {
            Logger.Info("ホストが替わったので、TOHL の画面をやめて本編の客に戻ります", "ModClientSync");
            Reset();
        }
    }

    /// <summary>客の間に入る時・出る時・ホストが替わった時。届いた物を全部捨てる</summary>
    public static void Reset()
    {
        bool had = linkedHostId >= 0 || rolesReady || endReady;
        linkedHostId = -1;
        rolesReady = false;
        endReady = false;
        endProgress.Clear();
        RemoteHud = RoleHudState.None;
        ModClientView.ForgetHud();
        OptionItem.EndRemoteValues();
        // 自分がホストだった試合の役職のインスタンスを、客の画面の描き方 (別の役職としての見せ方など) に拾わせない
        CustomRoleManager.AllActiveRoles.Clear();
        Main.ShowChangeMainRole.Clear();
        if (had)
        {
            PlayerState.Clear();
            HudManagerPatch.HideOptionShower();
        }
        // ホストとしての送り先と待ち
        peers.Clear();
        dirtyOptions.Clear();
        fullOptionsPending = false;
        rolesSent = false;
    }

    /// <summary>客の側でホストからの電文を受ける。読んだら true</summary>
    public static bool Receive(CustomRPC call, MessageReader reader)
    {
        if (!IsLinked) return call is CustomRPC.ModClientOptions or CustomRPC.ModClientRoles or CustomRPC.ModClientEnd or CustomRPC.ModClientHud;
        try
        {
            switch (call)
            {
                case CustomRPC.ModClientOptions: ReadOptions(reader); return true;
                case CustomRPC.ModClientRoles: ReadRoles(reader); return true;
                case CustomRPC.ModClientEnd: ReadEnd(reader); return true;
                case CustomRPC.ModClientHud: ReadHud(reader); return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, $"ModClientSync.{call}");
            return true;
        }
        return false;
    }

    private static void ReadOptions(MessageReader reader)
    {
        if (reader.ReadBoolean())
        {
            int total = reader.ReadPackedInt32();
            if (total != OptionItem.AllOptions.Count)
            {
                Logger.Warn($"ホストの設定の数 ({total}) が手元 ({OptionItem.AllOptions.Count}) と違うので、TOHL の画面をやめます", "ModClientSync");
                Reset();
                return;
            }
            OptionItem.BeginRemoteValues();
        }
        int count = reader.ReadPackedInt32();
        for (int i = 0; i < count; i++)
        {
            int index = reader.ReadPackedInt32();
            int value = reader.ReadPackedInt32();
            OptionItem.SetRemoteValue(index, value);
        }
    }

    private static void ReadPlayer(MessageReader reader, byte id)
    {
        var name = reader.ReadString();
        var c = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), 255);
        // 客の間は試合の始めの処理 (名前と色の表を作る) が走らないので、ここで作る
        Main.AllPlayerNames ??= new();
        Main.PlayerColors ??= new();
        if (name != "") Main.AllPlayerNames[id] = name;
        Main.PlayerColors[id] = c;
    }

    private static PlayerState ReadRolesInto(MessageReader reader, byte id)
    {
        var state = PlayerState.GetByPlayerId(id);
        if (state == null) { PlayerState.Create(id); state = PlayerState.GetByPlayerId(id); }
        int mainCount = reader.ReadPackedInt32();
        var mains = new List<CustomRoles>(mainCount);
        for (int i = 0; i < mainCount; i++) mains.Add((CustomRoles)reader.ReadPackedInt32());
        int subCount = reader.ReadPackedInt32();
        state.SubRoles.Clear();
        for (int i = 0; i < subCount; i++) state.SubRoles.Add((CustomRoles)reader.ReadPackedInt32());
        state.MainRoleList.Clear();
        for (int i = 0; i < mains.Count - 1; i++) state.MainRoleList.Add(mains[i]);
        if (mains.Count > 0) state.SetMainRole(mains[^1]);
        return state;
    }

    private static void ReadRoles(MessageReader reader)
    {
        bool initial = reader.ReadBoolean();
        // 結果を受けた後に遅れて届いた送り直しは、次の試合の物ではないので読まない
        if (!initial && !rolesReady) return;
        if (initial)
        {
            // 新しい試合の配役。前の試合の物を捨てる
            PlayerState.Clear();
            CustomRoleManager.AllActiveRoles.Clear();
            Main.ShowChangeMainRole.Clear();
            Main.AllPlayerNames = new();
            Main.PlayerColors = new();
            RemoteHud = RoleHudState.None;
            endReady = false;
            endProgress.Clear();
        }
        int count = reader.ReadPackedInt32();
        for (int i = 0; i < count; i++)
        {
            byte id = reader.ReadByte();
            ReadPlayer(reader, id);
            ReadRolesInto(reader, id);
        }
        rolesReady = true;
    }

    private static void ReadHud(MessageReader reader)
    {
        var hud = RoleHudState.Read(reader);
        // 配役より前・結果の後に届いた物は、この試合の物ではない
        if (rolesReady) RemoteHud = hud;
    }

    private static void ReadEnd(MessageReader reader)
    {
        int part = reader.ReadByte();
        int parts = reader.ReadByte();
        if (part == 0)
        {
            endReady = false;
            endProgress.Clear();
            CustomWinnerHolder.WinnerTeam = (CustomWinner)reader.ReadPackedInt32();
            CustomWinnerHolder.AdditionalWinnerRoles = new();
            int add = reader.ReadPackedInt32();
            for (int i = 0; i < add; i++) CustomWinnerHolder.AdditionalWinnerRoles.Add((CustomRoles)reader.ReadPackedInt32());
            Main.winnerList = new();
            int winners = reader.ReadPackedInt32();
            for (int i = 0; i < winners; i++) Main.winnerList.Add(reader.ReadByte());
        }
        int count = reader.ReadPackedInt32();
        for (int i = 0; i < count; i++)
        {
            byte id = reader.ReadByte();
            ReadPlayer(reader, id);
            var state = ReadRolesInto(reader, id);
            state.IsDead = reader.ReadBoolean();
            state.DeathReason = (CustomDeathReason)reader.ReadPackedInt32();
            byte killer = reader.ReadByte();
            state.RealKiller = killer == byte.MaxValue ? (DateTime.MinValue, byte.MaxValue) : (DateTime.Now, killer);
            endProgress[id] = reader.ReadString();
        }
        if (part != parts - 1) return;
        EndGamePatch.SummaryText = new();
        foreach (var id in PlayerState.AllPlayerStates.Keys)
        {
            try { EndGamePatch.SummaryText[id] = Utils.SummaryTexts(id, false, endProgress.TryGetValue(id, out var p) ? p : ""); }
            catch (Exception ex) { EndGamePatch.SummaryText[id] = $"{id}"; Logger.Exception(ex, "ModClientSync.Summary"); }
        }
        endReady = true;
        rolesReady = false;
        RemoteHud = RoleHudState.None;
    }

    /// <summary>結果画面に出し終えた時</summary>
    public static void OnEndShown() => endReady = false;
}
