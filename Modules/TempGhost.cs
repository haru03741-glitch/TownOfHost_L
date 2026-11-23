using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using TownOfHostL.Attributes;
using UnityEngine;

namespace TownOfHostL.Modules;

/// <summary>
/// 生きたまま、決めた時間だけ幽霊になる (ホストのみ)。
///
/// 全員の画面でその人の役職を守護天使にし、時間が来たら生きている時の見え方 (RoleView の表) に戻す。
/// PlayerInfo の IsDead は倒さないので、MOD の上でも本編の上でも生きたまま (勝敗の人数・投票・AntiBlackout はそのまま)。
/// 守護天使の役職は RoleView に記録されないので、戻す時は幽霊になる前の見え方がそのまま引ける。
///
/// 幽霊の間はキルも守護もできず、狙われもしない (CheckMurderPatch・CheckProtectPatch)。
/// 会議の前に必ず戻す。SetRole の直後に会議が来ると本編の客の会議の画面が壊れるので、
/// 幽霊になる時と戻す時の両方で Revive の「落ち着くまでの間」を始め、その間の通報は Revive が預かる。
/// 通報を通らずに開いた会議 (ホストの操作盤など) の時は会議の間は送らず、会議が明けてから戻す。
/// </summary>
public static class TempGhost
{
    private sealed class Entry
    {
        public float EndAt;
        public int ShownSeconds;
        public Action<PlayerControl> OnReturned;
    }

    // 会議明けの役職の配り直し (0.5 秒後) より後に送る
    private const float AfterMeetingReplayDelay = 1f;
    // 会議明けから能力を使えるまでの間 (会議明けの配り直しと重ねない)
    private const float AfterMeetingGap = 1.5f;
    // 幽霊の役職の能力 (守護) のボタンを止めておくクールダウン
    public const float LockedGuardianAngelCooldown = 900f;

    private static readonly Dictionary<byte, Entry> ghosts = new();
    // 会議に持ち越した人 (会議の間は送らず、会議明けに戻す)
    private static readonly List<byte> returnAfterMeeting = new();
    private static readonly List<byte> scratch = new();
    private static bool wasInMeeting;
    private static int generation;

    [GameModuleInitializer]
    public static void Init()
    {
        ghosts.Clear();
        returnAfterMeeting.Clear();
        wasInMeeting = false;
        generation++;
    }

    public static bool IsGhost(byte playerId) => ghosts.Count != 0 && ghosts.ContainsKey(playerId);
    public static bool IsGhost(PlayerControl pc) => pc != null && IsGhost(pc.PlayerId);

    /// <summary>幽霊でいる残りの秒 (切り上げ)。幽霊でなければ 0</summary>
    public static int RemainingSeconds(byte playerId)
        => ghosts.TryGetValue(playerId, out var e) ? Math.Max(0, CeilSeconds(e.EndAt - Time.realtimeSinceStartup)) : 0;

    private static int CeilSeconds(float seconds) => (int)Math.Ceiling(seconds);

    /// <summary>今、幽霊になれるか (会議・追放・会議明けの配り直しの間と、復活や幽霊化の SetRole が落ち着く前は不可)</summary>
    public static bool CanBegin(PlayerControl pc) => BlockReason(pc, true) == null;

    /// <summary>ベントの中かどうかを除いて <see cref="CanBegin"/> と同じ (ベントを能力のボタンにする役職が、ベントに入った時に見る)</summary>
    public static bool CanBeginIgnoringVent(PlayerControl pc) => BlockReason(pc, false) == null;

