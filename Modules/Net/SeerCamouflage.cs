using System.Collections.Generic;
using HarmonyLib;
using Hazel;
using InnerNet;
using TownOfHostL.Attributes;

namespace TownOfHostL.Modules.Net;

// 決めた人の画面でだけ、他の全員を同じ見た目 (灰色・装飾なし) にして名前も隠す (ホストのみ)。
// OutfitSync と同じく、PlayerInfo を serialize して本人を対象にした Shapeshift で着せ直させるが、送り先は見る人の客だけ (宛先指定の GameData)。
// PlayerInfo はホストの手元の外見を一時的に替えて書き出し、すぐ元へ戻す (ホストの手元と他の客には何も変わらない)。
// 着せ直しを受けた客は名前を外見の名前に戻すので、続けて見る人へ名前を送り直す。名前は NotifyRoles が隠す (IsActive)。
// ホストが見る人の時は、手元で見た目だけ描き替える。
// 限り: 本物の変身・会議・他の外見の変更が見る人の客で描き直されると、その人だけ元の見た目に戻る。使う側は会議の前に End を呼ぶ。
public static class SeerCamouflage
{
    // 1 つの GameData に詰める大きさの目安 (SendGuard が分ける 1000 バイトより小さく)
    const int ChunkLimit = 700;
    // 同じ見る人へ続けて送る時の間
    const float ChunkGap = 0.25f;

    static readonly HashSet<byte> seers = new();
    // 見る人ごとの送りの世代。新しい送りを始めたら、前の送りの遅れて出る分は捨てる (灰色と元の見た目の追い越しを防ぐ)
    static readonly Dictionary<byte, int> generation = new();
    // 今は PlayerInfo を送れない間に頼まれた送り (見る人 → 見分けられなくするか)。送れるようになってから送る
    static readonly Dictionary<byte, bool> pending = new();

    static NetworkedPlayerInfo.PlayerOutfit UnknownOutfit => new NetworkedPlayerInfo.PlayerOutfit().Set("", 15, "", "", "", "");

    public static bool IsActive(byte seerId) => seers.Count > 0 && seers.Contains(seerId);

    [GameModuleInitializer]
    public static void Init()
    {
        seers.Clear();
        generation.Clear();
        pending.Clear();
    }

    /// <summary>seer の画面で、他の全員を見分けられなくする</summary>
    public static void Begin(PlayerControl seer)
    {
        if (seer == null || !AmongUsClient.Instance.AmHost || !seers.Add(seer.PlayerId)) return;
        Logger.Info($"見分けられなくする: {seer.GetNameWithRole()}", nameof(SeerCamouflage));
        // 先に隠した名前を組み立てておく (着せ直しの後に、その名前を送り直す)
        Utils.NotifyRolesNow(SpecifySeer: seer);
        Request(seer, disguise: true);
    }

    /// <summary>seer の画面の見た目を元へ戻す</summary>
    public static void End(PlayerControl seer)
    {
        if (seer == null || !seers.Remove(seer.PlayerId)) return;
        Logger.Info($"見分けを戻す: {seer.GetNameWithRole()}", nameof(SeerCamouflage));
        Utils.NotifyRolesNow(SpecifySeer: seer);
        Request(seer, disguise: false);
    }

    static void Request(PlayerControl seer, bool disguise)
    {
        int gen = generation.GetValueOrDefault(seer.PlayerId) + 1;
        generation[seer.PlayerId] = gen;
        if (!CanSend())
        {
            pending[seer.PlayerId] = disguise;
            return;
        }
        pending.Remove(seer.PlayerId);
        Send(seer, disguise, gen);
    }

    // 試合開始の直後は PlayerInfo の送信を止めている。AntiBlackout の退避中は IsDead が偽りの値になっている。
    // 鬼の体の差し替えの窓の間は GameManager が無いか、かくれんぼの物
    static bool CanSend() => !GameDataSerializePatch.DontTouch && !AntiBlackout.IsCached && !SeekerBody.InWindow;

