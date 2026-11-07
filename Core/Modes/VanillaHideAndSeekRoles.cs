using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHostL.Roles.Core;

using static TownOfHostL.Translator;

namespace TownOfHostL.Core.Modes;

/// <summary>
/// 本編のかくれんぼ (TOHL が休んでいる間) で、隠れる側に狐とトロールを載せる。
/// 本編の役職 (鬼 = Impostor・隠れる側 = Engineer) と勝敗はそのままにし、TOHL は誰がどの役職かを覚えて帽子で印を付け、
/// ロビーに戻ってきた人ごとに、狐とトロールを含めた勝者をチャットで知らせる。
/// 人数と確率は TOHL のかくれんぼの狐・トロールの設定を使う。
/// </summary>
[PatchBundle(PatchBundles.VanillaHideAndSeek)]
public static class VanillaHideAndSeekRoles
{
    private static readonly (CustomRoles Role, string HatId)[] Roles =
    [
        (CustomRoles.HASFox, "hat_claws_foxOrange"),
        (CustomRoles.HASTroll, "hat_clown_purple"),
        (CustomRoles.HASPortalMaker, "hat_pk05_Wizardhat"),
    ];
    // ポータルメーカーはペットのボタンで印を置くので、ペットを持っていなければ持たせる
    private const string PortalMakerPet = "pet_UFO";

    // イントロが明けてから印を付ける
    private const float MarkDelay = 8f;
    // 客は結果画面から好きな時にロビーへ戻ってくるので、戻ってきた人ごとに知らせる。
    // 戻ってから画面が落ち着くのを待って送り、結果は試合の後しばらくだけ覚えておく
    private const float AnnounceDelay = 2.5f;
    private const float ResultKeepSeconds = 120f;

    private static readonly Dictionary<byte, CustomRoles> assigned = new();
    private static readonly Dictionary<byte, string> originalHats = new();
    private static readonly Dictionary<byte, string> originalPets = new();
    private static string pendingResult;
    private static float resultUntil;

    private static void Assign()
    {
        assigned.Clear();
        originalHats.Clear();
        originalPets.Clear();
        pendingResult = null;
        VanillaHnsPortalMaker.Reset();
        var hiders = Players.All
            .Where(pc => pc.Data != null && pc.Data.Role != null && !pc.Data.Role.IsImpostor)
            .ToList();
        var rand = IRandom.Instance;
        foreach (var (role, _) in Roles)
        {
            int count = Math.Min(role.GetCount(), hiders.Count);
            if (count <= 0 || rand.Next(100) >= role.GetChance()) continue;
            for (int i = 0; i < count; i++)
            {
                var pc = hiders[rand.Next(hiders.Count)];
                hiders.Remove(pc);
                assigned[pc.PlayerId] = role;
            }
        }
        if (assigned.Count == 0) return;
        Logger.Info("割り当て: " + string.Join(", ", assigned.Select(p => $"{Players.ById(p.Key)?.Data?.PlayerName}={p.Value}")), "VanillaHideAndSeekRoles");
        _ = new LateTask(Mark, MarkDelay, "VanillaHideAndSeekRoles.Mark");
    }

    private static void Mark()
    {
        foreach (var (id, role) in assigned)
        {
            var pc = Players.ById(id);
            if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
            originalHats[id] = pc.Data.DefaultOutfit.HatId;
            SetHat(pc, Roles.First(r => r.Role == role).HatId);
            var petId = pc.Data.DefaultOutfit.PetId;
            if (role == CustomRoles.HASPortalMaker && (string.IsNullOrEmpty(petId) || petId == "pet_EmptyPet"))
            {
                originalPets[id] = petId;
                SetPet(pc, PortalMakerPet);
            }
        }
    }

    // 帽子とペットの電文には通し番号が付き、受け手は手元の番号より新しい物しか受け付けない。
    // 本人が自分で外見を送った分をホストの手元の番号が数えていないことがあり、そのまま送ると本人の画面で捨てられる。
    // 送る前にホストの手元の番号を先へ進めておく (番号は 1 バイトで、差が 127 までなら新しいと見なされる)
    private const byte CosmeticSequenceSkip = 16;

    private static void SetHat(PlayerControl pc, string hatId)
    {
        pc.Data.DefaultOutfit.HatSequenceId += CosmeticSequenceSkip;
        pc.RpcSetHat(hatId);
    }

    private static void SetPet(PlayerControl pc, string petId)
    {
        pc.Data.DefaultOutfit.PetSequenceId += CosmeticSequenceSkip;
        pc.RpcSetPet(petId);
    }

    /// <summary>今の試合で TOHL が配った役職。配っていなければ NotAssigned。</summary>
    public static CustomRoles RoleOf(byte playerId) => assigned.TryGetValue(playerId, out var role) ? role : CustomRoles.NotAssigned;