    /// <summary>今は幽霊になれない理由 (なれるなら null)</summary>
    private static string BlockReason(PlayerControl pc, bool checkVent)
    {
        if (pc == null || !AmongUsClient.Instance.AmHost) return "host";
        if (!GameStates.IsInTask || AmongUsClient.Instance.IsGameOver) return "not in task";
        if (MeetingHud.Instance || ExileController.Instance || MeetingStates.MeetingCalled || AntiBlackout.IsCached || AntiBlackout.IsRevertPending) return "meeting";
        if (Revive.IsSettling) return "settling";
        if (Time.realtimeSinceStartup - Revive.LastMeetingAt < AfterMeetingGap || returnAfterMeeting.Count != 0) return "after meeting";
        if (!pc.IsAlive()) return "dead";
        if (IsGhost(pc.PlayerId)) return "already ghost";
        if (pc.inMovingPlat || pc.onLadder) return "moving";
        if (checkVent && pc.inVent) return "in vent";
        return null;
    }

    /// <summary>
    /// duration 秒だけ幽霊にする。なれたら true。onReturned は会議以外で戻った後に呼ぶ (キルのクールの立て直しなど)。
    /// ignoreVent は、ベントから押し出した直後に呼ぶ時に使う (押し出された客は出ても、ホストの手元では inVent が立ったまま残る)。
    /// </summary>
    public static bool Begin(PlayerControl pc, float duration, Action<PlayerControl> onReturned = null, bool ignoreVent = false)
    {
        if (BlockReason(pc, !ignoreVent) is { } reason)
        {
            Logger.Info($"幽霊になれない ({reason}): {pc?.GetNameWithRole()}", "TempGhost");
            return false;
        }
        Logger.Info($"幽霊になる: {pc.GetNameWithRole()} {duration}s", "TempGhost");

        ghosts[pc.PlayerId] = new Entry
        {
            EndAt = Time.realtimeSinceStartup + duration,
            ShownSeconds = -1,
            OnReturned = onReturned,
        };
        // 見る人ごとに 1 本ずつ (守護天使は表に記録されない)
        foreach (var seer in Main.AllPlayerControls)
        {
            try { RoleView.Send(pc, seer, RoleTypes.GuardianAngel); }
            catch (Exception ex) { Logger.Exception(ex, "TempGhost"); }
        }
        Revive.BeginSettling();

        int gen = generation;
        // 守護のボタンのクールダウンを掛け直す (役職の設定で 900 秒にしてある)
        _ = new LateTask(() =>
        {
            if (gen == generation && IsGhost(pc)) pc.RpcResetAbilityCooldown();
        }, 0.2f, "TempGhost_LockProtect");
        Utils.NotifyRoles(SpecifySeer: pc);
        return true;
    }

    /// <summary>通報で会議が開く前に、幽霊の人を全員戻す。戻した人がいれば true (その後の通報は Revive が預かる)</summary>
    public static bool ReturnAllForMeeting()
    {
        if (ghosts.Count == 0) return false;
        scratch.Clear();
        scratch.AddRange(ghosts.Keys);
        foreach (var id in scratch) Return(id, forMeeting: true);
        return true;
    }

    private static void Return(byte playerId, bool forMeeting)
    {
        if (!ghosts.Remove(playerId, out var entry)) return;
        var pc = Utils.GetPlayerById(playerId);
        if (pc == null || pc.Data == null || pc.Data.Disconnected || !pc.IsAlive()) return;
        Logger.Info($"幽霊から戻る: {pc.GetNameWithRole()}{(forMeeting ? " (会議の前)" : "")}", "TempGhost");

        RoleView.Replay(pc);
        RestoreHostView(pc);
        Revive.BeginSettling();
        // 幽霊の間はベントに入れないので、ホストの手元に残った inVent (ベントから押し出した客で残ることがある) を下ろす。
        // 立ったままだとキルの対象にならない (CheckMurderPatch)
        if (pc.inVent) pc.inVent = false;
        if (forMeeting) return;

        BringInside(pc);
        AfterReturn(pc, entry, generation, AfterReturnPolls);
    }