    static void FlushPending()
    {
        if (pending.Count == 0 || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !CanSend()) return;
        foreach (var (seerId, disguise) in new List<KeyValuePair<byte, bool>>(pending))
        {
            pending.Remove(seerId);
            var seer = Utils.GetPlayerById(seerId);
            if (seer != null) Send(seer, disguise, generation.GetValueOrDefault(seerId));
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.FixedUpdate))]
    static class TickPatch
    {
        public static void Postfix() => FlushPending();
    }

    static void Send(PlayerControl seer, bool disguise, int gen)
    {
        if (seer.AmOwner)
        {
            SetLocally(seer, disguise);
            return;
        }
        if (seer.LacksClient("SeerCamouflage")) return;
        int clientId = seer.GetClientId();

        var chunks = new List<(MessageWriter writer, List<byte> targets)>();
        MessageWriter current = null;
        List<byte> currentTargets = null;
        foreach (var target in Main.AllPlayerControls)
        {
            if (target.PlayerId == seer.PlayerId || target.Data == null || target.Data.Disconnected) continue;
            // 死んだ人は見る人の画面に出ない。戻す時だけ PlayerInfo を書き戻す (見分けられない間に死んだ人の分)
            if (disguise && !target.IsAlive()) continue;
            if (current == null)
            {
                current = MessageWriter.Get(SendOption.Reliable);
                current.StartMessage(6);
                current.Write(AmongUsClient.Instance.GameId);
                current.WritePacked(clientId);
                currentTargets = new();
            }
            WriteTarget(current, target, disguise);
            currentTargets.Add(target.PlayerId);
            if (current.Length >= ChunkLimit)
            {
                current.EndMessage();
                chunks.Add((current, currentTargets));
                current = null;
            }
        }
        if (current != null)
        {
            current.EndMessage();
            chunks.Add((current, currentTargets));
        }

        byte seerId = seer.PlayerId;
        for (int i = 0; i < chunks.Count; i++)
        {
            var (writer, targets) = chunks[i];
            void SendChunk()
            {
                if (generation.GetValueOrDefault(seerId) != gen || !GameStates.IsInGame)
                {
                    writer.Recycle();
                    return;
                }
                AmongUsClient.Instance.SendOrDisconnect(writer);
                writer.Recycle();
                ResendNames(seerId, targets);
            }
            if (i == 0) SendChunk();
            else _ = new LateTask(SendChunk, ChunkGap * i, "SeerCamouflage", true);
        }
    }

    // target の PlayerInfo (見た目を替えた物か今の物) と、着せ直しのための本人への Shapeshift を書く
    static void WriteTarget(MessageWriter writer, PlayerControl target, bool disguise)
    {
        var data = target.Data;
        Dictionary<PlayerOutfitType, NetworkedPlayerInfo.PlayerOutfit> saved = null;
        if (disguise)
        {
            saved = new();
            foreach (var kv in data.Outfits) saved[kv.Key] = kv.Value;
            data.Outfits[PlayerOutfitType.Default] = UnknownOutfit;
            if (saved.ContainsKey(PlayerOutfitType.Shapeshifted)) data.Outfits[PlayerOutfitType.Shapeshifted] = UnknownOutfit;
        }
        GameDataSerializePatch.SerializeMessageCount++;
        try
        {
            writer.StartMessage(1);
            writer.WritePacked(data.NetId);
            data.Serialize(writer, false);
            writer.EndMessage();
        }
        finally
        {
            GameDataSerializePatch.SerializeMessageCount--;
            if (saved != null) foreach (var kv in saved) data.Outfits[kv.Key] = kv.Value;
        }
        if (!target.IsAlive()) return;

        // 着せ直し。本物の変身中の人は、戻す時は変身先へ変身し直す
        PlayerControl shiftTo = null;
        if (!disguise && Main.CheckShapeshift.TryGetValue(target.PlayerId, out var shifted) && shifted
            && Main.ShapeshiftTarget.TryGetValue(target.PlayerId, out var toId) && toId != target.PlayerId)
            shiftTo = Utils.GetPlayerById(toId);
        writer.StartMessage(2);
        writer.WritePacked(target.NetId);
        writer.Write((byte)RpcCalls.Shapeshift);
        writer.WriteNetObject(shiftTo ?? target);
        writer.Write(false);
        writer.EndMessage();
    }

    // 着せ直しを受けた客は名前を外見の名前に戻すので、NotifyRoles が配った名前を送り直す
    static void ResendNames(byte seerId, List<byte> targets)
    {
        var seer = Utils.GetPlayerById(seerId);
        if (seer == null || Main.LastNotifyNames == null) return;
        foreach (var id in targets)
        {
            var target = Utils.GetPlayerById(id);
            if (target == null) continue;
            if (Main.LastNotifyNames.TryGetValue((id, seerId), out var name) && name != null)
                target.RpcSetNamePrivate(name, true, seer, force: true);
        }
    }

    // ホストが見る人の時: 手元で見た目だけ描き替える (PlayerInfo は替えない)
    static void SetLocally(PlayerControl seer, bool disguise)
    {
        foreach (var target in Main.AllPlayerControls)
        {
            if (target.PlayerId == seer.PlayerId || target.Data == null || !target.IsAlive()) continue;
            try
            {
                if (disguise) target.RawSetOutfit(UnknownOutfit, PlayerOutfitType.Default);
                else
                {
                    var type = target.CurrentOutfitType == PlayerOutfitType.Shapeshifted ? PlayerOutfitType.Shapeshifted : PlayerOutfitType.Default;
                    if (!target.Data.Outfits.TryGetValue(type, out var outfit) || outfit == null) (type, outfit) = (PlayerOutfitType.Default, target.Data.DefaultOutfit);
                    target.RawSetOutfit(outfit, type);
                }
            }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(SeerCamouflage)); }
        }
        Utils.NotifyRoles(SpecifySeer: seer);
    }
}
