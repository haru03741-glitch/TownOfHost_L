using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using UnityEngine;
using TownOfHostL.Attributes;

namespace TownOfHostL.Modules.Net;

// 決めた死者を、バイタルで生きている (OK) ように見せる (ホストのみ)。
// 本編のバイタルは客の手元の Data.IsDead と床の死体だけで決まるので、死者の PlayerInfo を IsDead=偽 にして見る人の客にだけ送る。
// ホストの手元の値と他の客には何も変わらない (SeerCamouflage と同じく、書き出す間だけ替えてすぐ戻す)。
// 同じ値で、見る人の画面ではその死者の幽霊が生きた体で見える (2026-11-26 実機)。そのため嘘を送るのはバイタルの台の近くにいる人だけにし、離れたら本物へ戻す。
// どこでもバイタルを開ける科学者の型の役職には送らない (台の近くかどうかで絞れない)。
// 本編のバイタルは開いている間に死亡へは変わるが生存へは戻らないので、本物が届いた時は同じフレームのうちに嘘を送り直す。
// 会議・試合の終わりの前には必ず本物へ戻す。
public static class VitalsMask
{
    // 嘘を出している死者 → 解く時刻 (Time.time・0 は会議まで)
    static readonly Dictionary<byte, float> masked = new();
    // 嘘を送ってある見る人
    static readonly HashSet<byte> seers = new();
    // 本物の PlayerInfo が誰かへ書き出されたので、見る人へ嘘を送り直す
    static bool resendPending;
    // 全員への送りと見る人への送りは別の電文で、届く順が入れ替わることがあるので、少し後にもう一度送る
    const float FollowUpDelay = 0.3f;
    static float followUpAt;
    // 自分で書き出している間 (Serialize の Postfix で送り直しの印を立てない)
    static bool writing;
    const float TickInterval = 0.1f;
    static float nextTick;

    public static bool HasAny => masked.Count > 0;
    public static bool IsMasked(byte playerId) => masked.Count > 0 && masked.ContainsKey(playerId);

    [GameModuleInitializer]
    public static void Init()
    {
        masked.Clear();
        seers.Clear();
        resendPending = false;
        followUpAt = 0f;
        nextTick = 0f;
    }