    /// <summary>
    /// ホストの手元の役職を、生きている時の見え方へ直接戻す。ホストの手元の SetRole (CoSetRole) では、
    /// 幽霊の役職から生きている役職へ戻らない (2026-11-20 実機で確認。守護天使のまま残り、キルのクールの設定も飛ばされる)
    /// </summary>
    private static void RestoreHostView(PlayerControl pc)
    {
        var host = PlayerControl.LocalPlayer;
        if (host == null || pc.Data?.Role == null || !RoleManager.IsGhostRole(pc.Data.Role.Role)) return;
        try { pc.SetRoleEx(RoleView.Living(pc, host)); }
        catch (Exception ex) { Logger.Exception(ex, "TempGhost"); }
    }

    // 戻した後に、ホストの手元の役職が守護天使でなくなるのを待つ間隔と上限
    private const float AfterReturnPoll = 0.2f;
    private const int AfterReturnPolls = 10;

    /// <summary>
    /// SetRole が届いてから、キルと能力のクールを立て直す (役職が替わるとクールは本編の既定に戻る)。
    /// ホストの手元の役職 (SetRole はコルーチンで替わる) が守護天使のままだとキルのクールの設定が飛ばされるので、替わるまで待つ
    /// </summary>
    private static void AfterReturn(PlayerControl pc, Entry entry, int gen, int pollsLeft)
    {
        _ = new LateTask(() =>
        {
            if (gen != generation || pc == null || !pc.IsAlive() || IsGhost(pc) || !GameStates.IsInTask) return;
            if (pc.Data?.Role?.Role == RoleTypes.GuardianAngel && pollsLeft > 0)
            {
                AfterReturn(pc, entry, gen, pollsLeft - 1);
                return;
            }
            pc.ResetKillCooldown();
            pc.SetKillCooldown();
            pc.RpcResetAbilityCooldown();
            try { entry.OnReturned?.Invoke(pc); }
            catch (Exception ex) { Logger.Exception(ex, "TempGhost"); }
            PetAbility.EnsurePet(pc);
            Utils.NotifyRoles(SpecifySeer: pc);
        }, AfterReturnPoll, "TempGhost_AfterReturn");
    }

    /// <summary>幽霊の間に壁の中や外へ出ていたら、いちばん近いベントへ戻す</summary>
    private static void BringInside(PlayerControl pc)
    {
        Vector2 pos = pc.GetTruePosition();
        if (IsStandable(pos)) return;
        Vent nearest = null;
        float best = float.MaxValue;
        foreach (var vent in ShipStatus.Instance.AllVents)
        {
            float d = ((Vector2)vent.transform.position - pos).sqrMagnitude;
            if (d < best) { best = d; nearest = vent; }
        }
        if (nearest == null) return;
        Logger.Info($"マップの外にいたのでベントへ戻す: {pc.GetNameWithRole()} {pos} → {nearest.name}", "TempGhost");
        // 取りやめると戻れなくなるので、送りすぎの抑えを越えて送る
        pc.RpcSnapTo((Vector2)nearest.transform.position + new Vector2(0f, 0.3636f), force: true);
    }

    private static readonly Collider2D[] overlapBuffer = new Collider2D[10];

    /// <summary>
    /// 立っていられる場所か。床の当たり (ShipOnlyMask) の重なりと部屋の範囲で見る。
    /// マップごとの分け方と Polus の範囲の値は Endless Host Roles の ExtendedPlayerControl.IsInsideMap
    /// (https://github.com/Gurge44/EndlessHostRoles, GPL-3.0) から取った。
    /// マップごとに床の作りが違うので、どちらを使うかを分ける (スケルドは床の当たりが外にも広がっている)
    /// </summary>
    internal static bool IsStandable(Vector2 pos)
    {
        int floors = Physics2D.OverlapPointNonAlloc(pos, overlapBuffer, Constants.ShipOnlyMask);
        // 幽霊の体は当たり判定が切れているので、体の当たりではなく位置で部屋を引く
        bool inRoom = pos.GetPlainShipRoom() != null;
        return (MapNames)Main.NormalOptions.MapId switch
        {
            MapNames.Skeld or MapNames.Dleks => inRoom,
            MapNames.MiraHQ => floors >= 1 || inRoom,
            MapNames.Polus => floors >= 1 || (pos.y is >= -26.11f and <= -6.41f && pos.x is >= 3.56f and <= 32.68f),
            MapNames.Airship => floors >= 1,
            MapNames.Fungle => floors >= 2,
            _ => true,
        };
    }