    private static void Judge(GameOverReason reason)
    {
        if (assigned.Count == 0) return;
        bool seekersWon = GameManager.Instance
            ? GameManager.Instance.DidImpostorsWin(reason)
            : reason is GameOverReason.HideAndSeek_ImpostorsByKills or GameOverReason.CrewmateDisconnect;
        var troll = assigned.FirstOrDefault(p => p.Value == CustomRoles.HASTroll && Players.ById(p.Key)?.Data is { IsDead: true, Disconnected: false });
        var sb = new StringBuilder();
        foreach (var (id, role) in assigned)
            sb.Append(GetString(role.ToString())).Append(": ").Append(Players.ById(id)?.Data?.PlayerName ?? "?").Append('\n');
        if (troll.Value == CustomRoles.HASTroll)
        {
            // 鬼にキルされたトロールの単独勝ち
            sb.Append(string.Format(GetString("VanillaHns.TrollWin"), Players.ById(troll.Key)?.Data?.PlayerName));
        }
        else
        {
            sb.Append(GetString(seekersWon ? "VanillaHns.SeekersWin" : "VanillaHns.HidersWin"));
            var foxes = assigned.Where(p => p.Value == CustomRoles.HASFox && Players.ById(p.Key)?.Data is { IsDead: false, Disconnected: false })
                .Select(p => Players.ById(p.Key).Data.PlayerName).ToList();
            if (foxes.Count > 0) sb.Append('\n').Append(string.Format(GetString("VanillaHns.FoxAlsoWin"), string.Join(", ", foxes)));
        }
        pendingResult = Truncate(sb.ToString(), MaxResultBytes);
        resultUntil = UnityEngine.Time.realtimeSinceStartup + ResultKeepSeconds;
        Logger.Info($"結果 ({reason}): {pendingResult.Replace("\n", " / ")}", "VanillaHideAndSeekRoles");
    }

    // 1 通の SendChat に入れる長さ。公式鯖は 1 通 1200 バイト前後で切断するので、ヘッダの分を残して切る
    private const int MaxResultBytes = 800;

    private static string Truncate(string text, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
        const string ellipsis = "…";
        int budget = maxBytes - Encoding.UTF8.GetByteCount(ellipsis);
        int len = 0, bytes = 0;
        while (len < text.Length)
        {
            int step = char.IsHighSurrogate(text[len]) && len + 1 < text.Length ? 2 : 1;
            int b = Encoding.UTF8.GetByteCount(text.AsSpan(len, step));
            if (bytes + b > budget) break;
            bytes += b;
            len += step;
        }
        return text[..len] + ellipsis;
    }

    private static void OnLobby()
    {
        // 試合の中で付けた帽子を、元の帽子に戻す
        foreach (var (id, hat) in originalHats)
        {
            var pc = Players.ById(id);
            if (pc != null && pc.Data != null && !pc.Data.Disconnected) SetHat(pc, hat);
        }
        originalHats.Clear();
        foreach (var (id, pet) in originalPets)
        {
            var pc = Players.ById(id);
            if (pc != null && pc.Data != null && !pc.Data.Disconnected) SetPet(pc, pet ?? "");
        }
        originalPets.Clear();
        assigned.Clear();
        VanillaHnsPortalMaker.Reset();
        if (pendingResult == null) return;
        // 前の試合の結果を、別の部屋や次のロビーで出し直さない
        if (UnityEngine.Time.realtimeSinceStartup > resultUntil) { pendingResult = null; return; }
        // ホストは自分のチャット欄に出すだけ (送らない)
        if (HudManager.InstanceExists && PlayerControl.LocalPlayer)
            HudManager.Instance.Chat.AddChat(PlayerControl.LocalPlayer, pendingResult);
    }

    private static void OnPlayerJoined(int clientId)
    {
        if (pendingResult == null) return;
        if (UnityEngine.Time.realtimeSinceStartup > resultUntil) { pendingResult = null; return; }
        var text = pendingResult;
        _ = new LateTask(() =>
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || !PlayerControl.LocalPlayer || client.GetClient(clientId) == null) return;
            var writer = client.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)RpcCalls.SendChat, Hazel.SendOption.Reliable, clientId);
            writer.Write(text);
            client.FinishRpcImmediately(writer);
        }, AnnounceDelay, "VanillaHideAndSeekRoles.Announce", outlivesSession: true);
    }

    // 本編の割り当て (鬼と隠れる側) が済んだ後に選ぶ
    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    private static class AssignPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            try { Assign(); }
            catch (Exception ex) { Logger.Exception(ex, "VanillaHideAndSeekRoles"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    private static class EndPatch
    {
        public static void Postfix([HarmonyArgument(0)] EndGameResult endGameResult)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            try { Judge(endGameResult.GameOverReason); }
            catch (Exception ex) { Logger.Exception(ex, "VanillaHideAndSeekRoles"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    private static class JoinPatch
    {
        public static void Postfix([HarmonyArgument(0)] InnerNet.ClientData data)
        {
            if (!AmongUsClient.Instance.AmHost || data == null) return;
            try { OnPlayerJoined(data.Id); }
            catch (Exception ex) { Logger.Exception(ex, "VanillaHideAndSeekRoles"); }
        }
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    private static class LobbyPatch
    {
        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            try { OnLobby(); }
            catch (Exception ex) { Logger.Exception(ex, "VanillaHideAndSeekRoles"); }
        }
    }
}
