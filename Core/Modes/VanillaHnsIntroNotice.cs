using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TownOfHostL.Roles.Core;

using static TownOfHostL.Translator;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼで、TOHL の役職をもらった人にイントロで役職を知らせる。
/// 隠れる側のイントロには自分の名前が出ないが、鬼の名前 (鬼の PlayerInfo の名前) は出る。役職を持つ人にだけ、鬼の名前に
/// 「あなたは〇〇」を足した PlayerInfo を送り (本人宛ての Data)、少したってから元の名前の PlayerInfo を送り直す。
/// SetName は頭上の名前を書き換えるだけで PlayerInfo の名前は変えないので、イントロには届かない (2026-11-08 実機)。
/// 戻すのはホストのイントロの終わりを待たない (ホストが鬼だと、ホストのイントロは隠れる側より長い)。
/// ホスト自身の分は電文を使わず、ホストの画面のイントロの文字に足す。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHnsIntroNotice
{
    // 書き換えた名前を戻す相手: (鬼, 見る人の clientId)
    private static readonly List<(PlayerControl Seeker, int ClientId)> sent = new();
    private static string hostLine;
    // 配ってから元の名前に戻すまで。隠れる側のイントロが鬼の名前を出し終えた後にする
    private const float RestoreDelay = 10f;
    private const float SendDelay = 0.4f;

    private static string Line(CustomRoles role)
        => $" <size=50%>{string.Format(GetString("VanillaHns.YouAre"), $"<color={Utils.GetRoleColorCode(role)}>{GetString(role.ToString())}</color>")}</size>";

    /// <summary>前の試合の分を捨てる。役職を配る前に呼ぶ (配る役職が無い試合でも)。</summary>
    public static void Reset()
    {
        sent.Clear();
        hostLine = null;
    }

    /// <summary>役職を配った直後に呼ぶ。</summary>
    public static void Send(IReadOnlyDictionary<byte, CustomRoles> assigned)
    {
        sent.Clear();
        hostLine = null;
        var seekers = Players.All.Where(pc => pc.Data is { Role: not null, Disconnected: false } && pc.Data.Role.IsImpostor).ToList();
        foreach (var (id, role) in assigned)
        {
            var pc = Players.ById(id);
            if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
            if (pc.AmOwner) { hostLine = Line(role); continue; }
            foreach (var seeker in seekers) sent.Add((seeker, pc.OwnerId));
        }
        if (sent.Count == 0) return;
        // 役職を配ったフレームの終わりに、本編が鬼の PlayerInfo を (本当の名前で) 全員へ送る。それより後に届くよう少し待つ。
        // 客のイントロが鬼の名前を読むのは役職を受け取ってから 2 秒ほど後
        var lines = assigned.ToDictionary(p => p.Key, p => Line(p.Value));
        _ = new LateTask(() =>
        {
            foreach (var (seeker, clientId) in sent)
            {
                try
                {
                    var holder = Players.All.FirstOrDefault(p => p.OwnerId == clientId);
                    if (seeker && seeker.Data != null && holder && lines.TryGetValue(holder.PlayerId, out var line))
                        SendData(seeker.Data, clientId, seeker.Data.PlayerName + line);
                }
                catch (System.Exception ex) { Logger.Exception(ex, "VanillaHnsIntroNotice"); }
            }
        }, SendDelay, "VanillaHnsIntroNotice.Send");
        // 試合がすぐ閉じても、書き換えた名前を残さないよう戻す
        _ = new LateTask(Restore, RestoreDelay, "VanillaHnsIntroNotice.Restore", outlivesSession: true);
    }

    // 名前を一瞬だけ書き換えて serialize し、すぐ戻す。name が null なら今の名前のまま送る
    private static void SendData(NetworkedPlayerInfo data, int clientId, string name)
    {
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || data == null || client.GetClient(clientId) == null) return;
        var outfit = data.DefaultOutfit;
        string original = outfit.PlayerName;
        var msg = Hazel.MessageWriter.Get(Hazel.SendOption.Reliable);
        try
        {
            msg.StartMessage(6);
            msg.Write(client.GameId);
            msg.WritePacked(clientId);
            msg.StartMessage(1);
            msg.WritePacked(data.NetId);
            if (name != null) outfit.PlayerName = name;
            try { data.Serialize(msg, false); }
            finally { outfit.PlayerName = original; }
            msg.EndMessage();
            msg.EndMessage();
            client.SendOrDisconnect(msg);
        }
        finally
        {
            msg.Recycle();
        }
    }

    private static void Restore()
    {
        foreach (var (seeker, clientId) in sent)
        {
            try
            {
                if (seeker && seeker.Data != null && !seeker.Data.Disconnected) SendData(seeker.Data, clientId, null);
            }
            catch (System.Exception ex) { Logger.Exception(ex, "VanillaHnsIntroNotice"); }
        }
        sent.Clear();
        hostLine = null;
    }

    // 鬼の名前はイントロの途中で ImpostorName に入る。ホストの画面では入った後に書き足す
    // (IntroCutscene に Update が無いので HudManager の毎フレームで見る)
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    private static class HostPatch
    {
        public static void Postfix()
        {
            if (hostLine == null) return;
            var intro = IntroCutscene.Instance;
            if (!intro || !intro.ImpostorName || !intro.ImpostorName.gameObject.activeInHierarchy) return;
            var text = intro.ImpostorName.text;
            if (string.IsNullOrEmpty(text) || text.EndsWith(hostLine)) return;
            intro.ImpostorName.text = text + hostLine;
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    private static class EndPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            Restore();
        }
    }
}