    private static void Update()
    {
        // 幽霊も会議に持ち越した人もいなければ何もしない (ふだんの毎フレームはここで終わる)
        if (ghosts.Count == 0 && returnAfterMeeting.Count == 0) return;
        var client = AmongUsClient.Instance;
        if (!client || !client.AmHost || !GameStates.IsInGame) return;
        float now = Time.realtimeSinceStartup;

        bool inMeeting = MeetingHud.Instance || ExileController.Instance || AntiBlackout.IsCached;
        if (inMeeting)
        {
            wasInMeeting = true;
            // 通報を通らずに開いた会議: 会議の間は送らず、会議明けに戻す
            if (ghosts.Count != 0)
            {
                foreach (var id in ghosts.Keys)
                    if (!returnAfterMeeting.Contains(id)) returnAfterMeeting.Add(id);
                ghosts.Clear();
            }
            return;
        }
        if (wasInMeeting)
        {
            wasInMeeting = false;
            if (returnAfterMeeting.Count != 0) ScheduleReturnAfterMeeting();
        }

        if (ghosts.Count == 0) return;
        scratch.Clear();
        foreach (var (id, entry) in ghosts)
        {
            var pc = Utils.GetPlayerById(id);
            // 抜けた・本当に死んだ (死ねば本編の幽霊になるので戻さない)
            if (pc == null || pc.Data == null || pc.Data.Disconnected || !pc.IsAlive())
            {
                scratch.Add(id);
                continue;
            }
            if (now >= entry.EndAt)
            {
                scratch.Add(id);
                continue;
            }
            // 残りの秒が変わった時だけ、本人の名前の下の秒読みを書き直す
            int shown = CeilSeconds(entry.EndAt - now);
            if (shown != entry.ShownSeconds)
            {
                entry.ShownSeconds = shown;
                Utils.NotifyRoles(SpecifySeer: pc);
            }
        }
        foreach (var id in scratch)
        {
            if (ghosts.TryGetValue(id, out var entry) && now >= entry.EndAt) Return(id, forMeeting: false);
            else ghosts.Remove(id);
        }
    }

    private static void ScheduleReturnAfterMeeting()
    {
        var ids = returnAfterMeeting.ToArray();
        int gen = generation;
        _ = new LateTask(() =>
        {
            // 暗転対策が客の画面の役職を戻し終えるまで待つ (先に送ると身代わりを上書きする)
            if (gen == generation && GameStates.IsInTask && AntiBlackout.IsRevertPending) { ScheduleReturnAfterMeeting(); return; }
            returnAfterMeeting.Clear();
            if (gen != generation || !GameStates.IsInTask) return;
            foreach (var id in ids)
            {
                var pc = Utils.GetPlayerById(id);
                if (pc == null || pc.Data == null || pc.Data.Disconnected || !pc.IsAlive()) continue;
                Logger.Info($"会議を持ち越した幽霊を戻す: {pc.GetNameWithRole()}", "TempGhost");
                RoleView.Replay(pc);
                RestoreHostView(pc);
                // 本編の客は幽霊の間にペットを外している。PlayerInfo は SetRole の後に届くよう間を空けて付け直す
                _ = new LateTask(() => PetAbility.EnsurePet(pc), AfterReturnPoll, "TempGhost_AfterMeetingPet");
            }
            Revive.BeginSettling();
        }, AfterMeetingReplayDelay, "TempGhost_AfterMeeting");
    }

    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    private static class DrivePatch
    {
        public static void Postfix() => Update();
    }
}