    /// <summary>死者 playerId を、duration 秒 (0 は次の会議まで) バイタルで生きているように見せる</summary>
    public static void Mask(byte playerId, float duration)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        masked[playerId] = duration > 0f ? Time.time + duration : 0f;
        Logger.Info($"バイタルで生きているように見せる: {playerId} ({(duration > 0f ? $"{duration}秒" : "会議まで")})", nameof(VitalsMask));
        // 今いる見る人へは、新しい死者の分を送る (死んだ時の本物の後に届く)
        if (seers.Count > 0) resendPending = true;
    }

    /// <summary>全部の嘘を本物へ戻す (会議・試合の終わりの前)</summary>
    public static void EndAll()
    {
        if (masked.Count == 0 && seers.Count == 0) return;
        Logger.Info($"バイタルの嘘を全部戻す (死者 {masked.Count}・見る人 {seers.Count})", nameof(VitalsMask));
        var victims = new List<byte>(masked.Keys);
        masked.Clear();
        // 暗転対策の退避中は IsDead が偽りの値なので送らない (退避を戻す時に全員の PlayerInfo が配られ、嘘も上書きされる)
        if (CanSend())
            foreach (var seerId in seers)
                if (Utils.GetPlayerById(seerId) is { } seer) Send(seer, victims, fake: false);
        seers.Clear();
        resendPending = false;
        followUpAt = 0f;
    }

    // 試合開始の直後は PlayerInfo の送信を止めている。暗転対策の退避中は IsDead が偽りの値になっている。
    // 鬼の体の差し替えの窓の間は GameManager が無いか、かくれんぼの物
    static bool CanSend() => !GameDataSerializePatch.DontTouch && !AntiBlackout.IsCached && !SeekerBody.InWindow;

    // 嘘を受けてよい人。台の近くかどうかは別に見る
    static bool CanSee(PlayerControl pc)
        => pc != null && pc.Data != null && !pc.Data.Disconnected && pc.IsAlive()
        && !masked.ContainsKey(pc.PlayerId) && !pc.Is(RoleTypes.Scientist);

    // 台の近くと見なす距離。使える距離に、0.1 秒ごとの判定と届くまでの間に歩ける分を足す
    static float EnterDistance()
    {
        var speed = Main.NormalOptions?.PlayerSpeedMod ?? 1f;
        return DisableDevice.UsableDistance() + 2.5f * System.Math.Max(1f, speed);
    }

    static readonly List<Vector2> consoles = new();
    static int consolesMap = -1;
    static List<Vector2> VitalsConsoles()
    {
        var map = (int)GameMap.Base;
        if (map == consolesMap) return consoles;
        consolesMap = map;
        consoles.Clear();
        switch (GameMap.Base)
        {
            case MapNames.Polus: consoles.Add(DisableDevice.Pos("PolusVital")); break;
            case MapNames.Airship: consoles.Add(DisableDevice.Pos("AirshipVital")); break;
            case MapNames.Fungle: consoles.Add(DisableDevice.Pos("FungleVital")); break;
        }
        return consoles;
    }

    static bool NearVitals(PlayerControl pc, float distance)
    {
        var pos = pc.GetTruePosition();
        foreach (var c in VitalsConsoles())
            if (Vector2.Distance(pos, c) <= distance) return true;
        return false;
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
    static class TickPatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            if ((masked.Count == 0 && seers.Count == 0) || __instance != PlayerControl.LocalPlayer || !AmongUsClient.Instance.AmHost) return;
            var now = Time.time;
            if (followUpAt > 0f && now >= followUpAt)
            {
                followUpAt = 0f;
                // 追いの 1 回は、次の追いを立てない (立てると 0.3 秒ごとに送り続ける)
                ResendAll(followUp: true);
            }
            if (resendPending) ResendAll();
            if (now < nextTick) return;
            nextTick = now + TickInterval;
            try { Tick(now); }
            catch (System.Exception ex) { Logger.Exception(ex, nameof(VitalsMask)); }
        }
    }

    static void Tick(float now)
    {
        if (!GameStates.IsInTask || !CanSend()) return;

        // 時間切れ・生き返り・抜けた死者の嘘を戻す
        List<byte> ended = null;
        foreach (var (id, until) in masked)
        {
            var pc = Utils.GetPlayerById(id);
            if ((until > 0f && now >= until) || pc == null || pc.Data == null || pc.Data.Disconnected || pc.IsAlive())
                (ended ??= new()).Add(id);
        }
        if (ended != null)
        {
            foreach (var id in ended) masked.Remove(id);
            var real = new List<byte>();
            foreach (var id in ended)
                if (Utils.GetPlayerById(id) is { Data.Disconnected: false }) real.Add(id);
            if (real.Count > 0)
                foreach (var seerId in seers)
                    if (Utils.GetPlayerById(seerId) is { } seer) Send(seer, real, fake: false);
        }

        // 見る人の出入り
        var enter = EnterDistance();
        var leave = enter + 1f;
        foreach (var pc in Main.AllPlayerControls)
        {
            if (pc.AmOwner) continue;
            var active = seers.Contains(pc.PlayerId);
            var want = masked.Count > 0 && CanSee(pc) && NearVitals(pc, active ? leave : enter);
            if (want == active) continue;
            if (want)
            {
                seers.Add(pc.PlayerId);
                Send(pc, new List<byte>(masked.Keys), fake: true);
            }
            else
            {
                seers.Remove(pc.PlayerId);
                // 死んだ・抜けた見る人には戻しを送らなくてよいが、送っても害は無い (会議の前に全部戻す決まりをここで崩さない)
                if (pc.Data != null && !pc.Data.Disconnected) Send(pc, new List<byte>(masked.Keys), fake: false);
            }
        }
    }

    static void ResendAll(bool followUp = false)
    {
        resendPending = false;
        if (masked.Count == 0 || seers.Count == 0 || !GameStates.IsInTask || !CanSend()) return;
        var victims = new List<byte>(masked.Keys);
        foreach (var seerId in seers)
            if (Utils.GetPlayerById(seerId) is { } seer) Send(seer, victims, fake: true);
        if (!followUp && followUpAt <= 0f) followUpAt = Time.time + FollowUpDelay;
    }

    // 1 人分の失敗で、残りの見る人への戻し (会議の前には必ず本物へ戻す) を止めない
    static void Send(PlayerControl seer, List<byte> victims, bool fake)
    {
        try { SendTo(seer, victims, fake); }
        catch (System.Exception ex) { Logger.Exception(ex, nameof(VitalsMask)); }
    }

    static void SendTo(PlayerControl seer, List<byte> victims, bool fake)
    {
        if (victims.Count == 0 || seer.AmOwner || seer.LacksClient(nameof(VitalsMask))) return;
        var writer = MessageWriter.Get(SendOption.Reliable);
        try
        {
            writer.StartMessage(6);
            writer.Write(AmongUsClient.Instance.GameId);
            writer.WritePacked(seer.GetClientId());
            var written = 0;
            foreach (var id in victims)
            {
                var data = Utils.GetPlayerById(id)?.Data;
                if (data == null || data.Disconnected) continue;
                WriteData(writer, data, fake);
                written++;
            }
            writer.EndMessage();
            if (written > 0) AmongUsClient.Instance.SendOrDisconnect(writer);
        }
        finally { writer.Recycle(); }
    }

    static void WriteData(MessageWriter writer, NetworkedPlayerInfo data, bool fake)
    {
        var isDead = data.IsDead;
        // 宛先 1 人への書き出しで dirty を消すと、同じ時に他の客へ配るはずの更新 (タスクなど) が届かなくなる
        var dirty = data.DirtyBits;
        writing = true;
        GameDataSerializePatch.SerializeMessageCount++;
        try
        {
            if (fake) data.IsDead = false;
            writer.StartMessage(1);
            writer.WritePacked(data.NetId);
            data.Serialize(writer, false);
            writer.EndMessage();
        }
        finally
        {
            data.IsDead = isDead;
            data.DirtyBits = dirty;
            GameDataSerializePatch.SerializeMessageCount--;
            writing = false;
        }
    }

    // 嘘を出している死者の本物の PlayerInfo が、誰か (全員・宛先指定のどちらも) へ書き出された
    [HarmonyPatch(typeof(NetworkedPlayerInfo), nameof(NetworkedPlayerInfo.Serialize))]
    static class SerializePatch
    {
        public static void Postfix(NetworkedPlayerInfo __instance, bool __result)
        {
            if (writing || !__result || seers.Count == 0 || !IsMasked(__instance.PlayerId)) return;
            resendPending = true;
        }
    }

    // 全員への PlayerInfo は SendAllStreamedObjects の中で書かれる。同じフレームのうちに見る人へ嘘を送り直す
    [HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.SendAllStreamedObjects))]
    static class AfterStreamPatch
    {
        public static void Postfix()
        {
            if (resendPending && AmongUsClient.Instance.AmHost) ResendAll();
        }
    }

    // ホストが見る人の時: Data は替えず、手元のバイタルの札だけを生存に描き直す (本編の Update が毎フレーム死亡へ戻すので毎フレーム)
    [HarmonyPatch(typeof(VitalsMinigame), nameof(VitalsMinigame.Update))]
    static class HostVitalsPatch
    {
        public static void Postfix(VitalsMinigame __instance)
        {
            if (masked.Count == 0 || !AmongUsClient.Instance.AmHost || !CanSee(PlayerControl.LocalPlayer)) return;
            var panels = __instance.vitals;
            if (panels == null) return;
            foreach (var panel in panels)
            {
                if (panel == null || !panel.IsDead || panel.PlayerInfo == null || !masked.ContainsKey(panel.PlayerInfo.PlayerId)) continue;
                panel.SetAlive();
            }
        }
    }
}
